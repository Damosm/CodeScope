using System.Xml;
using System.Xml.Linq;
using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using ProjectInfo = CodeScope.Domain.ProjectInfo;
using SymbolKind = CodeScope.Domain.SymbolKind;

namespace CodeScope.Infrastructure;

internal static class SemanticSolutionScanner
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "node_modules", "packages" };

    public static async Task<Analysis?> TryScanAsync(
        Guid analysisId,
        string rootPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var solutionPaths = EnumerateSolutions(rootPath).OrderBy(path => path.Length).ThenBy(path => path).ToList();
        if (solutionPaths.Count == 0) return null;

        var warnings = Math.Max(0, solutionPaths.Count - 1);
        var solutionPath = solutionPaths[0];
        progress?.Report(new AnalysisProgress(
            "loading",
            $"Chargement sémantique de {Path.GetFileName(solutionPath)}.",
            0,
            0,
            0,
            0,
            warnings));

        MSBuildRegistration.EnsureRegistered();
        using var workspace = MSBuildWorkspace.Create();
        workspace.LoadMetadataForReferencedProjects = true;
        workspace.WorkspaceFailed += (_, _) => Interlocked.Increment(ref warnings);

        var solution = await workspace.OpenSolutionAsync(
            solutionPath,
            progress: null,
            cancellationToken);
        var roslynProjects = solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp && !string.IsNullOrWhiteSpace(project.FilePath))
            .ToList();
        if (roslynProjects.Count == 0)
            throw new InvalidOperationException("La solution ne contient aucun projet C# chargeable.");

        var analysis = new Analysis
        {
            Id = analysisId,
            RootPath = rootPath,
            Status = AnalysisStatus.Running
        };
        var projectMap = new Dictionary<ProjectId, ProjectInfo>();
        foreach (var roslynProject in roslynProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = CreateProject(analysisId, roslynProject, ref warnings);
            analysis.Projects.Add(project);
            projectMap[roslynProject.Id] = project;
        }

        foreach (var roslynProject in roslynProjects)
        {
            var project = projectMap[roslynProject.Id];
            foreach (var reference in roslynProject.ProjectReferences)
            {
                var referencedPath = solution.GetProject(reference.ProjectId)?.FilePath;
                if (string.IsNullOrWhiteSpace(referencedPath)) continue;
                project.References.Add(new ProjectReferenceInfo
                {
                    ProjectInfoId = project.Id,
                    ReferencedPath = Path.GetFullPath(referencedPath)
                });
            }
        }

        var documents = roslynProjects
            .SelectMany(project => project.Documents.Select(document => new DocumentContext(document, projectMap[project.Id])))
            .Where(context => context.Document.SourceCodeKind == SourceCodeKind.Regular &&
                context.Document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        var symbolsByKey = new Dictionary<string, CodeSymbol>(StringComparer.Ordinal);
        var symbolDisplays = new Dictionary<Guid, string>();
        var filesProcessed = 0;
        var symbolsFound = 0;

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = documents[index];
            var root = await context.Document.GetSyntaxRootAsync(cancellationToken);
            var model = await context.Document.GetSemanticModelAsync(cancellationToken);
            if (root is null || model is null)
            {
                warnings++;
                continue;
            }

            foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(type, cancellationToken) is not INamedTypeSymbol semanticSymbol) continue;
                var codeSymbol = NewTypeSymbol(semanticSymbol, type, context.Project.Id, context.Document.FilePath!);
                context.Project.Symbols.Add(codeSymbol);
                AddSymbolKey(symbolsByKey, semanticSymbol, codeSymbol);
                symbolDisplays[codeSymbol.Id] = Display(semanticSymbol);
                symbolsFound++;
            }

            foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamespaceSymbol semanticSymbol) continue;
                var codeSymbol = New(SymbolKind.Namespace, semanticSymbol.ToDisplayString(), null, declaration, context.Document.FilePath!, context.Project.Id);
                context.Project.Symbols.Add(codeSymbol);
                AddSymbolKey(symbolsByKey, semanticSymbol, codeSymbol);
                symbolDisplays[codeSymbol.Id] = semanticSymbol.ToDisplayString();
                symbolsFound++;
            }

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method, cancellationToken) is not IMethodSymbol semanticSymbol) continue;
                var codeSymbol = NewMethodSymbol(semanticSymbol, method, context.Project.Id, context.Document.FilePath!);
                context.Project.Symbols.Add(codeSymbol);
                AddSymbolKey(symbolsByKey, semanticSymbol, codeSymbol);
                symbolDisplays[codeSymbol.Id] = Display(semanticSymbol);
                symbolsFound++;
            }

            foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(constructor, cancellationToken) is not IMethodSymbol semanticSymbol) continue;
                var codeSymbol = NewMemberSymbol(SymbolKind.Constructor, semanticSymbol, constructor, context.Project.Id, context.Document.FilePath!, null);
                context.Project.Symbols.Add(codeSymbol);
                AddSymbolKey(symbolsByKey, semanticSymbol, codeSymbol);
                symbolDisplays[codeSymbol.Id] = Display(semanticSymbol);
                symbolsFound++;
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(property, cancellationToken) is not IPropertySymbol semanticSymbol) continue;
                var codeSymbol = NewMemberSymbol(SymbolKind.Property, semanticSymbol, property, context.Project.Id, context.Document.FilePath!, semanticSymbol.Type);
                context.Project.Symbols.Add(codeSymbol);
                AddSymbolKey(symbolsByKey, semanticSymbol, codeSymbol);
                symbolDisplays[codeSymbol.Id] = Display(semanticSymbol);
                symbolsFound++;
            }

            foreach (var variable in root.DescendantNodes().OfType<FieldDeclarationSyntax>().SelectMany(field => field.Declaration.Variables))
            {
                if (model.GetDeclaredSymbol(variable, cancellationToken) is not IFieldSymbol semanticSymbol) continue;
                var codeSymbol = NewMemberSymbol(SymbolKind.Field, semanticSymbol, variable, context.Project.Id, context.Document.FilePath!, semanticSymbol.Type);
                context.Project.Symbols.Add(codeSymbol);
                AddSymbolKey(symbolsByKey, semanticSymbol, codeSymbol);
                symbolDisplays[codeSymbol.Id] = Display(semanticSymbol);
                symbolsFound++;
            }

            filesProcessed++;
            progress?.Report(new AnalysisProgress(
                "symbols",
                $"{context.Project.Name} : symboles du fichier {index + 1}/{documents.Count}.",
                roslynProjects.FindIndex(project => project.Id == context.Document.Project.Id),
                roslynProjects.Count,
                filesProcessed,
                symbolsFound,
                warnings));
        }

        var relationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await context.Document.GetSyntaxRootAsync(cancellationToken);
            var model = await context.Document.GetSemanticModelAsync(cancellationToken);
            if (root is null || model is null) continue;

            ExtractTypeRelations(
                analysis,
                root,
                model,
                symbolsByKey,
                symbolDisplays,
                relationKeys,
                context.Document.FilePath!,
                cancellationToken);
            ExtractMethodRelations(
                analysis,
                root,
                model,
                symbolsByKey,
                symbolDisplays,
                relationKeys,
                context.Document.FilePath!,
                cancellationToken);
        }

        analysis.Status = AnalysisStatus.Completed;
        analysis.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(new AnalysisProgress(
            "completed",
            $"Analyse sémantique terminée : {analysis.Relations.Count} relation(s).",
            roslynProjects.Count,
            roslynProjects.Count,
            filesProcessed,
            symbolsFound,
            warnings));
        return analysis;
    }

    private static void ExtractTypeRelations(
        Analysis analysis,
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyDictionary<string, CodeSymbol> symbolsByKey,
        IReadOnlyDictionary<Guid, string> symbolDisplays,
        ISet<string> relationKeys,
        string filePath,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol sourceSemantic ||
                !TryFindSymbol(symbolsByKey, sourceSemantic, out var source))
                continue;

            if (sourceSemantic.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
                AddRelation(analysis, source!, symbolDisplays[source!.Id], baseType, RelationKind.Inherits,
                    declaration, filePath, symbolsByKey, relationKeys);

            foreach (var implementedInterface in sourceSemantic.Interfaces)
            {
                var kind = sourceSemantic.TypeKind == TypeKind.Interface
                    ? RelationKind.Inherits
                    : RelationKind.Implements;
                AddRelation(analysis, source!, symbolDisplays[source!.Id], implementedInterface, kind,
                    declaration, filePath, symbolsByKey, relationKeys);
            }
        }
    }

    private static void ExtractMethodRelations(
        Analysis analysis,
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyDictionary<string, CodeSymbol> symbolsByKey,
        IReadOnlyDictionary<Guid, string> symbolDisplays,
        ISet<string> relationKeys,
        string filePath,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol sourceSemantic ||
                !TryFindSymbol(symbolsByKey, sourceSemantic, out var source))
                continue;

            var sourceDisplay = symbolDisplays[source!.Id];
            foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var target = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (target is null) continue;
                AddRelation(analysis, source!, sourceDisplay, target.ReducedFrom ?? target, RelationKind.Calls,
                    invocation, filePath, symbolsByKey, relationKeys);
            }

            foreach (var creation in declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var constructor = model.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
                var createdType = constructor?.ContainingType ??
                    model.GetTypeInfo(creation, cancellationToken).Type as INamedTypeSymbol;
                if (createdType is null) continue;
                AddRelation(analysis, source!, sourceDisplay, createdType, RelationKind.Creates,
                    creation, filePath, symbolsByKey, relationKeys);
            }
        }
    }

    private static void AddRelation(
        Analysis analysis,
        CodeSymbol source,
        string sourceDisplay,
        ISymbol targetSemantic,
        RelationKind kind,
        SyntaxNode location,
        string filePath,
        IReadOnlyDictionary<string, CodeSymbol> symbolsByKey,
        ISet<string> relationKeys)
    {
        TryFindSymbol(symbolsByKey, targetSemantic, out var target);
        var targetDisplay = Display(targetSemantic);
        var relationKey = $"{source.Id:N}|{kind}|{target?.Id.ToString("N") ?? targetDisplay}";
        if (!relationKeys.Add(relationKey)) return;

        var line = location.SyntaxTree.GetLineSpan(location.Span).StartLinePosition.Line + 1;
        analysis.Relations.Add(new CodeRelation
        {
            AnalysisId = analysis.Id,
            SourceSymbolId = source.Id,
            TargetSymbolId = target?.Id,
            SourceDisplay = sourceDisplay,
            TargetDisplay = targetDisplay,
            Kind = kind,
            Confidence = RelationConfidence.Certain,
            FilePath = filePath,
            Line = line
        });
    }

    private static ProjectInfo CreateProject(Guid analysisId, Microsoft.CodeAnalysis.Project project, ref int warnings)
    {
        var result = new ProjectInfo
        {
            AnalysisId = analysisId,
            Name = project.Name,
            Path = Path.GetFullPath(project.FilePath!)
        };

        try
        {
            var document = XDocument.Load(result.Path, LoadOptions.None);
            result.TargetFramework = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")?.Value;
            ProjectFileMetadata.AddPackages(result, document);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            warnings++;
        }

        return result;
    }

    private static CodeSymbol NewTypeSymbol(
        INamedTypeSymbol semanticSymbol,
        BaseTypeDeclarationSyntax syntax,
        Guid projectId,
        string filePath)
    {
        var kind = syntax switch
        {
            InterfaceDeclarationSyntax => SymbolKind.Interface,
            RecordDeclarationSyntax => SymbolKind.Record,
            EnumDeclarationSyntax => SymbolKind.Enum,
            _ => SymbolKind.Class
        };
        return New(kind, semanticSymbol.Name, semanticSymbol.ContainingNamespace?.ToDisplayString(), syntax, filePath, projectId);
    }

    private static CodeSymbol NewMethodSymbol(
        IMethodSymbol semanticSymbol,
        MethodDeclarationSyntax syntax,
        Guid projectId,
        string filePath)
    {
        var result = New(
            SymbolKind.Method,
            semanticSymbol.Name,
            semanticSymbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            syntax,
            filePath,
            projectId);
        result.ReturnType = semanticSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        result.Complexity = CalculateComplexity(syntax);
        return result;
    }

    private static CodeSymbol NewMemberSymbol(
        SymbolKind kind,
        ISymbol semanticSymbol,
        SyntaxNode syntax,
        Guid projectId,
        string filePath,
        ITypeSymbol? valueType)
    {
        var result = New(
            kind,
            semanticSymbol.Name,
            semanticSymbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            syntax,
            filePath,
            projectId);
        result.ReturnType = valueType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return result;
    }

    private static CodeSymbol New(
        SymbolKind kind,
        string name,
        string? container,
        SyntaxNode node,
        string path,
        Guid projectId)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        return new CodeSymbol
        {
            ProjectInfoId = projectId,
            Kind = kind,
            Name = name,
            Container = container,
            FilePath = path,
            Line = span.StartLinePosition.Line + 1,
            LineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1
        };
    }

    private static int CalculateComplexity(MethodDeclarationSyntax method) =>
        1 + method.DescendantNodes().Count(node =>
            node is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or
                CaseSwitchLabelSyntax or ConditionalExpressionSyntax ||
            node is BinaryExpressionSyntax binary &&
                (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression)));

    private static void AddSymbolKey(
        IDictionary<string, CodeSymbol> symbolsByKey,
        ISymbol semanticSymbol,
        CodeSymbol codeSymbol)
    {
        var key = SymbolKey(semanticSymbol);
        if (key is not null) symbolsByKey[key] = codeSymbol;
    }

    private static bool TryFindSymbol(
        IReadOnlyDictionary<string, CodeSymbol> symbolsByKey,
        ISymbol semanticSymbol,
        out CodeSymbol? codeSymbol)
    {
        var key = SymbolKey(semanticSymbol);
        if (key is not null && symbolsByKey.TryGetValue(key, out var found))
        {
            codeSymbol = found;
            return true;
        }

        codeSymbol = null;
        return false;
    }

    private static string? SymbolKey(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => (method.ReducedFrom ?? method).OriginalDefinition.GetDocumentationCommentId(),
        INamedTypeSymbol type => type.OriginalDefinition.GetDocumentationCommentId(),
        _ => symbol.OriginalDefinition.GetDocumentationCommentId()
    };

    private static string Display(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static IEnumerable<string> EnumerateSolutions(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files) yield return file;

            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (Excluded.Contains(Path.GetFileName(child))) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Le dossier est ignoré ; MSBuild signalera les projets manquants si nécessaire.
                }
            }
        }
    }

    private sealed record DocumentContext(Document Document, ProjectInfo Project);
}
