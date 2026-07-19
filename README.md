# CodeScope

CodeScope analyse localement une base .NET en lecture seule et présente ses projets, références, classes, interfaces, méthodes et métriques. Les analyses s'exécutent dans une file d'arrière-plan avec progression et annulation.

## Prérequis et démarrage

.NET SDK 6.0.421 ou compatible. Depuis la racine :

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --project src/CodeScope.Api
```

Ouvrir l'adresse indiquée, saisir un dossier local contenant des `.csproj`, puis lancer l'analyse. Les résultats sont conservés dans SQLite sous `src/CodeScope.Api/data/`.

L'interface permet de suivre les projets et fichiers traités, d'annuler une analyse, de consulter les analyses précédentes et de supprimer leurs résultats.

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

La file est locale au processus : les analyses en attente ne survivent pas à un redémarrage et sont alors marquées en échec. Il n'y a pas encore d'analyse SQL, de graphe, d'analyse sémantique inter-projets ni de React compilé (Node 10 de l'environnement est incompatible). Le nombre d'avertissements de lecture est visible pendant l'analyse, mais leur inventaire détaillé reste à ajouter.

Voir `docs/roadmap.md` pour la suite.
