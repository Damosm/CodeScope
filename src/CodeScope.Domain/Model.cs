namespace CodeScope.Domain;

public enum AnalysisStatus { Pending, Running, Completed, Failed, Cancelled }
public enum SymbolKind { Class, Interface, Record, Enum, Method, Namespace, Constructor, Property, Field }
public enum RelationKind { Calls, Inherits, Implements, Creates }
public enum RelationConfidence { Certain, Probable, Textual }
public enum SqlObjectKind { Table, View, Procedure, Function, Trigger }
public enum SqlOperationKind { Select, Insert, Update, Delete, Execute, Join, Reference }
public enum ImpactElementKind { CodeSymbol, SqlObject, External, CobolSymbol }
public enum ImpactRisk { Low, Medium, High, Critical }
public enum SourceFileCategory { SourceCode, Configuration, Sql, Cobol, Project, Solution, Documentation, Other }
public enum CobolSymbolKind { Program, Section, Paragraph, Copybook }
public enum CobolRelationKind { Calls, Copies }
public enum DiagnosticSeverity { Info, Warning, Error }
public enum OrmMappingSource { TableAttribute, FluentApi, DbSetConvention, PropertyAttribute, PropertyConvention }

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
    public List<SourceFileInfo> Files { get; set; } = new();
    public List<RepositorySnapshot> RepositorySnapshots { get; set; } = new();
    public List<SqlColumn> SqlColumns { get; set; } = new();
    public List<SqlColumnReference> SqlColumnReferences { get; set; } = new();
    public List<CobolSymbol> CobolSymbols { get; set; } = new();
    public List<CobolRelation> CobolRelations { get; set; } = new();
    public List<AnalysisDiagnostic> Diagnostics { get; set; } = new();
    public List<OrmEntityMapping> OrmEntityMappings { get; set; } = new();
    public List<OrmPropertyMapping> OrmPropertyMappings { get; set; } = new();
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

public sealed class SourceFileInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid? ProjectInfoId { get; set; }
    public string RelativePath { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Extension { get; set; } = "";
    public SourceFileCategory Category { get; set; }
    public long Size { get; set; }
    public int LineCount { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTimeOffset LastWriteUtc { get; set; }
}

public sealed class RepositorySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public bool IsGitRepository { get; set; }
    public string? RepositoryRoot { get; set; }
    public string? CommitHash { get; set; }
    public string? Branch { get; set; }
    public bool IsDirty { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SqlColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid SqlObjectId { get; set; }
    public string Name { get; set; } = "";
    public string? DataType { get; set; }
    public bool? IsNullable { get; set; }
    public int Ordinal { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class SqlColumnReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid? SqlObjectId { get; set; }
    public Guid? SqlColumnId { get; set; }
    public Guid? SourceCodeSymbolId { get; set; }
    public string ObjectName { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public SqlOperationKind Operation { get; set; }
    public RelationConfidence Confidence { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class CobolSymbol
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public CobolSymbolKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class CobolRelation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid? SourceSymbolId { get; set; }
    public Guid? TargetSymbolId { get; set; }
    public string SourceDisplay { get; set; } = "";
    public string TargetDisplay { get; set; } = "";
    public CobolRelationKind Kind { get; set; }
    public RelationConfidence Confidence { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class AnalysisDiagnostic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Warning;
    public string Code { get; set; } = "CSCOPE000";
    public string Stage { get; set; } = "analysis";
    public string Message { get; set; } = "";
    public string? FilePath { get; set; }
    public int? Line { get; set; }
}

public sealed class OrmEntityMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid? ProjectInfoId { get; set; }
    public Guid? CodeSymbolId { get; set; }
    public Guid? SqlObjectId { get; set; }
    public string EntityName { get; set; } = "";
    public string TableName { get; set; } = "";
    public OrmMappingSource Source { get; set; }
    public RelationConfidence Confidence { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}

public sealed class OrmPropertyMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid OrmEntityMappingId { get; set; }
    public Guid? CodeSymbolId { get; set; }
    public Guid? SqlColumnId { get; set; }
    public string PropertyName { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public OrmMappingSource Source { get; set; }
    public RelationConfidence Confidence { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}
