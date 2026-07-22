# Informazioni sulle fonti dati

Quando cataloghi un libro per ISBN (Ctrl+I o il pulsante della barra degli strumenti), BookDB recupera i metadati da quattro fonti pubbliche simultaneamente.

## Flusso di ricerca

1. Inserisci un ISBN
2. BookDB recupera da tutte e quattro le fonti in parallelo — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. La finestra di dialogo **Revisione unione** si apre — scegli quali campi accettare da ciascuna fonte
4. Il record del libro viene salvato

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

Google Books è il più grande database di libri per uso generale, con ampia copertura di titoli in lingua inglese e popolari titoli internazionali.

**Campi tipicamente forniti:**
- Titolo, Sottotitolo, Autori
- Editore, Data di pubblicazione
- Descrizione (Informazioni sul libro)
- Numero di pagine
- Lingua
- ISBN-10 e ISBN-13
- Immagine di copertina (miniatura e grande)
- Categorie

**Note:**
- Funziona senza chiave, ma le richieste non autenticate condividono una piccola quota giornaliera e vengono spesso limitate (429). Aggiungi una chiave API personale (vedi sotto) per usare la tua quota
- La copertura è più forte per le pubblicazioni commerciali successive al 1980
- I nomi degli autori potrebbero non corrispondere sempre al formato preferito

**Ottenere una chiave API di Google Books (facoltativo)**

Senza una chiave, BookDB condivide una piccola quota giornaliera anonima con tutte le altre chiamate non autenticate, quindi Google Books viene spesso limitato — un avviso nella finestra di revisione dell'unione indica le fonti escluse. Una chiave personale gratuita sposta le tue ricerche sulla tua quota:

1. Accedi alla **Google Cloud Console** su https://console.cloud.google.com.
2. Crea un nuovo progetto oppure selezionane uno esistente.
3. Apri **APIs & Services → Library**, cerca **Books API** e fai clic su **Enable**.
4. Apri **APIs & Services → Credentials**, fai clic su **Create credentials → API key** e copia la chiave.
5. Consigliato: modifica la chiave e, in **API restrictions**, limitala alla **Books API**.
6. In BookDB, apri **Impostazioni → Ricerca**, incolla la chiave sotto **Google Books** e fai clic su **Salva**.

La chiave ha effetto alla ricerca successiva — nessun riavvio necessario. Svuota il campo e salva per tornare alla quota condivisa.

## Open Library

**URL:** https://openlibrary.org

Open Library è un catalogo ad accesso aperto gestito da Internet Archive. Privilegia la completezza rispetto alla precisione — i record possono avere più campi ma una formattazione meno coerente.

**Campi tipicamente forniti:**
- Titolo, Autori
- Editore, Data di pubblicazione, Luogo di pubblicazione
- Numero di pagine
- ISBN, LCCN, Dewey Decimal
- Immagine di copertina

**Note:**
- Gestito dalla comunità — la qualità dei dati varia
- Particolarmente utile per libri più vecchi o fuori catalogo
- Spesso fornisce identificatori (LCCN, Dewey) che Google Books non fornisce

## Libris KB

**URL:** https://libris.kb.se

Libris è il catalogo nazionale delle biblioteche svedesi, gestito dalla Biblioteca Nazionale di Svezia (Kungliga biblioteket). Ha un'eccellente copertura delle pubblicazioni svedesi e delle traduzioni in svedese.

**Campi tipicamente forniti:**
- Titolo, Autori
- Editore, Anno di pubblicazione
- Lingua
- ISBN
- Informazioni sulla serie
- Dewey Decimal, Collocazione

**Note:**
- Fonte migliore per libri pubblicati in Svezia o tradotti in svedese
- Descrizioni e riassunti possono essere in svedese
- La copertura di titoli non svedesi è limitata

## IsbnSearch.org

**URL:** https://isbnsearch.org

IsbnSearch.org è un servizio gratuito di ricerca ISBN che fornisce dati bibliografici di base estratti dalle sue pagine web. Serve come utile fonte supplementare per ISBN che non restituiscono risultati dalle fonti basate su API.

**Campi tipicamente forniti:**
- Titolo, Autori
- Editore, Data di pubblicazione
- Immagine di copertina

**Note:**
- I dati vengono estratti tramite parsing HTML — la formattazione potrebbe essere meno coerente rispetto alle fonti basate su API
- È meglio utilizzarla come fonte supplementare insieme a Google Books, Open Library e Libris KB

## Revisione unione

Dopo che BookDB ha recuperato i risultati da tutte le fonti disponibili, la finestra di dialogo **Revisione unione** mostra tutti i campi recuperati affiancati:

| Campo | Attuale | Google Books | Open Library | Libris KB |
|-------|---------|-------------|--------------|-----------|
| Titolo | — | The Great... | The Great... | — |
| Autore | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Editore | — | Scribner | — | — |
| Pagine | — | 180 | 172 | — |

Per ogni campo puoi:
- **Accettare** un valore da una fonte (fai clic sul valore per selezionarlo)
- **Mantenere** il tuo valore attuale
- **Accetta tutto** per prendere tutti i valori in arrivo contemporaneamente

Quando fai clic su **Salva**, vengono aggiornati solo i campi che hai accettato. I tuoi dati esistenti non vengono mai sovrascritti automaticamente.

## Quando una fonte non restituisce risultati

Se una fonte non restituisce risultati per un ISBN:
- La colonna della fonte è semplicemente assente dalla tabella Revisione unione
- Le altre fonti non sono interessate
- Questo è normale per libri più recenti, pubblicazioni regionali o ISBN insoliti

## Limiti di velocità

BookDB rispetta automaticamente i limiti di velocità di ciascuna API. Durante la ricatalogazione in batch (Strumenti > Ricataloga), le richieste vengono distribuite in modo da non essere mai bloccato da nessuna fonte.
