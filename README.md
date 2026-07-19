# CodeScope

CodeScope analyse localement une base .NET en lecture seule et présente ses projets, packages, références, classes, interfaces, méthodes, endpoints, objets SQL et métriques. Pour une solution `.sln`, il résout aussi les appels, héritages, implémentations et instanciations avec Roslyn/MSBuild. Les analyses s'exécutent dans une file d'arrière-plan avec progression et annulation.

## Prérequis et démarrage

.NET SDK 6.0.421 ou compatible. Depuis la racine :

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --project src/CodeScope.Api
```

Ouvrir l'adresse indiquée, saisir un dossier local contenant des `.csproj`, puis lancer l'analyse. Les résultats sont conservés dans SQLite sous `src/CodeScope.Api/data/`.

L'interface permet de suivre les projets et fichiers traités, d'annuler une analyse, de consulter les analyses précédentes, de rechercher symboles et endpoints, d'explorer les objets SQL, de calculer un impact direct/indirect avec risque expliqué et de supprimer les résultats. Une documentation technique peut être consultée ou exportée en HTML. En l'absence de solution chargeable, une analyse syntaxique de secours reste disponible.

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

La file est locale au processus : les analyses en attente ne survivent pas à un redémarrage et sont alors marquées en échec. Il n'y a pas encore de graphe visuel interactif, d'analyse SQL complète des colonnes et du SQL dynamique, de comparaison Git, ni de React compilé (Node 10 de l'environnement est incompatible). La profondeur d'impact est actuellement fixée à 2 dans l'interface, même si l'API accepte 1 à 4. Le nombre d'avertissements est visible pendant l'analyse, mais leur inventaire détaillé reste à ajouter.

Le calcul du risque d'impact et les limites des niveaux de confiance sont documentés dans `docs/impact-analysis.md`.

Voir `docs/roadmap.md` pour la suite.
