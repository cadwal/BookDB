# À propos des sources de données

Lorsque vous cataloguez un livre par ISBN (Ctrl+I ou le bouton de la barre d'outils), BookDB récupère simultanément les métadonnées de trois API publiques.

## Flux de recherche

1. Vous saisissez l'ISBN
2. BookDB interroge les quatre sources en parallèle — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. La boîte de dialogue **Vérification de fusion** s'ouvre — vous choisissez quels champs accepter de chaque source
4. Fiche livre enregistrée

## Google Books

**URL :** https://books.google.com (API : books.googleapis.com)

Google Books est la plus grande base de données généraliste, avec une large couverture des titres anglophones et des titres internationaux populaires.

**Champs généralement fournis :**
- Titre, Sous-titre, Auteurs
- Éditeur, Date de publication
- Description (Informations sur le livre)
- Nombre de pages
- Langue
- ISBN-10 et ISBN-13
- Image de couverture (miniature et grande)
- Catégories

**Remarques :**
- Aucune clé API requise pour les recherches de base
- La couverture est la plus forte pour les publications commerciales après 1980
- Les noms d'auteurs peuvent ne pas toujours correspondre au format souhaité

## Open Library

**URL :** https://openlibrary.org

Open Library est un catalogue en accès libre maintenu par l'Internet Archive. Il privilégie l'exhaustivité sur la qualité — les fiches peuvent avoir plus de champs mais une mise en forme moins homogène.

**Champs généralement fournis :**
- Titre, Auteurs
- Éditeur, Date de publication, Lieu de publication
- Nombre de pages
- ISBN, LCCN, Dewey
- Image de couverture

**Remarques :**
- Maintenu par la communauté — la qualité des données varie
- Particulièrement utile pour les livres anciens ou épuisés
- Fournit souvent des identifiants (LCCN, Dewey) que Google Books ne propose pas

## Libris KB

**URL :** https://libris.kb.se

Libris est le catalogue national suédois, maintenu par la Bibliothèque nationale de Suède (Kungliga biblioteket). Il offre une excellente couverture des publications suédoises et des traductions en suédois.

**Champs généralement fournis :**
- Titre, Auteurs
- Éditeur, Année de publication
- Langue
- ISBN
- Informations de série
- Dewey, Cote

**Remarques :**
- Meilleure source pour les livres publiés en Suède ou traduits en suédois
- Les descriptions et résumés peuvent être en suédois
- La couverture des titres non suédois est limitée

## IsbnSearch.org

**URL :** https://isbnsearch.org

IsbnSearch.org est un service gratuit de recherche ISBN qui fournit des données bibliographiques de base extraites de ses pages web. Il sert de source complémentaire utile pour les ISBN qui ne donnent aucun résultat dans les sources basées sur des API.

**Champs généralement fournis :**
- Titre, Auteurs
- Éditeur, Date de publication
- Image de couverture

**Remarques :**
- Les données sont extraites par analyse HTML — la mise en forme peut être moins homogène que les sources basées sur des API
- Mieux utilisé comme source complémentaire aux côtés de Google Books, Open Library et Libris KB
## Vérification de fusion

Après que BookDB a récupéré les résultats de toutes les sources disponibles, la boîte de dialogue **Vérification de fusion** affiche tous les champs récupérés côte à côte :

| Champ | Actuel | Google Books | Open Library | Libris KB |
|-------|--------|-------------|--------------|-----------|
| Titre | — | The Great... | The Great... | — |
| Auteur | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Éditeur | — | Scribner | — | — |
| Pages | — | 180 | 172 | — |

Pour chaque champ, vous pouvez :
- **Accepter** une valeur d'une source (cliquer sur la valeur pour la sélectionner)
- **Conserver** votre valeur actuelle
- **Tout accepter** pour reprendre toutes les valeurs entrantes en une seule fois

Lorsque vous cliquez sur **Enregistrer**, seuls les champs que vous avez acceptés sont mis à jour. Vos données existantes ne sont jamais écrasées automatiquement.

## Quand une source ne renvoie aucun résultat

Si une source ne renvoie aucun résultat pour un ISBN :
- La colonne de source est simplement absente du tableau de fusion
- Les autres sources ne sont pas affectées
- C'est normal pour les livres récents, les publications régionales ou les ISBN inhabituels

## Limites de fréquence

BookDB respecte automatiquement les limites de fréquence de chaque API. Lors du recatalogage en masse (Outils > Recataloguer), les requêtes sont espacées afin que vous ne soyez jamais bloqué par aucune source.
