# Architecture

Application web locale en couches : Domain ne dépend de rien, Application expose les ports, Infrastructure fournit Roslyn, MSBuildWorkspace, SQLite et la file bornée, et Api compose l'ensemble. Le dépôt cible est toujours en lecture seule. De futurs analyseurs SQL et COBOL implémenteront le même contrat de source.

Le scanner cherche une solution sans suivre les jonctions ni les dossiers exclus, enregistre l'instance MSBuild locale une seule fois, puis effectue deux passes Roslyn : inventaire des symboles, puis résolution des relations. Les identifiants de documentation Roslyn assurent la correspondance des symboles entre compilations de projets référencés.

Des passes tolérantes complètent ensuite l'inventaire pour les endpoints et les scripts SQL. `ImpactAnalysisService` construit à la demande un graphe unifié C# et SQL, sans recopier ce graphe dans SQLite. `DocumentationGenerator` produit une page HTML déterministe à partir des résultats persistés et encode toutes les valeurs provenant du dépôt analysé.

`AnalysisJobQueue` conserve au maximum 20 travaux et un seul consommateur limite la pression mémoire. `AnalysisWorker` crée une portée de dépendances par travail, propage l'annulation au scanner et persiste les transitions `Pending`, `Running`, `Completed`, `Failed` et `Cancelled`. Un redémarrage marque explicitement les travaux interrompus en échec au lieu de laisser un état trompeur.
