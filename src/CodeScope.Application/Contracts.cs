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
    Task<IReadOnlyList<Analysis>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSymbol>> SearchAsync(Guid analysisId, string query, CancellationToken cancellationToken);
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

public sealed record Dashboard(int Projects, int Files, int Classes, int Interfaces, int Methods, int Lines, int MaxComplexity);
