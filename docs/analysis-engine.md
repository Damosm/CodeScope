# Moteur d'analyse

Le scanner exclut `.git`, `.vs`, `bin`, `obj`, `node_modules` et `packages`. Il ignore également les points de réanalyse Windows afin de ne pas suivre les jonctions ou liens symboliques circulaires. Les erreurs d'accès sont comptées comme avertissements et n'arrêtent pas les autres fichiers.

Lorsqu'une solution `.sln` est présente, `MSBuildWorkspace` la charge avec le MSBuild local. Roslyn résout alors les types et méthodes entre projets et produit quatre relations : `Calls`, `Inherits`, `Implements` et `Creates`. Une cible appartenant à la solution reçoit son identifiant `CodeSymbol`; une cible de framework ou de package reste externe mais conserve son nom qualifié. Si la solution ne peut pas être chargée, le scanner revient à l'analyse syntaxique par `.csproj` sans arrêter le travail complet.

Les relations sont dédupliquées par triplet source, nature et cible. La métrique « Relations sémantiques » compte donc les relations distinctes, et non le nombre d'occurrences dans le texte. « Appels distincts » est le sous-ensemble de nature `Calls`. Ces relations ont une confiance `Certain`, car elles proviennent du modèle sémantique Roslyn.

XML fournit les frameworks ; Roslyn extrait espaces de noms, types, constructeurs, méthodes, propriétés et champs. La complexité cyclomatique simplifiée vaut 1 plus les branches, boucles, cas, expressions conditionnelles et opérateurs logiques. Les lignes correspondent à la portée syntaxique inclusive. « Propriétés C# » compte les symboles Roslyn de nature `Property` ; les indexeurs ne sont pas inclus dans cette première version.

L'inventaire parcourt tous les fichiers hors dossiers exclus. « Fichiers » compte les chemins relatifs uniques. Pour chacun, CodeScope conserve taille, catégorie, nombre de lignes pour les formats texte reconnus, date de modification et SHA-256 calculé en flux. Git est interrogé uniquement par commandes de lecture (`rev-parse`, branche et état porcelain) ; aucun nom de fichier Git ni contenu source n'est journalisé.

Les `PackageReference` sont lus dans les `.csproj`, y compris une version portée par un élément enfant. Les endpoints de contrôleur sont détectés à partir de `Route` et des attributs `HttpGet`, `HttpPost`, `HttpPut`, `HttpDelete`, `HttpPatch`, `HttpHead` et `HttpOptions`. Une route explicite est certaine ; une route conventionnelle est probable. Les appels Minimal API `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch` et `MapMethods` sont classés probables, car cette passe reste syntaxique.

## Analyse SQL tolérante

Le scanner SQL masque les commentaires et chaînes tout en conservant les numéros de ligne, puis reconnaît `CREATE`/`CREATE OR ALTER` pour les tables, vues, procédures, fonctions et triggers. Il extrait les cibles de `FROM`, `JOIN`, `INSERT`, `UPDATE`, `DELETE` et `EXEC`. Pour `CREATE TABLE`, il découpe les définitions au niveau supérieur afin de relever nom, type, ordre et nullabilité des colonnes. Les listes de `SELECT`, `INSERT` et `UPDATE SET` produisent des références de colonnes lorsqu'une table et une colonne uniques sont résolues.

Une cible reliée à une définition unique est certaine ; une cible non résolue est probable. Les chaînes C# littérales, interpolées et assemblées par concaténation sont inspectées. Une occurrence de code reste toujours textuelle, même si le nom correspond à un objet ou une colonne connus, car sa valeur finale peut dépendre de l'exécution.

Dans les `SELECT` multi-tables, les sources `FROM`/`JOIN` et leurs alias sont associées aux objets SQL. Une colonne qualifiée (`o.Id`) est attribuée à la source de l'alias ; une colonne non qualifiée n'est retenue que si elle existe dans une seule source du `SELECT`. Cela évite d'attribuer arbitrairement une colonne ambiguë à la première table.

Cette approche accepte les scripts incomplets et les variables de déploiement sans exiger une grammaire SQL Server complète. Elle ne prétend pas résoudre les procédures stockées générées à l'exécution, les alias ambigus ni toutes les variantes dialectales.

## Correspondances EF Core

Le scanner reconnaît les attributs `[Table]` et `[Column]`, les appels Fluent API `ToTable` et `HasColumnName`, les classes `IEntityTypeConfiguration<TEntity>` et les propriétés `DbSet<TEntity>`. Fluent API prend la priorité sur les annotations, conformément au comportement d'EF Core ; les conventions `DbSet`/nom de propriété servent de repli.

Une correspondance explicite dont les deux extrémités sont résolues est `Certain`. Une convention résolue est `Probable`. Une entité, table, propriété ou colonne absente du périmètre reste `Textual`. Les métriques « Entités ORM » et le graphe `orm` utilisent ces correspondances ; l'analyse d'impact relie directement classes/propriétés C# et objets SQL.

## Analyse COBOL tolérante

Les extensions `.cbl`, `.cob` et `.cpy` sont reconnues. Le scanner relève `PROGRAM-ID`, sections, paragraphes, copybooks, `CALL` et `COPY`. Une relation vers un programme/copybook présent dans le périmètre est certaine ; une cible absente est probable. Les formats préprocesseur et les constructions dépendant d'un compilateur COBOL particulier peuvent nécessiter une validation manuelle.

## Comparaison incrémentale

La comparaison aligne les chemins relatifs et leurs SHA-256 : un même chemin avec un hash différent est modifié, un chemin présent d'un seul côté est ajouté ou supprimé, sinon il est inchangé. Les symboles, endpoints, objets SQL et symboles COBOL sont comparés par clés stables. Les commits Git capturés contextualisent le résultat. Cette optimisation identifie le delta ; le scanner sémantique reste complet à chaque lancement.

La progression publiée comprend l'étape, les projets terminés, les fichiers traités, les symboles détectés et les avertissements. L'annulation est vérifiée entre les projets et chaque fichier.

## Diagnostics persistants

Les problèmes non bloquants sont enregistrés sous forme de diagnostics avec niveau, code stable, étape, message sûr et emplacement éventuel. Les messages ne contiennent ni source ni texte d'exception arbitraire. La métrique « Diagnostics » compte toutes les entrées ; l'interface peut filtrer `Info`, `Warning` et `Error`. Les avertissements sont également inclus dans l'export SARIF sous la règle `CSCOPE004`.
