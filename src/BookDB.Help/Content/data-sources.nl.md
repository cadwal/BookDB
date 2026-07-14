# Over gegevensbronnen

Wanneer je een boek catalogiseert via ISBN (Ctrl+I of de werkbalkknop), haalt BookDB gelijktijdig metadata op van drie openbare API's.

## Zoekstroom

1. Je voert het ISBN in
2. BookDB benadert alle vier bronnen parallel — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. Het dialoogvenster **Samenvoegbeoordeling** opent — je kiest welke velden je van elke bron wilt overnemen
4. Boekrecord opgeslagen

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

Google Books is de grootste algemene boekendatabase, met brede dekking van Engelstalige en populaire internationale titels.

**Doorgaans beschikbare velden:**
- Titel, Ondertitel, Auteurs
- Uitgever, Publicatiedatum
- Beschrijving (Boekinformatie)
- Aantal pagina's
- Taal
- ISBN-10 en ISBN-13
- Omslagafbeelding (miniatuur en groot)
- Categorieën

**Opmerkingen:**
- Geen API-sleutel vereist voor basiszoekopdrachten
- Dekking is het sterkst voor commerciële publicaties na 1980
- Auteurnamen komen mogelijk niet altijd overeen met je gewenste opmaak

## Open Library

**URL:** https://openlibrary.org

Open Library is een openbaar toegankelijke catalogus, beheerd door het Internet Archive. Het benadrukt volledigheid boven kwaliteit — records kunnen meer velden hebben maar minder consistente opmaak.

**Doorgaans beschikbare velden:**
- Titel, Auteurs
- Uitgever, Publicatiedatum, Publicatieplaats
- Aantal pagina's
- ISBN, LCCN, Dewey Decimaal
- Omslagafbeelding

**Opmerkingen:**
- Door de gemeenschap onderhouden — gegevenskwaliteit varieert
- Bijzonder goed voor oudere of uitverkochte boeken
- Biedt vaak identificatoren (LCCN, Dewey) die Google Books niet levert

## Libris KB

**URL:** https://libris.kb.se

Libris is de Zweedse nationale bibliotheek­catalogus, beheerd door de Nationale Bibliotheek van Zweden (Kungliga biblioteket). Het heeft uitstekende dekking van Zweedse publicaties en vertalingen naar het Zweeds.

**Doorgaans beschikbare velden:**
- Titel, Auteurs
- Uitgever, Publicatiejaar
- Taal
- ISBN
- Reeksinformatie
- Dewey Decimaal, Signatuur

**Opmerkingen:**
- Beste bron voor boeken gepubliceerd in Zweden of vertaald naar het Zweeds
- Beschrijvingen en samenvattingen kunnen in het Zweeds zijn
- Dekking van niet-Zweedse titels is beperkt

## IsbnSearch.org

**URL:** https://isbnsearch.org

IsbnSearch.org is een gratis ISBN-zoekservice die basisbiblografische gegevens verstrekt, geëxtraheerd van zijn webpagina's. Het dient als nuttige aanvullende bron voor ISBN's die geen resultaten opleveren bij de API-gebaseerde bronnen.

**Doorgaans beschikbare velden:**
- Titel, Auteurs
- Uitgever, Publicatiedatum
- Omslagafbeelding

**Opmerkingen:**
- Gegevens worden geëxtraheerd via HTML-analyse — de opmaak kan minder consistent zijn dan bij API-gebaseerde bronnen
- Beste gebruikt als aanvullende bron naast Google Books, Open Library en Libris KB
## Samenvoegbeoordeling

Nadat BookDB de resultaten van alle beschikbare bronnen heeft opgehaald, toont het dialoogvenster **Samenvoegbeoordeling** alle opgehaalde velden naast elkaar:

| Veld | Huidig | Google Books | Open Library | Libris KB |
|------|--------|-------------|--------------|-----------|
| Titel | — | The Great... | The Great... | — |
| Auteur | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Uitgever | — | Scribner | — | — |
| Pagina's | — | 180 | 172 | — |

Voor elk veld kun je:
- Een waarde van een bron **overnemen** (klik op de waarde om deze te selecteren)
- Je huidige waarde **behouden**
- **Alles overnemen** om alle binnenkomende waarden tegelijk te accepteren

Wanneer je op **Opslaan** klikt, worden alleen de door jou geaccepteerde velden bijgewerkt. Je bestaande gegevens worden nooit automatisch overschreven.

## Wanneer een bron geen resultaten geeft

Als een bron geen resultaten oplevert voor een ISBN:
- De bronkolom is simpelweg afwezig in de samenvoegbeoordeling
- Andere bronnen worden niet beïnvloed
- Dit is normaal voor nieuwere boeken, regionale publicaties of ongebruikelijke ISBN's

## Snelheidslimieten

BookDB respecteert automatisch de snelheidslimieten van elke API. Tijdens bulk hercatalogisering (Extra > Hercatalogiseren) worden verzoeken gespreid zodat je nooit van een bron wordt geblokkeerd.
