# Modèle de données

`Analysis` possède des `ProjectInfo`, qui possèdent des `CodeSymbol`, `ProjectReferenceInfo` et `PackageReferenceInfo`. Une analyse possède aussi des `CodeRelation`, `SqlObject`, `SqlReference` et `ApiEndpoint`. Chaque relation référence sa source, éventuellement une cible interne, les noms qualifiés des deux extrémités, sa nature, sa confiance et l'emplacement de la première occurrence. Les clés sont des GUID. Les recherches sont indexées par projet et nom ; les relations le sont par analyse/source et analyse/cible, avec suppression en cascade depuis l'analyse.

Le statut d'une analyse est persisté. La progression fine est volontairement volatile et conservée par la file en mémoire ; elle ne gonfle donc pas SQLite avec une écriture par fichier. Les résultats complets ne sont attachés à l'analyse qu'après succès, dans une unité de travail EF Core.

Au démarrage, un initialiseur idempotent crée les nouvelles tables et leurs index lorsqu'une base provenant de la première version existe déjà. Les anciennes analyses restent consultables ; il suffit de les relancer pour produire relations sémantiques, objets SQL, packages et endpoints.
