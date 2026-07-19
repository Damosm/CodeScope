using System.Text.RegularExpressions;
using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

internal static class CobolSourceAnalyzer
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "node_modules", "packages" };
    private static readonly Regex Program = new(@"\bPROGRAM-ID\s*\.\s*(?<name>[A-Z0-9-]+)", Options);
    private static readonly Regex Section = new(@"^\s*(?<name>[A-Z0-9-]+)\s+SECTION\s*\.", Options);
    private static readonly Regex Paragraph = new(@"^\s*(?<name>[A-Z0-9-]+)\s*\.", Options);
    private static readonly Regex Call = new("\\bCALL\\s+(?:['\\\"](?<quoted>[A-Z0-9-]+)['\\\"]|(?<name>[A-Z0-9-]+))", Options);
    private static readonly Regex Copy = new(@"\bCOPY\s+(?<name>[A-Z0-9-]+)", Options);
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    public static async Task AnalyzeAsync(Analysis analysis, string rootPath, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        var warnings = 0;
        var paths = EnumerateCobolSafely(rootPath, () => warnings++).ToList();
        var pending = new List<(CobolSymbol? Source, string Target, CobolRelationKind Kind, string File, int Line)>();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try { text = await File.ReadAllTextAsync(path, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings++;
                DiagnosticReporter.Warning(analysis, "CSCOPE301", "cobol", "Le fichier COBOL n'a pas pu être lu.", path);
                continue;
            }
            CobolSymbol? currentProgram = null;
            foreach (Match match in Program.Matches(text))
            {
                currentProgram = AddSymbol(analysis, CobolSymbolKind.Program, match.Groups["name"].Value, path, Line(text, match.Index));
            }
            if (Path.GetExtension(path).Equals(".cpy", StringComparison.OrdinalIgnoreCase))
                AddSymbol(analysis, CobolSymbolKind.Copybook, Path.GetFileNameWithoutExtension(path), path, 1);
            foreach (Match match in Section.Matches(text))
                AddSymbol(analysis, CobolSymbolKind.Section, match.Groups["name"].Value, path, Line(text, match.Index));
            foreach (Match match in Paragraph.Matches(text))
            {
                var name = match.Groups["name"].Value;
                if (!name.Equals("PROGRAM-ID", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("DIVISION", StringComparison.OrdinalIgnoreCase))
                    AddSymbol(analysis, CobolSymbolKind.Paragraph, name, path, Line(text, match.Index));
            }
            foreach (Match match in Call.Matches(text))
                pending.Add((currentProgram, match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["name"].Value, CobolRelationKind.Calls, path, Line(text, match.Index)));
            foreach (Match match in Copy.Matches(text))
                pending.Add((currentProgram, match.Groups["name"].Value, CobolRelationKind.Copies, path, Line(text, match.Index)));
        }

        foreach (var item in pending)
        {
            var expectedKind = item.Kind == CobolRelationKind.Copies ? CobolSymbolKind.Copybook : CobolSymbolKind.Program;
            var target = analysis.CobolSymbols.FirstOrDefault(symbol => symbol.Kind == expectedKind && symbol.Name.Equals(item.Target, StringComparison.OrdinalIgnoreCase));
            analysis.CobolRelations.Add(new CobolRelation
            {
                AnalysisId = analysis.Id,
                SourceSymbolId = item.Source?.Id,
                TargetSymbolId = target?.Id,
                SourceDisplay = item.Source?.Name ?? Path.GetFileNameWithoutExtension(item.File),
                TargetDisplay = item.Target.ToUpperInvariant(),
                Kind = item.Kind,
                Confidence = target is null ? RelationConfidence.Probable : RelationConfidence.Certain,
                FilePath = item.File,
                Line = item.Line
            });
        }
        progress?.Report(new AnalysisProgress("cobol", $"COBOL : {analysis.CobolSymbols.Count} symbole(s), {analysis.CobolRelations.Count} relation(s).", analysis.Projects.Count, analysis.Projects.Count, paths.Count, analysis.CobolSymbols.Count, warnings));
    }

    private static CobolSymbol AddSymbol(Analysis analysis, CobolSymbolKind kind, string name, string path, int line)
    {
        var existing = analysis.CobolSymbols.FirstOrDefault(symbol => symbol.Kind == kind && symbol.FilePath == path && symbol.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var result = new CobolSymbol { AnalysisId = analysis.Id, Kind = kind, Name = name.ToUpperInvariant(), FilePath = path, Line = line };
        analysis.CobolSymbols.Add(result);
        return result;
    }

    private static int Line(string text, int index)
    {
        var line = 1;
        for (var position = 0; position < index; position++)
            if (text[position] == '\n') line++;
        return line;
    }

    private static IEnumerable<string> EnumerateCobolSafely(string root, Action onWarning)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { onWarning(); continue; }
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".cbl", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".cob", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".cpy", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { onWarning(); continue; }
            foreach (var child in children)
            {
                if (Excluded.Contains(Path.GetFileName(child))) continue;
                try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { onWarning(); }
            }
        }
    }
}
