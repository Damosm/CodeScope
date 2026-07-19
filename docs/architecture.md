# Architecture

Application web locale en couches : Domain ne dépend de rien, Application expose les ports, Infrastructure fournit Roslyn, SQLite et la file bornée, et Api compose l'ensemble. Le dépôt cible est toujours en lecture seule. De futurs analyseurs SQL et COBOL implémenteront le même contrat de source.

`AnalysisJobQueue` conserve au maximum 20 travaux et un seul consommateur limite la pression mémoire. `AnalysisWorker` crée une portée de dépendances par travail, propage l'annulation au scanner et persiste les transitions `Pending`, `Running`, `Completed`, `Failed` et `Cancelled`. Un redémarrage marque explicitement les travaux interrompus en échec au lieu de laisser un état trompeur.
