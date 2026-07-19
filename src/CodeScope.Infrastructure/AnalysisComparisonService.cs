using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

public sealed class AnalysisComparisonService : IAnalysisComparisonService
{
    private readonly IAnalysisRepository _repository;
    public AnalysisComparisonService(IAnalysisRepository repository) => _repository = repository;

    public async Task<AnalysisComparison?> CompareAsync(Guid fromAnalysisId, Guid toAnalysisId, CancellationToken cancellationToken)
    {
        var from = await _repository.GetAsync(fromAnalysisId, cancellationToken);
        var to = await _repository.GetAsync(toAnalysisId, cancellationToken);
        if (from is null || to is null || from.Status != AnalysisStatus.Completed || to.Status != AnalysisStatus.Completed) return null;

        var added = new List<ComparisonItem>();
        var removed = new List<ComparisonItem>();
        var modified = new List<ComparisonItem>();
        var renamed = new List<ComparisonRename>();
        var fromFiles = from.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var toFiles = to.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var unchanged = 0;
        foreach (var file in toFiles.Values.Where(file => fromFiles.ContainsKey(file.RelativePath)))
        {
            var previous = fromFiles[file.RelativePath];
            if (!string.Equals(previous.Sha256, file.Sha256, StringComparison.Ordinal))
                modified.Add(new ComparisonItem(file.RelativePath, "File", file.RelativePath, "Modified"));
            else unchanged++;
        }
        var addedFiles = toFiles.Values.Where(file => !fromFiles.ContainsKey(file.RelativePath)).ToList();
        var removedFiles = fromFiles.Values.Where(file => !toFiles.ContainsKey(file.RelativePath)).ToList();
        var matchedRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in addedFiles)
        {
            var previous = removedFiles.FirstOrDefault(candidate => !matchedRemoved.Contains(candidate.RelativePath) &&
                candidate.Sha256.Length > 0 && string.Equals(candidate.Sha256, file.Sha256, StringComparison.Ordinal));
            if (previous is null)
                added.Add(new ComparisonItem(file.RelativePath, "File", file.RelativePath, "Added"));
            else
            {
                matchedRemoved.Add(previous.RelativePath);
                renamed.Add(new ComparisonRename(previous.RelativePath, file.RelativePath, file.Sha256));
            }
        }
        foreach (var file in removedFiles.Where(file => !matchedRemoved.Contains(file.RelativePath)))
            removed.Add(new ComparisonItem(file.RelativePath, "File", file.RelativePath, "Removed"));

        CompareSet(SymbolKeys(from), SymbolKeys(to), "Symbol", added, removed);
        CompareSet(from.Endpoints.ToDictionary(item => $"{item.HttpMethod} {item.Route}", item => item.FilePath, StringComparer.OrdinalIgnoreCase),
            to.Endpoints.ToDictionary(item => $"{item.HttpMethod} {item.Route}", item => item.FilePath, StringComparer.OrdinalIgnoreCase), "Endpoint", added, removed);
        CompareSet(from.SqlObjects.ToDictionary(item => $"{item.Kind}:{item.Name}", item => item.FilePath, StringComparer.OrdinalIgnoreCase),
            to.SqlObjects.ToDictionary(item => $"{item.Kind}:{item.Name}", item => item.FilePath, StringComparer.OrdinalIgnoreCase), "SqlObject", added, removed);
        CompareSet(from.CobolSymbols.GroupBy(item => $"{item.Kind}:{item.Name}", StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().FilePath, StringComparer.OrdinalIgnoreCase),
            to.CobolSymbols.GroupBy(item => $"{item.Kind}:{item.Name}", StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().FilePath, StringComparer.OrdinalIgnoreCase), "CobolSymbol", added, removed);
        CompareSet(from.OrmEntityMappings.GroupBy(item => $"{item.EntityName}:{item.TableName}", StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().FilePath, StringComparer.OrdinalIgnoreCase),
            to.OrmEntityMappings.GroupBy(item => $"{item.EntityName}:{item.TableName}", StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().FilePath, StringComparer.OrdinalIgnoreCase), "OrmMapping", added, removed);

        return new AnalysisComparison(fromAnalysisId, toAnalysisId,
            from.RepositorySnapshots.FirstOrDefault()?.CommitHash,
            to.RepositorySnapshots.FirstOrDefault()?.CommitHash,
            added.OrderBy(item => item.Kind).ThenBy(item => item.Key).ToList(),
            removed.OrderBy(item => item.Kind).ThenBy(item => item.Key).ToList(),
            modified.OrderBy(item => item.Key).ToList(),
            renamed.OrderBy(item => item.FromPath).ToList(),
            unchanged);
    }

    private static Dictionary<string, string> SymbolKeys(Analysis analysis) => analysis.Projects
        .SelectMany(project => project.Symbols)
        .GroupBy(symbol => $"{symbol.Kind}:{symbol.Container}:{symbol.Name}", StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => Relative(analysis.RootPath, group.First().FilePath), StringComparer.OrdinalIgnoreCase);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void CompareSet(
        IReadOnlyDictionary<string, string> from,
        IReadOnlyDictionary<string, string> to,
        string kind,
        ICollection<ComparisonItem> added,
        ICollection<ComparisonItem> removed)
    {
        foreach (var item in to.Where(item => !from.ContainsKey(item.Key))) added.Add(new ComparisonItem(item.Key, kind, item.Value, "Added"));
        foreach (var item in from.Where(item => !to.ContainsKey(item.Key))) removed.Add(new ComparisonItem(item.Key, kind, item.Value, "Removed"));
    }
}
