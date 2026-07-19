using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

public sealed class DependencyGraphService : IDependencyGraphService
{
    private readonly IAnalysisRepository _repository;
    public DependencyGraphService(IAnalysisRepository repository) => _repository = repository;

    public async Task<GraphData?> BuildAsync(Guid analysisId, string kind, int limit, CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetAsync(analysisId, cancellationToken);
        if (analysis is null || analysis.Status != AnalysisStatus.Completed) return null;
        limit = Math.Clamp(limit, 10, 500);
        return kind.ToLowerInvariant() switch
        {
            "projects" => Projects(analysis, limit),
            "sql" => Sql(analysis, limit),
            "cobol" => Cobol(analysis, limit),
            _ => Types(analysis, limit)
        };
    }

    private static GraphData Projects(Analysis analysis, int limit)
    {
        var projects = analysis.Projects.Take(limit).ToList();
        var byPath = projects.ToDictionary(project => Path.GetFullPath(project.Path), StringComparer.OrdinalIgnoreCase);
        var nodes = projects.Select(project => new GraphNode(project.Id.ToString("N"), project.Name, "Project", project.TargetFramework, project.Path, Math.Max(1, project.Symbols.Count))).ToList();
        var edges = new List<GraphEdge>();
        foreach (var project in projects)
        foreach (var reference in project.References)
            if (byPath.TryGetValue(Path.GetFullPath(reference.ReferencedPath), out var target))
                edges.Add(new GraphEdge(project.Id.ToString("N"), target.Id.ToString("N"), "References", RelationConfidence.Certain));
        return new GraphData("projects", nodes, edges, analysis.Projects.Count > limit);
    }

    private static GraphData Types(Analysis analysis, int limit)
    {
        var allSymbols = analysis.Projects.SelectMany(project => project.Symbols.Select(symbol => (Project: project, Symbol: symbol))).ToList();
        var interesting = allSymbols.Where(item => item.Symbol.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Record or SymbolKind.Method)
            .OrderByDescending(item => analysis.Relations.Count(relation => relation.SourceSymbolId == item.Symbol.Id || relation.TargetSymbolId == item.Symbol.Id))
            .Take(limit).ToList();
        var ids = interesting.Select(item => item.Symbol.Id).ToHashSet();
        var nodes = interesting.Select(item => new GraphNode(item.Symbol.Id.ToString("N"), Display(item.Symbol), item.Symbol.Kind.ToString(), item.Project.Name, item.Symbol.FilePath,
            Math.Max(1, analysis.Relations.Count(relation => relation.SourceSymbolId == item.Symbol.Id || relation.TargetSymbolId == item.Symbol.Id)))).ToList();
        var edges = analysis.Relations.Where(relation => relation.TargetSymbolId.HasValue && ids.Contains(relation.SourceSymbolId) && ids.Contains(relation.TargetSymbolId.Value))
            .Select(relation => new GraphEdge(relation.SourceSymbolId.ToString("N"), relation.TargetSymbolId!.Value.ToString("N"), relation.Kind.ToString(), relation.Confidence)).ToList();
        return new GraphData("types", nodes, edges, allSymbols.Count > limit);
    }

    private static GraphData Sql(Analysis analysis, int limit)
    {
        var objects = analysis.SqlObjects.Take(limit).ToList();
        var ids = objects.Select(item => item.Id).ToHashSet();
        var nodes = objects.Select(item => new GraphNode(item.Id.ToString("N"), item.Name, item.Kind.ToString(), "SQL", item.FilePath,
            Math.Max(1, analysis.SqlReferences.Count(reference => reference.SourceSqlObjectId == item.Id || reference.TargetSqlObjectId == item.Id)))).ToList();
        var edges = analysis.SqlReferences.Where(reference => reference.SourceSqlObjectId.HasValue && reference.TargetSqlObjectId.HasValue &&
                ids.Contains(reference.SourceSqlObjectId.Value) && ids.Contains(reference.TargetSqlObjectId.Value))
            .Select(reference => new GraphEdge(reference.SourceSqlObjectId!.Value.ToString("N"), reference.TargetSqlObjectId!.Value.ToString("N"), reference.Operation.ToString(), reference.Confidence)).ToList();
        return new GraphData("sql", nodes, edges, analysis.SqlObjects.Count > limit);
    }

    private static GraphData Cobol(Analysis analysis, int limit)
    {
        var symbols = analysis.CobolSymbols.Take(limit).ToList();
        var ids = symbols.Select(item => item.Id).ToHashSet();
        var nodes = symbols.Select(item => new GraphNode(item.Id.ToString("N"), item.Name, item.Kind.ToString(), "COBOL", item.FilePath,
            Math.Max(1, analysis.CobolRelations.Count(relation => relation.SourceSymbolId == item.Id || relation.TargetSymbolId == item.Id)))).ToList();
        var edges = analysis.CobolRelations.Where(relation => relation.SourceSymbolId.HasValue && relation.TargetSymbolId.HasValue &&
                ids.Contains(relation.SourceSymbolId.Value) && ids.Contains(relation.TargetSymbolId.Value))
            .Select(relation => new GraphEdge(relation.SourceSymbolId!.Value.ToString("N"), relation.TargetSymbolId!.Value.ToString("N"), relation.Kind.ToString(), relation.Confidence)).ToList();
        return new GraphData("cobol", nodes, edges, analysis.CobolSymbols.Count > limit);
    }

    private static string Display(CodeSymbol symbol) => string.IsNullOrWhiteSpace(symbol.Container) ? symbol.Name : $"{symbol.Container}.{symbol.Name}";
}
