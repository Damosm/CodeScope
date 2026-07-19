using CodeScope.Application;
using CodeScope.Domain;
using CodeScope.Infrastructure;

namespace CodeScope.Tests;

public sealed class ProjectScannerTests
{
    [Fact]
    public async Task Scan_extracts_project_types_methods_and_complexity()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root, "Service.cs"), "namespace Demo; public interface IService { void Run(); } public class Service : IService { public void Run() { if (true) { } } }");
            var analysisId = Guid.NewGuid();
            var progressValues = new List<AnalysisProgress>();
            var result = await new ProjectScanner().ScanAsync(
                analysisId,
                root,
                new ImmediateProgress<AnalysisProgress>(progressValues.Add),
                default);
            Assert.Equal(analysisId, result.Id);
            Assert.Equal(AnalysisStatus.Completed, result.Status); Assert.Single(result.Projects); Assert.Contains(result.Projects[0].Symbols, x => x.Kind == SymbolKind.Interface && x.Name == "IService"); Assert.Contains(result.Projects[0].Symbols, x => x.Kind == SymbolKind.Method && x.Name == "Run" && x.Complexity == 2);
            Assert.Contains(progressValues, x => x.Stage == "completed" && x.SymbolsFound >= 4);
            Assert.Contains(result.Projects[0].Symbols, x => x.Kind == SymbolKind.Namespace && x.Name == "Demo");
            Assert.Contains(result.Files, file => file.RelativePath == "Service.cs" && file.Sha256.Length == 64 && file.LineCount == 1);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CSCOPE402" && diagnostic.Severity == DiagnosticSeverity.Info);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Scan_solution_extracts_certain_semantic_relations()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "Sample");
        Directory.CreateDirectory(projectRoot);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Sample.sln"),
                @"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Sample"", ""Sample\Sample.csproj"", ""{2E253913-669B-4BF9-A078-328643DAFB26}""
EndProject
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
        {2E253913-669B-4BF9-A078-328643DAFB26}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {2E253913-669B-4BF9-A078-328643DAFB26}.Debug|Any CPU.Build.0 = Debug|Any CPU
    EndGlobalSection
EndGlobal
");
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "Services.cs"),
                "namespace Demo; public interface IService { void Execute(); } public sealed class Service : IService { public void Execute() { } } public sealed class Caller { public void Run(IService service) { service.Execute(); var created = new Service(); } }");

            var result = await new ProjectScanner().ScanAsync(Guid.NewGuid(), root, null, default);

            Assert.Contains(result.Relations, relation =>
                relation.Kind == RelationKind.Implements &&
                relation.SourceDisplay == "Demo.Service" &&
                relation.TargetDisplay == "Demo.IService" &&
                relation.TargetSymbolId.HasValue &&
                relation.Confidence == RelationConfidence.Certain);
            Assert.Contains(result.Relations, relation =>
                relation.Kind == RelationKind.Calls &&
                relation.SourceDisplay.Contains("Caller.Run") &&
                relation.TargetDisplay.Contains("IService.Execute") &&
                relation.TargetSymbolId.HasValue);
            Assert.Contains(result.Relations, relation =>
                relation.Kind == RelationKind.Creates &&
                relation.SourceDisplay.Contains("Caller.Run") &&
                relation.TargetDisplay == "Demo.Service" &&
                relation.TargetSymbolId.HasValue);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Scan_extracts_sql_objects_operations_and_textual_code_references()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Repository.cs"),
                "namespace Demo; public sealed class Repository { public string ConnectionName { get; } = \"main\"; public string Query(int id) { return $\"SELECT Id, Name FROM dbo.Customers WHERE Id = {id}\"; } }");
            await File.WriteAllTextAsync(
                Path.Combine(root, "database.sql"),
                @"CREATE TABLE [dbo].[Customers] (Id int NOT NULL, Name nvarchar(100) NULL);
GO
CREATE OR ALTER PROCEDURE dbo.RefreshCustomers AS
BEGIN
    SELECT Id FROM dbo.Customers;
    UPDATE dbo.Customers SET Id = Id;
END;");

            var result = await new ProjectScanner().ScanAsync(Guid.NewGuid(), root, null, default);
            var table = Assert.Single(result.SqlObjects.Where(sqlObject =>
                sqlObject.Kind == SqlObjectKind.Table && sqlObject.Name == "dbo.Customers"));
            var procedure = Assert.Single(result.SqlObjects.Where(sqlObject =>
                sqlObject.Kind == SqlObjectKind.Procedure && sqlObject.Name == "dbo.RefreshCustomers"));

            Assert.Contains(result.SqlReferences, reference =>
                reference.SourceSqlObjectId == procedure.Id &&
                reference.TargetSqlObjectId == table.Id &&
                reference.Operation == SqlOperationKind.Select &&
                reference.Confidence == RelationConfidence.Certain);
            Assert.Contains(result.SqlReferences, reference =>
                reference.SourceSqlObjectId == procedure.Id &&
                reference.TargetSqlObjectId == table.Id &&
                reference.Operation == SqlOperationKind.Update);
            Assert.Contains(result.SqlReferences, reference =>
                reference.SourceCodeSymbolId.HasValue &&
                reference.TargetSqlObjectId == table.Id &&
                reference.Operation == SqlOperationKind.Select &&
                reference.Confidence == RelationConfidence.Textual);
            Assert.Contains(result.Projects[0].Symbols, symbol => symbol.Kind == SymbolKind.Property && symbol.Name == "ConnectionName" && symbol.ReturnType == "string");
            Assert.Collection(result.SqlColumns.Where(column => column.SqlObjectId == table.Id).OrderBy(column => column.Ordinal),
                id => { Assert.Equal("Id", id.Name); Assert.False(id.IsNullable); },
                name => { Assert.Equal("Name", name.Name); Assert.True(name.IsNullable); });
            Assert.Contains(result.SqlColumnReferences, reference => reference.SqlObjectId == table.Id && reference.ColumnName == "Id");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Scan_extracts_cobol_programs_calls_and_copybooks()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "MAIN.cbl"), @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. MAIN.
       PROCEDURE DIVISION.
           COPY CUSTOMER.
           CALL 'WORKER'.
           STOP RUN.");
            await File.WriteAllTextAsync(Path.Combine(root, "WORKER.cob"), @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. WORKER.
       PROCEDURE DIVISION.
           GOBACK.");
            await File.WriteAllTextAsync(Path.Combine(root, "CUSTOMER.cpy"), "       01 CUSTOMER-ID PIC 9(9).");

            var result = await new ProjectScanner().ScanAsync(Guid.NewGuid(), root, null, default);

            var main = Assert.Single(result.CobolSymbols.Where(symbol => symbol.Kind == CobolSymbolKind.Program && symbol.Name == "MAIN"));
            var worker = Assert.Single(result.CobolSymbols.Where(symbol => symbol.Kind == CobolSymbolKind.Program && symbol.Name == "WORKER"));
            var copybook = Assert.Single(result.CobolSymbols.Where(symbol => symbol.Kind == CobolSymbolKind.Copybook && symbol.Name == "CUSTOMER"));
            Assert.Contains(result.CobolRelations, relation => relation.SourceSymbolId == main.Id && relation.TargetSymbolId == worker.Id && relation.Kind == CobolRelationKind.Calls && relation.Confidence == RelationConfidence.Certain);
            Assert.Contains(result.CobolRelations, relation => relation.SourceSymbolId == main.Id && relation.TargetSymbolId == copybook.Id && relation.Kind == CobolRelationKind.Copies);
            Assert.Equal(3, result.Files.Count(file => file.Category == SourceFileCategory.Cobol));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Scan_resolves_qualified_columns_to_the_correct_sql_alias()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "aliases.sql"), @"CREATE TABLE dbo.Customers (Id int NOT NULL, Name nvarchar(100) NULL);
CREATE TABLE dbo.Orders (Id int NOT NULL, CustomerId int NOT NULL);
CREATE VIEW dbo.OrderCustomers AS
SELECT o.Id, c.Name
FROM dbo.Orders AS o
JOIN dbo.Customers c ON c.Id = o.CustomerId;");

            var result = await new ProjectScanner().ScanAsync(Guid.NewGuid(), root, null, default);
            var customers = Assert.Single(result.SqlObjects.Where(item => item.Name == "dbo.Customers"));
            var orders = Assert.Single(result.SqlObjects.Where(item => item.Name == "dbo.Orders"));

            Assert.Contains(result.SqlColumnReferences, item => item.SqlObjectId == orders.Id && item.ColumnName == "Id" && item.Operation == SqlOperationKind.Select);
            Assert.Contains(result.SqlColumnReferences, item => item.SqlObjectId == customers.Id && item.ColumnName == "Name" && item.Operation == SqlOperationKind.Select);
            Assert.DoesNotContain(result.SqlColumnReferences, item => item.SqlObjectId == customers.Id && item.ColumnName == "Id" && item.Operation == SqlOperationKind.Select);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Scan_links_ef_core_fluent_and_attribute_mappings_to_sql()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root, "Models.cs"), @"[Table(""customers"", Schema = ""dbo"")]
public class Customer {
    [Column(""customer_id"")] public int Id { get; set; }
    public string Name { get; set; }
}
public class CustomerConfiguration : IEntityTypeConfiguration<Customer> {
    public void Configure(EntityTypeBuilder<Customer> builder) {
        builder.ToTable(""customers"", ""dbo"");
        builder.Property(x => x.Name).HasColumnName(""customer_name"");
    }
}
public class StoreContext { public DbSet<Customer> Customers { get; set; } }");
            await File.WriteAllTextAsync(Path.Combine(root, "database.sql"), "CREATE TABLE dbo.customers (customer_id int NOT NULL, customer_name nvarchar(100) NULL);");

            var result = await new ProjectScanner().ScanAsync(Guid.NewGuid(), root, null, default);
            var mapping = Assert.Single(result.OrmEntityMappings);
            Assert.Equal("Customer", mapping.EntityName);
            Assert.Equal("dbo.customers", mapping.TableName);
            Assert.Equal(OrmMappingSource.FluentApi, mapping.Source);
            Assert.Equal(RelationConfidence.Certain, mapping.Confidence);
            Assert.Collection(result.OrmPropertyMappings.Where(item => item.OrmEntityMappingId == mapping.Id).OrderBy(item => item.PropertyName),
                id => { Assert.Equal("Id", id.PropertyName); Assert.Equal("customer_id", id.ColumnName); Assert.Equal(OrmMappingSource.PropertyAttribute, id.Source); },
                name => { Assert.Equal("Name", name.PropertyName); Assert.Equal("customer_name", name.ColumnName); Assert.Equal(OrmMappingSource.FluentApi, name.Source); });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Scan_extracts_packages_controller_routes_and_minimal_api_endpoints()
    {
        var root = Path.Combine(Path.GetTempPath(), "codescope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Web.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Sample.Package\" Version=\"1.2.3\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(root, "OrdersController.cs"),
                "namespace Demo; [ApiController] [Route(\"api/[controller]\")] public sealed class OrdersController { [HttpGet(\"{id}\")] public string Get(int id) => id.ToString(); }");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Program.cs"),
                "var app = WebApplication.Create(); app.MapPost(\"/orders\", () => \"ok\"); app.Run();");

            var result = await new ProjectScanner().ScanAsync(Guid.NewGuid(), root, null, default);
            var project = Assert.Single(result.Projects);
            var package = Assert.Single(project.Packages);
            Assert.Equal("Sample.Package", package.Name);
            Assert.Equal("1.2.3", package.Version);
            Assert.Contains(result.Endpoints, endpoint =>
                endpoint.HttpMethod == "GET" &&
                endpoint.Route == "/api/Orders/{id}" &&
                endpoint.HandlerDisplay == "OrdersController.Get" &&
                endpoint.Confidence == RelationConfidence.Certain);
            Assert.Contains(result.Endpoints, endpoint =>
                endpoint.HttpMethod == "POST" &&
                endpoint.Route == "/orders" &&
                endpoint.Confidence == RelationConfidence.Probable);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public ImmediateProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
