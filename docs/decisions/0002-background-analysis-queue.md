# ADR 0002 — File d'analyse locale bornée

Accepté. Le MVP utilise un `Channel` borné à 20 travaux et un `BackgroundService` avec un consommateur unique. Cette solution ne requiert aucun service externe, permet la progression et l'annulation, et protège la mémoire sur les grandes solutions.

La progression détaillée reste en mémoire ; les statuts et résultats sont persistés dans SQLite. Après un arrêt du processus, un travail incomplet est marqué en échec au prochain démarrage. Une file durable multi-processus n'est justifiée que si un futur besoin de reprise automatique apparaît.
