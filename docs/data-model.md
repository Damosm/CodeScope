# Modèle de données

`Analysis` possède des `ProjectInfo`, qui possèdent des `CodeSymbol`, `ProjectReferenceInfo` et `PackageReferenceInfo`. Une analyse possède aussi des `CodeRelation`, `SourceFileInfo`, `RepositorySnapshot`, `SqlObject`, `SqlColumn`, `SqlReference`, `SqlColumnReference`, `ApiEndpoint`, `CobolSymbol`, `CobolRelation` et `AnalysisDiagnostic`. Chaque relation référence sa source, éventuellement une cible interne, les noms qualifiés des deux extrémités, sa nature, sa confiance et l'emplacement de la première occurrence. Les clés sont des GUID. Les recherches sont indexées par projet, chemin et nom ; les relations et diagnostics le sont par analyse, source/cible ou niveau, avec suppression en cascade depuis l'analyse.

Le statut d'une analyse est persisté. La progression fine est volontairement volatile et conservée par la file en mémoire ; elle ne gonfle donc pas SQLite avec une écriture par fichier. Les résultats complets ne sont attachés à l'analyse qu'après succès, dans une unité de travail EF Core.

Au démarrage, un initialiseur idempotent crée les nouvelles tables et leurs index lorsqu'une base provenant d'une version antérieure existe déjà. Les anciennes analyses restent consultables ; il suffit de les relancer pour produire l'inventaire, les colonnes, les instantanés Git et les données COBOL.
