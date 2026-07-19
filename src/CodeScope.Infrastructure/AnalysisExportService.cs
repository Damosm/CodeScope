using System.Text;
using System.Text.Json;
using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

public sealed class AnalysisExportService : IAnalysisExportService
{
    private readonly IAnalysisRepository _repository;
    public AnalysisExportService(IAnalysisRepository repository) => _repository = repository;

    public async Task<GeneratedFile?> GeneratePdfAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetAsync(analysisId, cancellationToken);
        if (analysis is null || analysis.Status != AnalysisStatus.Completed) return null;
        var symbols = analysis.Projects.SelectMany(project => project.Symbols).ToList();
        var lines = new List<string>
        {
            "CodeScope - Rapport d'analyse",
            $"Racine : {analysis.RootPath}",
            $"Analyse : {analysis.Id}",
            $"Date : {(analysis.CompletedAt ?? analysis.CreatedAt):yyyy-MM-dd HH:mm:ss zzz}",
            "",
            $"Projets : {analysis.Projects.Count}",
            $"Fichiers : {analysis.Files.Count}",
            $"Symboles C# : {symbols.Count}",
            $"Proprietes C# : {symbols.Count(symbol => symbol.Kind == SymbolKind.Property)}",
            $"Relations C# : {analysis.Relations.Count}",
            $"Objets / colonnes SQL : {analysis.SqlObjects.Count} / {analysis.SqlColumns.Count}",
            $"Symboles / relations COBOL : {analysis.CobolSymbols.Count} / {analysis.CobolRelations.Count}",
            $"Endpoints : {analysis.Endpoints.Count}",
            $"Diagnostics : {analysis.Diagnostics.Count}",
            $"Correspondances ORM : {analysis.OrmEntityMappings.Count} entites / {analysis.OrmPropertyMappings.Count} proprietes",
            "",
            "Projets"
        };
        lines.AddRange(analysis.Projects.OrderBy(project => project.Name).Select(project => $"- {project.Name} ({project.TargetFramework ?? "framework inconnu"}) : {project.Symbols.Count} symboles"));
        lines.Add("");
        lines.Add("Methodes les plus complexes");
        lines.AddRange(symbols.Where(symbol => symbol.Kind == SymbolKind.Method).OrderByDescending(symbol => symbol.Complexity).Take(25)
            .Select(symbol => $"- {Display(symbol)} : complexite {symbol.Complexity}, {symbol.LineCount} lignes"));
        lines.Add("");
        lines.Add("Objets SQL");
        lines.AddRange(analysis.SqlObjects.OrderBy(item => item.Name).Take(100).Select(item => $"- {item.Kind} {item.Name} ({analysis.SqlColumns.Count(column => column.SqlObjectId == item.Id)} colonnes)"));
        lines.Add("");
        lines.Add("Programmes COBOL");
        lines.AddRange(analysis.CobolSymbols.Where(item => item.Kind == CobolSymbolKind.Program).OrderBy(item => item.Name).Take(100).Select(item => $"- {item.Name}"));
        return new GeneratedFile($"CodeScope-{analysis.Id:N}.pdf", "application/pdf", MinimalPdf.Create(lines));
    }

    public async Task<GeneratedFile?> GenerateSarifAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetAsync(analysisId, cancellationToken);
        if (analysis is null || analysis.Status != AnalysisStatus.Completed) return null;
        var results = new List<object>();
        foreach (var method in analysis.Projects.SelectMany(project => project.Symbols).Where(symbol => symbol.Kind == SymbolKind.Method && symbol.Complexity >= 15))
            results.Add(Result("CSCOPE001", "warning", $"Complexite cyclomatique elevee ({method.Complexity}) pour {Display(method)}.", method.FilePath, method.Line));
        foreach (var reference in analysis.SqlReferences.Where(reference => reference.Confidence != RelationConfidence.Certain))
            results.Add(Result("CSCOPE002", "note", $"Reference SQL {reference.Confidence.ToString().ToLowerInvariant()} vers {reference.TargetDisplay}.", reference.FilePath, reference.Line));
        foreach (var relation in analysis.CobolRelations.Where(relation => !relation.TargetSymbolId.HasValue))
            results.Add(Result("CSCOPE003", "warning", $"Cible COBOL non resolue : {relation.TargetDisplay}.", relation.FilePath, relation.Line));
        foreach (var diagnostic in analysis.Diagnostics.Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Info))
            results.Add(Result("CSCOPE004", diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning", $"{diagnostic.Code} : {diagnostic.Message}", diagnostic.FilePath ?? analysis.RootPath, diagnostic.Line ?? 1));
        foreach (var mapping in analysis.OrmEntityMappings.Where(mapping => mapping.Confidence == RelationConfidence.Textual))
            results.Add(Result("CSCOPE005", "note", $"Correspondance ORM non resolue : {mapping.EntityName} vers {mapping.TableName}.", mapping.FilePath, mapping.Line));

        var document = new
        {
            schema = "https://json.schemastore.org/sarif-2.1.0.json",
            version = "2.1.0",
            runs = new[] { new { tool = new { driver = new { name = "CodeScope", informationUri = "https://github.com/Damosm/CodeScope", rules = Rules() } }, results } }
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        json = json.Replace("\"schema\":", "\"$schema\":", StringComparison.Ordinal);
        return new GeneratedFile($"CodeScope-{analysis.Id:N}.sarif", "application/sarif+json", Encoding.UTF8.GetBytes(json));
    }

    private static object Result(string ruleId, string level, string message, string path, int line) => new
    {
        ruleId,
        level,
        message = new { text = message },
        locations = new[] { new { physicalLocation = new { artifactLocation = new { uri = path.Replace('\\', '/') }, region = new { startLine = Math.Max(1, line) } } } }
    };

    private static object[] Rules() => new object[]
    {
        new { id = "CSCOPE001", name = "HighComplexity", shortDescription = new { text = "Methode trop complexe" } },
        new { id = "CSCOPE002", name = "UncertainSql", shortDescription = new { text = "Reference SQL incertaine" } },
        new { id = "CSCOPE003", name = "UnresolvedCobol", shortDescription = new { text = "Dependance COBOL non resolue" } },
        new { id = "CSCOPE004", name = "AnalysisDiagnostic", shortDescription = new { text = "Diagnostic du moteur d'analyse" } },
        new { id = "CSCOPE005", name = "UnresolvedOrmMapping", shortDescription = new { text = "Correspondance ORM non resolue" } }
    };

    private static string Display(CodeSymbol symbol) => string.IsNullOrWhiteSpace(symbol.Container) ? symbol.Name : $"{symbol.Container}.{symbol.Name}";
}

internal static class MinimalPdf
{
    public static byte[] Create(IEnumerable<string> sourceLines)
    {
        var lines = sourceLines.SelectMany(Wrap).Select(ToAscii).ToList();
        var pages = lines.Chunk(48).ToList();
        if (pages.Count == 0) pages.Add(Array.Empty<string>());
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(index => $"{4 + index * 2} 0 R"))}] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        for (var index = 0; index < pages.Count; index++)
        {
            var content = BuildPage(pages[index]);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + index * 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }
        using var stream = new MemoryStream();
        Write(stream, "%PDF-1.4\n%CodeScope\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            Write(stream, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = stream.Position;
        Write(stream, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(stream, $"{offset:0000000000} 00000 n \n");
        Write(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }

    private static string BuildPage(IEnumerable<string> lines)
    {
        var content = new StringBuilder("BT\n/F1 10 Tf\n50 790 Td\n14 TL\n");
        foreach (var line in lines) content.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj\nT*\n");
        return content.Append("ET").ToString();
    }

    private static IEnumerable<string> Wrap(string value)
    {
        if (value.Length == 0) { yield return ""; yield break; }
        for (var index = 0; index < value.Length; index += 90) yield return value.Substring(index, Math.Min(90, value.Length - index));
    }

    private static string ToAscii(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(character => character <= 127 && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static void Write(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
