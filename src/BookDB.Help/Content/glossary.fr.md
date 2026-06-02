# Glossaire des champs

Descriptions de tous les champs dans BookDB. Les champs marqués comme *facultatifs* n'ont pas besoin d'être remplis pour enregistrer un livre.

## Informations sur le titre

| Champ | Description |
|-------|-------------|
| Titre | Le titre principal du livre. Obligatoire. |
| Sous-titre | Un titre secondaire, généralement affiché sous le titre principal en couverture. *Facultatif.* |
| Titre alternatif | Un titre alternatif ou en langue d'origine (p. ex. le titre anglais d'une œuvre traduite). *Facultatif.* |

## Contributeurs

| Champ | Description |
|-------|-------------|
| Auteurs / Contributeurs | Les personnes ayant participé à la création du livre — Auteur, Éditeur, Illustrateur, Designer et autres rôles. Chaque contributeur est une fiche personne liée au livre avec un rôle. |

## Détails de publication

| Champ | Description |
|-------|-------------|
| Éditeur | La maison d'édition qui a publié le livre. *Facultatif.* |
| Lieu de publication | La ville ou le pays de publication. *Facultatif.* |
| Date de publication | L'année de publication. Stocké en texte pour prendre en charge les dates partielles ou approximatives comme « ca. 1950 ». *Facultatif.* |
| Date de copyright | L'année du copyright, qui peut différer de la date de publication pour les éditions ultérieures. *Facultatif.* |
| Format | Le format physique : Relié, Broché, Grand caractère, etc. *Facultatif.* |
| Édition | L'édition du livre : Première, Deuxième, Révisée, etc. *Facultatif.* |
| Pages | Le nombre total de pages. *Facultatif.* |
| Langue | La langue du texte du livre. *Facultatif.* |

## Identifiants

| Champ | Description |
|-------|-------------|
| ISBN | Le Numéro international normalisé du livre (ISBN-10 ou ISBN-13). Utilisé pour la recherche de métadonnées et la détection des doublons. *Facultatif.* |
| ISSN | Le Numéro international normalisé des publications en série, pour les périodiques. *Facultatif.* |
| LCCN | Numéro de contrôle de la Bibliothèque du Congrès. *Facultatif.* |
| Classification décimale de Dewey | Code de classification décimale de Dewey. *Facultatif.* |
| Cote | La cote de bibliothèque pour la localisation en rayon. *Facultatif.* |

## Série

| Champ | Description |
|-------|-------------|
| Série | La série à laquelle appartient le livre, le cas échéant. *Facultatif.* |
| Numéro de série | La position de ce livre dans la série (p. ex. « 3 » ou « 3.5 »). *Facultatif.* |

## Votre exemplaire

| Champ | Description |
|-------|-------------|
| Exemplaires | Le nombre d'exemplaires physiques que vous possédez. Par défaut : 1. |
| État | L'état physique de votre exemplaire : Très bon, Bon, Acceptable, Médiocre, etc. *Facultatif.* |
| Emplacement | L'étagère, la pièce ou l'emplacement de stockage où se trouve cet exemplaire. *Facultatif.* |
| Propriétaire | Le propriétaire de cet exemplaire (utile pour les collections partagées). *Facultatif.* |
| Dédicacé | Indique si cet exemplaire est dédicacé. |
| Épuisé | Indique si le livre est marqué comme épuisé. |

## Suivi de lecture

| Champ | Description |
|-------|-------------|
| Statut | Votre statut de lecture : À lire, En cours, Lu, Abandonné, etc. *Facultatif.* |
| Nombre de lectures | Le nombre de fois que vous avez lu ce livre. |
| Dernière lecture | La date à laquelle vous avez terminé ce livre pour la dernière fois. *Facultatif.* |
| Note | Votre note personnelle. *Facultatif.* |
| Favori | Indique si ce livre est marqué comme favori. |
| Niveau de lecture | Le niveau de lecture visé (âge ou classe). *Facultatif.* |

## Achat et valeur

| Champ | Description |
|-------|-------------|
| Prix d'achat | Le prix payé pour cet exemplaire. *Facultatif.* |
| Devise d'achat | La devise du prix d'achat (p. ex. EUR, USD, SEK). *Facultatif.* |
| Lieu d'achat | L'endroit où vous avez acheté le livre. *Facultatif.* |
| Date d'achat | La date à laquelle vous avez acheté le livre. *Facultatif.* |
| Prix conseillé | Le prix de vente conseillé par l'éditeur. *Facultatif.* |
| Devise du prix conseillé | La devise du prix conseillé. *Facultatif.* |
| Valeur de l'exemplaire | La valeur monétaire estimée de cet exemplaire (p. ex. pour les assurances). *Facultatif.* |
| Date d'évaluation | La date à laquelle la valeur a été évaluée. *Facultatif.* |

## Description et notes

| Champ | Description |
|-------|-------------|
| Mots-clés | Mots-clés libres pour votre usage personnel. *Facultatif.* |
| Notes | Vos notes personnelles sur ce livre. *Facultatif.* |
| Informations sur le livre | Une description étendue ou un résumé. *Facultatif.* |
| Dimensions | Les dimensions physiques du livre (p. ex. « 24 × 16 × 3 cm »). *Facultatif.* |
| Poids | Le poids physique du livre. *Facultatif.* |

## Champs système et source

| Champ | Description |
|-------|-------------|
| Source | L'origine de la fiche catalogue (p. ex. Importé, Manuel, Recherche ISBN). *Facultatif.* |
| Lien média | Une URL vers des médias associés ou la page de l'éditeur pour ce livre. *Facultatif.* |
| Catégories | Les catégories de collection auxquelles appartient ce livre (p. ex. Fiction, Bandes dessinées). Géré dans le panneau de filtres. |
| Ajouté | La date et l'heure de création de cette fiche dans BookDB. Défini automatiquement. |
| Mis à jour | La date et l'heure de la dernière modification. Mis à jour automatiquement lors de l'enregistrement. |
