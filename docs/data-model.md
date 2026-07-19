# Modèle de données

`Analysis` possède des `ProjectInfo`, qui possèdent des `CodeSymbol` et `ProjectReferenceInfo`. Les clés sont des GUID. Les recherches sont indexées par projet et nom, avec suppression en cascade depuis l'analyse.
