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
                    Symbols = { new CodeSymbol { Name = "Service", Kind = SymbolKind.Class, FilePath = "Service.cs" } }
                }
            }
        };

        await repository.CompleteAsync(completed, default);
        var saved = await repository.GetAsync(pending.Id, default);
        Assert.NotNull(saved);
        Assert.Equal(AnalysisStatus.Completed, saved!.Status);
        Assert.Single(saved.Projects);
        Assert.Single(saved.Projects[0].Symbols);

        await repository.DeleteAsync(pending.Id, default);
        Assert.Null(await repository.GetAsync(pending.Id, default));
    }
}
