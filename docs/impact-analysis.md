# Analyse d'impact

L'impact est calculé à la demande sur un graphe unifié. Les symboles C# sont reliés par appels, héritages, implémentations et instanciations. Les objets SQL sont reliés par lecture, écriture, jointure et exécution ; les chaînes C# peuvent relier un symbole à un objet SQL avec une confiance textuelle.

Le parcours explore les relations dans les deux sens : dépendances utilisées par l'élément et éléments qui en dépendent. La profondeur est réglable de 1 à 4 dans l'interface et le résultat est borné à 250 nœuds. La confiance d'un chemin est celle de son maillon le plus faible : `Certain`, puis `Probable`, puis `Textual`.

Les chemins critiques sont les chemins les plus profonds atteignant une feuille du sous-graphe visité. CodeScope en restitue au plus dix, triés par profondeur puis par nom. Ils servent à repérer rapidement les chaînes de dépendances longues ; il ne s'agit pas d'un calcul de durée d'exécution.

## Score de risque

Le score est la somme suivante :

- 2 points par relation directe ;
- 1 point par relation indirecte ;
- 3 points par projet concerné ;
- 2 points par fichier de test concerné ;
- 2 points par opération SQL d'écriture liée à l'objet sélectionné ;
- 1 point par tranche entière de 5 au-dessus de 1 pour la complexité de la méthode racine ;
- jusqu'à 5 points supplémentaires pour les résultats probables ou textuels.

Les niveaux sont `Low` de 0 à 5, `Medium` de 6 à 15, `High` de 16 à 30 et `Critical` au-delà. Le rapport restitue les facteurs calculés ; le score est une aide au triage, pas une preuve qu'une modification cassera un composant.
