# Guida all'importazione

BookDB può importare la tua raccolta di libri esistente da un backup di Readerware — il file zip di backup stesso o la cartella di backup estratta.

## Flusso dell'importazione guidata

1. **Selezione file** — Scegli un file .zip di backup o una cartella di backup estratta
2. **Anteprima dry-run** — Anteprima: conteggio record, copertura campi, duplicati
3. **Impostazioni** — Imposta la raccolta di destinazione e le opzioni di importazione
4. **Avanzamento importazione** — Segui l'avanzamento durante l'importazione dei record
5. **Riepilogo report** — Esamina il report dei risultati

## Istruzioni passo dopo passo

## Passaggio 1 — Seleziona un file

Apri l'importazione guidata da **File > Importa backup di Readerware…** o dalla barra degli strumenti.

Fai clic su **Sfoglia** e seleziona uno dei seguenti:
- Un **file zip di backup** Readerware (.zip) — un archivio di backup Readerware creato con la funzione *Backup* di Readerware
- Una **cartella di backup** Readerware — il contenuto estratto di tale zip

Fai clic su **Avanti** per procedere all'anteprima dry-run.

## Passaggio 2 — Anteprima dry-run

Prima che vengano scritti dati, BookDB analizza il backup e mostra:
- **Conteggio record** — quanti libri sono stati trovati
- **Copertura campi** — quali campi sono stati rilevati e quanti record hanno ciascun campo compilato
- **ISBN duplicati** — ISBN già esistenti nella tua raccolta
- **Problemi di codifica** — eventuali problemi di codifica dei caratteri trovati nel file

Esamina attentamente l'anteprima. Nessun dato viene importato finché non confermi al Passaggio 4.

Fai clic su **Avanti** per procedere alle impostazioni di importazione.

## Passaggio 3 — Opzioni di importazione

**Raccolta di destinazione** — scegli in quale raccolta (Narrativa, Saggistica, Fumetti, ecc.) verranno assegnati i libri importati. Puoi modificarlo in seguito modificando i singoli libri.

**Gestione duplicati** — se un libro con lo stesso ISBN esiste già nella tua raccolta, BookDB può:
- Saltare il duplicato (predefinito)
- Sovrascrivere il record esistente
- Chiedere ogni volta

Fai clic su **Avanti** per avviare l'importazione.

## Passaggio 4 — Avanzamento importazione

BookDB importa i record in batch. La barra di avanzamento mostra:
- Quanti record sono stati elaborati
- Eventuali record saltati o non riusciti

Puoi annullare l'importazione in qualsiasi momento. I record parzialmente importati vengono conservati.

## Passaggio 5 — Report di importazione

Il report finale mostra:
- **Record importati** — salvati correttamente nel database
- **Record saltati** — duplicati o record con errori
- **Campi mancanti** — campi vuoti in tutto il file di importazione
- **Problemi di codifica** — eventuali problemi di caratteri riscontrati

Fai clic su **Fine** per chiudere la procedura guidata. L'elenco libri si aggiorna automaticamente.

## Formati di file supportati

| Formato | Creato da | Note |
|---------|-----------|------|
| Zip | Readerware > Backup | Archivio di backup contenente dati sui libri e immagini di copertina |
| Cartella | Estrai il zip | Il contenuto estratto di un zip di backup Readerware |

## Immagini di copertina

Le immagini di copertina incorporate nell'archivio di backup vengono importate automaticamente e associate a ciascun libro.

## Più immagini dello stesso tipo

Un libro può ritrovarsi con più di un'immagine dello stesso tipo — Readerware spesso memorizza diverse immagini di copertina o miniatura per libro, e potrebbero essere importate tutte come lo stesso tipo (ad esempio, due immagini *Prima di copertina*). BookDB conserva ogni immagine, ma ogni tipo ne mostra solo una nell'anteprima: quella con l'ordine più basso.

Questi libri sono contrassegnati nell'elenco con un indicatore **!** sulla miniatura ("Tipi di immagine duplicati — controlla la scheda Immagini").

Per sistemarli, apri il libro in modifica e vai alla scheda **Immagini**. Quando un tipo contiene due o più immagini, compare la sezione **Gestisci tutte le immagini**, che elenca ogni immagine. Per ciascuna puoi:

- **Riassegnarla a un tipo di immagine diverso** — ad esempio, cambiare una seconda *Prima di copertina* in *Quarta di copertina* o *Dorso*.
- **Spostarla su o giù all'interno del tipo** — l'immagine in alto (con l'ordine più basso) diventa l'anteprima di quel tipo.
- **Rimuovere l'immagine**.

Salva il libro per mantenere le modifiche. Quando ogni tipo contiene al massimo un'immagine, l'indicatore **!** scompare.

## Importare da un database Readerware attivo

Se non hai un backup ma disponi ancora del tuo database Readerware attivo (la cartella `.rw4`, ad es. `MyBooks.rw4`), BookDB può leggerlo direttamente:

1. Apri **Strumenti > Importa database di Readerware…**.
2. Fai clic su **Sfoglia** e seleziona la cartella del database `.rw4`.
3. Fai clic su **Converti**. BookDB copia prima il database — l'originale non viene mai aperto né modificato — e lo converte in una cartella di backup.
4. Al termine della conversione, fai clic su **Apri la procedura guidata di importazione** per proseguire con gli stessi passaggi di anteprima, impostazioni e importazione descritti sopra.

Ciò richiede una configurazione una tantum: imposta la cartella degli strumenti HSQLDB + Java in **Impostazioni > Importa**. Tale cartella deve contenere `jre\bin\java.exe` e `lib\hsqldb.jar`.

### Versione di Readerware supportata

Questa funzione supporta i database di **Readerware 4** — il formato `DBCATALOG40`, archiviato come database HSQLDB 1.8.x. Vengono importate le immagini di copertina e miniatura in formato **JPEG, PNG, GIF o BMP**.

## Risoluzione dei problemi

**"Nessun record trovato"** — Il file potrebbe essere vuoto o non essere un backup Readerware valido. Verifica che sia stato creato con la funzione Backup di Readerware, non con un'esportazione.

**"Problemi di codifica rilevati"** — BookDB gestisce automaticamente la codifica dei caratteri. Se vedi caratteri illeggibili nell'anteprima, il file di backup potrebbe essere danneggiato — prova a creare un nuovo backup da Readerware.

**Molti duplicati visualizzati** — Se hai già importato alcuni libri tramite ricerca ISBN, appariranno come duplicati. Scegli "Salta" per evitare di sovrascrivere i tuoi record rivisti manualmente.
