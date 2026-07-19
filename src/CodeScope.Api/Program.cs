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
builder.Services.AddSingleton<IAnalysisJobQueue, AnalysisJobQueue>();
builder.Services.AddHostedService<AnalysisWorker>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<CodeScopeDbContext>().Database.EnsureCreatedAsync();

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
    await repository.GetAsync(id, ct) is { } analysis ? Results.Ok(analysis) : Results.NotFound());

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
        var analysis = await repository.GetAsync(id, ct);
        if (analysis is null) return Results.NotFound();
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

    var analysis = await repository.GetAsync(id, ct);
    if (analysis is null) return Results.NotFound();
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
        symbols.Select(symbol => symbol.FilePath).Distinct().Count(),
        symbols.Count(symbol => symbol.Kind == SymbolKind.Class),
        symbols.Count(symbol => symbol.Kind == SymbolKind.Interface),
        symbols.Count(symbol => symbol.Kind == SymbolKind.Method),
        symbols.Where(symbol => symbol.Kind == SymbolKind.Method).Sum(symbol => symbol.LineCount),
        symbols.Where(symbol => symbol.Kind == SymbolKind.Method)
            .Select(symbol => symbol.Complexity)
            .DefaultIfEmpty()
            .Max()));
});

app.MapGet("/api/analyses/{id:guid}/search", (
    Guid id,
    string q,
    IAnalysisRepository repository,
    CancellationToken ct) =>
    repository.SearchAsync(id, q?.Trim() ?? string.Empty, ct));

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
