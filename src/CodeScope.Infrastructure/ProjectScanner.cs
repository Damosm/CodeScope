using System.Xml.Linq;
using System.Xml;
using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace CodeScope.Infrastructure;

public sealed class ProjectScanner : IProjectScanner
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules", "packages" };

    public async Task<Analysis> ScanAsync(
        Guid analysisId,
        string rootPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken ct)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException("Le dossier indiqué n'existe pas.");

        Analysis? semanticResult = null;
        string? fallbackReason = null;
        try
        {
            semanticResult = await SemanticSolutionScanner.TryScanAsync(
                analysisId,
                fullRoot,
                progress,
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or AggregateException)
        {
            fallbackReason = exception.GetType().Name;
            progress?.Report(new AnalysisProgress(
                "fallback",
                $"Chargement sémantique indisponible ({exception.GetType().Name}) ; analyse syntaxique de secours.",
                0,
                0,
                0,
                0,
                1));
        }

        if (semanticResult is not null)
        {
            await ApiEndpointAnalyzer.AnalyzeAsync(semanticResult, progress, ct);
            await SqlScriptAnalyzer.AnalyzeAsync(semanticResult, fullRoot, progress, ct);
            await EfCoreMappingAnalyzer.AnalyzeAsync(semanticResult, progress, ct);
            await CobolSourceAnalyzer.AnalyzeAsync(semanticResult, fullRoot, progress, ct);
            await FileInventoryAnalyzer.AnalyzeAsync(semanticResult, fullRoot, progress, ct);
            return semanticResult;
        }

        var analysis = new Analysis { Id = analysisId, RootPath = fullRoot, Status = AnalysisStatus.Running };
        if (fallbackReason is not null)
            DiagnosticReporter.Warning(analysis, "CSCOPE101", "semantic", $"Analyse sémantique indisponible ({fallbackReason}) ; le mode syntaxique a été utilisé.");
        var filesProcessed = 0;
        var symbolsFound = 0;
        var warnings = 0;

        progress?.Report(new AnalysisProgress("discovering", "Recherche des projets .NET.", 0, 0, 0, 0, 0));
        var projectPaths = EnumerateSafely(fullRoot, "*.csproj", () => warnings++).ToList();

        for (var projectIndex = 0; projectIndex < projectPaths.Count; projectIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var projectPath = projectPaths[projectIndex];
            progress?.Report(new AnalysisProgress(
                "projects",
                $"Analyse de {Path.GetFileName(projectPath)}.",
                projectIndex,
                projectPaths.Count,
                filesProcessed,
                symbolsFound,
                warnings));

            XDocument document;
            try
            {
                document = XDocument.Load(projectPath, LoadOptions.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
            {
                warnings++;
                DiagnosticReporter.Warning(analysis, "CSCOPE102", "projects", "Le fichier projet n'a pas pu être lu.", projectPath);
                continue;
            }

            var project = CreateProject(analysis.Id, projectPath, document, ref warnings);
            foreach (var file in EnumerateSafely(Path.GetDirectoryName(projectPath)!, "*.cs", () => warnings++))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var symbols = (await ParseAsync(file, project.Id, ct)).ToList();
                    project.Symbols.AddRange(symbols);
                    symbolsFound += symbols.Count;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings++;
                    DiagnosticReporter.Warning(analysis, "CSCOPE103", "symbols", "Le fichier source n'a pas pu être lu.", file);
                }

                filesProcessed++;
                progress?.Report(new AnalysisProgress(
                    "files",
                    $"{project.Name} : {filesProcessed} fichier(s) traité(s).",
                    projectIndex,
                    projectPaths.Count,
                    filesProcessed,
                    symbolsFound,
                    warnings));
            }

            analysis.Projects.Add(project);
            progress?.Report(new AnalysisProgress(
                "projects",
                $"Projet {project.Name} terminé.",
                projectIndex + 1,
                projectPaths.Count,
                filesProcessed,
                symbolsFound,
                warnings));
        }

        await ApiEndpointAnalyzer.AnalyzeAsync(analysis, progress, ct);
        await SqlScriptAnalyzer.AnalyzeAsync(analysis, fullRoot, progress, ct);
        await EfCoreMappingAnalyzer.AnalyzeAsync(analysis, progress, ct);
        await CobolSourceAnalyzer.AnalyzeAsync(analysis, fullRoot, progress, ct);
        await FileInventoryAnalyzer.AnalyzeAsync(analysis, fullRoot, progress, ct);
        analysis.Status = AnalysisStatus.Completed;
        analysis.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(new AnalysisProgress(
            "completed",
            "Analyse terminée.",
            projectPaths.Count,
            projectPaths.Count,
            filesProcessed,
            symbolsFound,
            warnings));
        return analysis;
    }

    private static ProjectInfo CreateProject(Guid analysisId, string path, XDocument document, ref int warnings)
    {
        var project = new ProjectInfo
        {
            AnalysisId = analysisId,
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            TargetFramework = document.Descendants()
                .FirstOrDefault(x => x.Name.LocalName is "TargetFramework" or "TargetFrameworks")?.Value
        };
        ProjectFileMetadata.AddPackages(project, document);

        foreach (var reference in document.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
        {
            try
            {
                var relativePath = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(relativePath)) continue;
                project.References.Add(new ProjectReferenceInfo
                {
                    ReferencedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, relativePath))
                });
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warnings++;
            }
        }

        return project;
    }

    private static IEnumerable<string> EnumerateSafely(string root, string pattern, Action onWarning)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, pattern); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                onWarning();
                continue;
            }

            using (var enumerator = files.GetEnumerator())
            {
                while (true)
                {
                    string file;
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                        file = enumerator.Current;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        onWarning();
                        break;
                    }
                    yield return file;
                }
            }

            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                onWarning();
                continue;
            }

            foreach (var child in directories)
            {
                if (Excluded.Contains(Path.GetFileName(child))) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    onWarning();
                }
            }
        }
    }

    private static async Task<IEnumerable<CodeSymbol>> ParseAsync(string path, Guid projectId, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        var root = await CSharpSyntaxTree.ParseText(text, cancellationToken: ct).GetRootAsync(ct);
        var result = new List<CodeSymbol>();
        foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            result.Add(New(SymbolKind.Namespace, declaration.Name.ToString(), null, declaration, path, projectId));
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
        foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            result.Add(New(SymbolKind.Constructor, constructor.Identifier.Text, constructor.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text, constructor, path, projectId));
        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var symbol = New(SymbolKind.Property, property.Identifier.Text, property.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text, property, path, projectId);
            symbol.ReturnType = property.Type.ToString();
            result.Add(symbol);
        }
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        foreach (var variable in field.Declaration.Variables)
        {
            var symbol = New(SymbolKind.Field, variable.Identifier.Text, field.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text, variable, path, projectId);
            symbol.ReturnType = field.Declaration.Type.ToString();
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
