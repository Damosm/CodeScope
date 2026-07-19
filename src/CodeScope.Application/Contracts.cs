using CodeScope.Domain;

namespace CodeScope.Application;

public interface IProjectScanner
{
    Task<Analysis> ScanAsync(
        Guid analysisId,
        string rootPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IAnalysisRepository
{
    Task AddAsync(Analysis analysis, CancellationToken cancellationToken);
    Task CompleteAsync(Analysis analysis, CancellationToken cancellationToken);
    Task UpdateStatusAsync(Guid id, AnalysisStatus status, string? error, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Analysis?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<AnalysisStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Analysis>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSymbol>> SearchAsync(Guid analysisId, string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeRelation>> GetRelationsAsync(Guid analysisId, Guid? symbolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SqlObject>> SearchSqlAsync(Guid analysisId, string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<SqlReference>> GetSqlReferencesAsync(Guid analysisId, Guid? objectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiEndpoint>> SearchEndpointsAsync(Guid analysisId, string query, CancellationToken cancellationToken);
}

public interface IAnalysisJobQueue
{
    ValueTask EnqueueAsync(Guid analysisId, string rootPath, CancellationToken cancellationToken);
    ValueTask<QueuedAnalysis> DequeueAsync(CancellationToken cancellationToken);
    AnalysisJobSnapshot? Get(Guid analysisId);
    void MarkRunning(Guid analysisId);
    void Report(Guid analysisId, AnalysisProgress progress);
    void MarkFinished(Guid analysisId, AnalysisStatus status, string? error = null);
    bool TryCancel(Guid analysisId);
    void Remove(Guid analysisId);
}

public interface IImpactAnalysisService
{
    Task<ImpactReport?> AnalyzeAsync(
        Guid analysisId,
        ImpactElementKind kind,
        Guid elementId,
        int maxDepth,
        CancellationToken cancellationToken);
}

public interface IDocumentationGenerator
{
    Task<GeneratedDocumentation?> GenerateHtmlAsync(
        Guid analysisId,
        CancellationToken cancellationToken);
}

public sealed record QueuedAnalysis(Guid Id, string RootPath, CancellationToken CancellationToken);

public sealed record AnalysisProgress(
    string Stage,
    string Message,
    int ProjectsProcessed,
    int TotalProjects,
    int FilesProcessed,
    int SymbolsFound,
    int Warnings);

public sealed record AnalysisJobSnapshot(
    Guid Id,
    AnalysisStatus Status,
    string Stage,
    string Message,
    int ProjectsProcessed,
    int TotalProjects,
    int FilesProcessed,
    int SymbolsFound,
    int Warnings,
    string? Error);

public sealed record Dashboard(
    int Projects,
    int Files,
    int Classes,
    int Interfaces,
    int Methods,
    int Lines,
    int MaxComplexity,
    int Relations,
    int Calls,
    int SqlObjects,
    int SqlReferences,
    int Endpoints,
    int Packages);

public sealed record ImpactNode(
    string Key,
    Guid? Id,
    ImpactElementKind Kind,
    string Name,
    string? FilePath,
    int Depth,
    RelationConfidence Confidence,
    string Relationship);

public sealed record ImpactEdge(
    string SourceKey,
    string TargetKey,
    string Relationship,
    RelationConfidence Confidence);

public sealed record ImpactReport(
    ImpactNode Root,
    IReadOnlyList<ImpactNode> Nodes,
    IReadOnlyList<ImpactEdge> Edges,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Tests,
    ImpactRisk Risk,
    int Score,
    IReadOnlyList<string> Reasons,
    int MaxDepth,
    bool Truncated);

public sealed record GeneratedDocumentation(string FileName, string Html);
