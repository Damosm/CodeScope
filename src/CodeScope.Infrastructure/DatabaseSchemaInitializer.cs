using Microsoft.EntityFrameworkCore;

namespace CodeScope.Infrastructure;

public static class DatabaseSchemaInitializer
{
    public static async Task EnsureCompatibleSchemaAsync(
        CodeScopeDbContext db,
        CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        if (!db.Database.IsSqlite()) return;

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""CodeRelations"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_CodeRelations"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL,
                ""SourceSymbolId"" TEXT NOT NULL,
                ""TargetSymbolId"" TEXT NULL,
                ""SourceDisplay"" TEXT NOT NULL,
                ""TargetDisplay"" TEXT NOT NULL,
                ""Kind"" INTEGER NOT NULL,
                ""Confidence"" INTEGER NOT NULL,
                ""FilePath"" TEXT NOT NULL,
                ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_CodeRelations_Analyses_AnalysisId""
                    FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_CodeRelations_AnalysisId_SourceSymbolId\" ON \"CodeRelations\" (\"AnalysisId\", \"SourceSymbolId\");",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_CodeRelations_AnalysisId_TargetSymbolId\" ON \"CodeRelations\" (\"AnalysisId\", \"TargetSymbolId\");",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SqlObjects"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SqlObjects"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL,
                ""Kind"" INTEGER NOT NULL,
                ""Name"" TEXT NOT NULL,
                ""FilePath"" TEXT NOT NULL,
                ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_SqlObjects_Analyses_AnalysisId""
                    FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_SqlObjects_AnalysisId_Name\" ON \"SqlObjects\" (\"AnalysisId\", \"Name\");",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SqlReferences"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SqlReferences"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL,
                ""SourceSqlObjectId"" TEXT NULL,
                ""SourceCodeSymbolId"" TEXT NULL,
                ""TargetSqlObjectId"" TEXT NULL,
                ""SourceDisplay"" TEXT NOT NULL,
                ""TargetDisplay"" TEXT NOT NULL,
                ""Operation"" INTEGER NOT NULL,
                ""Confidence"" INTEGER NOT NULL,
                ""FilePath"" TEXT NOT NULL,
                ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_SqlReferences_Analyses_AnalysisId""
                    FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_SqlReferences_AnalysisId_SourceSqlObjectId\" ON \"SqlReferences\" (\"AnalysisId\", \"SourceSqlObjectId\");",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_SqlReferences_AnalysisId_TargetSqlObjectId\" ON \"SqlReferences\" (\"AnalysisId\", \"TargetSqlObjectId\");",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""PackageReferences"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PackageReferences"" PRIMARY KEY,
                ""ProjectInfoId"" TEXT NOT NULL,
                ""Name"" TEXT NOT NULL,
                ""Version"" TEXT NULL,
                CONSTRAINT ""FK_PackageReferences_Projects_ProjectInfoId""
                    FOREIGN KEY (""ProjectInfoId"") REFERENCES ""Projects"" (""Id"") ON DELETE CASCADE
            );",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_PackageReferences_ProjectInfoId_Name\" ON \"PackageReferences\" (\"ProjectInfoId\", \"Name\");",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""ApiEndpoints"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ApiEndpoints"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL,
                ""ProjectInfoId"" TEXT NOT NULL,
                ""CodeSymbolId"" TEXT NULL,
                ""HttpMethod"" TEXT NOT NULL,
                ""Route"" TEXT NOT NULL,
                ""HandlerDisplay"" TEXT NOT NULL,
                ""Confidence"" INTEGER NOT NULL,
                ""FilePath"" TEXT NOT NULL,
                ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_ApiEndpoints_Analyses_AnalysisId""
                    FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_ApiEndpoints_AnalysisId_Route\" ON \"ApiEndpoints\" (\"AnalysisId\", \"Route\");",
            cancellationToken);
    }
}
