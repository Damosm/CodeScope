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
            Assert.Contains(progressValues, x => x.Stage == "completed" && x.SymbolsFound == 4);
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
                "namespace Demo; public sealed class Repository { public string Query() { return \"SELECT Id FROM dbo.Customers\"; } }");
            await File.WriteAllTextAsync(
                Path.Combine(root, "database.sql"),
                @"CREATE TABLE [dbo].[Customers] (Id int NOT NULL);
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
