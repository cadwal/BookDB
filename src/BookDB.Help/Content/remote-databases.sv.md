# Fjärrdatabaser

BookDB lagrar som standard ditt bibliotek i en lokal SQLite-fil — ingen installation krävs. Vill du nå samma bibliotek från flera datorer kan du i stället lägga det på en databasserver: **PostgreSQL** eller **MySQL / MariaDB**. Alla funktioner i BookDB fungerar likadant oavsett var biblioteket finns.

## Välja databasbackend

Öppna **Verktyg › Inställningar › Databas**. Under **Databasbackend** väljer du mellan:

- **Lokal fil (SQLite)** — standardbiblioteket för en enda dator
- **PostgreSQL-server**
- **MySQL-/MariaDB-server**

Serveralternativen kräver en nyckelring i operativsystemet (ett säkert valv för inloggningsuppgifter). BookDB sparar serverlösenordet **endast** i nyckelringen — det skrivs aldrig till någon konfigurationsfil, och det finns ingen reservlösning i klartext. Finns ingen nyckelring på systemet är serveralternativen inaktiverade.

För en serveranslutning fyller du i:

- **Värd** och **port** — porten är som standard 5432 för PostgreSQL och 3306 för MySQL/MariaDB
- **Databas**-namn
- **Användarnamn** och **lösenord**
- **TLS/SSL-läge** — vilka alternativ som finns beror på vald motor

Om ett lösenord redan är sparat för anslutningen visas en upplysning om det, och lösenordsfältet kan lämnas tomt.

**Testa anslutning** kontrollerar inställningarna innan du sparar. Lyckas anslutningen visas serverns version och hur många böcker databasen innehåller. Misslyckas den får du veta vad som gick fel: fel inloggningsuppgifter, nekad anslutning, tidsgräns, TLS-problem eller en serverversion som inte stöds.

När du sparar ett byte av backend uppmanas du att starta om — **den nya backenden börjar gälla först när BookDB har startats om**. Kan servern inte nås nästa gång BookDB startar visas en dialogruta med valen **Försök igen**, **Öppna inställningar** och **Avsluta**.

## Krav på serverversion

- **PostgreSQL 12 eller senare** — krävs för fritextsökningen
- **MySQL 8.0 eller senare** / **MariaDB 10.6 eller senare**

Versionen kontrolleras när du testar anslutningen och igen vid start; en för gammal server avvisas med ett meddelande som anger vilken version som krävs.

## Flytta biblioteket mellan backender

**Verktyg › Underhåll › Flytta biblioteket** kopierar hela biblioteket mellan två valfria backender — till exempel från den lokala SQLite-filen till en ny PostgreSQL-server, eller tillbaka igen.

Flytten är utformad för att vara säker:

- En **CSV-säkerhetskopia av källan** tas alltid innan något kopieras.
- Innehåller måldatabasen redan data säkerhetskopierar BookDB även målet, och flytten startar först när du uttryckligen bockar i **Jag förstår — ersätt all data i måldatabasen**.
- Ett tomt mål får sitt schema skapat automatiskt.
- Efter kopieringen jämförs radantalen i källa och mål; flytten räknas som klar först när de stämmer överens.
- Om du vill växlar BookDB den aktiva databasen till målet och startar om.

## Använda biblioteket från flera datorer

Ett serverbibliotek håller reda på anslutna BookDB-klienter, uppdaterat med en pulssignal var 60:e sekund:

- Om en annan klient verkar vara ansluten när du startar varnar BookDB. Du kan välja **Avsluta** eller **Anslut ändå** — den knappen blir tillgänglig efter 3 sekunders fördröjning.
- En klient som kraschat utan att koppla ner slutar räknas som ansluten efter ungefär 3 minuter.

Oberoende av serversessionen kan bara en BookDB-instans köras åt gången per användare på samma dator — startar du en till får det redan öppna fönstret fokus i stället.

Tappas serveranslutningen medan du arbetar berättar BookDB det och erbjuder **Fortsätt vänta** (rekommenderas) eller **Avsluta**.

## Säkerhetskopior av ett serverbibliotek

Filbaserad säkerhetskopiering gäller bara den lokala SQLite-filen. När biblioteket ligger på en server skapar **Säkerhetskopiering...** och de automatiska säkerhetskopiorna alltid **CSV-arkivet** — dialogrutan berättar det i stället för att misslyckas. En SQLite-filkopia kan inte återställas till ett serverbibliotek; använd en CSV-arkivkopia, eller växla först tillbaka till den lokala databasen.
