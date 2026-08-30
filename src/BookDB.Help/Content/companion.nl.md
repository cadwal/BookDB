# Metgezel

Met de BookDB-metgezel kun je boeken catalogiseren vanaf een **telefoon of tablet** op hetzelfde lokale netwerk. Je scant een ISBN-streepjescode (en fotografeert eventueel de omslag) op het apparaat, en het boek stroomt de bibliotheek in die op deze computer draait. Er verlaat niets je netwerk: het apparaat praat rechtstreeks met BookDB via je wifi, nooit via internet of een externe server.

De metgezel staat **standaard uit**. Je zet hem aan, koppelt elk apparaat één keer, en daarna verbindt het apparaat zichzelf opnieuw zodra beide op hetzelfde netwerk zitten.

## De metgezel inschakelen

Open **Extra › Instellingen › Metgezel** en vink **Metgezel inschakelen** aan. De instelling wordt van kracht wanneer je op **Opslaan** drukt — niet op het moment dat je het vinkje zet — zodat je eerst de poort en de opname-opties kunt aanpassen en alles in één keer kunt toepassen.

Zodra hij draait, laat de **Status**-regel zien dat de metgezel luistert. Als hij niet kan starten — meestal omdat de poort al in gebruik is — blijft het dialoogvenster open op het tabblad Metgezel en vertelt het waarom, en de instelling blijft ingeschakeld zodat je de poort kunt wijzigen en het opnieuw kunt proberen.

Je kunt de metgezel tussen sessies ingeschakeld laten; hij start automatisch met BookDB zolang de instelling aan staat.

### Instellingen

- **Poort** — de netwerkpoort waarop de metgezel luistert (standaard **7443**). Wijzig hem alleen als een ander programma die poort al gebruikt. Als je hem wijzigt, is opnieuw koppelen niet nodig, maar het apparaat kan even nodig hebben om BookDB op de nieuwe poort terug te vinden.
- **Grootste afbeeldingsformaat** en **JPEG-kwaliteit** — hoe omslag- en paginafoto's worden geschaald en gecomprimeerd voordat ze worden opgeslagen. Lagere waarden besparen ruimte; hogere waarden behouden meer detail. Dit geldt voor foto's die op het apparaat worden gemaakt.

## Een apparaat koppelen

Een apparaat moet één keer worden gekoppeld voordat het boeken kan versturen. Bij het koppelen wordt een set beveiligingscertificaten uitgewisseld zodat alleen apparaten die **jij** hebt goedgekeurd verbinding kunnen maken, en alles wat tussen hen loopt is versleuteld.

1. Zorg dat de metgezel is ingeschakeld en draait, en dat het apparaat op **hetzelfde wifinetwerk** zit als deze computer.
2. Open **Extra › Onderhoud › Apparaten** en druk op **Koppelingscode tonen…**. (Er is ook een knop om apparaten te beheren op het tabblad Instellingen ▸ Metgezel die dezelfde plek opent.)
3. Een venster toont een **QR-code**. Kies in de BookDB-app op het apparaat om te koppelen en scan de code.
4. Wanneer het apparaat verbinding maakt, bevestigt het venster de koppeling. Je kunt nog een apparaat koppelen of het venster sluiten.

De koppelingscode is **eenmalig en kortstondig** — hij vernieuwt zichzelf ongeveer elke twee minuten en kan maar één keer worden gebruikt. Als een code verloopt voordat je hem scant, verschijnt er automatisch een nieuwe. Is het automatisch gekozen netwerkadres niet het adres dat het apparaat kan bereiken, kies dan een ander uit de vervolgkeuzelijst voordat je scant.

Je kunt tot **vijf apparaten** koppelen. Elk verschijnt in de apparatenlijst met de naam die het kreeg, wanneer het is gekoppeld en wanneer het voor het laatst is gebruikt. Verwijder een apparaat met **Verwijderen** om zijn plek vrij te maken; een verwijderd apparaat moet opnieuw worden gekoppeld voordat het opnieuw verbinding kan maken.

## De firewall

De eerste keer dat de metgezel start, vragen Windows en macOS of BookDB inkomende verbindingen mag accepteren. Je moet dit toestaan, anders kunnen apparaten BookDB niet bereiken. **Op Linux vraagt niets je iets** — draait er een firewall, dan moet je de poorten zelf openzetten.

- **Windows** — sta BookDB toe op **privénetwerken** (je thuis- of kantoornetwerk). Je hoeft het **niet** toe te staan op openbare netwerken. Als je de melding hebt weggeklikt of op *Annuleren* hebt geklikt, komt er geen enkele verbinding door; sta het opnieuw toe via **Windows-beveiliging › Firewall en netwerkbeveiliging › Een app door firewall toestaan**, vink het vakje **Privé** van BookDB aan, of verwijder de blokkeerregel van BookDB zodat de melding de volgende keer weer verschijnt.
- **macOS** — sta inkomende verbindingen voor BookDB toe wanneer daarom wordt gevraagd. Je kunt dit later bekijken via **Systeeminstellingen › Netwerk › Firewall**.
- **Linux** — er verschijnt geen melding. Als `ufw` of `firewalld` draait, worden inkomende poorten standaard geblokkeerd en moet de poort van de metgezel (standaard **7443**) open staan voor **zowel TCP als UDP**: TCP draagt de verbinding en via UDP vindt een apparaat deze computer terug wanneer het adres verandert. Alleen TCP openzetten laat koppelen en bladeren werken terwijl het terugvinden stilletjes mislukt — meestal pas veel later opgemerkt, als een sporadische storing na een adreswijziging. Voor `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, waarbij je `192.168.1.0/24` vervangt door je eigen netwerk. Voor `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Heb je de poort van de metgezel gewijzigd, gebruik dan je eigen poort in beide opdrachten.

De metgezel luistert altijd alleen op je lokale netwerk, en alleen zolang hij is ingeschakeld.

## Als een apparaat geen verbinding kan maken

- **Beide op hetzelfde netwerk?** Het apparaat en deze computer moeten op dezelfde wifi zitten. Een «gastnetwerk» is meestal afgeschermd van het hoofdnetwerk en werkt niet.
- **Firewall** — controleer de firewallnotities hierboven; een geblokkeerde poort is de meest voorkomende oorzaak.
- **Draait de metgezel?** De Status-regel op het tabblad Instellingen ▸ Metgezel moet hem als draaiend tonen. Als hij niet kon starten, wijzig de poort en sla opnieuw op.
- **Nog gekoppeld?** Als het apparaat uit de apparatenlijst is verwijderd, of als het lang geleden is, koppel het dan opnieuw met een nieuwe code.
- **Slaapstand** — is deze computer in slaapstand gegaan, dan hervat de metgezel bij het ontwaken; het apparaat verbindt zichzelf opnieuw zodra beide wakker zijn en op het netwerk zitten.

## Wat het apparaat wel en niet kan

Een gekoppeld apparaat kan ISBN's scannen, omslag- en paginafoto's maken, ze versturen om te catalogiseren en door de bibliotheek bladeren om te zien of je een boek al hebt. Het werkt op de bibliotheek die op dat moment in BookDB open is — inclusief een bibliotheek die op een externe databaseserver staat. Het kan de instellingen van BookDB niet wijzigen en geen andere apparaten verwijderen; dat blijft op deze computer.
