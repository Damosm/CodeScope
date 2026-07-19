using System.Text.RegularExpressions;
using CodeScope.Application;
using CodeScope.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SymbolKind = CodeScope.Domain.SymbolKind;

namespace CodeScope.Infrastructure;

internal static class SqlScriptAnalyzer
{
    private const string IdentifierPattern = @"(?:\[[^\]\r\n]+\]|[#A-Za-z_][A-Za-z0-9_$#@]*)";
    private const string QualifiedNamePattern = IdentifierPattern + @"(?:\s*\.\s*" + IdentifierPattern + @"){0,2}";

    private static readonly Regex DefinitionRegex = new(
        @"\bCREATE\s+(?:OR\s+ALTER\s+)?(?<kind>TABLE|VIEW|PROC(?:EDURE)?|FUNCTION|TRIGGER)\s+(?<name>" + QualifiedNamePattern + ")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly OperationPattern[] OperationPatterns =
    {
        new(SqlOperationKind.Insert, new Regex(@"\bINSERT\s+(?:INTO\s+)?(?<name>" + QualifiedNamePattern + ")", Options)),
        new(SqlOperationKind.Update, new Regex(@"\bUPDATE\s+(?<name>" + QualifiedNamePattern + ")", Options)),
        new(SqlOperationKind.Delete, new Regex(@"\bDELETE\s+(?:FROM\s+)?(?<name>" + QualifiedNamePattern + ")", Options)),
        new(SqlOperationKind.Execute, new Regex(@"\bEXEC(?:UTE)?\s+(?<name>" + QualifiedNamePattern + ")", Options)),
        new(SqlOperationKind.Join, new Regex(@"\bJOIN\s+(?<name>" + QualifiedNamePattern + ")", Options)),
        new(SqlOperationKind.Select, new Regex(@"\bFROM\s+(?<name>" + QualifiedNamePattern + ")", Options))
    };

    private static readonly Regex NameTokenRegex = new(QualifiedNamePattern, Options);
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "node_modules", "packages" };
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static async Task AnalyzeAsync(
        Analysis analysis,
        string rootPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var warnings = 0;
        var parsedFiles = new List<ParsedSqlFile>();
        var sqlPaths = EnumerateSafely(rootPath, "*.sql", () => warnings++).ToList();
        var symbolsFound = analysis.Projects.SelectMany(project => project.Symbols).Count();
        var codeFiles = analysis.Projects.SelectMany(project => project.Symbols)
            .Select(symbol => symbol.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        for (var index = 0; index < sqlPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = sqlPaths[index];
            try
            {
                var original = await File.ReadAllTextAsync(path, cancellationToken);
                var sanitized = Sanitize(original);
                var parsed = new ParsedSqlFile(path, sanitized);
                foreach (Match match in DefinitionRegex.Matches(sanitized))
                {
                    var sqlObject = new SqlObject
                    {
                        AnalysisId = analysis.Id,
                        Kind = ParseObjectKind(match.Groups["kind"].Value),
                        Name = NormalizeName(match.Groups["name"].Value),
                        FilePath = path,
                        Line = GetLine(sanitized, match.Index)
                    };
                    analysis.SqlObjects.Add(sqlObject);
                    parsed.Definitions.Add(new DefinitionContext(match.Index, sqlObject));
                }
                parsedFiles.Add(parsed);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings++;
            }

            progress?.Report(new AnalysisProgress(
                "sql",
                $"SQL : {index + 1}/{sqlPaths.Count} fichier(s), {analysis.SqlObjects.Count} objet(s).",
                analysis.Projects.Count,
                analysis.Projects.Count,
                codeFiles + index + 1,
                symbolsFound,
                warnings));
        }

        var objectLookup = BuildLookup(analysis.SqlObjects, sqlObject => sqlObject.Name);
        var shortNameLookup = BuildLookup(analysis.SqlObjects, sqlObject => ShortName(sqlObject.Name));
        var referenceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parsed in parsedFiles)
            ExtractSqlReferences(analysis, parsed, objectLookup, shortNameLookup, referenceKeys);

        if (analysis.SqlObjects.Count > 0)
            await ExtractCodeReferencesAsync(
                analysis,
                objectLookup,
                shortNameLookup,
                referenceKeys,
                cancellationToken);

        progress?.Report(new AnalysisProgress(
            "sql",
            $"SQL terminé : {analysis.SqlObjects.Count} objet(s), {analysis.SqlReferences.Count} référence(s).",
            analysis.Projects.Count,
            analysis.Projects.Count,
            codeFiles + sqlPaths.Count,
            symbolsFound,
            warnings));
    }

    private static void ExtractSqlReferences(
        Analysis analysis,
        ParsedSqlFile parsed,
        IReadOnlyDictionary<string, List<SqlObject>> objectLookup,
        IReadOnlyDictionary<string, List<SqlObject>> shortNameLookup,
        ISet<string> referenceKeys)
    {
        foreach (var operationPattern in OperationPatterns)
        {
            foreach (Match match in operationPattern.Pattern.Matches(parsed.SanitizedText))
            {
                if (operationPattern.Operation == SqlOperationKind.Select && IsDeleteFrom(parsed.SanitizedText, match.Index))
                    continue;

                var targetName = NormalizeName(match.Groups["name"].Value);
                if (string.IsNullOrWhiteSpace(targetName)) continue;
                var source = parsed.Definitions
                    .Where(definition => definition.Index <= match.Index)
                    .OrderByDescending(definition => definition.Index)
                    .FirstOrDefault()?.Object;
                var target = ResolveObject(targetName, objectLookup, shortNameLookup);
                AddReference(
                    analysis,
                    source?.Id,
                    null,
                    source?.Name ?? Path.GetFileName(parsed.Path),
                    target,
                    targetName,
                    operationPattern.Operation,
                    target is null ? RelationConfidence.Probable : RelationConfidence.Certain,
                    parsed.Path,
                    GetLine(parsed.SanitizedText, match.Index),
                    referenceKeys);
            }
        }
    }

    private static async Task ExtractCodeReferencesAsync(
        Analysis analysis,
        IReadOnlyDictionary<string, List<SqlObject>> objectLookup,
        IReadOnlyDictionary<string, List<SqlObject>> shortNameLookup,
        ISet<string> referenceKeys,
        CancellationToken cancellationToken)
    {
        var symbolsByFile = analysis.Projects
            .SelectMany(project => project.Symbols)
            .GroupBy(symbol => symbol.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var fileGroup in symbolsByFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourceText;
            try { sourceText = await File.ReadAllTextAsync(fileGroup.Key, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var root = await CSharpSyntaxTree.ParseText(sourceText, cancellationToken: cancellationToken)
                .GetRootAsync(cancellationToken);
            foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Where(node => node.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                var value = literal.Token.ValueText;
                if (string.IsNullOrWhiteSpace(value)) continue;
                var line = literal.SyntaxTree.GetLineSpan(literal.Span).StartLinePosition.Line + 1;
                var sourceSymbol = fileGroup
                    .Where(symbol => symbol.Line <= line && line < symbol.Line + Math.Max(1, symbol.LineCount))
                    .OrderBy(symbol => symbol.Kind == SymbolKind.Method ? 0 : 1)
                    .ThenBy(symbol => symbol.LineCount)
                    .FirstOrDefault();

                foreach (Match nameMatch in NameTokenRegex.Matches(value))
                {
                    var candidate = NormalizeName(nameMatch.Value);
                    var target = ResolveObject(candidate, objectLookup, shortNameLookup);
                    if (target is null) continue;
                    AddReference(
                        analysis,
                        null,
                        sourceSymbol?.Id,
                        Display(sourceSymbol) ?? Path.GetFileName(fileGroup.Key),
                        target,
                        target.Name,
                        InferOperation(value, nameMatch.Index),
                        RelationConfidence.Textual,
                        fileGroup.Key,
                        line,
                        referenceKeys);
                }
            }
        }
    }

    private static void AddReference(
        Analysis analysis,
        Guid? sourceSqlObjectId,
        Guid? sourceCodeSymbolId,
        string sourceDisplay,
        SqlObject? target,
        string targetDisplay,
        SqlOperationKind operation,
        RelationConfidence confidence,
        string filePath,
        int line,
        ISet<string> referenceKeys)
    {
        var key = $"{sourceSqlObjectId?.ToString("N")}|{sourceCodeSymbolId?.ToString("N")}|{sourceDisplay}|{operation}|{target?.Id.ToString("N") ?? targetDisplay}";
        if (!referenceKeys.Add(key)) return;

        analysis.SqlReferences.Add(new SqlReference
        {
            AnalysisId = analysis.Id,
            SourceSqlObjectId = sourceSqlObjectId,
            SourceCodeSymbolId = sourceCodeSymbolId,
            TargetSqlObjectId = target?.Id,
            SourceDisplay = sourceDisplay,
            TargetDisplay = targetDisplay,
            Operation = operation,
            Confidence = confidence,
            FilePath = filePath,
            Line = line
        });
    }

    private static Dictionary<string, List<SqlObject>> BuildLookup(
        IEnumerable<SqlObject> sqlObjects,
        Func<SqlObject, string> keySelector) =>
        sqlObjects.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

    private static SqlObject? ResolveObject(
        string name,
        IReadOnlyDictionary<string, List<SqlObject>> objectLookup,
        IReadOnlyDictionary<string, List<SqlObject>> shortNameLookup)
    {
        if (objectLookup.TryGetValue(name, out var exact) && exact.Count == 1) return exact[0];
        var shortName = ShortName(name);
        return shortNameLookup.TryGetValue(shortName, out var candidates) && candidates.Count == 1
            ? candidates[0]
            : null;
    }

    private static SqlOperationKind InferOperation(string value, int nameIndex)
    {
        var prefix = value.Substring(0, Math.Min(value.Length, nameIndex));
        if (Regex.IsMatch(prefix, @"\bINSERT\s+(?:INTO\s+)?\s*$", RegexOptions.IgnoreCase)) return SqlOperationKind.Insert;
        if (Regex.IsMatch(prefix, @"\bUPDATE\s*$", RegexOptions.IgnoreCase)) return SqlOperationKind.Update;
        if (Regex.IsMatch(prefix, @"\bDELETE\s+(?:FROM\s+)?\s*$", RegexOptions.IgnoreCase)) return SqlOperationKind.Delete;
        if (Regex.IsMatch(prefix, @"\bEXEC(?:UTE)?\s*$", RegexOptions.IgnoreCase)) return SqlOperationKind.Execute;
        if (Regex.IsMatch(prefix, @"\bJOIN\s*$", RegexOptions.IgnoreCase)) return SqlOperationKind.Join;
        if (Regex.IsMatch(prefix, @"\bFROM\s*$", RegexOptions.IgnoreCase)) return SqlOperationKind.Select;
        return SqlOperationKind.Reference;
    }

    private static bool IsDeleteFrom(string text, int fromIndex)
    {
        var start = Math.Max(0, fromIndex - 20);
        return Regex.IsMatch(text.Substring(start, fromIndex - start), @"\bDELETE\s*$", RegexOptions.IgnoreCase);
    }

    private static SqlObjectKind ParseObjectKind(string value) => value.ToUpperInvariant() switch
    {
        "TABLE" => SqlObjectKind.Table,
        "VIEW" => SqlObjectKind.View,
        "FUNCTION" => SqlObjectKind.Function,
        "TRIGGER" => SqlObjectKind.Trigger,
        _ => SqlObjectKind.Procedure
    };

    private static string NormalizeName(string value)
    {
        var identifiers = Regex.Matches(value, IdentifierPattern)
            .Select(match => match.Value.Trim().TrimStart('[').TrimEnd(']'))
            .Where(identifier => identifier.Length > 0);
        return string.Join('.', identifiers);
    }

    private static string ShortName(string value) => value.Split('.').LastOrDefault() ?? value;

    private static string? Display(CodeSymbol? symbol) => symbol is null
        ? null
        : string.IsNullOrWhiteSpace(symbol.Container) ? symbol.Name : $"{symbol.Container}.{symbol.Name}";

    private static int GetLine(string text, int index)
    {
        var line = 1;
        for (var position = 0; position < index && position < text.Length; position++)
            if (text[position] == '\n') line++;
        return line;
    }

    private static string Sanitize(string text)
    {
        var result = text.ToCharArray();
        var state = SanitizerState.Normal;
        for (var index = 0; index < result.Length; index++)
        {
            var current = result[index];
            var next = index + 1 < result.Length ? result[index + 1] : '\0';
            if (state == SanitizerState.Normal && current == '-' && next == '-')
            {
                result[index] = result[index + 1] = ' ';
                index++;
                state = SanitizerState.LineComment;
            }
            else if (state == SanitizerState.Normal && current == '/' && next == '*')
            {
                result[index] = result[index + 1] = ' ';
                index++;
                state = SanitizerState.BlockComment;
            }
            else if (state == SanitizerState.Normal && current == '\'')
            {
                result[index] = ' ';
                state = SanitizerState.String;
            }
            else if (state == SanitizerState.LineComment)
            {
                if (current == '\r' || current == '\n') state = SanitizerState.Normal;
                else result[index] = ' ';
            }
            else if (state == SanitizerState.BlockComment)
            {
                if (current == '*' && next == '/')
                {
                    result[index] = result[index + 1] = ' ';
                    index++;
                    state = SanitizerState.Normal;
                }
                else if (current != '\r' && current != '\n') result[index] = ' ';
            }
            else if (state == SanitizerState.String)
            {
                if (current == '\'' && next == '\'')
                {
                    result[index] = result[index + 1] = ' ';
                    index++;
                }
                else if (current == '\'')
                {
                    result[index] = ' ';
                    state = SanitizerState.Normal;
                }
                else if (current != '\r' && current != '\n') result[index] = ' ';
            }
        }
        return new string(result);
    }

    private static IEnumerable<string> EnumerateSafely(string root, string pattern, Action onWarning)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                onWarning();
                continue;
            }
            foreach (var file in files) yield return file;

            string[] directories;
            try { directories = Directory.GetDirectories(directory); }
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
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    onWarning();
                }
            }
        }
    }

    private sealed record OperationPattern(SqlOperationKind Operation, Regex Pattern);
    private sealed record DefinitionContext(int Index, SqlObject Object);
    private sealed record ParsedSqlFile(string Path, string SanitizedText)
    {
        public List<DefinitionContext> Definitions { get; } = new();
    }
    private enum SanitizerState { Normal, LineComment, BlockComment, String }
}
