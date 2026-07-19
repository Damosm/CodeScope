using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProjectInfo = CodeScope.Domain.ProjectInfo;
using SymbolKind = CodeScope.Domain.SymbolKind;

namespace CodeScope.Infrastructure;

internal static class ApiEndpointAnalyzer
{
    private static readonly IReadOnlyDictionary<string, string> HttpAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HttpGet"] = "GET",
            ["HttpPost"] = "POST",
            ["HttpPut"] = "PUT",
            ["HttpDelete"] = "DELETE",
            ["HttpPatch"] = "PATCH",
            ["HttpHead"] = "HEAD",
            ["HttpOptions"] = "OPTIONS"
        };

    private static readonly IReadOnlyDictionary<string, string> MinimalApiMethods =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MapGet"] = "GET",
            ["MapPost"] = "POST",
            ["MapPut"] = "PUT",
            ["MapDelete"] = "DELETE",
            ["MapPatch"] = "PATCH",
            ["MapMethods"] = "MULTI"
        };

    public static async Task AnalyzeAsync(
        Analysis analysis,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesByPath = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in analysis.Projects)
        {
            foreach (var symbolFile in project.Symbols.Select(symbol => symbol.FilePath))
                filesByPath.TryAdd(symbolFile, project);
            foreach (var sourceFile in EnumerateProjectFiles(Path.GetDirectoryName(project.Path)!))
                filesByPath.TryAdd(sourceFile, project);
        }
        var files = filesByPath.Select(pair => new EndpointFile(pair.Key, pair.Value)).ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try { text = await File.ReadAllTextAsync(file.Path, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DiagnosticReporter.Warning(analysis, "CSCOPE104", "endpoints", "Le fichier n'a pas pu être inspecté pour les endpoints.", file.Path);
                continue;
            }

            var root = await CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken)
                .GetRootAsync(cancellationToken);
            var symbols = file.Project.Symbols
                .Where(symbol => string.Equals(symbol.FilePath, file.Path, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ExtractControllerEndpoints(analysis, file.Project, symbols, root, file.Path, keys);
            ExtractMinimalApiEndpoints(analysis, file.Project, symbols, root, file.Path, keys);
        }

        progress?.Report(new AnalysisProgress(
            "endpoints",
            $"{analysis.Endpoints.Count} endpoint(s) ASP.NET détecté(s).",
            analysis.Projects.Count,
            analysis.Projects.Count,
            files.Count,
            analysis.Projects.SelectMany(project => project.Symbols).Count(),
            0));
    }

    private static void ExtractControllerEndpoints(
        Analysis analysis,
        ProjectInfo project,
        IReadOnlyCollection<CodeSymbol> symbols,
        SyntaxNode root,
        string filePath,
        ISet<string> keys)
    {
        foreach (var controller in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var classAttributes = Attributes(controller.AttributeLists).ToList();
            if (!controller.Identifier.Text.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) &&
                classAttributes.All(attribute => AttributeName(attribute) != "ApiController"))
                continue;

            var controllerName = controller.Identifier.Text.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                ? controller.Identifier.Text[..^"Controller".Length]
                : controller.Identifier.Text;
            var classRoutes = classAttributes
                .Where(attribute => AttributeName(attribute) == "Route")
                .Select(RouteArgument)
                .Where(route => route is not null)
                .Select(route => route!)
                .DefaultIfEmpty("")
                .ToList();

            foreach (var method in controller.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodAttributes = Attributes(method.AttributeLists).ToList();
                var routeAttribute = methodAttributes.FirstOrDefault(attribute => AttributeName(attribute) == "Route");
                var methodRoute = routeAttribute is null ? null : RouteArgument(routeAttribute);
                foreach (var httpAttribute in methodAttributes)
                {
                    if (!HttpAttributes.TryGetValue(AttributeName(httpAttribute), out var httpMethod)) continue;
                    var attributeRoute = RouteArgument(httpAttribute) ?? methodRoute ?? "";
                    foreach (var classRoute in classRoutes)
                    {
                        var hasExplicitRoute = !string.IsNullOrWhiteSpace(classRoute) || !string.IsNullOrWhiteSpace(attributeRoute);
                        var route = hasExplicitRoute
                            ? CombineRoute(classRoute, attributeRoute, controllerName, method.Identifier.Text)
                            : "(route conventionnelle)";
                        var codeSymbol = FindSymbol(symbols, method.Identifier.Text, method);
                        AddEndpoint(
                            analysis,
                            project,
                            codeSymbol,
                            httpMethod,
                            route,
                            $"{controller.Identifier.Text}.{method.Identifier.Text}",
                            hasExplicitRoute ? RelationConfidence.Certain : RelationConfidence.Probable,
                            filePath,
                            method,
                            keys);
                    }
                }
            }
        }
    }

    private static void ExtractMinimalApiEndpoints(
        Analysis analysis,
        ProjectInfo project,
        IReadOnlyCollection<CodeSymbol> symbols,
        SyntaxNode root,
        string filePath,
        ISet<string> keys)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !MinimalApiMethods.TryGetValue(memberAccess.Name.Identifier.Text, out var httpMethod))
                continue;
            var firstArgument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (firstArgument is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
                continue;

            var containingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var codeSymbol = containingMethod is null
                ? null
                : FindSymbol(symbols, containingMethod.Identifier.Text, containingMethod);
            var handler = codeSymbol is null
                ? $"{Path.GetFileName(filePath)}:{GetLine(invocation)}"
                : string.IsNullOrWhiteSpace(codeSymbol.Container)
                    ? codeSymbol.Name
                    : $"{codeSymbol.Container}.{codeSymbol.Name}";
            AddEndpoint(
                analysis,
                project,
                codeSymbol,
                httpMethod,
                NormalizeRoute(literal.Token.ValueText),
                handler,
                RelationConfidence.Probable,
                filePath,
                invocation,
                keys);
        }
    }

    private static void AddEndpoint(
        Analysis analysis,
        ProjectInfo project,
        CodeSymbol? codeSymbol,
        string httpMethod,
        string route,
        string handler,
        RelationConfidence confidence,
        string filePath,
        SyntaxNode syntax,
        ISet<string> keys)
    {
        var key = $"{project.Id:N}|{httpMethod}|{route}|{handler}";
        if (!keys.Add(key)) return;
        analysis.Endpoints.Add(new ApiEndpoint
        {
            AnalysisId = analysis.Id,
            ProjectInfoId = project.Id,
            CodeSymbolId = codeSymbol?.Id,
            HttpMethod = httpMethod,
            Route = route,
            HandlerDisplay = handler,
            Confidence = confidence,
            FilePath = filePath,
            Line = GetLine(syntax)
        });
    }

    private static IEnumerable<AttributeSyntax> Attributes(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(list => list.Attributes);

    private static string AttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.').Last();
        return name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase)
            ? name[..^"Attribute".Length]
            : name;
    }

    private static string? RouteArgument(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments
            .FirstOrDefault(candidate => candidate.NameEquals is null && candidate.NameColon is null);
        return argument?.Expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static string CombineRoute(
        string classRoute,
        string methodRoute,
        string controllerName,
        string actionName)
    {
        var route = string.Join('/', new[] { classRoute, methodRoute }
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim('/')));
        route = route.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
        return NormalizeRoute(route);
    }

    private static string NormalizeRoute(string route) =>
        string.IsNullOrWhiteSpace(route) ? "/" : "/" + route.Trim().Trim('/');

    private static CodeSymbol? FindSymbol(
        IEnumerable<CodeSymbol> symbols,
        string name,
        SyntaxNode syntax)
    {
        var line = GetLine(syntax);
        return symbols
            .Where(symbol => symbol.Kind == SymbolKind.Method && symbol.Name == name && symbol.Line == line)
            .FirstOrDefault() ??
            symbols.Where(symbol => symbol.Kind == SymbolKind.Method && symbol.Name == name)
                .OrderBy(symbol => Math.Abs(symbol.Line - line))
                .FirstOrDefault();
    }

    private static int GetLine(SyntaxNode syntax) =>
        syntax.SyntaxTree.GetLineSpan(syntax.Span).StartLinePosition.Line + 1;

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly); }
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
                var name = Path.GetFileName(child);
                if (name is "bin" or "obj" or ".git" or ".vs") continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Le fichier inaccessible est ignoré sans interrompre les autres endpoints.
                }
            }
        }
    }

    private sealed record EndpointFile(string Path, ProjectInfo Project);
}
