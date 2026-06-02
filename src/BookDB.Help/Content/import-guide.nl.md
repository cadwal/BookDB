# Importgids

BookDB kan uw bestaande boekenverzameling importeren vanuit een Readerware-back-up — als back-up zip-bestand of als uitgepakte back-upmap.

## Stroom van de importwizard

1. **Bestandsselectie** — Kies een back-up .zip-bestand of uitgepakte back-upmap
2. **Droge-run voorbeeld** — Aantal records, velddekking, duplicaten
3. **Instellingen** — Stel doelcollectie en importopties in
4. **Importvoortgang** — Bekijk de voortgang terwijl records worden geïmporteerd
5. **Rapportoverzicht** — Bekijk het resultatenoverzicht

## Stap-voor-stap instructies

## Stap 1 — Een bestand selecteren

Open de importwizard via **Bestand > Readerware-back-up importeren…** of de werkbalk.

Klik op **Bladeren** en selecteer een van de volgende:
- Een Readerware **back-up zip** (.zip) — een back-uparchief gemaakt met de *Back-up*-functie van Readerware
- Een Readerware **back-upmap** — de uitgepakte inhoud van zo'n zip

Klik op **Volgende** om door te gaan naar de voorvertoning.

## Stap 2 — Droge run voorvertoning

Voordat gegevens worden geschreven, analyseert BookDB de back-up en toont:
- **Aantal records** — hoeveel boeken zijn gevonden
- **Velddekking** — welke velden zijn gedetecteerd en hoeveel records elk veld hebben ingevuld
- **Dubbele ISBN's** — ISBN's die al in uw collectie bestaan
- **Coderingsproblemen** — tekencoderingsproblemen gevonden in het bestand

Bekijk de voorvertoning zorgvuldig. Er worden geen gegevens geïmporteerd totdat u bevestigt in Stap 4.

Klik op **Volgende** om door te gaan naar de importinstellingen.

## Stap 3 — Importopties

**Doelcollectie** — kies aan welke collectie (Fictie, Non-fictie, Comics, enz.) de geïmporteerde boeken worden toegewezen. U kunt dit later wijzigen door afzonderlijke boeken te bewerken.

**Afhandeling van duplicaten** — als een boek met hetzelfde ISBN al in uw collectie bestaat, kan BookDB:
- Het duplicaat overslaan (standaard)
- Het bestaande record overschrijven
- U elke keer vragen

Klik op **Volgende** om de import te starten.

## Stap 4 — Importvoortgang

BookDB importeert records in batches. De voortgangsbalk toont:
- Hoeveel records zijn verwerkt
- Records die zijn overgeslagen of mislukt

U kunt de import op elk moment annuleren. Gedeeltelijk geïmporteerde records worden behouden.

## Stap 5 — Importrapport

Het eindrapport toont:
- **Geïmporteerde records** — succesvol opgeslagen in de database
- **Overgeslagen records** — duplicaten of records met fouten
- **Ontbrekende velden** — velden die leeg waren in het importbestand
- **Coderingsproblemen** — tekenproblemen die zijn opgetreden

Klik op **Voltooien** om de wizard te sluiten. Uw boekenlijst wordt automatisch vernieuwd.

## Ondersteunde bestandsindelingen

| Indeling | Gemaakt door | Opmerkingen |
|----------|-------------|-------------|
| Zip | Readerware > Back-up | Back-uparchief met boekengegevens en omslagafbeeldingen |
| Map | Pak de zip uit | De uitgepakte inhoud van een Readerware back-up zip |

## Omslagafbeeldingen

Omslagafbeeldingen ingebed in het back-uparchief worden automatisch geïmporteerd en gekoppeld aan elk boek.

## Importeren uit een actieve Readerware-database

Als u geen back-up hebt maar nog wel uw actieve Readerware-database (de `.rw4`-map, bijv. `MyBooks.rw4`), kan BookDB die rechtstreeks lezen:

1. Open **Extra > Readerware-database importeren…**.
2. Klik op **Bladeren** en selecteer uw `.rw4`-databasemap.
3. Klik op **Converteren**. BookDB kopieert eerst de database — uw origineel wordt nooit geopend of gewijzigd — en converteert die naar een back-upmap.
4. Klik na het voltooien van de conversie op **Importwizard openen** om verder te gaan met dezelfde stappen voor voorbeeld, instellingen en import die hierboven zijn beschreven.

Dit vereist een eenmalige instelling: stel de map met HSQLDB- en Java-hulpprogramma's in via **Instellingen > Importeren**. Die map moet `jre\bin\java.exe` en `lib\hsqldb.jar` bevatten.

### Ondersteunde Readerware-versie

Deze functie ondersteunt **Readerware 4**-databases — de `DBCATALOG40`-indeling, opgeslagen als een HSQLDB 1.8.x-database. Omslag- en miniatuurafbeeldingen in de indeling **JPEG, PNG, GIF of BMP** worden geïmporteerd.

## Probleemoplossing

**"Geen records gevonden"** — Het bestand is mogelijk leeg of geen geldige Readerware-back-up. Controleer of het is gemaakt met de Back-up functie van Readerware, niet een export.

**"Coderingsproblemen gedetecteerd"** — BookDB verwerkt tekencodering automatisch. Als u verminkte tekens ziet in de voorvertoning, is het back-upbestand mogelijk beschadigd — probeer een nieuwe back-up te maken vanuit Readerware.

**Veel duplicaten worden weergegeven** — Als u al boeken hebt geïmporteerd via ISBN-opzoeken, verschijnen deze als duplicaten. Kies "Overslaan" om te voorkomen dat uw handmatig beoordeelde records worden overschreven.
