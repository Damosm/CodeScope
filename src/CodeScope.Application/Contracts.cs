using CodeScope.Domain;

namespace CodeScope.Application;

public interface IProjectScanner
{
    Task<Analysis> ScanAsync(string rootPath, CancellationToken cancellationToken);
}

public interface IAnalysisRepository
{
    Task AddAsync(Analysis analysis, CancellationToken cancellationToken);
    Task<Analysis?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Analysis>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSymbol>> SearchAsync(Guid analysisId, string query, CancellationToken cancellationToken);
}

public sealed record Dashboard(int Projects, int Files, int Classes, int Interfaces, int Methods, int Lines, int MaxComplexity);
