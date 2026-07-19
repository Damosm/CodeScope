# Moteur d'analyse

Le scanner exclut `.git`, `.vs`, `bin`, `obj`, `node_modules` et `packages`. Il ignore également les points de réanalyse Windows afin de ne pas suivre les jonctions ou liens symboliques circulaires. Les erreurs d'accès sont comptées comme avertissements et n'arrêtent pas les autres fichiers.

Lorsqu'une solution `.sln` est présente, `MSBuildWorkspace` la charge avec le MSBuild local. Roslyn résout alors les types et méthodes entre projets et produit quatre relations : `Calls`, `Inherits`, `Implements` et `Creates`. Une cible appartenant à la solution reçoit son identifiant `CodeSymbol`; une cible de framework ou de package reste externe mais conserve son nom qualifié. Si la solution ne peut pas être chargée, le scanner revient à l'analyse syntaxique par `.csproj` sans arrêter le travail complet.

Les relations sont dédupliquées par triplet source, nature et cible. La métrique « Relations sémantiques » compte donc les relations distinctes, et non le nombre d'occurrences dans le texte. « Appels distincts » est le sous-ensemble de nature `Calls`. Ces relations ont une confiance `Certain`, car elles proviennent du modèle sémantique Roslyn.

XML fournit les frameworks ; Roslyn extrait types et méthodes. La complexité cyclomatique simplifiée vaut 1 plus les branches, boucles, cas, expressions conditionnelles et opérateurs logiques. Les lignes correspondent à la portée syntaxique inclusive.

Les `PackageReference` sont lus dans les `.csproj`, y compris une version portée par un élément enfant. Les endpoints de contrôleur sont détectés à partir de `Route` et des attributs `HttpGet`, `HttpPost`, `HttpPut`, `HttpDelete`, `HttpPatch`, `HttpHead` et `HttpOptions`. Une route explicite est certaine ; une route conventionnelle est probable. Les appels Minimal API `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch` et `MapMethods` sont classés probables, car cette passe reste syntaxique.

## Analyse SQL tolérante

Le scanner SQL masque les commentaires et chaînes tout en conservant les numéros de ligne, puis reconnaît `CREATE`/`CREATE OR ALTER` pour les tables, vues, procédures, fonctions et triggers. Il extrait les cibles de `FROM`, `JOIN`, `INSERT`, `UPDATE`, `DELETE` et `EXEC`. Une cible reliée à une définition unique est certaine ; une cible non résolue est probable. Une occurrence trouvée dans une chaîne C# est toujours textuelle, même si le nom correspond à un objet connu.

Cette approche accepte les scripts incomplets et les variables de déploiement sans exiger une grammaire SQL Server complète. Elle ne prétend pas encore résoudre les colonnes, le SQL dynamique construit par concaténation ni toutes les variantes dialectales.

La progression publiée comprend l'étape, les projets terminés, les fichiers traités, les symboles détectés et les avertissements. L'annulation est vérifiée entre les projets et chaque fichier.
