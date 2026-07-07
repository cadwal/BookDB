# Entfernte Datenbanken

BookDB speichert Ihre Bibliothek standardmäßig in einer lokalen SQLite-Datei — ganz ohne Einrichtung. Wenn Sie dieselbe Bibliothek von mehreren Computern aus erreichen möchten, können Sie sie stattdessen auf einem Datenbankserver ablegen: **PostgreSQL** oder **MySQL / MariaDB**. Alle Funktionen von BookDB arbeiten unabhängig vom Speicherort der Bibliothek gleich.

## Datenbank-Backend wählen

Öffnen Sie **Extras › Einstellungen › Datenbank**. Unter **Datenbank-Backend** wählen Sie zwischen:

- **Lokale Datei (SQLite)** — die Standardbibliothek für einen einzelnen Computer
- **PostgreSQL-Server**
- **MySQL-/MariaDB-Server**

Die Serveroptionen setzen einen Schlüsselbund des Betriebssystems voraus (einen sicheren Speicher für Anmeldedaten). BookDB legt das Serverpasswort **ausschließlich** im Schlüsselbund ab — es wird nie in eine Konfigurationsdatei geschrieben, und es gibt keinen Klartext-Ersatz. Ist auf Ihrem System kein Schlüsselbund verfügbar, sind die Serveroptionen deaktiviert.

Für eine Serververbindung geben Sie Folgendes an:

- **Host** und **Port** — der Port ist standardmäßig 5432 für PostgreSQL und 3306 für MySQL/MariaDB
- **Datenbank**-Name
- **Benutzername** und **Passwort**
- **TLS-/SSL-Modus** — die verfügbaren Optionen richten sich nach der gewählten Engine

Ist für die Verbindung bereits ein Passwort gespeichert, weist ein Hinweis darauf hin und das Passwortfeld kann leer bleiben.

**Verbindung testen** prüft die Einstellungen vor dem Speichern. Bei Erfolg werden die Serverversion und die Anzahl der Bücher in der Datenbank angezeigt. Bei einem Fehler erfahren Sie, was schiefgelaufen ist: falsche Anmeldedaten, Verbindung abgelehnt, Zeitüberschreitung, TLS-Problem oder eine nicht unterstützte Serverversion.

Beim Speichern eines Backend-Wechsels werden Sie zum Neustart aufgefordert — **das neue Backend wird erst nach einem Neustart von BookDB wirksam**. Ist der Server beim nächsten Start von BookDB nicht erreichbar, bietet ein Dialog **Erneut versuchen**, **Einstellungen öffnen** oder **Beenden** an.

## Anforderungen an die Serverversion

- **PostgreSQL 12 oder neuer** — erforderlich für die Volltextsuche
- **MySQL 8.0 oder neuer** / **MariaDB 10.6 oder neuer**

Die Version wird beim Verbindungstest und erneut beim Start geprüft; ein zu alter Server wird mit einer Meldung abgelehnt, die die erforderliche Version nennt.

## Bibliothek zwischen Backends verschieben

**Extras › Wartung › Bibliothek verschieben** kopiert die gesamte Bibliothek zwischen zwei beliebigen Backends — zum Beispiel von der lokalen SQLite-Datei auf einen neuen PostgreSQL-Server oder zurück.

Der Umzug ist auf Sicherheit ausgelegt:

- Vor dem Kopieren wird immer eine **CSV-Sicherheitskopie der Quelle** angelegt.
- Enthält die Zieldatenbank bereits Daten, sichert BookDB auch das Ziel, und der Umzug beginnt erst, wenn Sie ausdrücklich **Ich verstehe — alle Daten in der Zieldatenbank ersetzen** ankreuzen.
- Bei einem leeren Ziel wird das Schema automatisch angelegt.
- Nach dem Kopieren werden die Zeilenzahlen von Quelle und Ziel verglichen; der Umzug gilt erst als abgeschlossen, wenn sie übereinstimmen.
- Auf Wunsch stellt BookDB die aktive Datenbank auf das Ziel um und startet neu.

## Bibliothek von mehreren Computern nutzen

Eine Serverbibliothek verfolgt die verbundenen BookDB-Clients über ein Lebenszeichen alle 60 Sekunden:

- Scheint beim Start bereits ein anderer Client verbunden zu sein, warnt BookDB. Sie können **Beenden** wählen oder **Trotzdem verbinden** — diese Schaltfläche wird nach einer Verzögerung von 3 Sekunden verfügbar.
- Ein Client, der ohne Abmeldung abgestürzt ist, zählt nach etwa 3 Minuten nicht mehr als verbunden.

Unabhängig von der Serversitzung kann pro Benutzer auf demselben Computer nur eine BookDB-Instanz laufen — beim Start einer zweiten erhält das bereits geöffnete Fenster den Fokus.

Bricht die Serververbindung während der Arbeit ab, meldet BookDB dies und bietet **Weiter warten** (empfohlen) oder **Beenden** an.

## Sicherungen einer Serverbibliothek

Die dateibasierte Sicherung gilt nur für die lokale SQLite-Datei. Liegt die Bibliothek auf einem Server, erzeugen **Sicherung...** und die automatischen Sicherungen immer das **CSV-Archiv** — der Sicherungsdialog weist darauf hin, statt fehlzuschlagen. Eine SQLite-Dateisicherung kann nicht in eine Serverbibliothek zurückgespielt werden; verwenden Sie eine CSV-Archiv-Sicherung, oder wechseln Sie zunächst zur lokalen Datenbank zurück.
