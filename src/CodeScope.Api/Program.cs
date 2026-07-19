using CodeScope.Application;
using CodeScope.Domain;
using CodeScope.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data"); Directory.CreateDirectory(dataDir);
builder.Services.AddDbContext<CodeScopeDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dataDir, "codescope.db")}"));
builder.Services.AddScoped<IAnalysisRepository, AnalysisRepository>();
builder.Services.AddScoped<IProjectScanner, ProjectScanner>();
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<CodeScopeDbContext>().Database.EnsureCreatedAsync();
app.UseDefaultFiles(); app.UseStaticFiles();

app.MapPost("/api/analyses", async (StartRequest request, IProjectScanner scanner, IAnalysisRepository repository, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Path)) return Results.BadRequest(new { error = "Le chemin est obligatoire." });
    try { var analysis = await scanner.ScanAsync(request.Path, ct); await repository.AddAsync(analysis, ct); return Results.Created($"/api/analyses/{analysis.Id}", analysis); }
    catch (DirectoryNotFoundException e) { return Results.BadRequest(new { error = e.Message }); }
});
app.MapGet("/api/analyses", (IAnalysisRepository repository, CancellationToken ct) => repository.ListAsync(ct));
app.MapGet("/api/analyses/{id:guid}", async (Guid id, IAnalysisRepository repository, CancellationToken ct) => await repository.GetAsync(id, ct) is { } x ? Results.Ok(x) : Results.NotFound());
app.MapGet("/api/analyses/{id:guid}/dashboard", async (Guid id, IAnalysisRepository repository, CancellationToken ct) =>
{
    var x = await repository.GetAsync(id, ct); if (x is null) return Results.NotFound(); var s = x.Projects.SelectMany(p => p.Symbols).ToList();
    return Results.Ok(new Dashboard(x.Projects.Count, s.Select(v => v.FilePath).Distinct().Count(), s.Count(v => v.Kind == SymbolKind.Class), s.Count(v => v.Kind == SymbolKind.Interface), s.Count(v => v.Kind == SymbolKind.Method), s.Where(v => v.Kind == SymbolKind.Method).Sum(v => v.LineCount), s.Where(v => v.Kind == SymbolKind.Method).Select(v => v.Complexity).DefaultIfEmpty().Max()));
});
app.MapGet("/api/analyses/{id:guid}/search", (Guid id, string q, IAnalysisRepository repository, CancellationToken ct) => repository.SearchAsync(id, q ?? "", ct));
app.MapFallbackToFile("index.html");
app.Run();
public sealed record StartRequest(string Path);
public partial class Program { }
