# Bases de données distantes

Par défaut, BookDB enregistre votre bibliothèque dans un fichier SQLite local — aucune configuration n'est nécessaire. Pour accéder à la même bibliothèque depuis plusieurs ordinateurs, vous pouvez l'héberger sur un serveur de base de données : **PostgreSQL** ou **MySQL / MariaDB**. Toutes les fonctions de BookDB se comportent de la même manière quel que soit l'emplacement de la bibliothèque.

## Choisir le moteur de base de données

Ouvrez **Outils › Paramètres › Base de données**. Sous **Moteur de base de données**, vous avez le choix entre :

- **Fichier local (SQLite)** — la bibliothèque par défaut pour un seul ordinateur
- **Serveur PostgreSQL**
- **Serveur MySQL / MariaDB**

Les options serveur nécessitent un trousseau du système d'exploitation (un coffre sécurisé pour les identifiants). BookDB conserve le mot de passe du serveur **uniquement** dans le trousseau — il n'est jamais écrit dans un fichier de configuration et il n'existe aucun repli en texte clair. Si aucun trousseau n'est disponible sur votre système, les options serveur sont désactivées.

Pour une connexion à un serveur, renseignez :

- **Hôte** et **port** — le port par défaut est 5432 pour PostgreSQL et 3306 pour MySQL/MariaDB
- Le nom de la **base de données**
- **Nom d'utilisateur** et **mot de passe**
- **Mode TLS/SSL** — les options proposées dépendent du moteur choisi

Si un mot de passe est déjà enregistré pour la connexion, une indication le signale et le champ du mot de passe peut rester vide.

**Tester la connexion** vérifie les paramètres avant l'enregistrement. En cas de succès, la version du serveur et le nombre de livres de la base sont affichés. En cas d'échec, la cause est précisée : identifiants incorrects, connexion refusée, délai dépassé, problème TLS ou version de serveur non prise en charge.

L'enregistrement d'un changement de moteur vous invite à redémarrer — **le nouveau moteur ne prend effet qu'après le redémarrage de BookDB**. Si le serveur est injoignable au prochain démarrage de BookDB, une boîte de dialogue propose **Réessayer**, **Ouvrir les paramètres** ou **Quitter**.

## Versions de serveur requises

- **PostgreSQL 12 ou ultérieur** — nécessaire à la recherche en texte intégral
- **MySQL 8.0 ou ultérieur** / **MariaDB 10.6 ou ultérieur**

La version est vérifiée lors du test de connexion puis au démarrage ; un serveur trop ancien est refusé avec un message indiquant la version requise.

## Déplacer la bibliothèque d'un moteur à l'autre

**Outils › Maintenance › Déplacer la bibliothèque** copie l'intégralité de la bibliothèque entre deux moteurs quelconques — par exemple du fichier SQLite local vers un nouveau serveur PostgreSQL, ou inversement.

Le déplacement est conçu pour être sûr :

- Une **sauvegarde CSV de sécurité de la source** est toujours réalisée avant toute copie.
- Si la base de données cible contient déjà des données, BookDB sauvegarde également la cible, et le déplacement ne commence qu'après avoir coché explicitement **Je comprends — remplacer toutes les données dans la base de données cible**.
- Le schéma d'une cible vide est créé automatiquement.
- Après la copie, les nombres de lignes de la source et de la cible sont comparés ; le déplacement n'est considéré comme terminé que lorsqu'ils concordent.
- En option, BookDB bascule la base de données active vers la cible et redémarre.

## Utiliser la bibliothèque depuis plusieurs ordinateurs

Une bibliothèque sur serveur tient à jour la liste des clients BookDB connectés grâce à un signal de présence toutes les 60 secondes :

- Si un autre client semble connecté au démarrage, BookDB vous avertit. Vous pouvez **Quitter** ou choisir **Se connecter quand même** — ce bouton devient disponible après un délai de 3 secondes.
- Un client qui s'est arrêté brutalement sans se déconnecter cesse d'être compté comme connecté au bout de 3 minutes environ.

Indépendamment de la session serveur, une seule instance de BookDB peut s'exécuter à la fois par utilisateur sur un même ordinateur — en lancer une seconde ramène au premier plan la fenêtre déjà ouverte.

Si la connexion au serveur est perdue pendant votre travail, BookDB vous en informe et propose **Continuer d'attendre** (recommandé) ou **Quitter**.

## Sauvegardes d'une bibliothèque sur serveur

La sauvegarde sous forme de fichier ne concerne que le fichier SQLite local. Lorsque la bibliothèque est sur un serveur, **Sauvegarde...** et les sauvegardes automatiques produisent toujours l'**archive CSV** — la boîte de dialogue l'indique au lieu d'échouer. Une sauvegarde de fichier SQLite ne peut pas être restaurée dans une bibliothèque sur serveur ; utilisez une archive CSV ou revenez d'abord à la base de données locale.
