namespace CodeScope.Domain;

public enum AnalysisStatus { Pending, Running, Completed, Failed, Cancelled }
public enum SymbolKind { Class, Interface, Record, Enum, Method }

public sealed class Analysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RootPath { get; set; } = "";
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public List<ProjectInfo> Projects { get; set; } = new();
}

public sealed class ProjectInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? TargetFramework { get; set; }
    public List<CodeSymbol> Symbols { get; set; } = new();
    public List<ProjectReferenceInfo> References { get; set; } = new();
}

public sealed class CodeSymbol
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectInfoId { get; set; }
    public SymbolKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Container { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int LineCount { get; set; }
    public int Complexity { get; set; } = 1;
    public string? ReturnType { get; set; }
}

public sealed class ProjectReferenceInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectInfoId { get; set; }
    public string ReferencedPath { get; set; } = "";
}
