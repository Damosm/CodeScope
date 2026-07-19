using System.Xml.Linq;
using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace CodeScope.Infrastructure;

public sealed class ProjectScanner : IProjectScanner
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules", "packages" };

    public async Task<Analysis> ScanAsync(string rootPath, CancellationToken ct)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException("Le dossier indiqué n'existe pas.");
        var analysis = new Analysis { RootPath = fullRoot, Status = AnalysisStatus.Running };
        foreach (var projectPath in Enumerate(fullRoot, "*.csproj"))
        {
            ct.ThrowIfCancellationRequested();
            var doc = XDocument.Load(projectPath, LoadOptions.None);
            var project = new ProjectInfo { AnalysisId = analysis.Id, Name = Path.GetFileNameWithoutExtension(projectPath), Path = projectPath, TargetFramework = doc.Descendants().FirstOrDefault(x => x.Name.LocalName is "TargetFramework" or "TargetFrameworks")?.Value };
            foreach (var reference in doc.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
                project.References.Add(new ProjectReferenceInfo { ReferencedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, reference.Attribute("Include")?.Value ?? "")) });
            foreach (var file in Enumerate(Path.GetDirectoryName(projectPath)!, "*.cs"))
            {
                ct.ThrowIfCancellationRequested();
                try { project.Symbols.AddRange(await ParseAsync(file, project.Id, ct)); } catch (IOException) { }
            }
            analysis.Projects.Add(project);
        }
        analysis.Status = AnalysisStatus.Completed; analysis.CompletedAt = DateTimeOffset.UtcNow;
        return analysis;
    }

    private static IEnumerable<string> Enumerate(string root, string pattern) => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(p => !p.Split(Path.DirectorySeparatorChar).Any(Excluded.Contains));

    private static async Task<IEnumerable<CodeSymbol>> ParseAsync(string path, Guid projectId, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        var root = await CSharpSyntaxTree.ParseText(text, cancellationToken: ct).GetRootAsync(ct);
        var result = new List<CodeSymbol>();
        foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var kind = type switch { InterfaceDeclarationSyntax => SymbolKind.Interface, RecordDeclarationSyntax => SymbolKind.Record, EnumDeclarationSyntax => SymbolKind.Enum, _ => SymbolKind.Class };
            result.Add(New(kind, type.Identifier.Text, null, type, path, projectId));
        }
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = New(SymbolKind.Method, method.Identifier.Text, method.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text, method, path, projectId);
            symbol.ReturnType = method.ReturnType.ToString();
            symbol.Complexity = 1 + method.DescendantNodes().Count(n =>
                n is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or CaseSwitchLabelSyntax or ConditionalExpressionSyntax
                || n is BinaryExpressionSyntax b &&
                   (b.RawKind == (int)SyntaxKind.LogicalAndExpression || b.RawKind == (int)SyntaxKind.LogicalOrExpression));
            result.Add(symbol);
        }
        return result;
    }

    private static CodeSymbol New(SymbolKind kind, string name, string? container, SyntaxNode node, string path, Guid projectId)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        return new CodeSymbol { ProjectInfoId = projectId, Kind = kind, Name = name, Container = container, FilePath = path, Line = span.StartLinePosition.Line + 1, LineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1 };
    }
}
