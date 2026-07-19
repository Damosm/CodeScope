using CodeScope.Domain;
using CodeScope.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Tests;

public sealed class ImpactAnalysisServiceTests
{
    [Fact]
    public async Task Analyze_follows_indirect_callers_and_explains_risk()
    {
        var options = new DbContextOptionsBuilder<CodeScopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new CodeScopeDbContext(options);
        var repository = new AnalysisRepository(db);
        var pending = new Analysis { RootPath = "C:\\source", Status = AnalysisStatus.Pending };
        await repository.AddAsync(pending, default);

        var entry = Method("Entry", "Entry.cs");
        var middle = Method("Middle", "Middle.cs");
        var target = Method("Target", "Target.cs");
        var completed = new Analysis
        {
            Id = pending.Id,
            RootPath = pending.RootPath,
            Status = AnalysisStatus.Completed,
            Projects =
            {
                new ProjectInfo
                {
                    AnalysisId = pending.Id,
                    Name = "Application",
                    Path = "Application.csproj",
                    Symbols = { entry, middle, target }
                }
            },
            Relations =
            {
                Call(pending.Id, entry, middle),
                Call(pending.Id, middle, target)
            }
        };
        await repository.CompleteAsync(completed, default);

        var report = await new ImpactAnalysisService(repository).AnalyzeAsync(
            pending.Id,
            ImpactElementKind.CodeSymbol,
            target.Id,
            2,
            default);

        Assert.NotNull(report);
        Assert.Contains(report!.Nodes, node => node.Id == middle.Id && node.Depth == 1 && node.Relationship == "est appelé par");
        Assert.Contains(report.Nodes, node => node.Id == entry.Id && node.Depth == 2);
        Assert.Equal(ImpactRisk.Medium, report.Risk);
        Assert.Contains(report.Reasons, reason => reason.Contains("indirecte"));
        Assert.False(report.Truncated);
    }

    private static CodeSymbol Method(string name, string filePath) => new()
    {
        Name = name,
        Container = "Demo.Service",
        Kind = SymbolKind.Method,
        FilePath = filePath,
        Line = 1,
        LineCount = 3,
        Complexity = 1
    };

    private static CodeRelation Call(Guid analysisId, CodeSymbol source, CodeSymbol target) => new()
    {
        AnalysisId = analysisId,
        SourceSymbolId = source.Id,
        TargetSymbolId = target.Id,
        SourceDisplay = $"Demo.Service.{source.Name}",
        TargetDisplay = $"Demo.Service.{target.Name}",
        Kind = RelationKind.Calls,
        Confidence = RelationConfidence.Certain,
        FilePath = source.FilePath,
        Line = source.Line
    };
}
