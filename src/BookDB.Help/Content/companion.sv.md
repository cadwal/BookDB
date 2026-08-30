# Mobilskanner

Mobilskannern låter dig katalogisera böcker från en **telefon eller surfplatta** på samma lokala nätverk. Du skannar en ISBN-streckkod (och fotograferar eventuellt omslaget) på enheten, och boken flödar in i biblioteket som körs på den här datorn. Ingenting lämnar ditt nätverk: enheten pratar direkt med BookDB över ditt Wi-Fi, aldrig via internet eller någon extern server.

Mobilskannern är **avstängd som standard**. Du slår på den, kopplar varje enhet en gång, och därefter återansluter enheten på egen hand så snart båda är på samma nätverk.

## Slå på mobilskannern

Öppna **Verktyg › Inställningar › Mobilskanner** och kryssa i **Aktivera mobilskanner**. Inställningen träder i kraft när du trycker på **Spara** — inte i det ögonblick du kryssar i rutan — så du kan justera porten och bildinställningarna först och tillämpa allt på en gång.

När den körs visar **Status**-raden att mobilskannern lyssnar. Om den inte kan starta — oftast för att porten redan används — förblir dialogrutan öppen på fliken Mobilskanner och talar om varför, och inställningen förblir aktiverad så att du kan ändra porten och försöka igen.

Du kan lämna mobilskannern aktiverad mellan sessioner; den startar automatiskt med BookDB närhelst inställningen är på.

### Inställningar

- **Port** — nätverksporten som mobilskannern lyssnar på (standard **7443**). Ändra den bara om ett annat program redan använder den porten. Om du ändrar den behöver du inte koppla om, men enheten kan ta en stund på sig att hitta BookDB igen på den nya porten.
- **Största bildstorlek** och **JPEG-kvalitet** — hur omslags- och sidfoton skalas och komprimeras innan de lagras. Lägre värden sparar utrymme; högre värden behåller mer detaljer. Detta gäller foton som tas på enheten.

## Koppla en enhet

En enhet måste kopplas en gång innan den kan skicka böcker. Kopplingen utbyter en uppsättning säkerhetscertifikat så att bara enheter **du** har godkänt kan ansluta, och allt mellan dem är krypterat.

1. Se till att mobilskannern är aktiverad och körs, och att enheten är på **samma Wi-Fi-nätverk** som den här datorn.
2. Öppna **Verktyg › Underhåll › Enheter** och tryck på **Visa kopplingskod…**. (Det finns också en knapp för att hantera enheter på fliken Inställningar ▸ Mobilskanner som öppnar samma ställe.)
3. Ett fönster visar en **QR-kod**. I BookDB-appen på enheten väljer du att koppla och skannar koden.
4. När enheten ansluter bekräftar fönstret kopplingen. Du kan koppla en enhet till eller stänga fönstret.

Kopplingskoden är **för engångsbruk och kortlivad** — den förnyar sig själv varannan minut ungefär och kan bara användas en gång. Om en kod går ut innan du hinner skanna den dyker en ny upp automatiskt. Om den automatiskt valda nätverksadressen inte är den enheten kan nå väljer du en annan i listrutan innan du skannar.

Du kan koppla upp till **fem enheter**. Var och en visas i enhetslistan med namnet den fick, när den kopplades och när den senast användes. Ta bort en enhet med **Ta bort** för att frigöra dess plats; en borttagen enhet måste kopplas igen innan den kan återansluta.

## Brandväggen

Första gången mobilskannern startar frågar Windows och macOS om BookDB ska få ta emot inkommande anslutningar. Du måste tillåta det, annars kan enheter inte nå BookDB. **På Linux frågar ingenting** — om en brandvägg är igång måste du öppna portarna själv.

- **Windows** — tillåt BookDB på **privata nätverk** (ditt hem- eller kontorsnätverk). Du behöver **inte** tillåta det på offentliga nätverk. Om du stängde frågan eller klickade *Avbryt* släpps inga anslutningar igenom; tillåt det på nytt under **Windows-säkerhet › Brandvägg och nätverksskydd › Tillåt en app genom brandväggen**, kryssa i BookDB:s **Privat**-ruta, eller ta bort BookDB:s blockeringsregel så att frågan visas igen nästa gång.
- **macOS** — tillåt inkommande anslutningar för BookDB när du blir tillfrågad. Du kan granska detta senare under **Systeminställningar › Nätverk › Brandvägg**.
- **Linux** — ingenting frågar dig. Om `ufw` eller `firewalld` är igång blockeras inkommande portar som standard, och mobilskannerns port (**7443** som standard) måste vara öppen för **både TCP och UDP**: TCP bär anslutningen, och via UDP hittar en enhet den här datorn igen när dess adress ändras. Att bara öppna TCP gör att parkoppling och bläddring fungerar medan återupptäckten tyst misslyckas — vilket oftast märks långt senare, som ett sporadiskt fel efter en adressändring. För `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, där du byter ut `192.168.1.0/24` mot ditt eget nätverk. För `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Om du har ändrat mobilskannerns port använder du din egen port i båda kommandona.

Mobilskannern lyssnar bara någonsin på ditt lokala nätverk, och bara medan den är aktiverad.

## Om en enhet inte kan ansluta

- **Båda på samma nätverk?** Enheten och den här datorn måste vara på samma Wi-Fi. Ett gästnätverk är oftast avskilt från huvudnätverket och fungerar inte.
- **Brandvägg** — kontrollera brandväggsnoterna ovan; en blockerad port är den vanligaste orsaken.
- **Körs mobilskannern?** Status-raden på fliken Inställningar ▸ Mobilskanner måste visa den som igång. Om den inte kunde starta, ändra porten och spara igen.
- **Fortfarande kopplad?** Om enheten togs bort från enhetslistan, eller om det har gått lång tid, koppla den igen med en ny kod.
- **Viloläge** — om den här datorn gick i viloläge återupptas mobilskannern när den vaknar; enheten återansluter på egen hand så snart båda är vakna och på nätverket.

## Vad enheten kan och inte kan göra

En kopplad enhet kan skanna ISBN, ta omslags- och sidfoton, skicka dem för katalogisering och bläddra i biblioteket för att se om du redan äger en bok. Den arbetar mot biblioteket som just nu är öppet i BookDB — inklusive ett bibliotek som lagras på en fjärrdatabasserver. Den kan inte ändra BookDB:s inställningar eller ta bort andra enheter; det stannar på den här datorn.
