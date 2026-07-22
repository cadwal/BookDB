# Om datakällor

När du katalogiserar en bok via ISBN (Ctrl+I eller verktygsfältsknappen) hämtar BookDB metadata från fyra publika källor samtidigt.

## Sökflöde

1. Du anger ett ISBN
2. BookDB hämtar från alla fyra källor parallellt — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. Dialogrutan **Sammanslagningsgranskning** öppnas — du väljer vilka fält som ska accepteras från varje källa
4. Bokposten sparas

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

Google Books är den största allmänna bokdatabasen med bred täckning av engelskspråkiga och populära internationella titlar.

**Fält som vanligtvis tillhandahålls:**
- Titel, Undertitel, Författare
- Förlag, Utgivningsdatum
- Beskrivning (Bokinformation)
- Sidantal
- Språk
- ISBN-10 och ISBN-13
- Omslagsbild (miniatyr och stor)
- Kategorier

**Noteringar:**
- Fungerar utan nyckel, men förfrågningar utan autentisering delar en liten daglig kvot och blir ofta hastighetsbegränsade (429). Lägg till en personlig API-nyckel (se nedan) för att använda din egen kvot
- Täckningen är starkast för kommersiella utgivningar efter 1980
- Författarnamn kanske inte alltid matchar ditt önskade format

**Skaffa en API-nyckel för Google Books (valfritt)**

Utan en nyckel delar BookDB en liten anonym daglig kvot med alla andra oautentiserade anrop, så Google Books blir ofta hastighetsbegränsad — en banner i sammanslagningsgranskningen visar vilka källor som utelämnades. En kostnadsfri personlig nyckel flyttar dina sökningar till din egen kvot:

1. Logga in på **Google Cloud Console** på https://console.cloud.google.com.
2. Skapa ett nytt projekt, eller välj ett befintligt.
3. Öppna **APIs & Services → Library**, sök efter **Books API** och klicka på **Enable**.
4. Öppna **APIs & Services → Credentials**, klicka på **Create credentials → API key** och kopiera nyckeln.
5. Rekommenderas: redigera nyckeln och begränsa den till **Books API** under **API restrictions**.
6. I BookDB, öppna **Inställningar → Sökning**, klistra in nyckeln under **Google Books** och klicka på **Spara**.

Nyckeln börjar gälla vid nästa sökning — ingen omstart behövs. Töm fältet och spara för att återgå till den delade kvoten.

## Open Library

**URL:** https://openlibrary.org

Open Library är en öppen katalog som underhålls av Internet Archive. Den prioriterar fullständighet framför polish — poster kan ha fler fält men mindre konsekvent formatering.

**Fält som vanligtvis tillhandahålls:**
- Titel, Författare
- Förlag, Utgivningsdatum, Utgivningsort
- Sidantal
- ISBN, LCCN, Dewey-decimal
- Omslagsbild

**Noteringar:**
- Gemenskapsunderhållen — datakvaliteten varierar
- Särskilt bra för äldre eller utgångna böcker
- Tillhandahåller ofta identifierare (LCCN, Dewey) som Google Books inte gör

## Libris KB

**URL:** https://libris.kb.se

Libris är den svenska nationella bibliotekskatalogen, underhållen av Kungliga biblioteket. Den har utmärkt täckning av svenska utgivningar och översättningar till svenska.

**Fält som vanligtvis tillhandahålls:**
- Titel, Författare
- Förlag, Utgivningsår
- Språk
- ISBN
- Serieinformation
- Dewey-decimal, Signum

**Noteringar:**
- Bästa källan för böcker utgivna i Sverige eller översatta till svenska
- Beskrivningar och sammanfattningar kan vara på svenska
- Täckningen av icke-svenska titlar är begränsad

## IsbnSearch.org

**URL:** https://isbnsearch.org

IsbnSearch.org är en gratis ISBN-söktjänst som tillhandahåller grundläggande bibliografiska data hämtade från sina webbsidor. Den fungerar som en användbar kompletterande källa för ISBN som inte ger resultat från de API-baserade källorna.

**Fält som vanligtvis tillhandahålls:**
- Titel, Författare
- Förlag, Utgivningsdatum
- Omslagsbild

**Noteringar:**
- Data extraheras via HTML-tolkning — formateringen kan vara mindre konsekvent än API-baserade källor
- Bäst använd som en kompletterande källa tillsammans med Google Books, Open Library och Libris KB
## Sammanslagningsgranskning

Efter att BookDB hämtat resultat från alla tillgängliga källor visar dialogrutan **Sammanslagningsgranskning** alla hämtade fält sida vid sida:

| Fält | Aktuell | Google Books | Open Library | Libris KB |
|------|---------|-------------|--------------|-----------|
| Titel | — | The Great... | The Great... | — |
| Författare | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Förlag | — | Scribner | — | — |
| Sidor | — | 180 | 172 | — |

För varje fält kan du:
- **Acceptera** ett värde från en källa (klicka på värdet för att välja det)
- **Behålla** ditt nuvarande värde
- **Acceptera alla** för att ta alla inkommande värden på en gång

När du klickar på **Spara** uppdateras bara de fält du accepterade. Dina befintliga data skrivs aldrig över automatiskt.

## När en källa inte ger resultat

Om en källa inte returnerar resultat för ett ISBN:
- Källkolumnen saknas helt i sammanslagningsgranskningstabellen
- Övriga källor påverkas inte
- Detta är normalt för nyare böcker, regionala utgivningar eller ovanliga ISBN

## Hastighetsbegränsningar

BookDB respekterar varje API:s hastighetsbegränsningar automatiskt. Vid massom-katalogisering (Verktyg > Omkatalogisera) sprids förfrågningarna ut så att du aldrig blockeras från någon källa.
