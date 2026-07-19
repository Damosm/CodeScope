using System.Diagnostics;
using System.Security.Cryptography;
using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

internal static class FileInventoryAnalyzer
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "node_modules", "packages" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".csproj", ".sln", ".sql", ".cbl", ".cob", ".cpy", ".json", ".xml", ".yml", ".yaml", ".config", ".props", ".targets", ".md", ".txt", ".html", ".css", ".js", ".ts" };

    public static async Task AnalyzeAsync(
        Analysis analysis,
        string rootPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var projectRoots = analysis.Projects
            .Select(project => (Project: project, Root: Path.GetDirectoryName(project.Path)!))
            .OrderByDescending(item => item.Root.Length)
            .ToList();
        var warnings = 0;
        var paths = EnumerateFiles(rootPath, () => warnings++).ToList();

        for (var index = 0; index < paths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = paths[index];
            try
            {
                var info = new FileInfo(path);
                var extension = info.Extension.ToLowerInvariant();
                analysis.Files.Add(new SourceFileInfo
                {
                    AnalysisId = analysis.Id,
                    ProjectInfoId = projectRoots.FirstOrDefault(item => IsInside(path, item.Root)).Project?.Id,
                    RelativePath = Path.GetRelativePath(rootPath, path).Replace('\\', '/'),
                    FullPath = path,
                    Extension = extension,
                    Category = Classify(extension, info.Name),
                    Size = info.Length,
                    LineCount = TextExtensions.Contains(extension) ? await CountLinesAsync(path, cancellationToken) : 0,
                    Sha256 = await HashAsync(path, cancellationToken),
                    LastWriteUtc = info.LastWriteTimeUtc
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings++;
            }

            if ((index + 1) % 50 == 0 || index + 1 == paths.Count)
                progress?.Report(new AnalysisProgress(
                    "inventory",
                    $"Inventaire : {index + 1}/{paths.Count} fichier(s).",
                    analysis.Projects.Count,
                    analysis.Projects.Count,
                    index + 1,
                    analysis.Projects.Sum(project => project.Symbols.Count),
                    warnings));
        }

        analysis.RepositorySnapshots.Add(await CaptureGitAsync(analysis.Id, rootPath, cancellationToken));
    }

    private static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static SourceFileCategory Classify(string extension, string name) => extension switch
    {
        ".cs" => SourceFileCategory.SourceCode,
        ".sql" => SourceFileCategory.Sql,
        ".cbl" or ".cob" or ".cpy" => SourceFileCategory.Cobol,
        ".csproj" or ".fsproj" or ".vbproj" => SourceFileCategory.Project,
        ".sln" => SourceFileCategory.Solution,
        ".md" or ".txt" or ".rst" => SourceFileCategory.Documentation,
        ".json" or ".xml" or ".yml" or ".yaml" or ".config" or ".props" or ".targets" => SourceFileCategory.Configuration,
        _ when name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) => SourceFileCategory.Configuration,
        _ => SourceFileCategory.Other
    };

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<int> CountLinesAsync(string path, CancellationToken cancellationToken)
    {
        var lines = 0;
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true), detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync() is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines++;
        }
        return lines;
    }

    private static async Task<RepositorySnapshot> CaptureGitAsync(Guid analysisId, string rootPath, CancellationToken cancellationToken)
    {
        var repositoryRoot = await RunGitAsync(rootPath, new[] { "rev-parse", "--show-toplevel" }, cancellationToken);
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            return new RepositorySnapshot { AnalysisId = analysisId, IsGitRepository = false };

        var commit = await RunGitAsync(repositoryRoot, new[] { "rev-parse", "HEAD" }, cancellationToken);
        var branch = await RunGitAsync(repositoryRoot, new[] { "branch", "--show-current" }, cancellationToken);
        var status = await RunGitAsync(repositoryRoot, new[] { "status", "--porcelain" }, cancellationToken);
        return new RepositorySnapshot
        {
            AnalysisId = analysisId,
            IsGitRepository = true,
            RepositoryRoot = repositoryRoot,
            CommitHash = NullIfEmpty(commit),
            Branch = NullIfEmpty(branch),
            IsDirty = !string.IsNullOrWhiteSpace(status)
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<string?> RunGitAsync(string directory, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, Action onWarning)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { onWarning(); continue; }
            foreach (var file in files) yield return file;

            string[] directories;
            try { directories = Directory.GetDirectories(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { onWarning(); continue; }
            foreach (var child in directories)
            {
                if (Excluded.Contains(Path.GetFileName(child))) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { onWarning(); }
            }
        }
    }
}
