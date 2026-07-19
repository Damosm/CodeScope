using CodeScope.Domain;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Infrastructure;

public sealed class CodeScopeDbContext : DbContext
{
    public CodeScopeDbContext(DbContextOptions<CodeScopeDbContext> options) : base(options) { }
    public DbSet<Analysis> Analyses => Set<Analysis>();
    public DbSet<ProjectInfo> Projects => Set<ProjectInfo>();
    public DbSet<CodeSymbol> Symbols => Set<CodeSymbol>();
    public DbSet<ProjectReferenceInfo> ProjectReferences => Set<ProjectReferenceInfo>();
    public DbSet<PackageReferenceInfo> PackageReferences => Set<PackageReferenceInfo>();
    public DbSet<CodeRelation> CodeRelations => Set<CodeRelation>();
    public DbSet<SqlObject> SqlObjects => Set<SqlObject>();
    public DbSet<SqlReference> SqlReferences => Set<SqlReference>();
    public DbSet<ApiEndpoint> ApiEndpoints => Set<ApiEndpoint>();
    public DbSet<SourceFileInfo> SourceFiles => Set<SourceFileInfo>();
    public DbSet<RepositorySnapshot> RepositorySnapshots => Set<RepositorySnapshot>();
    public DbSet<SqlColumn> SqlColumns => Set<SqlColumn>();
    public DbSet<SqlColumnReference> SqlColumnReferences => Set<SqlColumnReference>();
    public DbSet<CobolSymbol> CobolSymbols => Set<CobolSymbol>();
    public DbSet<CobolRelation> CobolRelations => Set<CobolRelation>();
    public DbSet<AnalysisDiagnostic> AnalysisDiagnostics => Set<AnalysisDiagnostic>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Analysis>().HasMany(x => x.Projects).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.Relations).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.SqlObjects).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.SqlReferences).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.Endpoints).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.Files).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.RepositorySnapshots).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.SqlColumns).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.SqlColumnReferences).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.CobolSymbols).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.CobolRelations).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasMany(x => x.Diagnostics).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProjectInfo>().HasMany(x => x.Symbols).WithOne().HasForeignKey(x => x.ProjectInfoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProjectInfo>().HasMany(x => x.References).WithOne().HasForeignKey(x => x.ProjectInfoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProjectInfo>().HasMany(x => x.Packages).WithOne().HasForeignKey(x => x.ProjectInfoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CodeSymbol>().HasIndex(x => new { x.ProjectInfoId, x.Name });
        b.Entity<ProjectInfo>().HasIndex(x => new { x.AnalysisId, x.Name });
        b.Entity<CodeRelation>().HasIndex(x => new { x.AnalysisId, x.SourceSymbolId });
        b.Entity<CodeRelation>().HasIndex(x => new { x.AnalysisId, x.TargetSymbolId });
        b.Entity<SqlObject>().HasIndex(x => new { x.AnalysisId, x.Name });
        b.Entity<SqlReference>().HasIndex(x => new { x.AnalysisId, x.SourceSqlObjectId });
        b.Entity<SqlReference>().HasIndex(x => new { x.AnalysisId, x.TargetSqlObjectId });
        b.Entity<PackageReferenceInfo>().HasIndex(x => new { x.ProjectInfoId, x.Name });
        b.Entity<ApiEndpoint>().HasIndex(x => new { x.AnalysisId, x.Route });
        b.Entity<SourceFileInfo>().HasIndex(x => new { x.AnalysisId, x.RelativePath }).IsUnique();
        b.Entity<RepositorySnapshot>().HasIndex(x => x.AnalysisId);
        b.Entity<SqlColumn>().HasIndex(x => new { x.SqlObjectId, x.Name });
        b.Entity<SqlColumnReference>().HasIndex(x => new { x.AnalysisId, x.SqlColumnId });
        b.Entity<CobolSymbol>().HasIndex(x => new { x.AnalysisId, x.Name });
        b.Entity<CobolRelation>().HasIndex(x => new { x.AnalysisId, x.SourceSymbolId });
        b.Entity<AnalysisDiagnostic>().HasIndex(x => new { x.AnalysisId, x.Severity });
    }
}
