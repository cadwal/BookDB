# Glossario dei campi

Descrizioni di tutti i campi in BookDB. I campi contrassegnati come *facoltativi* non devono essere compilati per salvare un libro.

## Informazioni sul titolo

| Campo | Descrizione |
|-------|-------------|
| Titolo | Il titolo principale del libro. Obbligatorio. |
| Sottotitolo | Una riga di titolo secondaria, tipicamente mostrata sotto il titolo principale sulla copertina. *Facoltativo.* |
| Titolo alternativo | Un titolo alternativo o nella lingua originale (es. il titolo inglese di un'opera tradotta). *Facoltativo.* |

## Contributori

| Campo | Descrizione |
|-------|-------------|
| Autori / Contributori | Le persone coinvolte nella creazione del libro — Autore, Curatore, Illustratore, Designer e altri ruoli. Ogni contributore è un record persona collegato al libro con un ruolo. |

## Dettagli di pubblicazione

| Campo | Descrizione |
|-------|-------------|
| Editore | La casa editrice che ha pubblicato il libro. *Facoltativo.* |
| Luogo di pubblicazione | La città o il paese di pubblicazione. *Facoltativo.* |
| Anno di pubblicazione | L'anno di pubblicazione. Archiviato come testo per supportare date parziali o approssimative come "ca. 1950". *Facoltativo.* |
| Data di copyright | L'anno del copyright, che può differire dalla data di pubblicazione nelle edizioni successive. *Facoltativo.* |
| Formato | Il formato fisico: Copertina rigida, Brossura, Stampa grande, ecc. *Facoltativo.* |
| Edizione | L'edizione del libro: Prima, Seconda, Rivista, ecc. *Facoltativo.* |
| Pagine | Il numero totale di pagine. *Facoltativo.* |
| Lingua | La lingua del testo nel libro. *Facoltativo.* |

## Identificatori

| Campo | Descrizione |
|-------|-------------|
| ISBN | Il Numero Internazionale Normalizzato del Libro (ISBN-10 o ISBN-13). Utilizzato per la ricerca di metadati e il rilevamento dei duplicati. *Facoltativo.* |
| ISSN | Il Numero Internazionale Normalizzato delle Pubblicazioni Seriali, per i periodici. *Facoltativo.* |
| LCCN | Numero di controllo della Library of Congress. *Facoltativo.* |
| Classificazione decimale Dewey | Codice di classificazione decimale Dewey. *Facoltativo.* |
| Collocazione | Una collocazione bibliotecaria per la localizzazione sullo scaffale. *Facoltativo.* |

## Serie

| Campo | Descrizione |
|-------|-------------|
| Serie | La serie a cui appartiene il libro, se applicabile. *Facoltativo.* |
| Numero di serie | La posizione di questo libro all'interno della serie (es. "3" o "3.5"). *Facoltativo.* |

## La tua copia

| Campo | Descrizione |
|-------|-------------|
| Copie | Il numero di copie fisiche che possiedi. Il valore predefinito è 1. |
| Condizione | Le condizioni fisiche della tua copia: Ottimo, Molto buono, Buono, Discreto, Scarso, ecc. *Facoltativo.* |
| Posizione | Lo scaffale, la stanza o il luogo di archiviazione dove si trova questa copia. *Facoltativo.* |
| Proprietario | Chi possiede questa copia (utile per le collezioni condivise). *Facoltativo.* |
| Firmato | Se questa è una copia autografata. |
| Fuori catalogo | Se il libro è contrassegnato come fuori catalogo. |

## Monitoraggio lettura

| Campo | Descrizione |
|-------|-------------|
| Stato | Il tuo stato di lettura: Da leggere, In lettura, Letto, Abbandonato, ecc. *Facoltativo.* |
| Numero di letture | Quante volte hai letto questo libro. |
| Ultima lettura | La data in cui hai terminato di leggere questo libro per l'ultima volta. *Facoltativo.* |
| Valutazione | La tua valutazione personale. *Facoltativo.* |
| Preferito | Se questo libro è contrassegnato come preferito. |
| Livello di lettura | Il livello di lettura previsto (età o classe). *Facoltativo.* |

## Acquisto e valore

| Campo | Descrizione |
|-------|-------------|
| Prezzo d'acquisto | Il prezzo che hai pagato per questa copia. *Facoltativo.* |
| Valuta d'acquisto | La valuta del prezzo di acquisto (es. EUR, USD, SEK). *Facoltativo.* |
| Luogo d'acquisto | Dove hai acquistato il libro. *Facoltativo.* |
| Data d'acquisto | La data in cui hai acquistato il libro. *Facoltativo.* |
| Prezzo di listino | Il prezzo di vendita consigliato dall'editore. *Facoltativo.* |
| Valuta del prezzo di listino | La valuta del prezzo di listino. *Facoltativo.* |
| Valore dell'esemplare | Il valore monetario stimato di questa copia (es. per scopi assicurativi). *Facoltativo.* |
| Data di valutazione | La data in cui è stato stimato il valore. *Facoltativo.* |

## Descrizione e note

| Campo | Descrizione |
|-------|-------------|
| Parole chiave | Tag di testo libero per uso personale. *Facoltativo.* |
| Commenti | Le tue note personali su questo libro. *Facoltativo.* |
| Informazioni sul libro | Una descrizione estesa o una sinossi. *Facoltativo.* |
| Dimensioni | Dimensioni fisiche del libro (es. "24 × 16 × 3 cm"). *Facoltativo.* |
| Peso | Il peso fisico del libro. *Facoltativo.* |

## Campi di sistema e fonte

| Campo | Descrizione |
|-------|-------------|
| Fonte | L'origine del record di catalogo (es. Importato, Manuale, Ricerca ISBN). *Facoltativo.* |
| Link multimediale | Un URL a media correlati o alla pagina dell'editore per questo libro. *Facoltativo.* |
| Categorie | Le categorie della collezione a cui appartiene questo libro (es. Narrativa, Fumetti). Gestite nel pannello dei filtri. |
| Aggiunto | La data e l'ora di creazione di questo record in BookDB. Impostato automaticamente. |
| Aggiornato | La data e l'ora dell'ultima modifica. Aggiornato automaticamente al salvataggio. |
