using CodeScope.Domain;
using CodeScope.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Tests;

public sealed class AnalysisRepositoryTests
{
    [Fact]
    public async Task Complete_persists_results_and_delete_cascades()
    {
        var options = new DbContextOptionsBuilder<CodeScopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new CodeScopeDbContext(options);
        var repository = new AnalysisRepository(db);
        var pending = new Analysis { RootPath = "C:\\source", Status = AnalysisStatus.Pending };
        await repository.AddAsync(pending, default);
        var serviceSymbol = new CodeSymbol
        {
            Name = "Service",
            Kind = SymbolKind.Class,
            FilePath = "Service.cs"
        };

        var completed = new Analysis
        {
            Id = pending.Id,
            RootPath = pending.RootPath,
            Status = AnalysisStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Projects =
            {
                new ProjectInfo
                {
                    AnalysisId = pending.Id,
                    Name = "Sample",
                    Path = "C:\\source\\Sample.csproj",
                    Symbols = { serviceSymbol }
                }
            },
            Relations =
            {
                new CodeRelation
                {
                    AnalysisId = pending.Id,
                    SourceSymbolId = serviceSymbol.Id,
                    SourceDisplay = "Demo.Service",
                    TargetDisplay = "Demo.IService",
                    Kind = RelationKind.Implements,
                    FilePath = "Service.cs",
                    Line = 3
                }
            },
            Diagnostics =
            {
                new AnalysisDiagnostic { AnalysisId = pending.Id, Code = "CSCOPE999", Stage = "test", Message = "Diagnostic de test." }
            }
        };

        await repository.CompleteAsync(completed, default);
        var saved = await repository.GetAsync(pending.Id, default);
        Assert.NotNull(saved);
        Assert.Equal(AnalysisStatus.Completed, saved!.Status);
        Assert.Single(saved.Projects);
        Assert.Single(saved.Projects[0].Symbols);
        Assert.Single(saved.Relations);
        Assert.Single(saved.Diagnostics);
        Assert.Single(await repository.GetDiagnosticsAsync(pending.Id, DiagnosticSeverity.Warning, default));
        Assert.Single(await repository.GetRelationsAsync(pending.Id, serviceSymbol.Id, default));

        await repository.DeleteAsync(pending.Id, default);
        Assert.Null(await repository.GetAsync(pending.Id, default));
    }
}
