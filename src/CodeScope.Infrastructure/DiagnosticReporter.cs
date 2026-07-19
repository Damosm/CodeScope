using CodeScope.Domain;

namespace CodeScope.Infrastructure;

internal static class DiagnosticReporter
{
    public static void Warning(Analysis analysis, string code, string stage, string message, string? filePath = null, int? line = null) =>
        Add(analysis, DiagnosticSeverity.Warning, code, stage, message, filePath, line);

    public static void Info(Analysis analysis, string code, string stage, string message, string? filePath = null, int? line = null) =>
        Add(analysis, DiagnosticSeverity.Info, code, stage, message, filePath, line);

    private static void Add(Analysis analysis, DiagnosticSeverity severity, string code, string stage, string message, string? filePath, int? line)
    {
        if (analysis.Diagnostics.Any(item => item.Code == code && item.Stage == stage && item.FilePath == filePath && item.Line == line)) return;
        analysis.Diagnostics.Add(new AnalysisDiagnostic
        {
            AnalysisId = analysis.Id,
            Severity = severity,
            Code = code,
            Stage = stage,
            Message = message,
            FilePath = filePath,
            Line = line
        });
    }
}
