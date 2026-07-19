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
            "orm" => Orm(analysis, limit),
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

    private static GraphData Orm(Analysis analysis, int limit)
    {
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new List<GraphEdge>();
        var codeSymbols = analysis.Projects.SelectMany(project => project.Symbols).ToDictionary(symbol => symbol.Id);
        var sqlObjects = analysis.SqlObjects.ToDictionary(item => item.Id);
        var sqlColumns = analysis.SqlColumns.ToDictionary(item => item.Id);
        var mappings = analysis.OrmEntityMappings.Take(limit).ToList();
        foreach (var mapping in mappings)
        {
            if (!mapping.CodeSymbolId.HasValue || !mapping.SqlObjectId.HasValue ||
                !codeSymbols.TryGetValue(mapping.CodeSymbolId.Value, out var entity) ||
                !sqlObjects.TryGetValue(mapping.SqlObjectId.Value, out var table)) continue;
            var entityKey = $"code:{entity.Id:N}";
            var tableKey = $"sql:{table.Id:N}";
            nodes.TryAdd(entityKey, new GraphNode(entityKey, Display(entity), "Entity", mapping.Source.ToString(), entity.FilePath,
                Math.Max(1, analysis.OrmPropertyMappings.Count(item => item.OrmEntityMappingId == mapping.Id))));
            nodes.TryAdd(tableKey, new GraphNode(tableKey, table.Name, "Table", "SQL", table.FilePath,
                Math.Max(1, analysis.SqlColumns.Count(column => column.SqlObjectId == table.Id))));
            edges.Add(new GraphEdge(entityKey, tableKey, "MapsTo", mapping.Confidence));

            foreach (var propertyMapping in analysis.OrmPropertyMappings.Where(item => item.OrmEntityMappingId == mapping.Id))
            {
                if (!propertyMapping.CodeSymbolId.HasValue || !propertyMapping.SqlColumnId.HasValue ||
                    !codeSymbols.TryGetValue(propertyMapping.CodeSymbolId.Value, out var property) ||
                    !sqlColumns.TryGetValue(propertyMapping.SqlColumnId.Value, out var column)) continue;
                var propertyKey = $"code:{property.Id:N}";
                var columnKey = $"column:{column.Id:N}";
                nodes.TryAdd(propertyKey, new GraphNode(propertyKey, Display(property), "Property", mapping.EntityName, property.FilePath, 1));
                nodes.TryAdd(columnKey, new GraphNode(columnKey, $"{table.Name}.{column.Name}", "Column", table.Name, column.FilePath, 1));
                edges.Add(new GraphEdge(propertyKey, columnKey, "MapsToColumn", propertyMapping.Confidence));
            }
        }
        var selectedNodes = nodes.Values.Take(limit).ToList();
        var selectedIds = selectedNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        return new GraphData("orm", selectedNodes, edges.Where(edge => selectedIds.Contains(edge.Source) && selectedIds.Contains(edge.Target)).ToList(),
            analysis.OrmEntityMappings.Count > mappings.Count || nodes.Count > limit);
    }

    private static string Display(CodeSymbol symbol) => string.IsNullOrWhiteSpace(symbol.Container) ? symbol.Name : $"{symbol.Container}.{symbol.Name}";
}
