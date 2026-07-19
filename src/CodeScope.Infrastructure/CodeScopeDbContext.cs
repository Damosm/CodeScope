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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Analysis>().HasMany(x => x.Projects).WithOne().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProjectInfo>().HasMany(x => x.Symbols).WithOne().HasForeignKey(x => x.ProjectInfoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProjectInfo>().HasMany(x => x.References).WithOne().HasForeignKey(x => x.ProjectInfoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CodeSymbol>().HasIndex(x => new { x.ProjectInfoId, x.Name });
        b.Entity<ProjectInfo>().HasIndex(x => new { x.AnalysisId, x.Name });
    }
}
