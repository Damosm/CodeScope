using CodeScope.Domain;
using CodeScope.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Tests;

public sealed class DocumentationGeneratorTests
{
    [Fact]
    public async Task GenerateHtml_summarizes_results_and_encodes_analyzed_names()
    {
        var options = new DbContextOptionsBuilder<CodeScopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new CodeScopeDbContext(options);
        var repository = new AnalysisRepository(db);
        var pending = new Analysis { RootPath = "C:\\source\\Demo", Status = AnalysisStatus.Pending };
        await repository.AddAsync(pending, default);
        var project = new ProjectInfo
        {
            AnalysisId = pending.Id,
            Name = "<script>alert(1)</script>",
            Path = "Demo.csproj",
            TargetFramework = "net6.0",
            Symbols =
            {
                new CodeSymbol
                {
                    Kind = SymbolKind.Method,
                    Name = "Run",
                    Container = "Demo.Service",
                    FilePath = "Service.cs",
                    Line = 4,
                    LineCount = 12,
                    Complexity = 5
                }
            },
            Packages =
            {
                new PackageReferenceInfo { Name = "Example.Package", Version = "1.0.0" }
            }
        };
        var completed = new Analysis
        {
            Id = pending.Id,
            RootPath = pending.RootPath,
            Status = AnalysisStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Projects = { project },
            Endpoints =
            {
                new ApiEndpoint
                {
                    AnalysisId = pending.Id,
                    ProjectInfoId = project.Id,
                    HttpMethod = "GET",
                    Route = "/api/items",
                    HandlerDisplay = "ItemsController.Get",
                    FilePath = "ItemsController.cs",
                    Line = 8
                }
            }
        };
        await repository.CompleteAsync(completed, default);

        var generated = await new DocumentationGenerator(repository).GenerateHtmlAsync(pending.Id, default);

        Assert.NotNull(generated);
        Assert.Contains("Architecture détectée", generated!.Html);
        Assert.Contains("ItemsController.Get", generated.Html);
        Assert.Contains("Example.Package 1.0.0", generated.Html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", generated.Html);
        Assert.DoesNotContain("<script>alert(1)</script>", generated.Html);
        Assert.EndsWith(".html", generated.FileName);
    }
}
