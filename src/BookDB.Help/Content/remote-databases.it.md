# Database remoti

Per impostazione predefinita BookDB salva la tua libreria in un file SQLite locale — nessuna configurazione necessaria. Se vuoi raggiungere la stessa libreria da più computer, puoi tenerla su un server di database: **PostgreSQL** o **MySQL / MariaDB**. Tutte le funzioni di BookDB si comportano allo stesso modo, ovunque sia archiviata la libreria.

## Scegliere il motore del database

Apri **Strumenti › Impostazioni › Database**. Sotto **Motore del database** puoi scegliere tra:

- **File locale (SQLite)** — la libreria predefinita per un solo computer
- **Server PostgreSQL**
- **Server MySQL / MariaDB**

Le opzioni server richiedono un portachiavi del sistema operativo (un archivio sicuro delle credenziali). BookDB conserva la password del server **solo** nel portachiavi — non viene mai scritta in un file di configurazione e non esiste alcuna alternativa in chiaro. Se sul sistema non è disponibile un portachiavi, le opzioni server sono disattivate.

Per la connessione a un server indica:

- **Host** e **porta** — la porta predefinita è 5432 per PostgreSQL e 3306 per MySQL/MariaDB
- Nome del **database**
- **Nome utente** e **password**
- **Modalità TLS/SSL** — le opzioni disponibili dipendono dal motore scelto

Se per la connessione è già salvata una password, un suggerimento lo segnala e il campo della password può restare vuoto.

**Prova connessione** verifica le impostazioni prima del salvataggio. In caso di successo mostra la versione del server e quanti libri contiene il database. In caso di errore indica cosa è andato storto: credenziali errate, connessione rifiutata, timeout, problema TLS o versione del server non supportata.

Quando salvi un cambio di motore ti viene chiesto di riavviare — **il nuovo motore ha effetto solo dopo il riavvio di BookDB**. Se al successivo avvio il server non è raggiungibile, una finestra di dialogo propone **Riprova**, **Apri impostazioni** o **Esci**.

## Requisiti di versione del server

- **PostgreSQL 12 o successivo** — necessario per la ricerca a testo intero
- **MySQL 8.0 o successivo** / **MariaDB 10.6 o successivo**

La versione viene controllata alla prova della connessione e di nuovo all'avvio; un server troppo vecchio viene rifiutato con un messaggio che indica la versione richiesta.

## Spostare la libreria tra motori

**Strumenti › Manutenzione › Sposta libreria** copia l'intera libreria tra due motori qualsiasi — ad esempio dal file SQLite locale a un nuovo server PostgreSQL, o viceversa.

Lo spostamento è pensato per essere sicuro:

- Prima di copiare qualsiasi cosa viene sempre creato un **backup CSV di sicurezza dell'origine**.
- Se il database di destinazione contiene già dati, BookDB esegue il backup anche della destinazione, e lo spostamento parte solo dopo che hai spuntato esplicitamente **Ho capito — sostituisci tutti i dati nel database di destinazione**.
- In una destinazione vuota lo schema viene creato automaticamente.
- Dopo la copia i conteggi delle righe di origine e destinazione vengono confrontati; lo spostamento è considerato completo solo quando coincidono.
- Facoltativamente, BookDB imposta il database attivo sulla destinazione e si riavvia.

## Usare la libreria da più computer

Una libreria su server tiene traccia dei client BookDB connessi tramite un segnale di attività ogni 60 secondi:

- Se all'avvio un altro client risulta connesso, BookDB ti avvisa. Puoi scegliere **Esci** oppure **Connetti comunque** — quel pulsante diventa disponibile dopo un ritardo di 3 secondi.
- Un client terminato in modo anomalo senza disconnettersi smette di risultare connesso dopo circa 3 minuti.

Indipendentemente dalla sessione sul server, per ogni utente può essere in esecuzione una sola istanza di BookDB sullo stesso computer — avviandone una seconda viene portata in primo piano la finestra già aperta.

Se la connessione al server cade mentre lavori, BookDB te lo comunica e propone **Continua ad attendere** (consigliato) o **Esci**.

## Backup di una libreria su server

Il backup su file riguarda solo il file SQLite locale. Quando la libreria è su un server, **Backup...** e i backup automatici producono sempre l'**archivio CSV** — la finestra di dialogo lo segnala invece di fallire. Un backup su file SQLite non può essere ripristinato in una libreria su server; usa un backup ad archivio CSV, oppure torna prima al database locale.
