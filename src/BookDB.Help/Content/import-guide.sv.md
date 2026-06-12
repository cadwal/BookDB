# Importguide

BookDB kan importera din befintliga boksamling från en Readerware-säkerhetskopia — antingen som zip-fil eller som en uppackad säkerhetskopie-mapp.

## Importguidens flöde

1. **Filval** — Välj en säkerhetskopie-zip eller uppackad mapp
2. **Förhandsgranskning** — Antal poster, fälttäckning, dubbletter
3. **Inställningar** — Välj målsamling och importalternativ
4. **Importframsteg** — Se framstegen när poster importeras
5. **Rapportsammanfattning** — Granska resultatrapporten

## Steg-för-steg-instruktioner

## Steg 1 — Välj en fil

Öppna importguiden från **Arkiv > Importera Readerware-säkerhetskopia…** eller verktygsfältet.

Klicka på **Bläddra** och välj ett av följande:
- En Readerware **säkerhetskopie-zip** (.zip) — ett säkerhetskopieringsarkiv skapat med Readerwares *Backup*-funktion
- En Readerware **säkerhetskopie-mapp** — det uppackade innehållet från en sådan zip

Klicka **Nästa** för att fortsätta till förhandsgranskning.

## Steg 2 — Förhandsgranskning

Innan data skrivs analyserar BookDB säkerhetskopian och visar:
- **Antal poster** — hur många böcker som hittades
- **Fälttäckning** — vilka fält som hittades och hur många poster som har varje fält ifyllt
- **Dubbla ISBN** — ISBN som redan finns i din samling
- **Kodningsproblem** — teckenkodsfel i filen

Granska förhandsgranskningen noggrant. Ingen data importeras förrän du bekräftar i steg 4.

Klicka **Nästa** för att fortsätta till importinställningarna.

## Steg 3 — Importinställningar

**Målsamling** — välj vilken samling (Skönlitteratur, Facklitteratur, Serier osv.) de importerade böckerna ska tilldelas. Du kan ändra detta senare via redigering av enskilda böcker.

**Dubbletthantering** — om en bok med samma ISBN redan finns i din samling kan BookDB:
- Hoppa över dubletten (standard)
- Skriva över befintlig post
- Fråga dig varje gång

Klicka **Nästa** för att starta importen.

## Steg 4 — Importframsteg

BookDB importerar poster i omgångar. Förloppsraden visar:
- Hur många poster som har bearbetats
- Eventuella poster som hoppades över eller misslyckades

Du kan avbryta importen när som helst. Delvis importerade poster behålls.

## Steg 5 — Importrapport

Den sista rapporten visar:
- **Importerade poster** — sparade i databasen
- **Överhoppade poster** — dubbletter eller poster med fel
- **Saknade fält** — fält som var tomma i importfilen
- **Kodningsproblem** — eventuella teckenproblem

Klicka **Slutför** för att stänga guiden. Din boklista uppdateras automatiskt.

## Filformat som stöds

| Format | Skapas av | Noteringar |
|--------|-----------|------------|
| Zip | Readerware > Backup | Säkerhetskopieringsarkiv med bokdata och omslagsbilder |
| Mapp | Packa upp zipen | Det uppackade innehållet från en Readerware-säkerhetskopia |

## Omslagsbilder

Omslagsbilder inbäddade i säkerhetskopieringsarkivet importeras automatiskt och kopplas till varje bok.

## Flera bilder av samma typ

En bok kan få fler än en bild av samma typ — Readerware lagrar ofta flera omslags- eller miniatyrbilder per bok, och de kan alla importeras som samma typ (till exempel två *Framsida*-bilder). BookDB behåller alla bilder, men varje typ visar bara en i förhandsvisningen: den med lägst ordning.

Sådana böcker markeras i boklistan med en **!**-bricka på miniatyren ("Duplicerade bildtyper — kontrollera fliken Bilder").

För att reda ut detta öppnar du boken för redigering och går till fliken **Bilder**. När en typ har två eller fler bilder visas avsnittet **Hantera alla bilder** med alla bilder listade. För varje bild kan du:

- **Tilldela en annan bildtyp** — t.ex. ändra en andra *Framsida* till *Baksida* eller *Rygg*.
- **Flytta upp eller ned inom typen** — den översta bilden (med lägst ordning) blir typens förhandsvisning.
- **Ta bort bilden** helt.

Spara boken för att behålla ändringarna. När varje typ har högst en bild försvinner **!**-brickan.

## Importera från en aktiv Readerware-databas

Om du inte har en säkerhetskopia men fortfarande har din aktiva Readerware-databas (`.rw4`-mappen, t.ex. `MyBooks.rw4`) kan BookDB läsa den direkt:

1. Öppna **Verktyg > Importera Readerware-databas…**.
2. Klicka på **Bläddra** och välj din `.rw4`-databasmapp.
3. Klicka på **Konvertera**. BookDB kopierar först databasen — ditt original öppnas eller ändras aldrig — och konverterar den till en säkerhetskopie-mapp.
4. När konverteringen är klar klickar du på **Öppna importguiden** för att fortsätta genom samma steg för förhandsgranskning, inställningar och import som beskrivs ovan.

Detta kräver en engångsinställning: ange mappen med HSQLDB- och Java-verktyg under **Inställningar > Importera**. Mappen måste innehålla `jre\bin\java.exe` och `lib\hsqldb.jar`.

### Readerware-version som stöds

Den här funktionen stöder **Readerware 4**-databaser — `DBCATALOG40`-formatet, lagrat som en HSQLDB 1.8.x-databas. Omslags- och miniatyrbilder i formaten **JPEG, PNG, GIF eller BMP** importeras.

## Felsökning

**"Inga poster hittades"** — Filen kan vara tom eller inte en giltig Readerware-säkerhetskopia. Kontrollera att den skapades med Readerwares Backup-funktion, inte en export.

**"Kodningsproblem hittades"** — BookDB hanterar teckenkodning automatiskt. Om du ser trasiga tecken i förhandsgranskningen kan säkerhetskopian vara skadad — försök skapa en ny säkerhetskopia från Readerware.

**Många dubbletter visas** — Om du redan har importerat böcker via ISBN-sökning visas de som dubbletter. Välj "Hoppa över" för att undvika att skriva över dina manuellt granskade poster.
