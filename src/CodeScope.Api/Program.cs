using System.Text;
using System.Text.Json.Serialization;
using CodeScope.Api;
using CodeScope.Application;
using CodeScope.Domain;
using CodeScope.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
builder.Services.AddDbContext<CodeScopeDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "codescope.db")}"));
builder.Services.AddScoped<IAnalysisRepository, AnalysisRepository>();
builder.Services.AddScoped<IProjectScanner, ProjectScanner>();
builder.Services.AddScoped<IImpactAnalysisService, ImpactAnalysisService>();
builder.Services.AddScoped<IDocumentationGenerator, DocumentationGenerator>();
builder.Services.AddScoped<IDependencyGraphService, DependencyGraphService>();
builder.Services.AddScoped<IAnalysisComparisonService, AnalysisComparisonService>();
builder.Services.AddScoped<IAnalysisExportService, AnalysisExportService>();
builder.Services.AddSingleton<IAnalysisJobQueue, AnalysisJobQueue>();
builder.Services.AddHostedService<AnalysisWorker>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await DatabaseSchemaInitializer.EnsureCompatibleSchemaAsync(
        scope.ServiceProvider.GetRequiredService<CodeScopeDbContext>());

app.UseExceptionHandler(errorApplication => errorApplication.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new
    {
        title = "Une erreur interne est survenue.",
        status = StatusCodes.Status500InternalServerError
    });
}));
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/analyses", async (
    StartRequest request,
    IAnalysisJobQueue queue,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
        return Results.BadRequest(new { error = "Le chemin est obligatoire." });

    string rootPath;
    try { rootPath = Path.GetFullPath(request.Path.Trim()); }
    catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
    {
        return Results.BadRequest(new { error = "Le chemin indiqué n'est pas valide." });
    }

    if (!Directory.Exists(rootPath))
        return Results.BadRequest(new { error = "Le dossier indiqué n'existe pas." });

    var analysis = new Analysis { RootPath = rootPath, Status = AnalysisStatus.Pending };
    await repository.AddAsync(analysis, ct);
    try
    {
        await queue.EnqueueAsync(analysis.Id, rootPath, ct);
    }
    catch
    {
        await repository.UpdateStatusAsync(
            analysis.Id,
            AnalysisStatus.Failed,
            "Impossible d'ajouter l'analyse à la file.",
            CancellationToken.None);
        throw;
    }

    return Results.Accepted($"/api/analyses/{analysis.Id}/progress", analysis);
});

app.MapGet("/api/analyses", (IAnalysisRepository repository, CancellationToken ct) =>
    repository.ListAsync(ct));

app.MapGet("/api/analyses/{id:guid}", async (
    Guid id,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var analysis = await repository.GetAsync(id, ct);
    return analysis is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            analysis.Id,
            analysis.RootPath,
            analysis.Status,
            analysis.CreatedAt,
            analysis.CompletedAt,
            analysis.Error,
            analysis.Projects
        });
});

app.MapGet("/api/analyses/{id:guid}/progress", async (
    Guid id,
    IAnalysisJobQueue queue,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    if (queue.Get(id) is { } snapshot) return Results.Ok(snapshot);
    var analysis = await repository.GetAsync(id, ct);
    return analysis is null
        ? Results.NotFound()
        : Results.Ok(ToSnapshot(analysis));
});

app.MapPost("/api/analyses/{id:guid}/cancel", async (
    Guid id,
    IAnalysisJobQueue queue,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    if (!queue.TryCancel(id))
    {
        var existingStatus = await repository.GetStatusAsync(id, ct);
        if (!existingStatus.HasValue) return Results.NotFound();
        return Results.Conflict(new { error = "Cette analyse ne peut plus être annulée." });
    }

    await repository.UpdateStatusAsync(id, AnalysisStatus.Cancelled, null, ct);
    return Results.Accepted($"/api/analyses/{id}/progress", queue.Get(id));
});

app.MapDelete("/api/analyses/{id:guid}", async (
    Guid id,
    IAnalysisJobQueue queue,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var snapshot = queue.Get(id);
    if (snapshot?.Status is AnalysisStatus.Pending or AnalysisStatus.Running)
        return Results.Conflict(new { error = "Annulez l'analyse avant de la supprimer." });

    var existingStatus = await repository.GetStatusAsync(id, ct);
    if (!existingStatus.HasValue) return Results.NotFound();
    queue.Remove(id);
    await repository.DeleteAsync(id, ct);
    return Results.NoContent();
});

app.MapGet("/api/analyses/{id:guid}/dashboard", async (
    Guid id,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var analysis = await repository.GetAsync(id, ct);
    if (analysis is null) return Results.NotFound();
    if (analysis.Status != AnalysisStatus.Completed)
        return Results.Conflict(new { error = "L'analyse n'est pas terminée." });

    var symbols = analysis.Projects.SelectMany(project => project.Symbols).ToList();
    return Results.Ok(new Dashboard(
        analysis.Projects.Count,
        analysis.Files.Count,
        symbols.Count(symbol => symbol.Kind == SymbolKind.Class),
        symbols.Count(symbol => symbol.Kind == SymbolKind.Interface),
        symbols.Count(symbol => symbol.Kind == SymbolKind.Method),
        symbols.Where(symbol => symbol.Kind == SymbolKind.Method).Sum(symbol => symbol.LineCount),
        symbols.Where(symbol => symbol.Kind == SymbolKind.Method)
            .Select(symbol => symbol.Complexity)
            .DefaultIfEmpty()
            .Max(),
        analysis.Relations.Count,
        analysis.Relations.Count(relation => relation.Kind == RelationKind.Calls),
        analysis.SqlObjects.Count,
        analysis.SqlReferences.Count,
        analysis.Endpoints.Count,
        analysis.Projects.SelectMany(project => project.Packages)
            .Select(package => package.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(),
        symbols.Count(symbol => symbol.Kind == SymbolKind.Property),
        analysis.SqlColumns.Count,
        analysis.CobolSymbols.Count));
});

app.MapGet("/api/analyses/{id:guid}/search", (
    Guid id,
    string q,
    IAnalysisRepository repository,
    CancellationToken ct) =>
    repository.SearchAsync(id, q?.Trim() ?? string.Empty, ct));

app.MapGet("/api/analyses/{id:guid}/relations", async (
    Guid id,
    Guid? symbolId,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed)
        return Results.Conflict(new { error = "L'analyse n'est pas terminée." });

    return Results.Ok(await repository.GetRelationsAsync(id, symbolId, ct));
});

app.MapGet("/api/analyses/{id:guid}/sql", async (
    Guid id,
    string? q,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed)
        return Results.Conflict(new { error = "L'analyse n'est pas terminée." });

    return Results.Ok(await repository.SearchSqlAsync(id, q?.Trim() ?? string.Empty, ct));
});

app.MapGet("/api/analyses/{id:guid}/sql-references", async (
    Guid id,
    Guid? objectId,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed)
        return Results.Conflict(new { error = "L'analyse n'est pas terminée." });

    return Results.Ok(await repository.GetSqlReferencesAsync(id, objectId, ct));
});

app.MapGet("/api/analyses/{id:guid}/endpoints", async (
    Guid id,
    string? q,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed)
        return Results.Conflict(new { error = "L'analyse n'est pas terminée." });

    return Results.Ok(await repository.SearchEndpointsAsync(id, q?.Trim() ?? string.Empty, ct));
});

app.MapGet("/api/analyses/{id:guid}/files", async (
    Guid id,
    string? q,
    string? category,
    IAnalysisRepository repository,
    CancellationToken ct) =>
{
    SourceFileCategory? parsedCategory = null;
    if (!string.IsNullOrWhiteSpace(category))
    {
        if (!Enum.TryParse<SourceFileCategory>(category, true, out var value))
            return Results.BadRequest(new { error = "Catégorie de fichier inconnue." });
        parsedCategory = value;
    }
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed) return Results.Conflict(new { error = "L'analyse n'est pas terminée." });
    return Results.Ok(await repository.SearchFilesAsync(id, q?.Trim() ?? string.Empty, parsedCategory, ct));
});

app.MapGet("/api/analyses/{id:guid}/sql-columns", async (Guid id, Guid? objectId, IAnalysisRepository repository, CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed) return Results.Conflict(new { error = "L'analyse n'est pas terminée." });
    return Results.Ok(await repository.GetSqlColumnsAsync(id, objectId, ct));
});

app.MapGet("/api/analyses/{id:guid}/sql-column-references", async (Guid id, Guid? columnId, IAnalysisRepository repository, CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed) return Results.Conflict(new { error = "L'analyse n'est pas terminée." });
    return Results.Ok(await repository.GetSqlColumnReferencesAsync(id, columnId, ct));
});

app.MapGet("/api/analyses/{id:guid}/cobol", async (Guid id, string? q, IAnalysisRepository repository, CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed) return Results.Conflict(new { error = "L'analyse n'est pas terminée." });
    return Results.Ok(await repository.SearchCobolAsync(id, q?.Trim() ?? string.Empty, ct));
});

app.MapGet("/api/analyses/{id:guid}/cobol-relations", async (Guid id, Guid? symbolId, IAnalysisRepository repository, CancellationToken ct) =>
{
    var status = await repository.GetStatusAsync(id, ct);
    if (!status.HasValue) return Results.NotFound();
    if (status != AnalysisStatus.Completed) return Results.Conflict(new { error = "L'analyse n'est pas terminée." });
    return Results.Ok(await repository.GetCobolRelationsAsync(id, symbolId, ct));
});

app.MapGet("/api/analyses/{id:guid}/graph", async (Guid id, string? kind, int? limit, IDependencyGraphService graphs, CancellationToken ct) =>
{
    var graph = await graphs.BuildAsync(id, kind ?? "types", limit ?? 150, ct);
    return graph is null ? Results.NotFound() : Results.Ok(graph);
});

app.MapGet("/api/analyses/compare", async (Guid from, Guid to, IAnalysisComparisonService comparisons, CancellationToken ct) =>
{
    var comparison = await comparisons.CompareAsync(from, to, ct);
    return comparison is null ? Results.NotFound() : Results.Ok(comparison);
});

app.MapGet("/api/analyses/{id:guid}/impact", async (
    Guid id,
    string kind,
    Guid elementId,
    int? depth,
    IImpactAnalysisService impactAnalysis,
    CancellationToken ct) =>
{
    if (!Enum.TryParse<ImpactElementKind>(kind, true, out var elementKind) ||
        elementKind == ImpactElementKind.External)
        return Results.BadRequest(new { error = "Le type d'élément doit être CodeSymbol, SqlObject ou CobolSymbol." });

    var report = await impactAnalysis.AnalyzeAsync(id, elementKind, elementId, depth ?? 2, ct);
    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.MapGet("/api/analyses/{id:guid}/documentation", async (
    Guid id,
    IDocumentationGenerator generator,
    CancellationToken ct) =>
{
    var documentation = await generator.GenerateHtmlAsync(id, ct);
    return documentation is null
        ? Results.NotFound()
        : Results.Content(documentation.Html, "text/html", Encoding.UTF8);
});

app.MapGet("/api/analyses/{id:guid}/documentation/export", async (
    Guid id,
    IDocumentationGenerator generator,
    CancellationToken ct) =>
{
    var documentation = await generator.GenerateHtmlAsync(id, ct);
    return documentation is null
        ? Results.NotFound()
        : Results.File(Encoding.UTF8.GetBytes(documentation.Html), "text/html; charset=utf-8", documentation.FileName);
});

app.MapGet("/api/analyses/{id:guid}/export/pdf", async (Guid id, IAnalysisExportService exports, CancellationToken ct) =>
{
    var file = await exports.GeneratePdfAsync(id, ct);
    return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
});

app.MapGet("/api/analyses/{id:guid}/export/sarif", async (Guid id, IAnalysisExportService exports, CancellationToken ct) =>
{
    var file = await exports.GenerateSarifAsync(id, ct);
    return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
});

app.MapFallbackToFile("index.html");
app.Run();

static AnalysisJobSnapshot ToSnapshot(Analysis analysis) => new(
    analysis.Id,
    analysis.Status,
    analysis.Status.ToString().ToLowerInvariant(),
    analysis.Status switch
    {
        AnalysisStatus.Completed => "Analyse terminée.",
        AnalysisStatus.Cancelled => "Analyse annulée.",
        AnalysisStatus.Failed => "L'analyse a échoué.",
        AnalysisStatus.Running => "Analyse interrompue.",
        _ => "Analyse en attente."
    },
    0,
    analysis.Projects.Count,
    0,
    analysis.Projects.SelectMany(x => x.Symbols).Count(),
    0,
    analysis.Error);

public sealed record StartRequest(string Path);
public partial class Program { }
