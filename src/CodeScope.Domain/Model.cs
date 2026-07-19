namespace CodeScope.Domain;

public enum AnalysisStatus { Pending, Running, Completed, Failed, Cancelled }
public enum SymbolKind { Class, Interface, Record, Enum, Method }
public enum RelationKind { Calls, Inherits, Implements, Creates }
public enum RelationConfidence { Certain, Probable, Textual }
public enum SqlObjectKind { Table, View, Procedure, Function, Trigger }
public enum SqlOperationKind { Select, Insert, Update, Delete, Execute, Join, Reference }
public enum ImpactElementKind { CodeSymbol, SqlObject, External }
public enum ImpactRisk { Low, Medium, High, Critical }

public sealed class Analysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RootPath { get; set; } = "";
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public List<ProjectInfo> Projects { get; set; } = new();
    public List<CodeRelation> Relations { get; set; } = new();
    public List<SqlObject> SqlObjects { get; set; } = new();
    public List<SqlReference> SqlReferences { get; set; } = new();
    public List<ApiEndpoint> Endpoints { get; set; } = new();
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
    public List<PackageReferenceInfo> Packages { get; set; } = new();
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

public sealed class PackageReferenceInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectInfoId { get; set; }
    public string Name { get; set; } = "";
    public string? Version { get; set; }
}

public sealed class CodeRelation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid SourceSymbolId { get; set; }
    public Guid? TargetSymbolId { get; set; }
    public string SourceDisplay { get; set; } = "";
    public string TargetDisplay { get; set; } = "";
    public RelationKind Kind { get; set; }
    public RelationConfidence Confidence { get; set; } = RelationConfidence.Certain;
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class SqlObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public SqlObjectKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class SqlReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid? SourceSqlObjectId { get; set; }
    public Guid? SourceCodeSymbolId { get; set; }
    public Guid? TargetSqlObjectId { get; set; }
    public string SourceDisplay { get; set; } = "";
    public string TargetDisplay { get; set; } = "";
    public SqlOperationKind Operation { get; set; }
    public RelationConfidence Confidence { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class ApiEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid ProjectInfoId { get; set; }
    public Guid? CodeSymbolId { get; set; }
    public string HttpMethod { get; set; } = "";
    public string Route { get; set; } = "";
    public string HandlerDisplay { get; set; } = "";
    public RelationConfidence Confidence { get; set; } = RelationConfidence.Certain;
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}
