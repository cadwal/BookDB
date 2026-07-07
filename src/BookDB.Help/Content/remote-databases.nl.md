# Externe databases

BookDB bewaart je bibliotheek standaard in een lokaal SQLite-bestand — geen configuratie nodig. Wil je dezelfde bibliotheek vanaf meerdere computers bereiken, dan kun je haar in plaats daarvan op een databaseserver zetten: **PostgreSQL** of **MySQL / MariaDB**. Alle functies van BookDB werken hetzelfde, waar de bibliotheek ook staat.

## Een database-engine kiezen

Open **Extra › Instellingen › Database**. Onder **Database-engine** kies je uit:

- **Lokaal bestand (SQLite)** — de standaardbibliotheek voor één computer
- **PostgreSQL-server**
- **MySQL-/MariaDB-server**

De serveropties vereisen een sleutelhanger van het besturingssysteem (een beveiligde opslag voor aanmeldgegevens). BookDB bewaart het serverwachtwoord **alleen** in de sleutelhanger — het wordt nooit naar een configuratiebestand geschreven en er is geen terugval in platte tekst. Is er op je systeem geen sleutelhanger beschikbaar, dan zijn de serveropties uitgeschakeld.

Voor een serververbinding vul je in:

- **Host** en **poort** — de poort is standaard 5432 voor PostgreSQL en 3306 voor MySQL/MariaDB
- **Database**naam
- **Gebruikersnaam** en **wachtwoord**
- **TLS/SSL-modus** — de beschikbare opties hangen af van de gekozen engine

Is er voor de verbinding al een wachtwoord opgeslagen, dan meldt een hint dat en mag het wachtwoordveld leeg blijven.

**Verbinding testen** controleert de instellingen voordat je opslaat. Bij succes zie je de serverversie en hoeveel boeken de database bevat. Bij een fout zie je wat er misging: onjuiste aanmeldgegevens, verbinding geweigerd, time-out, TLS-probleem of een niet-ondersteunde serverversie.

Bij het opslaan van een engine-wissel word je gevraagd opnieuw te starten — **de nieuwe engine gaat pas in nadat BookDB opnieuw is gestart**. Is de server bij de volgende start van BookDB niet bereikbaar, dan biedt een dialoogvenster **Opnieuw proberen**, **Instellingen openen** of **Afsluiten**.

## Vereiste serverversies

- **PostgreSQL 12 of nieuwer** — nodig voor zoeken in volledige tekst
- **MySQL 8.0 of nieuwer** / **MariaDB 10.6 of nieuwer**

De versie wordt gecontroleerd bij het testen van de verbinding en opnieuw bij het opstarten; een te oude server wordt geweigerd met een melding die de vereiste versie noemt.

## De bibliotheek tussen engines verplaatsen

**Extra › Onderhoud › Bibliotheek verplaatsen** kopieert de volledige bibliotheek tussen twee willekeurige engines — bijvoorbeeld van het lokale SQLite-bestand naar een nieuwe PostgreSQL-server, of terug.

De verplaatsing is ontworpen om veilig te zijn:

- Er wordt altijd eerst een **CSV-veiligheidsback-up van de bron** gemaakt voordat er iets wordt gekopieerd.
- Bevat de doeldatabase al gegevens, dan maakt BookDB ook een back-up van het doel, en de verplaatsing begint pas nadat je uitdrukkelijk **Ik begrijp het — vervang alle gegevens in de doeldatabase** hebt aangevinkt.
- In een leeg doel wordt het schema automatisch aangemaakt.
- Na het kopiëren worden de rijaantallen van bron en doel vergeleken; de verplaatsing geldt pas als voltooid wanneer ze overeenkomen.
- Desgewenst schakelt BookDB de actieve database over naar het doel en start opnieuw.

## De bibliotheek vanaf meerdere computers gebruiken

Een serverbibliotheek houdt verbonden BookDB-clients bij via een hartslagsignaal elke 60 seconden:

- Lijkt er bij het starten al een andere client verbonden, dan waarschuwt BookDB je. Je kunt **Afsluiten** of **Toch verbinden** kiezen — die knop komt na een vertraging van 3 seconden beschikbaar.
- Een client die is gecrasht zonder de verbinding te verbreken, telt na ongeveer 3 minuten niet meer als verbonden.

Los van de serversessie kan er per gebruiker maar één BookDB-exemplaar tegelijk draaien op dezelfde computer — start je een tweede, dan krijgt het al geopende venster de focus.

Valt de serververbinding weg terwijl je werkt, dan meldt BookDB dat en biedt **Blijven wachten** (aanbevolen) of **Afsluiten**.

## Back-ups van een serverbibliotheek

Back-up op bestandsbasis geldt alleen voor het lokale SQLite-bestand. Staat de bibliotheek op een server, dan leveren **Back-up...** en de automatische back-ups altijd het **CSV-archief** op — het back-updialoogvenster meldt dat in plaats van te mislukken. Een SQLite-bestandsback-up kan niet worden teruggezet in een serverbibliotheek; gebruik een CSV-archiefback-up of schakel eerst terug naar de lokale database.
