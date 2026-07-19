# CodeScope

CodeScope analyse localement une base .NET/SQL/COBOL en lecture seule. Il inventorie les fichiers et leurs empreintes SHA-256, les projets, packages, symboles (dont propriétés et champs), endpoints, objets et colonnes SQL, programmes COBOL et dépendances. Il relie également les entités/propriétés EF Core aux tables/colonnes détectées. Pour une solution `.sln`, il résout les appels, héritages, implémentations et instanciations avec Roslyn/MSBuild. Les analyses s'exécutent dans une file d'arrière-plan avec progression et annulation.

## Prérequis et démarrage

.NET SDK 6.0.421 ou compatible. Depuis la racine :

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --project src/CodeScope.Api
```

Ouvrir l'adresse indiquée, saisir un dossier local contenant des `.csproj`, puis lancer l'analyse. Les résultats sont conservés dans SQLite sous `src/CodeScope.Api/data/`.

L'interface permet de suivre les projets et fichiers traités, d'annuler une analyse, de consulter les analyses précédentes, de rechercher symboles et endpoints, de parcourir une arborescence de fichiers/propriétés, d'explorer objets et colonnes SQL, les correspondances EF Core et les éléments COBOL, puis de manipuler des graphes SVG (zoom, déplacement, filtre et export PNG). Elle compare deux analyses à partir des empreintes de fichiers et des instantanés Git, y compris les renommages. L'impact unifié C#/ORM/SQL est réglable de 1 à 4 niveaux et présente les chemins critiques. Les diagnostics sont filtrables et intégrés aux exports HTML/SARIF ; la documentation est aussi exportable en PDF. En l'absence de solution chargeable, une analyse syntaxique de secours reste disponible.

## Tests

```powershell
dotnet test --no-restore
```

## Structure

- `Domain` : modèle sans dépendance technique.
- `Application` : contrats et cas d'usage.
- `Infrastructure` : analyse Roslyn et stockage EF Core/SQLite.
- `Api` : API et interface web locale.

## Limites actuelles

La file est locale au processus : les analyses en attente ne survivent pas à un redémarrage et sont alors marquées en échec. SQL et COBOL sont analysés de façon tolérante par structures fréquentes, pas par une grammaire exhaustive de chaque dialecte. Le mode incrémental détecte précisément les fichiers ajoutés, modifiés, renommés, supprimés et inchangés ; l'analyse sémantique complète est néanmoins rejouée pour garantir la cohérence. Le frontend reste sans compilation afin de fonctionner avec l'environnement Node existant.

Le calcul du risque d'impact et les limites des niveaux de confiance sont documentés dans `docs/impact-analysis.md`.

Voir `docs/roadmap.md` pour la suite.
