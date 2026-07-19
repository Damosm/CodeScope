using System.Text.Json;
using CodeScope.Domain;
using CodeScope.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Tests;

public sealed class AdvancedFeatureTests
{
    [Fact]
    public async Task Comparison_uses_file_hashes_and_reports_incremental_changes()
    {
        await using var db = CreateDatabase();
        var repository = new AnalysisRepository(db);
        var from = await SaveAsync(repository, "abc", "old.cs");
        var to = await SaveAsync(repository, "def", "new.cs");

        var comparison = await new AnalysisComparisonService(repository).CompareAsync(from.Id, to.Id, default);

        Assert.NotNull(comparison);
        Assert.Contains(comparison!.Modified, item => item.Kind == "File" && item.Key == "shared.cs");
        var rename = Assert.Single(comparison.Renamed);
        Assert.Equal("old.cs", rename.FromPath);
        Assert.Equal("new.cs", rename.ToPath);
        Assert.DoesNotContain(comparison.Added, item => item.Kind == "Symbol");
        Assert.DoesNotContain(comparison.Removed, item => item.Kind == "Symbol");
        Assert.Equal("from-commit", comparison.FromCommit);
        Assert.Equal("to-commit", comparison.ToCommit);
    }

    [Fact]
    public async Task Graph_pdf_and_sarif_exports_are_well_formed()
    {
        await using var db = CreateDatabase();
        var repository = new AnalysisRepository(db);
        var pending = new Analysis { RootPath = "C:\\sample", Status = AnalysisStatus.Pending };
        await repository.AddAsync(pending, default);
        var caller = new CodeSymbol { Kind = SymbolKind.Method, Name = "Run", Container = "Sample.Service", FilePath = "C:\\sample\\Service.cs", Line = 3, Complexity = 18 };
        var target = new CodeSymbol { Kind = SymbolKind.Method, Name = "Save", Container = "Sample.Repository", FilePath = "C:\\sample\\Repository.cs", Line = 4 };
        var completed = new Analysis
        {
            Id = pending.Id,
            RootPath = pending.RootPath,
            Status = AnalysisStatus.Completed,
            Projects = { new ProjectInfo { AnalysisId = pending.Id, Name = "Sample", Path = "C:\\sample\\Sample.csproj", Symbols = { caller, target } } },
            Relations = { new CodeRelation { AnalysisId = pending.Id, SourceSymbolId = caller.Id, TargetSymbolId = target.Id, SourceDisplay = "Sample.Service.Run", TargetDisplay = "Sample.Repository.Save", Kind = RelationKind.Calls, FilePath = caller.FilePath, Line = caller.Line } },
            CobolSymbols = { new CobolSymbol { AnalysisId = pending.Id, Kind = CobolSymbolKind.Program, Name = "MAIN", FilePath = "C:\\sample\\MAIN.cbl", Line = 2 } },
            CobolRelations = { new CobolRelation { AnalysisId = pending.Id, SourceDisplay = "MAIN", TargetDisplay = "MISSING", Kind = CobolRelationKind.Calls, Confidence = RelationConfidence.Probable, FilePath = "C:\\sample\\MAIN.cbl", Line = 8 } }
        };
        await repository.CompleteAsync(completed, default);

        var graph = await new DependencyGraphService(repository).BuildAsync(pending.Id, "types", 100, default);
        var impact = await new ImpactAnalysisService(repository).AnalyzeAsync(pending.Id, ImpactElementKind.CodeSymbol, caller.Id, 3, default);
        var exports = new AnalysisExportService(repository);
        var pdf = await exports.GeneratePdfAsync(pending.Id, default);
        var sarif = await exports.GenerateSarifAsync(pending.Id, default);

        Assert.NotNull(graph);
        Assert.Equal(2, graph!.Nodes.Count);
        Assert.Single(graph.Edges);
        Assert.NotNull(impact);
        Assert.Contains(impact!.CriticalPaths, path => path.Names.SequenceEqual(new[] { "Sample.Service.Run", "Sample.Repository.Save" }));
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(pdf!.Content, 0, 8));
        Assert.EndsWith("%%EOF\n", System.Text.Encoding.ASCII.GetString(pdf.Content));
        Assert.NotNull(sarif);
        using var document = JsonDocument.Parse(sarif!.Content);
        Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Contains(results.EnumerateArray(), result => result.GetProperty("ruleId").GetString() == "CSCOPE001");
        Assert.Contains(results.EnumerateArray(), result => result.GetProperty("ruleId").GetString() == "CSCOPE003");
    }

    [Fact]
    public async Task Orm_graph_and_impact_bridge_csharp_properties_to_sql_columns()
    {
        await using var db = CreateDatabase();
        var repository = new AnalysisRepository(db);
        var pending = new Analysis { RootPath = "C:\\sample", Status = AnalysisStatus.Pending };
        await repository.AddAsync(pending, default);
        var entity = new CodeSymbol { Kind = SymbolKind.Class, Name = "Customer", FilePath = "C:\\sample\\Customer.cs", Line = 1 };
        var property = new CodeSymbol { Kind = SymbolKind.Property, Name = "Id", Container = "Customer", FilePath = entity.FilePath, Line = 2, ReturnType = "int" };
        var table = new SqlObject { AnalysisId = pending.Id, Kind = SqlObjectKind.Table, Name = "dbo.Customers", FilePath = "C:\\sample\\database.sql", Line = 1 };
        var column = new SqlColumn { AnalysisId = pending.Id, SqlObjectId = table.Id, Name = "customer_id", DataType = "int", Ordinal = 1, FilePath = table.FilePath, Line = 1 };
        var entityMapping = new OrmEntityMapping { AnalysisId = pending.Id, CodeSymbolId = entity.Id, SqlObjectId = table.Id, EntityName = "Customer", TableName = table.Name, Source = OrmMappingSource.TableAttribute, Confidence = RelationConfidence.Certain, FilePath = entity.FilePath, Line = 1 };
        var propertyMapping = new OrmPropertyMapping { AnalysisId = pending.Id, OrmEntityMappingId = entityMapping.Id, CodeSymbolId = property.Id, SqlColumnId = column.Id, PropertyName = "Id", ColumnName = column.Name, Source = OrmMappingSource.PropertyAttribute, Confidence = RelationConfidence.Certain, FilePath = property.FilePath, Line = 2 };
        var completed = new Analysis
        {
            Id = pending.Id, RootPath = pending.RootPath, Status = AnalysisStatus.Completed,
            Projects = { new ProjectInfo { AnalysisId = pending.Id, Name = "Sample", Path = "C:\\sample\\Sample.csproj", Symbols = { entity, property } } },
            SqlObjects = { table }, SqlColumns = { column }, OrmEntityMappings = { entityMapping }, OrmPropertyMappings = { propertyMapping }
        };
        await repository.CompleteAsync(completed, default);

        var graph = await new DependencyGraphService(repository).BuildAsync(pending.Id, "orm", 100, default);
        var impact = await new ImpactAnalysisService(repository).AnalyzeAsync(pending.Id, ImpactElementKind.CodeSymbol, property.Id, 2, default);

        Assert.NotNull(graph);
        Assert.Equal(4, graph!.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);
        Assert.NotNull(impact);
        Assert.Contains(impact!.Nodes, node => node.Kind == ImpactElementKind.SqlObject && node.Name == table.Name && node.Relationship.Contains(column.Name));
    }

    private static CodeScopeDbContext CreateDatabase() => new(new DbContextOptionsBuilder<CodeScopeDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<Analysis> SaveAsync(AnalysisRepository repository, string hash, string symbolFile)
    {
        var pending = new Analysis { RootPath = "C:\\sample", Status = AnalysisStatus.Pending };
        await repository.AddAsync(pending, default);
        var symbol = new CodeSymbol { Kind = SymbolKind.Class, Name = "Value", FilePath = $"C:\\sample\\{symbolFile}", Line = 1 };
        var completed = new Analysis
        {
            Id = pending.Id,
            RootPath = pending.RootPath,
            Status = AnalysisStatus.Completed,
            Projects = { new ProjectInfo { AnalysisId = pending.Id, Name = "Sample", Path = "C:\\sample\\Sample.csproj", Symbols = { symbol } } },
            Files =
            {
                new SourceFileInfo { AnalysisId = pending.Id, RelativePath = "shared.cs", FullPath = "C:\\sample\\shared.cs", Extension = ".cs", Category = SourceFileCategory.SourceCode, Sha256 = hash },
                new SourceFileInfo { AnalysisId = pending.Id, RelativePath = symbolFile, FullPath = $"C:\\sample\\{symbolFile}", Extension = ".cs", Category = SourceFileCategory.SourceCode, Sha256 = "stable-symbol" }
            },
            RepositorySnapshots = { new RepositorySnapshot { AnalysisId = pending.Id, IsGitRepository = true, CommitHash = hash == "abc" ? "from-commit" : "to-commit" } }
        };
        await repository.CompleteAsync(completed, default);
        return completed;
    }
}
