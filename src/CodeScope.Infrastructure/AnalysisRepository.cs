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
            .AsSplitQuery()
            .Include(x => x.Projects)
            .Include(x => x.Relations)
            .Include(x => x.SqlObjects)
            .Include(x => x.SqlReferences)
            .Include(x => x.Endpoints)
            .Include(x => x.Files)
            .Include(x => x.RepositorySnapshots)
            .Include(x => x.SqlColumns)
            .Include(x => x.SqlColumnReferences)
            .Include(x => x.CobolSymbols)
            .Include(x => x.CobolRelations)
            .Include(x => x.Diagnostics)
            .SingleAsync(x => x.Id == result.Id, ct);

        if (analysis.Projects.Count > 0)
            _db.Projects.RemoveRange(analysis.Projects);
        if (analysis.Relations.Count > 0)
            _db.CodeRelations.RemoveRange(analysis.Relations);
        if (analysis.SqlObjects.Count > 0)
            _db.SqlObjects.RemoveRange(analysis.SqlObjects);
        if (analysis.SqlReferences.Count > 0)
            _db.SqlReferences.RemoveRange(analysis.SqlReferences);
        if (analysis.Endpoints.Count > 0)
            _db.ApiEndpoints.RemoveRange(analysis.Endpoints);
        if (analysis.Files.Count > 0) _db.SourceFiles.RemoveRange(analysis.Files);
        if (analysis.RepositorySnapshots.Count > 0) _db.RepositorySnapshots.RemoveRange(analysis.RepositorySnapshots);
        if (analysis.SqlColumns.Count > 0) _db.SqlColumns.RemoveRange(analysis.SqlColumns);
        if (analysis.SqlColumnReferences.Count > 0) _db.SqlColumnReferences.RemoveRange(analysis.SqlColumnReferences);
        if (analysis.CobolSymbols.Count > 0) _db.CobolSymbols.RemoveRange(analysis.CobolSymbols);
        if (analysis.CobolRelations.Count > 0) _db.CobolRelations.RemoveRange(analysis.CobolRelations);
        if (analysis.Diagnostics.Count > 0) _db.AnalysisDiagnostics.RemoveRange(analysis.Diagnostics);

        analysis.Status = AnalysisStatus.Completed;
        analysis.CompletedAt = result.CompletedAt ?? DateTimeOffset.UtcNow;
        analysis.Error = null;

        foreach (var project in result.Projects)
        {
            project.AnalysisId = analysis.Id;
            _db.Projects.Add(project);
        }

        foreach (var relation in result.Relations)
        {
            relation.AnalysisId = analysis.Id;
            _db.CodeRelations.Add(relation);
        }

        foreach (var sqlObject in result.SqlObjects)
        {
            sqlObject.AnalysisId = analysis.Id;
            _db.SqlObjects.Add(sqlObject);
        }

        foreach (var sqlReference in result.SqlReferences)
        {
            sqlReference.AnalysisId = analysis.Id;
            _db.SqlReferences.Add(sqlReference);
        }

        foreach (var endpoint in result.Endpoints)
        {
            endpoint.AnalysisId = analysis.Id;
            _db.ApiEndpoints.Add(endpoint);
        }

        foreach (var file in result.Files) { file.AnalysisId = analysis.Id; _db.SourceFiles.Add(file); }
        foreach (var snapshot in result.RepositorySnapshots) { snapshot.AnalysisId = analysis.Id; _db.RepositorySnapshots.Add(snapshot); }
        foreach (var column in result.SqlColumns) { column.AnalysisId = analysis.Id; _db.SqlColumns.Add(column); }
        foreach (var reference in result.SqlColumnReferences) { reference.AnalysisId = analysis.Id; _db.SqlColumnReferences.Add(reference); }
        foreach (var symbol in result.CobolSymbols) { symbol.AnalysisId = analysis.Id; _db.CobolSymbols.Add(symbol); }
        foreach (var relation in result.CobolRelations) { relation.AnalysisId = analysis.Id; _db.CobolRelations.Add(relation); }
        foreach (var diagnostic in result.Diagnostics) { diagnostic.AnalysisId = analysis.Id; _db.AnalysisDiagnostics.Add(diagnostic); }

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

    public Task<Analysis?> GetAsync(Guid id, CancellationToken ct) => _db.Analyses
        .AsNoTracking()
        .AsSplitQuery()
        .Include(x => x.Projects).ThenInclude(x => x.Symbols)
        .Include(x => x.Projects).ThenInclude(x => x.References)
        .Include(x => x.Projects).ThenInclude(x => x.Packages)
        .Include(x => x.Relations)
        .Include(x => x.SqlObjects)
        .Include(x => x.SqlReferences)
        .Include(x => x.Endpoints)
        .Include(x => x.Files)
        .Include(x => x.RepositorySnapshots)
        .Include(x => x.SqlColumns)
        .Include(x => x.SqlColumnReferences)
        .Include(x => x.CobolSymbols)
        .Include(x => x.CobolRelations)
        .Include(x => x.Diagnostics)
        .SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<AnalysisStatus?> GetStatusAsync(Guid id, CancellationToken ct) =>
        _db.Analyses.AsNoTracking()
            .Where(analysis => analysis.Id == id)
            .Select(analysis => (AnalysisStatus?)analysis.Status)
            .SingleOrDefaultAsync(ct);
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

    public async Task<IReadOnlyList<CodeRelation>> GetRelationsAsync(Guid id, Guid? symbolId, CancellationToken ct) =>
        await _db.CodeRelations.AsNoTracking()
            .Where(x => x.AnalysisId == id &&
                (!symbolId.HasValue || x.SourceSymbolId == symbolId || x.TargetSymbolId == symbolId))
            .OrderBy(x => x.SourceDisplay)
            .ThenBy(x => x.Kind)
            .ThenBy(x => x.TargetDisplay)
            .Take(500)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SqlObject>> SearchSqlAsync(Guid id, string query, CancellationToken ct) =>
        await _db.SqlObjects.AsNoTracking()
            .Where(x => x.AnalysisId == id && x.Name.Contains(query))
            .OrderBy(x => x.Name)
            .Take(500)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SqlReference>> GetSqlReferencesAsync(Guid id, Guid? objectId, CancellationToken ct) =>
        await _db.SqlReferences.AsNoTracking()
            .Where(x => x.AnalysisId == id && (!objectId.HasValue ||
                x.SourceSqlObjectId == objectId || x.TargetSqlObjectId == objectId))
            .OrderBy(x => x.SourceDisplay)
            .ThenBy(x => x.Operation)
            .ThenBy(x => x.TargetDisplay)
            .Take(500)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ApiEndpoint>> SearchEndpointsAsync(Guid id, string query, CancellationToken ct) =>
        await _db.ApiEndpoints.AsNoTracking()
            .Where(x => x.AnalysisId == id &&
                (x.Route.Contains(query) || x.HandlerDisplay.Contains(query) || x.HttpMethod.Contains(query)))
            .OrderBy(x => x.Route)
            .ThenBy(x => x.HttpMethod)
            .Take(500)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SourceFileInfo>> SearchFilesAsync(Guid id, string query, SourceFileCategory? category, CancellationToken ct) =>
        await _db.SourceFiles.AsNoTracking()
            .Where(x => x.AnalysisId == id && (!category.HasValue || x.Category == category) &&
                (query == "" || x.RelativePath.Contains(query)))
            .OrderBy(x => x.RelativePath).Take(2000).ToListAsync(ct);

    public async Task<IReadOnlyList<SqlColumn>> GetSqlColumnsAsync(Guid id, Guid? objectId, CancellationToken ct) =>
        await _db.SqlColumns.AsNoTracking()
            .Where(x => x.AnalysisId == id && (!objectId.HasValue || x.SqlObjectId == objectId))
            .OrderBy(x => x.SqlObjectId).ThenBy(x => x.Ordinal).Take(2000).ToListAsync(ct);

    public async Task<IReadOnlyList<SqlColumnReference>> GetSqlColumnReferencesAsync(Guid id, Guid? columnId, CancellationToken ct) =>
        await _db.SqlColumnReferences.AsNoTracking()
            .Where(x => x.AnalysisId == id && (!columnId.HasValue || x.SqlColumnId == columnId))
            .OrderBy(x => x.ObjectName).ThenBy(x => x.Line).Take(2000).ToListAsync(ct);

    public async Task<IReadOnlyList<CobolSymbol>> SearchCobolAsync(Guid id, string query, CancellationToken ct) =>
        await _db.CobolSymbols.AsNoTracking()
            .Where(x => x.AnalysisId == id && (query == "" || x.Name.Contains(query)))
            .OrderBy(x => x.Name).Take(1000).ToListAsync(ct);

    public async Task<IReadOnlyList<CobolRelation>> GetCobolRelationsAsync(Guid id, Guid? symbolId, CancellationToken ct) =>
        await _db.CobolRelations.AsNoTracking()
            .Where(x => x.AnalysisId == id && (!symbolId.HasValue || x.SourceSymbolId == symbolId || x.TargetSymbolId == symbolId))
            .OrderBy(x => x.SourceDisplay).ThenBy(x => x.TargetDisplay).Take(1000).ToListAsync(ct);

    public async Task<IReadOnlyList<AnalysisDiagnostic>> GetDiagnosticsAsync(Guid id, DiagnosticSeverity? severity, CancellationToken ct) =>
        await _db.AnalysisDiagnostics.AsNoTracking()
            .Where(x => x.AnalysisId == id && (!severity.HasValue || x.Severity == severity))
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Stage).ThenBy(x => x.FilePath).ThenBy(x => x.Line)
            .Take(2000).ToListAsync(ct);
}
