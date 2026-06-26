# Guide d'importation

BookDB peut importer votre collection de livres existante depuis une sauvegarde Readerware — soit le fichier zip de sauvegarde lui-même, soit le dossier de sauvegarde extrait.

## Flux de l'assistant d'importation

1. **Sélection de fichier** — Choisir un fichier .zip de sauvegarde ou un dossier extrait
2. **Aperçu préalable** — Nombre de fiches, couverture des champs, doublons
3. **Paramètres** — Définir la collection cible et les options d'importation
4. **Progression d'importation** — Suivre la progression de l'importation des fiches
5. **Rapport final** — Examiner le rapport de résultats

## Instructions pas à pas

## Étape 1 — Sélectionner un fichier

Ouvrez l'assistant d'importation depuis **Fichier > Importer une sauvegarde Readerware…** ou la barre d'outils.

Cliquez sur **Parcourir** et sélectionnez l'un des éléments suivants :
- Un **zip de sauvegarde** Readerware (.zip) — une archive de sauvegarde créée avec la fonction *Sauvegarde* de Readerware
- Un **dossier de sauvegarde** Readerware — le contenu extrait d'un tel zip

Cliquez sur **Suivant** pour passer à l'aperçu préalable.

## Étape 2 — Aperçu préalable

Avant l'écriture des données, BookDB analyse la sauvegarde et affiche :
- **Nombre de fiches** — combien de livres ont été trouvés
- **Couverture des champs** — quels champs ont été détectés et combien de fiches ont chaque champ rempli
- **ISBN en double** — les ISBN déjà présents dans votre collection
- **Problèmes d'encodage** — les problèmes d'encodage de caractères trouvés dans le fichier

Examinez attentivement l'aperçu. Aucune donnée n'est importée tant que vous ne confirmez pas à l'étape 4.

Cliquez sur **Suivant** pour passer aux paramètres d'importation.

## Étape 3 — Options d'importation

**Collection cible** — choisissez à quelle collection (Fiction, Non-fiction, Bandes dessinées, etc.) les livres importés seront assignés. Vous pouvez le modifier ultérieurement en éditant des livres individuels.

**Gestion des doublons** — si un livre avec le même ISBN existe déjà dans votre collection, BookDB peut :
- Ignorer le doublon (par défaut)
- Écraser la fiche existante
- Vous demander à chaque fois

Cliquez sur **Suivant** pour démarrer l'importation.

## Étape 4 — Progression de l'importation

BookDB importe les fiches par lots. La barre de progression indique :
- Combien de fiches ont été traitées
- Les fiches ignorées ou en échec

Vous pouvez annuler l'importation à tout moment. Les fiches partiellement importées sont conservées.

## Étape 5 — Rapport d'importation

Le rapport final affiche :
- **Fiches importées** — enregistrées avec succès dans la base de données
- **Fiches ignorées** — doublons ou fiches avec des erreurs
- **Champs manquants** — champs vides dans le fichier d'importation
- **Problèmes d'encodage** — problèmes de caractères rencontrés

Cliquez sur **Terminer** pour fermer l'assistant. Votre liste de livres se rafraîchit automatiquement.

## Formats de fichiers pris en charge

| Format | Créé par | Remarques |
|--------|----------|-----------|
| Zip | Readerware > Sauvegarde | Archive de sauvegarde contenant les données des livres et les images de couverture |
| Dossier | Extraire le zip | Le contenu extrait d'un zip de sauvegarde Readerware |

## Images de couverture

Les images de couverture intégrées dans l'archive de sauvegarde sont importées automatiquement et associées à chaque livre.

## Plusieurs images du même type

Un livre peut se retrouver avec plus d'une image du même type — Readerware stocke souvent plusieurs images de couverture ou miniatures par livre, et elles peuvent toutes être importées comme le même type (par exemple, deux images de *Première de couverture*). BookDB conserve toutes les images, mais chaque type n'en affiche qu'une dans l'aperçu : celle dont l'ordre est le plus bas.

Ces livres sont signalés dans la liste par un badge **!** sur la miniature ("Types d'images en double — vérifiez l'onglet Images").

Pour y remédier, ouvrez le livre en modification et accédez à l'onglet **Images**. Dès qu'un type contient deux images ou plus, une section **Gérer toutes les images** apparaît, répertoriant chaque image. Pour chacune, vous pouvez :

- **La réassigner à un autre type d'image** — par exemple, redéfinir une deuxième *Première de couverture* en *Quatrième de couverture* ou *Dos*.
- **La déplacer vers le haut ou le bas au sein du type** — l'image du haut (à l'ordre le plus bas) devient l'aperçu de ce type.
- **Supprimer l'image**.

Enregistrez le livre pour conserver vos modifications. Lorsque chaque type ne contient au plus qu'une image, le badge **!** disparaît.

## Importer depuis une base de données Readerware active

Si vous n'avez pas de sauvegarde mais que vous disposez encore de votre base de données Readerware active (le dossier `.rw4`, par ex. `MyBooks.rw4`), BookDB peut la lire directement :

1. Ouvrez **Fichier > Importer une base de données Readerware…**.
2. Cliquez sur **Parcourir** et sélectionnez votre dossier de base de données `.rw4`.
3. Cliquez sur **Convertir**. BookDB copie d'abord la base de données — votre original n'est jamais ouvert ni modifié — et la convertit en un dossier de sauvegarde.
4. Une fois la conversion terminée, cliquez sur **Ouvrir l'assistant d'importation** pour poursuivre avec les mêmes étapes d'aperçu, de paramètres et d'importation décrites ci-dessus.

Cela nécessite une configuration unique : définissez le dossier d'outils HSQLDB + Java dans **Paramètres > Importation**. Ce dossier doit contenir `jre\bin\java.exe` et `lib\hsqldb.jar`.

### Version de Readerware prise en charge

Cette fonctionnalité prend en charge les bases de données **Readerware 4** — le format `DBCATALOG40`, stocké sous forme de base de données HSQLDB 1.8.x. Les images de couverture et de miniature au format **JPEG, PNG, GIF ou BMP** sont importées.

## Dépannage

**« Aucune fiche trouvée »** — Le fichier est peut-être vide ou n'est pas une sauvegarde Readerware valide. Vérifiez qu'il a été créé avec la fonction Sauvegarde de Readerware, et non pas une exportation.

**« Problèmes d'encodage détectés »** — BookDB gère l'encodage des caractères automatiquement. Si vous voyez des caractères illisibles dans l'aperçu, la sauvegarde est peut-être endommagée — essayez de créer une nouvelle sauvegarde depuis Readerware.

**De nombreux doublons s'affichent** — Si vous avez déjà importé certains livres par recherche ISBN, ils apparaîtront comme des doublons. Choisissez « Ignorer » pour éviter d'écraser vos fiches vérifiées manuellement.
