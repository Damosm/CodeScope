# CodeScope

CodeScope analyse localement une base .NET en lecture seule et présente ses projets, références, classes, interfaces, méthodes et métriques.

## Prérequis et démarrage

.NET SDK 6.0.421 ou compatible. Depuis la racine :

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --project src/CodeScope.Api
```

Ouvrir l'adresse indiquée, saisir un dossier local contenant des `.csproj`, puis lancer l'analyse. Les résultats sont conservés dans SQLite sous `src/CodeScope.Api/data/`.

## Tests

```powershell
dotnet test --no-restore
```

## Structure

- `Domain` : modèle sans dépendance technique.
- `Application` : contrats et cas d'usage.
- `Infrastructure` : analyse Roslyn et stockage EF Core/SQLite.
- `Api` : API et interface web locale.

## Limites du MVP

Analyse synchrone au niveau de la requête HTTP, pas encore d'annulation persistante, d'analyse SQL, de graphe, d'analyse sémantique inter-projets ni de React compilé (Node 10 de l'environnement est incompatible). Les erreurs de lecture sont tolérées, mais leur inventaire détaillé reste à ajouter.

Voir `docs/roadmap.md` pour la suite.
