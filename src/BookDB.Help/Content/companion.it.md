# Companion

Il companion di BookDB ti permette di catalogare libri da un **telefono o tablet** sulla stessa rete locale. Scansioni un codice a barre ISBN (e facoltativamente fotografi la copertina) sul dispositivo, e il libro confluisce nella libreria in esecuzione su questo computer. Nulla lascia la tua rete: il dispositivo comunica direttamente con BookDB tramite il tuo Wi-Fi, mai attraverso internet o un server esterno.

Il companion è **disattivato per impostazione predefinita**. Lo attivi, associ ogni dispositivo una volta, e da quel momento il dispositivo si riconnette da solo ogni volta che entrambi sono sulla stessa rete.

## Attivare il companion

Apri **Strumenti › Impostazioni › Companion** e seleziona **Attiva companion**. L'impostazione ha effetto quando premi **Salva** — non nel momento in cui selezioni la casella — così puoi prima regolare la porta e le opzioni di acquisizione e applicare tutto in una volta.

Una volta in esecuzione, la riga **Stato** mostra che il companion è in ascolto. Se non riesce ad avviarsi — di solito perché la porta è già in uso — la finestra di dialogo resta aperta sulla scheda Companion e indica il motivo, e l'impostazione resta attivata così puoi cambiare la porta e riprovare.

Puoi lasciare il companion attivato tra una sessione e l'altra; si avvia automaticamente con BookDB ogni volta che l'impostazione è attiva.

### Impostazioni

- **Porta** — la porta di rete su cui il companion è in ascolto (predefinita **7443**). Cambiala solo se un altro programma usa già quella porta. Se la cambi, non è necessario riassociare, ma il dispositivo potrebbe impiegare un momento a ritrovare BookDB sulla nuova porta.
- **Dimensione massima immagine** e **qualità JPEG** — come le foto di copertina e di pagina vengono ridimensionate e compresse prima di essere archiviate. Valori più bassi risparmiano spazio; valori più alti mantengono più dettagli. Ciò vale per le foto scattate sul dispositivo.

## Associare un dispositivo

Un dispositivo deve essere associato una volta prima di poter inviare libri. L'associazione scambia una serie di certificati di sicurezza in modo che solo i dispositivi che **tu** hai approvato possano connettersi, e tutto ciò che passa tra loro è cifrato.

1. Assicurati che il companion sia attivato e in esecuzione, e che il dispositivo sia sulla **stessa rete Wi-Fi** di questo computer.
2. Apri **Strumenti › Manutenzione › Dispositivi** e premi **Mostra codice di associazione…**. (C'è anche un pulsante per gestire i dispositivi nella scheda Impostazioni ▸ Companion che apre lo stesso posto.)
3. Una finestra mostra un **codice QR**. Nell'app BookDB sul dispositivo, scegli di associare e scansiona il codice.
4. Quando il dispositivo si connette, la finestra conferma l'associazione. Puoi associare un altro dispositivo o chiudere la finestra.

Il codice di associazione è **monouso e di breve durata**: si rinnova da solo ogni paio di minuti circa e può essere riscattato una sola volta. Se un codice scade prima che tu lo scansioni, ne compare uno nuovo automaticamente. Se l'indirizzo di rete scelto automaticamente non è quello che il dispositivo può raggiungere, scegline un altro dall'elenco a discesa prima di scansionare.

Puoi associare fino a **cinque dispositivi**. Ciascuno compare nell'elenco dei dispositivi con il nome che gli è stato dato, quando è stato associato e quando è stato usato l'ultima volta. Rimuovi un dispositivo con **Rimuovi** per liberare il suo posto; un dispositivo rimosso deve essere associato di nuovo prima di potersi riconnettere.

## Il firewall

La prima volta che il companion si avvia, Windows e macOS chiedono se consentire a BookDB di accettare connessioni in ingresso. Devi consentirlo, altrimenti i dispositivi non potranno raggiungere BookDB. **Su Linux non ti viene chiesto nulla**: se è in esecuzione un firewall, devi aprire le porte da solo.

- **Windows** — consenti BookDB sulle **reti private** (la tua rete di casa o dell'ufficio). **Non** è necessario consentirlo sulle reti pubbliche. Se hai ignorato la richiesta o cliccato *Annulla*, nessuna connessione passerà; consentilo di nuovo in **Sicurezza di Windows › Firewall e protezione della rete › Consenti un'app attraverso il firewall**, seleziona la casella **Privata** di BookDB, oppure elimina la regola di blocco di BookDB così la richiesta ricompare la volta successiva.
- **macOS** — consenti le connessioni in ingresso per BookDB quando ti viene chiesto. Puoi rivederlo in seguito in **Impostazioni di sistema › Rete › Firewall**.
- **Linux** — non comparirà alcuna richiesta. Se `ufw` o `firewalld` è in esecuzione, blocca le porte in ingresso per impostazione predefinita, e la porta del companion (**7443** per impostazione predefinita) deve essere aperta sia in **TCP sia in UDP**: il TCP porta la connessione e l'UDP è ciò che permette a un dispositivo di ritrovare questo computer quando il suo indirizzo cambia. Aprire solo il TCP lascia funzionanti l'associazione e la navigazione mentre la riscoperta fallisce in silenzio, cosa che di solito si nota molto più tardi come un guasto intermittente dopo un cambio di indirizzo. Per `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, sostituendo `192.168.1.0/24` con la tua rete. Per `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Se hai cambiato la porta del companion, usa la tua porta in entrambi i comandi.

Il companion è sempre in ascolto solo sulla tua rete locale, e solo finché è attivato.

## Se un dispositivo non riesce a connettersi

- **Entrambi sulla stessa rete?** Il dispositivo e questo computer devono essere sulla stessa Wi-Fi. Una rete «ospite» è di solito isolata da quella principale e non funzionerà.
- **Firewall** — controlla le note sul firewall qui sopra; una porta bloccata è la causa più comune.
- **Companion in esecuzione?** La riga Stato nella scheda Impostazioni ▸ Companion deve mostrarlo come in esecuzione. Se non è riuscito ad avviarsi, cambia la porta e salva di nuovo.
- **Ancora associato?** Se il dispositivo è stato rimosso dall'elenco dei dispositivi, o se è passato molto tempo, associalo di nuovo con un codice nuovo.
- **Sospensione** — se questo computer è andato in sospensione, il companion riprende al risveglio; il dispositivo si riconnette da solo non appena entrambi sono svegli e sulla rete.

## Cosa può e cosa non può fare il dispositivo

Un dispositivo associato può scansionare ISBN, scattare foto di copertina e di pagina, inviarle per la catalogazione ed esplorare la libreria per verificare se possiedi già un libro. Lavora sulla libreria attualmente aperta in BookDB — inclusa una libreria archiviata su un server di database remoto. Non può modificare le impostazioni di BookDB né rimuovere altri dispositivi; questo resta su questo computer.
