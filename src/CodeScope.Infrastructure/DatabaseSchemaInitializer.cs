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

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SourceFiles"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SourceFiles"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL, ""ProjectInfoId"" TEXT NULL,
                ""RelativePath"" TEXT NOT NULL, ""FullPath"" TEXT NOT NULL,
                ""Extension"" TEXT NOT NULL, ""Category"" INTEGER NOT NULL,
                ""Size"" INTEGER NOT NULL, ""LineCount"" INTEGER NOT NULL,
                ""Sha256"" TEXT NOT NULL, ""LastWriteUtc"" TEXT NOT NULL,
                CONSTRAINT ""FK_SourceFiles_Analyses_AnalysisId"" FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SourceFiles_AnalysisId_RelativePath\" ON \"SourceFiles\" (\"AnalysisId\", \"RelativePath\");", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""RepositorySnapshots"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RepositorySnapshots"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL, ""IsGitRepository"" INTEGER NOT NULL,
                ""RepositoryRoot"" TEXT NULL, ""CommitHash"" TEXT NULL, ""Branch"" TEXT NULL,
                ""IsDirty"" INTEGER NOT NULL, ""CapturedAt"" TEXT NOT NULL,
                CONSTRAINT ""FK_RepositorySnapshots_Analyses_AnalysisId"" FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_RepositorySnapshots_AnalysisId\" ON \"RepositorySnapshots\" (\"AnalysisId\");", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SqlColumns"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SqlColumns"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL, ""SqlObjectId"" TEXT NOT NULL,
                ""Name"" TEXT NOT NULL, ""DataType"" TEXT NULL, ""IsNullable"" INTEGER NULL,
                ""Ordinal"" INTEGER NOT NULL, ""FilePath"" TEXT NOT NULL, ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_SqlColumns_Analyses_AnalysisId"" FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_SqlColumns_SqlObjectId_Name\" ON \"SqlColumns\" (\"SqlObjectId\", \"Name\");", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SqlColumnReferences"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SqlColumnReferences"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL, ""SqlObjectId"" TEXT NULL, ""SqlColumnId"" TEXT NULL,
                ""SourceCodeSymbolId"" TEXT NULL, ""ObjectName"" TEXT NOT NULL, ""ColumnName"" TEXT NOT NULL,
                ""Operation"" INTEGER NOT NULL, ""Confidence"" INTEGER NOT NULL,
                ""FilePath"" TEXT NOT NULL, ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_SqlColumnReferences_Analyses_AnalysisId"" FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_SqlColumnReferences_AnalysisId_SqlColumnId\" ON \"SqlColumnReferences\" (\"AnalysisId\", \"SqlColumnId\");", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""CobolSymbols"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_CobolSymbols"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL, ""Kind"" INTEGER NOT NULL, ""Name"" TEXT NOT NULL,
                ""FilePath"" TEXT NOT NULL, ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_CobolSymbols_Analyses_AnalysisId"" FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_CobolSymbols_AnalysisId_Name\" ON \"CobolSymbols\" (\"AnalysisId\", \"Name\");", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""CobolRelations"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_CobolRelations"" PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL, ""SourceSymbolId"" TEXT NULL, ""TargetSymbolId"" TEXT NULL,
                ""SourceDisplay"" TEXT NOT NULL, ""TargetDisplay"" TEXT NOT NULL,
                ""Kind"" INTEGER NOT NULL, ""Confidence"" INTEGER NOT NULL,
                ""FilePath"" TEXT NOT NULL, ""Line"" INTEGER NOT NULL,
                CONSTRAINT ""FK_CobolRelations_Analyses_AnalysisId"" FOREIGN KEY (""AnalysisId"") REFERENCES ""Analyses"" (""Id"") ON DELETE CASCADE
            );", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_CobolRelations_AnalysisId_SourceSymbolId\" ON \"CobolRelations\" (\"AnalysisId\", \"SourceSymbolId\");", cancellationToken);
    }
}
