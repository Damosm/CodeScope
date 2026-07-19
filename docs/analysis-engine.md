# Moteur d'analyse

Le scanner exclut `.git`, `.vs`, `bin`, `obj`, `node_modules` et `packages`. Il ignore également les points de réanalyse Windows afin de ne pas suivre les jonctions ou liens symboliques circulaires. Les erreurs d'accès sont comptées comme avertissements et n'arrêtent pas les autres fichiers.

XML fournit frameworks et références ; Roslyn extrait types et méthodes. La complexité vaut 1 plus les branches, boucles, cas, expressions conditionnelles et opérateurs logiques. Les lignes correspondent à la portée syntaxique inclusive.

La progression publiée comprend l'étape, les projets terminés, les fichiers traités, les symboles détectés et les avertissements. L'annulation est vérifiée entre les projets et chaque fichier.
