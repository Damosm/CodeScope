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
        var fromFiles = from.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var toFiles = to.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var unchanged = 0;
        foreach (var file in toFiles.Values)
        {
            if (!fromFiles.TryGetValue(file.RelativePath, out var previous))
                added.Add(new ComparisonItem(file.RelativePath, "File", file.RelativePath, "Added"));
            else if (!string.Equals(previous.Sha256, file.Sha256, StringComparison.Ordinal))
                modified.Add(new ComparisonItem(file.RelativePath, "File", file.RelativePath, "Modified"));
            else unchanged++;
        }
        foreach (var file in fromFiles.Values.Where(file => !toFiles.ContainsKey(file.RelativePath)))
            removed.Add(new ComparisonItem(file.RelativePath, "File", file.RelativePath, "Removed"));

        CompareSet(SymbolKeys(from), SymbolKeys(to), "Symbol", added, removed);
        CompareSet(from.Endpoints.ToDictionary(item => $"{item.HttpMethod} {item.Route}", item => item.FilePath, StringComparer.OrdinalIgnoreCase),
            to.Endpoints.ToDictionary(item => $"{item.HttpMethod} {item.Route}", item => item.FilePath, StringComparer.OrdinalIgnoreCase), "Endpoint", added, removed);
        CompareSet(from.SqlObjects.ToDictionary(item => $"{item.Kind}:{item.Name}", item => item.FilePath, StringComparer.OrdinalIgnoreCase),
            to.SqlObjects.ToDictionary(item => $"{item.Kind}:{item.Name}", item => item.FilePath, StringComparer.OrdinalIgnoreCase), "SqlObject", added, removed);
        CompareSet(from.CobolSymbols.GroupBy(item => $"{item.Kind}:{item.Name}", StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().FilePath, StringComparer.OrdinalIgnoreCase),
            to.CobolSymbols.GroupBy(item => $"{item.Kind}:{item.Name}", StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().FilePath, StringComparer.OrdinalIgnoreCase), "CobolSymbol", added, removed);

        return new AnalysisComparison(fromAnalysisId, toAnalysisId,
            from.RepositorySnapshots.FirstOrDefault()?.CommitHash,
            to.RepositorySnapshots.FirstOrDefault()?.CommitHash,
            added.OrderBy(item => item.Kind).ThenBy(item => item.Key).ToList(),
            removed.OrderBy(item => item.Kind).ThenBy(item => item.Key).ToList(),
            modified.OrderBy(item => item.Key).ToList(),
            unchanged);
    }

    private static Dictionary<string, string> SymbolKeys(Analysis analysis) => analysis.Projects
        .SelectMany(project => project.Symbols)
        .GroupBy(symbol => $"{symbol.Kind}:{symbol.Container}:{symbol.Name}:{Relative(analysis.RootPath, symbol.FilePath)}", StringComparer.OrdinalIgnoreCase)
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
