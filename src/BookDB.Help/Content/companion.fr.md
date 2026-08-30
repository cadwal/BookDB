# Compagnon

Le compagnon de BookDB vous permet de cataloguer des livres depuis un **téléphone ou une tablette** sur le même réseau local. Vous scannez un code-barres ISBN (et photographiez éventuellement la couverture) sur l'appareil, et le livre arrive dans la bibliothèque qui s'exécute sur cet ordinateur. Rien ne quitte votre réseau : l'appareil communique directement avec BookDB via votre Wi-Fi, jamais par internet ni par un serveur externe.

Le compagnon est **désactivé par défaut**. Vous l'activez, associez chaque appareil une fois, et à partir de là l'appareil se reconnecte tout seul dès que les deux sont sur le même réseau.

## Activer le compagnon

Ouvrez **Outils › Paramètres › Compagnon** et cochez **Activer le compagnon**. Le réglage prend effet lorsque vous appuyez sur **Enregistrer** — et non au moment où vous cochez la case — afin que vous puissiez d'abord ajuster le port et les options de capture, puis tout appliquer d'un coup.

Une fois en marche, la ligne **État** indique que le compagnon est à l'écoute. S'il ne peut pas démarrer — le plus souvent parce que le port est déjà utilisé — la boîte de dialogue reste ouverte sur l'onglet Compagnon et vous en indique la raison, et le réglage reste activé pour que vous puissiez changer le port et réessayer.

Vous pouvez laisser le compagnon activé entre les sessions ; il démarre automatiquement avec BookDB tant que le réglage est activé.

### Réglages

- **Port** — le port réseau sur lequel le compagnon écoute (par défaut **7443**). Ne le changez que si un autre programme utilise déjà ce port. Si vous le changez, il n'est pas nécessaire de réassocier, mais l'appareil peut mettre un instant à redécouvrir BookDB sur le nouveau port.
- **Taille maximale d'image** et **qualité JPEG** — comment les photos de couverture et de page sont redimensionnées et compressées avant d'être stockées. Des valeurs plus basses économisent de l'espace ; des valeurs plus élevées conservent plus de détails. Cela s'applique aux photos prises sur l'appareil.

## Associer un appareil

Un appareil doit être associé une fois avant de pouvoir envoyer des livres. L'association échange un ensemble de certificats de sécurité afin que seuls les appareils que **vous** avez approuvés puissent se connecter, et tout ce qui passe entre eux est chiffré.

1. Assurez-vous que le compagnon est activé et en marche, et que l'appareil est sur le **même réseau Wi-Fi** que cet ordinateur.
2. Ouvrez **Outils › Maintenance › Appareils** et appuyez sur **Afficher le code d'association…**. (Il y a aussi un bouton de gestion des appareils sur l'onglet Paramètres ▸ Compagnon qui ouvre le même endroit.)
3. Une fenêtre affiche un **code QR**. Dans l'application BookDB sur l'appareil, choisissez d'associer et scannez le code.
4. Lorsque l'appareil se connecte, la fenêtre confirme l'association. Vous pouvez associer un autre appareil ou fermer la fenêtre.

Le code d'association est **à usage unique et de courte durée** : il se renouvelle tout seul toutes les deux minutes environ et ne peut être utilisé qu'une fois. Si un code expire avant que vous le scanniez, un nouveau apparaît automatiquement. Si l'adresse réseau choisie automatiquement n'est pas celle que l'appareil peut atteindre, sélectionnez-en une autre dans la liste déroulante avant de scanner.

Vous pouvez associer jusqu'à **cinq appareils**. Chacun apparaît dans la liste des appareils avec le nom qui lui a été donné, la date d'association et la dernière utilisation. Supprimez un appareil avec **Supprimer** pour libérer sa place ; un appareil supprimé doit être réassocié avant de pouvoir se reconnecter.

## Le pare-feu

Au premier démarrage du compagnon, Windows et macOS demandent s'il faut autoriser BookDB à accepter les connexions entrantes. Vous devez l'autoriser, sinon les appareils ne pourront pas atteindre BookDB. **Sous Linux, rien ne vous le demande** : si un pare-feu est actif, vous devez ouvrir les ports vous-même.

- **Windows** — autorisez BookDB sur les **réseaux privés** (votre réseau domestique ou de bureau). Vous n'avez **pas** besoin de l'autoriser sur les réseaux publics. Si vous avez fermé l'invite ou cliqué sur *Annuler*, aucune connexion ne passera ; autorisez-le de nouveau dans **Sécurité Windows › Pare-feu et protection du réseau › Autoriser une application via le pare-feu**, cochez la case **Privé** de BookDB, ou supprimez la règle de blocage de BookDB pour que l'invite réapparaisse la prochaine fois.
- **macOS** — autorisez les connexions entrantes pour BookDB lorsque cela vous est demandé. Vous pouvez le revoir plus tard dans **Réglages Système › Réseau › Pare-feu**.
- **Linux** — aucune invite n'apparaît. Si `ufw` ou `firewalld` est actif, les ports entrants sont bloqués par défaut, et le port du compagnon (**7443** par défaut) doit être ouvert en **TCP et en UDP** : le TCP porte la connexion, et l'UDP permet à un appareil de retrouver cet ordinateur lorsque son adresse change. N'ouvrir que le TCP laisse l'appairage et la navigation fonctionner tandis que la redécouverte échoue silencieusement — ce que l'on remarque généralement bien plus tard, comme une panne intermittente après un changement d'adresse. Pour `ufw` : `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, en remplaçant `192.168.1.0/24` par votre propre réseau. Pour `firewalld` : `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Si vous avez changé le port du compagnon, utilisez votre propre port dans les deux commandes.

Le compagnon n'écoute jamais que sur votre réseau local, et seulement tant qu'il est activé.

## Si un appareil ne peut pas se connecter

- **Les deux sur le même réseau ?** L'appareil et cet ordinateur doivent être sur le même Wi-Fi. Un réseau « invité » est généralement isolé du réseau principal et ne fonctionnera pas.
- **Pare-feu** — vérifiez les notes sur le pare-feu ci-dessus ; un port bloqué est la cause la plus fréquente.
- **Compagnon en marche ?** La ligne État sur l'onglet Paramètres ▸ Compagnon doit l'indiquer comme en marche. S'il n'a pas pu démarrer, changez le port et enregistrez de nouveau.
- **Toujours associé ?** Si l'appareil a été retiré de la liste des appareils, ou si cela fait longtemps, réassociez-le avec un nouveau code.
- **Veille** — si cet ordinateur s'est mis en veille, le compagnon reprend au réveil ; l'appareil se reconnecte tout seul dès que les deux sont réveillés et sur le réseau.

## Ce que l'appareil peut et ne peut pas faire

Un appareil associé peut scanner des ISBN, prendre des photos de couverture et de page, les envoyer pour catalogage et parcourir la bibliothèque pour vérifier si vous possédez déjà un livre. Il travaille sur la bibliothèque actuellement ouverte dans BookDB — y compris une bibliothèque stockée sur un serveur de base de données distant. Il ne peut pas modifier les paramètres de BookDB ni supprimer d'autres appareils ; cela reste sur cet ordinateur.
