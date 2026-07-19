using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProjectInfo = CodeScope.Domain.ProjectInfo;
using SymbolKind = CodeScope.Domain.SymbolKind;
using DiagnosticSeverity = CodeScope.Domain.DiagnosticSeverity;

namespace CodeScope.Infrastructure;

internal static class EfCoreMappingAnalyzer
{
    public static async Task AnalyzeAsync(
        Analysis analysis,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = analysis.Projects
            .SelectMany(project => project.Symbols.Select(symbol => (Project: project, symbol.FilePath)))
            .DistinctBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parsedFiles = new List<ParsedFile>();
        foreach (var item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await File.ReadAllTextAsync(item.FilePath, cancellationToken);
                var root = await CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken).GetRootAsync(cancellationToken);
                parsedFiles.Add(new ParsedFile(item.FilePath, item.Project, root));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DiagnosticReporter.Warning(analysis, "CSCOPE203", "orm", "Le fichier C# n'a pas pu être inspecté pour les correspondances ORM.", item.FilePath);
            }
        }

        var entityCandidates = new List<EntityCandidate>();
        var propertyCandidates = new List<PropertyCandidate>();
        foreach (var file in parsedFiles)
        {
            ExtractAttributes(file, entityCandidates, propertyCandidates);
            ExtractFluentMappings(file, entityCandidates, propertyCandidates);
            ExtractDbSets(file, entityCandidates);
        }

        foreach (var candidate in entityCandidates
            .GroupBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => Priority(item.Source)).ThenBy(item => item.Line).First()))
        {
            var entitySymbols = analysis.Projects.SelectMany(project => project.Symbols.Select(symbol => (Project: project, Symbol: symbol)))
                .Where(item => item.Symbol.Kind is SymbolKind.Class or SymbolKind.Record && item.Symbol.Name.Equals(candidate.EntityName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var entity = entitySymbols.Count == 1 ? entitySymbols[0] : default;
            var table = ResolveTable(analysis.SqlObjects, candidate.TableName);
            var mapping = new OrmEntityMapping
            {
                AnalysisId = analysis.Id,
                ProjectInfoId = entity.Project?.Id ?? candidate.Project.Id,
                CodeSymbolId = entity.Symbol?.Id,
                SqlObjectId = table?.Id,
                EntityName = candidate.EntityName,
                TableName = table?.Name ?? candidate.TableName,
                Source = candidate.Source,
                Confidence = Confidence(candidate.Source != OrmMappingSource.DbSetConvention, entity.Symbol is not null, table is not null),
                FilePath = candidate.FilePath,
                Line = candidate.Line
            };
            analysis.OrmEntityMappings.Add(mapping);
            ExtractProperties(analysis, mapping, parsedFiles, propertyCandidates);
        }

        progress?.Report(new AnalysisProgress(
            "orm",
            $"ORM : {analysis.OrmEntityMappings.Count} entité(s), {analysis.OrmPropertyMappings.Count} propriété(s) reliée(s).",
            analysis.Projects.Count,
            analysis.Projects.Count,
            parsedFiles.Count,
            analysis.OrmEntityMappings.Count + analysis.OrmPropertyMappings.Count,
            analysis.Diagnostics.Count(item => item.Stage == "orm" && item.Severity != DiagnosticSeverity.Info)));
    }

    private static void ExtractAttributes(ParsedFile file, ICollection<EntityCandidate> entities, ICollection<PropertyCandidate> properties)
    {
        foreach (var declaration in file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var tableAttribute = declaration.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attribute => IsAttribute(attribute, "Table"));
            if (tableAttribute is not null && StringArgument(tableAttribute, 0) is { } tableName)
            {
                var schema = NamedStringArgument(tableAttribute, "Schema");
                entities.Add(new EntityCandidate(declaration.Identifier.Text, Qualify(schema, tableName), OrmMappingSource.TableAttribute,
                    file.Project, file.Path, Line(declaration)));
            }

            foreach (var property in declaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                var columnAttribute = property.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attribute => IsAttribute(attribute, "Column"));
                if (columnAttribute is not null && StringArgument(columnAttribute, 0) is { } columnName)
                    properties.Add(new PropertyCandidate(declaration.Identifier.Text, property.Identifier.Text, columnName,
                        OrmMappingSource.PropertyAttribute, file.Path, Line(property)));
            }
        }
    }

    private static void ExtractFluentMappings(ParsedFile file, ICollection<EntityCandidate> entities, ICollection<PropertyCandidate> properties)
    {
        foreach (var invocation in file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access) continue;
            if (access.Name.Identifier.Text == "ToTable" && StringArgument(invocation, 0) is { } tableName)
            {
                var entityName = EntityFromChain(access.Expression) ?? EntityFromConfiguration(invocation);
                if (entityName is null) continue;
                var schema = StringArgument(invocation, 1) ?? NamedStringArgument(invocation, "schema");
                entities.Add(new EntityCandidate(entityName, Qualify(schema, tableName), OrmMappingSource.FluentApi,
                    file.Project, file.Path, Line(invocation)));
            }
            else if (access.Name.Identifier.Text == "HasColumnName" && StringArgument(invocation, 0) is { } columnName &&
                access.Expression is InvocationExpressionSyntax propertyInvocation)
            {
                var entityName = EntityFromChain(propertyInvocation) ?? EntityFromConfiguration(invocation);
                var propertyName = PropertyFromInvocation(propertyInvocation);
                if (entityName is not null && propertyName is not null)
                    properties.Add(new PropertyCandidate(entityName, propertyName, columnName, OrmMappingSource.FluentApi,
                        file.Path, Line(invocation)));
            }
        }
    }

    private static void ExtractDbSets(ParsedFile file, ICollection<EntityCandidate> entities)
    {
        foreach (var property in file.Root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var dbSet = property.Type.DescendantNodesAndSelf().OfType<GenericNameSyntax>()
                .FirstOrDefault(name => name.Identifier.Text == "DbSet" && name.TypeArgumentList.Arguments.Count == 1);
            if (dbSet is null) continue;
            entities.Add(new EntityCandidate(dbSet.TypeArgumentList.Arguments[0].ToString().Split('.').Last(), property.Identifier.Text,
                OrmMappingSource.DbSetConvention, file.Project, file.Path, Line(property)));
        }
    }

    private static void ExtractProperties(
        Analysis analysis,
        OrmEntityMapping entityMapping,
        IEnumerable<ParsedFile> files,
        IEnumerable<PropertyCandidate> explicitCandidates)
    {
        var explicitByProperty = explicitCandidates
            .Where(item => item.EntityName.Equals(entityMapping.EntityName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => Priority(item.Source)).First(), StringComparer.OrdinalIgnoreCase);
        var propertyDeclarations = files.SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.Text.Equals(entityMapping.EntityName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(declaration => declaration.Members.OfType<PropertyDeclarationSyntax>().Select(property => (file, property))));
        foreach (var item in propertyDeclarations.GroupBy(item => item.property.Identifier.Text, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            var propertyName = item.property.Identifier.Text;
            explicitByProperty.TryGetValue(propertyName, out var explicitMapping);
            var columnName = explicitMapping?.ColumnName ?? propertyName;
            var sqlColumn = entityMapping.SqlObjectId.HasValue
                ? ResolveColumn(analysis.SqlColumns.Where(column => column.SqlObjectId == entityMapping.SqlObjectId.Value), columnName)
                : null;
            var symbolCandidates = item.file.Project.Symbols.Where(symbol => symbol.Kind == SymbolKind.Property &&
                symbol.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                symbol.FilePath.Equals(item.file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
            var source = explicitMapping?.Source ?? OrmMappingSource.PropertyConvention;
            analysis.OrmPropertyMappings.Add(new OrmPropertyMapping
            {
                AnalysisId = analysis.Id,
                OrmEntityMappingId = entityMapping.Id,
                CodeSymbolId = symbolCandidates.Count == 1 ? symbolCandidates[0].Id : null,
                SqlColumnId = sqlColumn?.Id,
                PropertyName = propertyName,
                ColumnName = sqlColumn?.Name ?? columnName,
                Source = source,
                Confidence = sqlColumn is null ? RelationConfidence.Textual :
                    source == OrmMappingSource.PropertyConvention ? RelationConfidence.Probable : RelationConfidence.Certain,
                FilePath = explicitMapping?.FilePath ?? item.file.Path,
                Line = explicitMapping?.Line ?? Line(item.property)
            });
        }
    }

    private static string? EntityFromChain(SyntaxNode node)
    {
        foreach (var generic in node.DescendantNodesAndSelf().OfType<GenericNameSyntax>())
            if (generic.Identifier.Text == "Entity" && generic.TypeArgumentList.Arguments.Count == 1)
                return generic.TypeArgumentList.Arguments[0].ToString().Split('.').Last();
        return null;
    }

    private static string? EntityFromConfiguration(SyntaxNode node)
    {
        var declaration = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        var generic = declaration?.BaseList?.Types.SelectMany(type => type.Type.DescendantNodesAndSelf().OfType<GenericNameSyntax>())
            .FirstOrDefault(name => name.Identifier.Text == "IEntityTypeConfiguration" && name.TypeArgumentList.Arguments.Count == 1);
        return generic?.TypeArgumentList.Arguments[0].ToString().Split('.').Last();
    }

    private static string? PropertyFromInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access || access.Name.Identifier.Text != "Property") return null;
        return invocation.ArgumentList.Arguments.Select(argument => argument.Expression)
            .SelectMany(expression => expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            .Select(member => member.Name.Identifier.Text).LastOrDefault();
    }

    private static bool IsAttribute(AttributeSyntax attribute, string name)
    {
        var value = attribute.Name.ToString().Split('.').Last();
        return value.Equals(name, StringComparison.OrdinalIgnoreCase) || value.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static string? StringArgument(AttributeSyntax attribute, int index) =>
        attribute.ArgumentList is { } list && list.Arguments.Count > index ? LiteralString(list.Arguments[index].Expression) : null;
    private static string? NamedStringArgument(AttributeSyntax attribute, string name) =>
        attribute.ArgumentList?.Arguments.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase) == true) is { } argument
            ? LiteralString(argument.Expression) : null;
    private static string? StringArgument(InvocationExpressionSyntax invocation, int index) =>
        invocation.ArgumentList.Arguments.Count > index ? LiteralString(invocation.ArgumentList.Arguments[index].Expression) : null;
    private static string? NamedStringArgument(InvocationExpressionSyntax invocation, string name) =>
        invocation.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase) == true) is { } argument
            ? LiteralString(argument.Expression) : null;
    private static string? LiteralString(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
        ? literal.Token.ValueText : null;

    private static SqlObject? ResolveTable(IEnumerable<SqlObject> objects, string name)
    {
        var candidates = objects.Where(item => item.Kind == SqlObjectKind.Table &&
            (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || item.Name.Split('.').Last().Equals(name.Split('.').Last(), StringComparison.OrdinalIgnoreCase))).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static SqlColumn? ResolveColumn(IEnumerable<SqlColumn> columns, string name)
    {
        var candidates = columns.Where(item => item.Name.Equals(name.Split('.').Last(), StringComparison.OrdinalIgnoreCase)).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static RelationConfidence Confidence(bool explicitMapping, bool entityResolved, bool tableResolved) =>
        entityResolved && tableResolved ? explicitMapping ? RelationConfidence.Certain : RelationConfidence.Probable : RelationConfidence.Textual;
    private static int Priority(OrmMappingSource source) => source == OrmMappingSource.FluentApi ? 0 : source is OrmMappingSource.TableAttribute or OrmMappingSource.PropertyAttribute ? 1 : 2;
    private static string Qualify(string? schema, string name) => string.IsNullOrWhiteSpace(schema) || name.Contains('.') ? name : $"{schema}.{name}";
    private static int Line(SyntaxNode node) => node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    private sealed record ParsedFile(string Path, ProjectInfo Project, SyntaxNode Root);
    private sealed record EntityCandidate(string EntityName, string TableName, OrmMappingSource Source, ProjectInfo Project, string FilePath, int Line);
    private sealed record PropertyCandidate(string EntityName, string PropertyName, string ColumnName, OrmMappingSource Source, string FilePath, int Line);
}
