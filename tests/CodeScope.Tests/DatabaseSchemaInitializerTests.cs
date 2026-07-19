using CodeScope.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodeScope.Tests;

public sealed class DatabaseSchemaInitializerTests
{
    [Fact]
    public async Task Compatible_schema_adds_new_tables_to_an_existing_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"CREATE TABLE ""Analyses"" (
                ""Id"" TEXT NOT NULL PRIMARY KEY,
                ""RootPath"" TEXT NOT NULL,
                ""Status"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""CompletedAt"" TEXT NULL,
                ""Error"" TEXT NULL
            );
            CREATE TABLE ""Projects"" (
                ""Id"" TEXT NOT NULL PRIMARY KEY,
                ""AnalysisId"" TEXT NOT NULL,
                ""Name"" TEXT NOT NULL,
                ""Path"" TEXT NOT NULL,
                ""TargetFramework"" TEXT NULL
            );";
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<CodeScopeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new CodeScopeDbContext(options);
        await DatabaseSchemaInitializer.EnsureCompatibleSchemaAsync(db);

        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tableNames.Add(reader.GetString(0));

        Assert.Contains("CodeRelations", tableNames);
        Assert.Contains("SqlObjects", tableNames);
        Assert.Contains("SqlReferences", tableNames);
        Assert.Contains("PackageReferences", tableNames);
        Assert.Contains("ApiEndpoints", tableNames);
        Assert.Contains("SourceFiles", tableNames);
        Assert.Contains("RepositorySnapshots", tableNames);
        Assert.Contains("SqlColumns", tableNames);
        Assert.Contains("SqlColumnReferences", tableNames);
        Assert.Contains("CobolSymbols", tableNames);
        Assert.Contains("CobolRelations", tableNames);
        Assert.Contains("AnalysisDiagnostics", tableNames);
        Assert.Contains("OrmEntityMappings", tableNames);
        Assert.Contains("OrmPropertyMappings", tableNames);
    }
}
