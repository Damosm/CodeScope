# Architecture

Application web locale en couches : Domain ne dépend de rien, Application expose les ports, Infrastructure fournit Roslyn et SQLite, et Api compose l'ensemble. Le dépôt cible est toujours en lecture seule. De futurs analyseurs SQL et COBOL implémenteront le même contrat de source.
