# Règles agents

- Le dossier analysé est toujours en lecture seule.
- Maintenir les dépendances Domain <- Application <- Infrastructure/API.
- Toute métrique nouvelle doit documenter son calcul et être testée.
- Ne jamais envoyer du code analysé vers un service externe ni journaliser des secrets.
- Exécuter build et tests avant d'annoncer une fonctionnalité terminée.
