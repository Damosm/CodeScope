# Modèle de données

`Analysis` possède des `ProjectInfo`, qui possèdent des `CodeSymbol` et `ProjectReferenceInfo`. Les clés sont des GUID. Les recherches sont indexées par projet et nom, avec suppression en cascade depuis l'analyse.

Le statut d'une analyse est persisté. La progression fine est volontairement volatile et conservée par la file en mémoire ; elle ne gonfle donc pas SQLite avec une écriture par fichier. Les résultats complets ne sont attachés à l'analyse qu'après succès, dans une unité de travail EF Core.
