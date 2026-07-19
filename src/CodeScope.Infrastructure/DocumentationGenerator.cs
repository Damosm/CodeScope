using System.Net;
using System.Text;
using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

public sealed class DocumentationGenerator : IDocumentationGenerator
{
    private readonly IAnalysisRepository _repository;

    public DocumentationGenerator(IAnalysisRepository repository) => _repository = repository;

    public async Task<GeneratedDocumentation?> GenerateHtmlAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetAsync(analysisId, cancellationToken);
        if (analysis is null || analysis.Status != AnalysisStatus.Completed) return null;

        var symbols = analysis.Projects.SelectMany(project => project.Symbols).ToList();
        var methods = symbols.Where(symbol => symbol.Kind == SymbolKind.Method).ToList();
        var packages = analysis.Projects.SelectMany(project => project.Packages)
            .GroupBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(package => package.Name)
            .ToList();
        var title = Path.GetFileName(analysis.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(title)) title = "Analyse CodeScope";

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Documentation CodeScope - ").Append(E(title)).Append("</title>")
            .Append("<style>")
            .Append("body{font:15px/1.55 system-ui,sans-serif;color:#172033;max-width:1100px;margin:auto;padding:32px}")
            .Append("h1,h2{color:#135d66}h2{margin-top:34px;border-bottom:1px solid #dce4ec;padding-bottom:7px}")
            .Append("table{width:100%;border-collapse:collapse;margin:12px 0}th,td{text-align:left;padding:8px;border-bottom:1px solid #e5e9ee;vertical-align:top}")
            .Append("th{background:#e9f5f4}.metrics{display:flex;flex-wrap:wrap;gap:10px}.metric{background:#e9f5f4;padding:12px;border-radius:8px}")
            .Append("code{overflow-wrap:anywhere}.warning{color:#8a4b08}.muted{color:#64748b}</style></head><body>");
        html.Append("<h1>Documentation technique — ").Append(E(title)).Append("</h1>")
            .Append("<p class=\"muted\">Générée localement par CodeScope à partir de l'analyse du ")
            .Append(E((analysis.CompletedAt ?? analysis.CreatedAt).ToString("yyyy-MM-dd HH:mm:ss zzz"))).Append(".</p>")
            .Append("<p>Cette documentation est déterministe : elle synthétise uniquement les éléments détectés, sans envoyer le code à un service externe.</p>");

        html.Append("<h2>Résumé</h2><div class=\"metrics\">");
        Metric(html, "Projets", analysis.Projects.Count);
        Metric(html, "Fichiers C#", symbols.Select(symbol => symbol.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Metric(html, "Classes", symbols.Count(symbol => symbol.Kind is SymbolKind.Class or SymbolKind.Record));
        Metric(html, "Méthodes", methods.Count);
        Metric(html, "Endpoints", analysis.Endpoints.Count);
        Metric(html, "Objets SQL", analysis.SqlObjects.Count);
        Metric(html, "Relations", analysis.Relations.Count + analysis.SqlReferences.Count);
        html.Append("</div>");

        html.Append("<h2>Architecture détectée</h2><p>")
            .Append(E(ArchitectureSummary(analysis))).Append("</p>")
            .Append("<table><thead><tr><th>Projet</th><th>Framework</th><th>Symboles</th><th>Dépendances projet</th><th>Packages</th></tr></thead><tbody>");
        foreach (var project in analysis.Projects.OrderBy(project => project.Name))
        {
            html.Append("<tr><td><strong>").Append(E(project.Name)).Append("</strong><br><code>")
                .Append(E(project.Path)).Append("</code></td><td>").Append(E(project.TargetFramework ?? "non déterminé"))
                .Append("</td><td>").Append(project.Symbols.Count).Append("</td><td>")
                .Append(project.References.Count).Append("</td><td>")
                .Append(E(string.Join(", ", project.Packages.Select(package => PackageDisplay(package)))))
                .Append("</td></tr>");
        }
        html.Append("</tbody></table>");

        html.Append("<h2>Endpoints API</h2>");
        if (analysis.Endpoints.Count == 0) html.Append("<p class=\"muted\">Aucun endpoint explicite détecté.</p>");
        else
        {
            html.Append("<table><thead><tr><th>Méthode</th><th>Route</th><th>Gestionnaire</th><th>Confiance</th></tr></thead><tbody>");
            foreach (var endpoint in analysis.Endpoints.OrderBy(endpoint => endpoint.Route).ThenBy(endpoint => endpoint.HttpMethod))
                html.Append("<tr><td>").Append(E(endpoint.HttpMethod)).Append("</td><td><code>")
                    .Append(E(endpoint.Route)).Append("</code></td><td>").Append(E(endpoint.HandlerDisplay))
                    .Append("</td><td>").Append(E(endpoint.Confidence.ToString())).Append("</td></tr>");
            html.Append("</tbody></table>");
        }

        html.Append("<h2>Objets SQL</h2>");
        if (analysis.SqlObjects.Count == 0) html.Append("<p class=\"muted\">Aucun objet SQL détecté.</p>");
        else
        {
            html.Append("<table><thead><tr><th>Type</th><th>Nom</th><th>Définition</th><th>Références</th></tr></thead><tbody>");
            foreach (var sqlObject in analysis.SqlObjects.OrderBy(sqlObject => sqlObject.Kind).ThenBy(sqlObject => sqlObject.Name))
                html.Append("<tr><td>").Append(E(sqlObject.Kind.ToString())).Append("</td><td><strong>")
                    .Append(E(sqlObject.Name)).Append("</strong></td><td><code>").Append(E(sqlObject.FilePath)).Append(":")
                    .Append(sqlObject.Line).Append("</code></td><td>")
                    .Append(analysis.SqlReferences.Count(reference => reference.SourceSqlObjectId == sqlObject.Id || reference.TargetSqlObjectId == sqlObject.Id))
                    .Append("</td></tr>");
            html.Append("</tbody></table>");
        }

        html.Append("<h2>Packages NuGet</h2>");
        if (packages.Count == 0) html.Append("<p class=\"muted\">Aucun PackageReference explicite.</p>");
        else html.Append("<ul>").Append(string.Join("", packages.Select(package => $"<li>{E(PackageDisplay(package))}</li>"))).Append("</ul>");

        html.Append("<h2>Zones complexes et points de vigilance</h2>");
        var complexMethods = methods.OrderByDescending(method => method.Complexity).ThenByDescending(method => method.LineCount).Take(20).ToList();
        if (complexMethods.Count == 0) html.Append("<p class=\"muted\">Aucune méthode inventoriée.</p>");
        else
        {
            html.Append("<table><thead><tr><th>Méthode</th><th>Complexité</th><th>Lignes</th><th>Emplacement</th></tr></thead><tbody>");
            foreach (var method in complexMethods)
                html.Append("<tr><td>").Append(E(Display(method))).Append("</td><td>").Append(method.Complexity)
                    .Append("</td><td>").Append(method.LineCount).Append("</td><td><code>").Append(E(method.FilePath))
                    .Append(":").Append(method.Line).Append("</code></td></tr>");
            html.Append("</tbody></table>");
        }
        var externalRelations = analysis.Relations.Count(relation => !relation.TargetSymbolId.HasValue);
        var uncertainSql = analysis.SqlReferences.Count(reference => reference.Confidence != RelationConfidence.Certain);
        html.Append("<ul class=\"warning\"><li>").Append(externalRelations)
            .Append(" relation(s) C# ciblent un élément externe à la solution.</li><li>").Append(uncertainSql)
            .Append(" référence(s) SQL probable(s) ou textuelle(s) nécessitent une vérification.</li></ul>");

        html.Append("<h2>Glossaire</h2><dl><dt><strong>Relation certaine</strong></dt><dd>Résolue par Roslyn ou vers un objet SQL défini sans ambiguïté.</dd>")
            .Append("<dt><strong>Relation probable</strong></dt><dd>Déduite d'une structure explicite, mais non reliée à une définition unique.</dd>")
            .Append("<dt><strong>Correspondance textuelle</strong></dt><dd>Nom trouvé dans une chaîne C# ; une validation manuelle est nécessaire.</dd>")
            .Append("<dt><strong>Complexité</strong></dt><dd>1 + branches, boucles, cas, expressions conditionnelles et opérateurs logiques.</dd></dl>")
            .Append("</body></html>");

        var safeName = string.Concat(title.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return new GeneratedDocumentation($"CodeScope-{safeName}.html", html.ToString());
    }

    private static string ArchitectureSummary(Analysis analysis)
    {
        var projectNames = analysis.Projects.Select(project => project.Name).OrderBy(name => name).ToList();
        if (projectNames.Count == 0) return "Aucun projet .NET n'a été détecté ; l'analyse peut néanmoins contenir des scripts SQL.";
        var references = analysis.Projects.Sum(project => project.References.Count);
        return $"La solution contient {projectNames.Count} projet(s) ({string.Join(", ", projectNames)}) et {references} référence(s) inter-projets.";
    }

    private static void Metric(StringBuilder html, string label, int value) =>
        html.Append("<span class=\"metric\"><strong>").Append(value).Append("</strong> ").Append(E(label)).Append("</span>");

    private static string PackageDisplay(PackageReferenceInfo package) =>
        string.IsNullOrWhiteSpace(package.Version) ? package.Name : $"{package.Name} {package.Version}";

    private static string Display(CodeSymbol symbol) => string.IsNullOrWhiteSpace(symbol.Container)
        ? symbol.Name
        : $"{symbol.Container}.{symbol.Name}";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
