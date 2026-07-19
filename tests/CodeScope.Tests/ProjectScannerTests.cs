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

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public ImmediateProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
