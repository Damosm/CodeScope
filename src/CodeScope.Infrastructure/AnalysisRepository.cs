using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Infrastructure;

public sealed class AnalysisRepository : IAnalysisRepository
{
    private readonly CodeScopeDbContext _db;
    public AnalysisRepository(CodeScopeDbContext db) => _db = db;

    public async Task AddAsync(Analysis value, CancellationToken ct)
    {
        _db.Add(value);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(Analysis result, CancellationToken ct)
    {
        var analysis = await _db.Analyses
            .Include(x => x.Projects)
            .SingleAsync(x => x.Id == result.Id, ct);

        if (analysis.Projects.Count > 0)
            _db.Projects.RemoveRange(analysis.Projects);

        analysis.Status = AnalysisStatus.Completed;
        analysis.CompletedAt = result.CompletedAt ?? DateTimeOffset.UtcNow;
        analysis.Error = null;

        foreach (var project in result.Projects)
        {
            project.AnalysisId = analysis.Id;
            _db.Projects.Add(project);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid id, AnalysisStatus status, string? error, CancellationToken ct)
    {
        var analysis = await _db.Analyses.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (analysis is null) return;

        analysis.Status = status;
        analysis.Error = error;
        if (status is AnalysisStatus.Completed or AnalysisStatus.Failed or AnalysisStatus.Cancelled)
            analysis.CompletedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var analysis = await _db.Analyses.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (analysis is null) return;
        _db.Analyses.Remove(analysis);
        await _db.SaveChangesAsync(ct);
    }

    public Task<Analysis?> GetAsync(Guid id, CancellationToken ct) => _db.Analyses.AsNoTracking().Include(x => x.Projects).ThenInclude(x => x.Symbols).Include(x => x.Projects).ThenInclude(x => x.References).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Analysis>> ListAsync(CancellationToken ct)
    {
        var analyses = await _db.Analyses.AsNoTracking().ToListAsync(ct);
        return analyses.OrderByDescending(x => x.CreatedAt).ToList();
    }
    public async Task<IReadOnlyList<CodeSymbol>> SearchAsync(Guid id, string query, CancellationToken ct) =>
        await _db.Symbols.AsNoTracking()
            .Where(x => x.Name.Contains(query) && _db.Projects.Any(p => p.Id == x.ProjectInfoId && p.AnalysisId == id))
            .OrderBy(x => x.Name)
            .Take(100)
            .ToListAsync(ct);
}
