using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

public sealed class ImpactAnalysisService : IImpactAnalysisService
{
    private const int MaximumNodes = 250;
    private readonly IAnalysisRepository _repository;

    public ImpactAnalysisService(IAnalysisRepository repository) => _repository = repository;

    public async Task<ImpactReport?> AnalyzeAsync(
        Guid analysisId,
        ImpactElementKind kind,
        Guid elementId,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetAsync(analysisId, cancellationToken);
        if (analysis is null || analysis.Status != AnalysisStatus.Completed) return null;

        maxDepth = Math.Clamp(maxDepth, 1, 4);
        var graph = BuildGraph(analysis);
        var rootKey = Key(kind, elementId);
        if (!graph.Nodes.TryGetValue(rootKey, out var rootDefinition)) return null;

        var visited = new Dictionary<string, VisitedNode>(StringComparer.Ordinal)
        {
            [rootKey] = new VisitedNode(0, RelationConfidence.Certain, "élément sélectionné", null)
        };
        var queue = new Queue<string>();
        queue.Enqueue(rootKey);
        var traversedEdges = new Dictionary<string, ImpactEdge>(StringComparer.Ordinal);
        var truncated = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentKey = queue.Dequeue();
            var current = visited[currentKey];
            if (current.Depth >= maxDepth || !graph.Adjacency.TryGetValue(currentKey, out var outgoing)) continue;

            foreach (var edge in outgoing)
            {
                var nextDepth = current.Depth + 1;
                var confidence = Weaker(current.Confidence, edge.Confidence);
                var edgeKey = $"{edge.SourceKey}|{edge.TargetKey}|{edge.Relationship}";
                traversedEdges[edgeKey] = new ImpactEdge(
                    edge.SourceKey,
                    edge.TargetKey,
                    edge.Relationship,
                    edge.Confidence);

                if (visited.TryGetValue(edge.TargetKey, out var existing))
                {
                    if (nextDepth == existing.Depth && confidence < existing.Confidence)
                        visited[edge.TargetKey] = existing with { Confidence = confidence };
                    continue;
                }

                if (visited.Count >= MaximumNodes)
                {
                    truncated = true;
                    continue;
                }

                visited[edge.TargetKey] = new VisitedNode(nextDepth, confidence, edge.Relationship, currentKey);
                queue.Enqueue(edge.TargetKey);
            }
        }

        var root = ToImpactNode(rootKey, rootDefinition, visited[rootKey]);
        var nodes = visited
            .Where(pair => pair.Key != rootKey)
            .Select(pair => ToImpactNode(pair.Key, graph.Nodes[pair.Key], pair.Value))
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.Kind)
            .ThenBy(node => node.Name)
            .ToList();
        var files = nodes.Append(root)
            .Select(node => node.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path)
            .ToList();
        var projects = nodes.Append(root)
            .Where(node => node.Id.HasValue && node.Kind == ImpactElementKind.CodeSymbol)
            .Select(node => graph.ProjectBySymbolId.GetValueOrDefault(node.Id!.Value))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
        var tests = nodes.Append(root)
            .Where(node => IsTest(node, graph.ProjectBySymbolId))
            .Select(node => node.FilePath!)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path)
            .ToList();

        var direct = nodes.Count(node => node.Depth == 1);
        var indirect = nodes.Count(node => node.Depth > 1);
        var uncertain = nodes.Count(node => node.Confidence != RelationConfidence.Certain);
        var destructiveSql = analysis.SqlReferences.Count(reference =>
            reference.Operation is SqlOperationKind.Update or SqlOperationKind.Delete or SqlOperationKind.Insert &&
            (reference.SourceSqlObjectId == elementId || reference.TargetSqlObjectId == elementId));
        var rootComplexity = kind == ImpactElementKind.CodeSymbol
            ? analysis.Projects.SelectMany(project => project.Symbols)
                .Where(symbol => symbol.Id == elementId)
                .Select(symbol => symbol.Complexity)
                .DefaultIfEmpty(1)
                .Max()
            : 1;
        var score = direct * 2 + indirect + projects.Count * 3 + tests.Count * 2 + destructiveSql * 2 +
            Math.Max(0, rootComplexity - 1) / 5;
        if (uncertain > 0) score += Math.Min(5, uncertain);
        var risk = score switch
        {
            <= 5 => ImpactRisk.Low,
            <= 15 => ImpactRisk.Medium,
            <= 30 => ImpactRisk.High,
            _ => ImpactRisk.Critical
        };
        var reasons = BuildReasons(direct, indirect, projects.Count, tests.Count, uncertain, destructiveSql, truncated);
        var criticalPaths = BuildCriticalPaths(rootKey, visited, graph);

        return new ImpactReport(
            root,
            nodes,
            traversedEdges.Values.ToList(),
            projects,
            files,
            tests,
            risk,
            score,
            reasons,
            criticalPaths,
            maxDepth,
            truncated);
    }

    private static Graph BuildGraph(Analysis analysis)
    {
        var graph = new Graph();
        foreach (var project in analysis.Projects)
        {
            foreach (var symbol in project.Symbols)
            {
                var key = Key(ImpactElementKind.CodeSymbol, symbol.Id);
                graph.Nodes[key] = new NodeDefinition(
                    symbol.Id,
                    ImpactElementKind.CodeSymbol,
                    Display(symbol),
                    symbol.FilePath);
                graph.ProjectBySymbolId[symbol.Id] = project.Name;
            }
        }

        foreach (var sqlObject in analysis.SqlObjects)
        {
            graph.Nodes[Key(ImpactElementKind.SqlObject, sqlObject.Id)] = new NodeDefinition(
                sqlObject.Id,
                ImpactElementKind.SqlObject,
                sqlObject.Name,
                sqlObject.FilePath);
        }

        foreach (var symbol in analysis.CobolSymbols)
        {
            graph.Nodes[Key(ImpactElementKind.CobolSymbol, symbol.Id)] = new NodeDefinition(
                symbol.Id,
                ImpactElementKind.CobolSymbol,
                symbol.Name,
                symbol.FilePath);
        }

        foreach (var relation in analysis.Relations)
        {
            var sourceKey = Key(ImpactElementKind.CodeSymbol, relation.SourceSymbolId);
            if (!graph.Nodes.ContainsKey(sourceKey)) continue;
            var targetKey = relation.TargetSymbolId.HasValue
                ? Key(ImpactElementKind.CodeSymbol, relation.TargetSymbolId.Value)
                : ExternalKey(relation.TargetDisplay);
            if (!graph.Nodes.ContainsKey(targetKey))
                graph.Nodes[targetKey] = new NodeDefinition(null, ImpactElementKind.External, relation.TargetDisplay, null);
            AddBidirectional(
                graph,
                sourceKey,
                targetKey,
                ForwardLabel(relation.Kind),
                ReverseLabel(relation.Kind),
                relation.Confidence);
        }

        foreach (var reference in analysis.SqlReferences)
        {
            var sourceKey = reference.SourceCodeSymbolId.HasValue
                ? Key(ImpactElementKind.CodeSymbol, reference.SourceCodeSymbolId.Value)
                : reference.SourceSqlObjectId.HasValue
                    ? Key(ImpactElementKind.SqlObject, reference.SourceSqlObjectId.Value)
                    : ExternalKey(reference.SourceDisplay);
            if (!graph.Nodes.ContainsKey(sourceKey))
                graph.Nodes[sourceKey] = new NodeDefinition(null, ImpactElementKind.External, reference.SourceDisplay, reference.FilePath);
            var targetKey = reference.TargetSqlObjectId.HasValue
                ? Key(ImpactElementKind.SqlObject, reference.TargetSqlObjectId.Value)
                : ExternalKey(reference.TargetDisplay);
            if (!graph.Nodes.ContainsKey(targetKey))
                graph.Nodes[targetKey] = new NodeDefinition(null, ImpactElementKind.External, reference.TargetDisplay, null);
            AddBidirectional(
                graph,
                sourceKey,
                targetKey,
                SqlForwardLabel(reference.Operation),
                "est référencé par",
                reference.Confidence);
        }

        foreach (var relation in analysis.CobolRelations)
        {
            var sourceKey = relation.SourceSymbolId.HasValue
                ? Key(ImpactElementKind.CobolSymbol, relation.SourceSymbolId.Value)
                : ExternalKey(relation.SourceDisplay);
            var targetKey = relation.TargetSymbolId.HasValue
                ? Key(ImpactElementKind.CobolSymbol, relation.TargetSymbolId.Value)
                : ExternalKey(relation.TargetDisplay);
            if (!graph.Nodes.ContainsKey(sourceKey)) graph.Nodes[sourceKey] = new NodeDefinition(null, ImpactElementKind.External, relation.SourceDisplay, relation.FilePath);
            if (!graph.Nodes.ContainsKey(targetKey)) graph.Nodes[targetKey] = new NodeDefinition(null, ImpactElementKind.External, relation.TargetDisplay, null);
            AddBidirectional(graph, sourceKey, targetKey,
                relation.Kind == CobolRelationKind.Calls ? "appelle" : "copie",
                relation.Kind == CobolRelationKind.Calls ? "est appelé par" : "est copié par",
                relation.Confidence);
        }

        foreach (var mapping in analysis.OrmEntityMappings.Where(mapping => mapping.CodeSymbolId.HasValue && mapping.SqlObjectId.HasValue))
        {
            var sourceKey = Key(ImpactElementKind.CodeSymbol, mapping.CodeSymbolId!.Value);
            var targetKey = Key(ImpactElementKind.SqlObject, mapping.SqlObjectId!.Value);
            if (graph.Nodes.ContainsKey(sourceKey) && graph.Nodes.ContainsKey(targetKey))
                AddBidirectional(graph, sourceKey, targetKey, "est mappé vers", "est mappée par", mapping.Confidence);
        }

        var entityMappings = analysis.OrmEntityMappings.ToDictionary(mapping => mapping.Id);
        foreach (var mapping in analysis.OrmPropertyMappings.Where(mapping => mapping.CodeSymbolId.HasValue))
        {
            if (!entityMappings.TryGetValue(mapping.OrmEntityMappingId, out var entityMapping) || !entityMapping.SqlObjectId.HasValue) continue;
            var sourceKey = Key(ImpactElementKind.CodeSymbol, mapping.CodeSymbolId!.Value);
            var targetKey = Key(ImpactElementKind.SqlObject, entityMapping.SqlObjectId.Value);
            if (graph.Nodes.ContainsKey(sourceKey) && graph.Nodes.ContainsKey(targetKey))
                AddBidirectional(graph, sourceKey, targetKey, $"mappe la colonne {mapping.ColumnName}", $"alimente la propriété {mapping.PropertyName}", mapping.Confidence);
        }

        return graph;
    }

    private static void AddBidirectional(
        Graph graph,
        string sourceKey,
        string targetKey,
        string forwardLabel,
        string reverseLabel,
        RelationConfidence confidence)
    {
        AddEdge(graph, new GraphEdge(sourceKey, targetKey, forwardLabel, confidence));
        AddEdge(graph, new GraphEdge(targetKey, sourceKey, reverseLabel, confidence));
    }

    private static void AddEdge(Graph graph, GraphEdge edge)
    {
        if (!graph.Adjacency.TryGetValue(edge.SourceKey, out var edges))
            graph.Adjacency[edge.SourceKey] = edges = new List<GraphEdge>();
        if (!edges.Any(existing => existing.TargetKey == edge.TargetKey && existing.Relationship == edge.Relationship))
            edges.Add(edge);
    }

    private static ImpactNode ToImpactNode(string key, NodeDefinition definition, VisitedNode visited) => new(
        key,
        definition.Id,
        definition.Kind,
        definition.Name,
        definition.FilePath,
        visited.Depth,
        visited.Confidence,
        visited.Relationship);

    private static IReadOnlyList<string> BuildReasons(
        int direct,
        int indirect,
        int projects,
        int tests,
        int uncertain,
        int destructiveSql,
        bool truncated)
    {
        var reasons = new List<string> { $"{direct} relation(s) directe(s) détectée(s)." };
        if (indirect > 0) reasons.Add($"{indirect} dépendance(s) indirecte(s) dans la profondeur demandée.");
        if (projects > 1) reasons.Add($"Le périmètre traverse {projects} projets.");
        if (tests > 0) reasons.Add($"{tests} fichier(s) de test potentiellement concerné(s).");
        if (destructiveSql > 0) reasons.Add($"{destructiveSql} opération(s) SQL d'écriture concernée(s).");
        if (uncertain > 0) reasons.Add($"{uncertain} résultat(s) probable(s) ou textuel(s) à vérifier manuellement.");
        if (truncated) reasons.Add($"Le graphe a été limité à {MaximumNodes} éléments pour rester lisible.");
        return reasons;
    }

    private static IReadOnlyList<ImpactPath> BuildCriticalPaths(
        string rootKey,
        IReadOnlyDictionary<string, VisitedNode> visited,
        Graph graph)
    {
        var leaves = visited
            .Where(pair => pair.Key != rootKey)
            .Where(pair => !graph.Adjacency.TryGetValue(pair.Key, out var edges) ||
                !edges.Any(edge => visited.TryGetValue(edge.TargetKey, out var target) && target.Depth == pair.Value.Depth + 1))
            .OrderByDescending(pair => pair.Value.Depth)
            .ThenBy(pair => graph.Nodes[pair.Key].Name)
            .Take(10);
        var paths = new List<ImpactPath>();
        foreach (var leaf in leaves)
        {
            var keys = new List<string>();
            string? current = leaf.Key;
            while (current is not null && visited.TryGetValue(current, out var item))
            {
                keys.Add(current);
                current = item.ParentKey;
            }
            keys.Reverse();
            paths.Add(new ImpactPath(
                keys,
                keys.Select(key => graph.Nodes[key].Name).ToList(),
                leaf.Value.Depth,
                leaf.Value.Confidence));
        }
        return paths;
    }

    private static bool IsTest(ImpactNode node, IReadOnlyDictionary<Guid, string> projects)
    {
        if (node.FilePath?.Contains("test", StringComparison.OrdinalIgnoreCase) == true) return true;
        return node.Id.HasValue && projects.TryGetValue(node.Id.Value, out var project) &&
            project.Contains("test", StringComparison.OrdinalIgnoreCase);
    }

    private static RelationConfidence Weaker(RelationConfidence left, RelationConfidence right) =>
        (RelationConfidence)Math.Max((int)left, (int)right);

    private static string Key(ImpactElementKind kind, Guid id) => kind switch
    {
        ImpactElementKind.CodeSymbol => $"code:{id:N}",
        ImpactElementKind.SqlObject => $"sql:{id:N}",
        ImpactElementKind.CobolSymbol => $"cobol:{id:N}",
        _ => $"external:{id:N}"
    };

    private static string ExternalKey(string name) => $"external:{name.Trim().ToUpperInvariant()}";
    private static string Display(CodeSymbol symbol) => string.IsNullOrWhiteSpace(symbol.Container)
        ? symbol.Name
        : $"{symbol.Container}.{symbol.Name}";

    private static string ForwardLabel(RelationKind kind) => kind switch
    {
        RelationKind.Calls => "appelle",
        RelationKind.Inherits => "hérite de",
        RelationKind.Implements => "implémente",
        _ => "instancie"
    };

    private static string ReverseLabel(RelationKind kind) => kind switch
    {
        RelationKind.Calls => "est appelé par",
        RelationKind.Inherits => "a pour type dérivé",
        RelationKind.Implements => "est implémenté par",
        _ => "est instancié par"
    };

    private static string SqlForwardLabel(SqlOperationKind operation) => operation switch
    {
        SqlOperationKind.Select => "lit",
        SqlOperationKind.Insert => "insère dans",
        SqlOperationKind.Update => "met à jour",
        SqlOperationKind.Delete => "supprime dans",
        SqlOperationKind.Execute => "exécute",
        SqlOperationKind.Join => "joint",
        _ => "référence"
    };

    private sealed class Graph
    {
        public Dictionary<string, NodeDefinition> Nodes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<GraphEdge>> Adjacency { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, string> ProjectBySymbolId { get; } = new();
    }

    private sealed record NodeDefinition(Guid? Id, ImpactElementKind Kind, string Name, string? FilePath);
    private sealed record GraphEdge(string SourceKey, string TargetKey, string Relationship, RelationConfidence Confidence);
    private sealed record VisitedNode(int Depth, RelationConfidence Confidence, string Relationship, string? ParentKey);
}
