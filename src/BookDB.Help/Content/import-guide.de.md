# Importleitfaden

BookDB kann Ihre vorhandene Buchsammlung aus einer Readerware-Sicherungskopie importieren — entweder als Sicherungs-ZIP-Datei oder als entpackten Sicherungsordner.

## Ablauf des Importassistenten

1. **Dateiauswahl** — Wählen Sie eine Sicherungs-ZIP oder einen entpackten Ordner
2. **Vorschauansicht** — Datensatzanzahl, Feldabdeckung, Duplikate
3. **Einstellungen** — Zielsammlung und Importoptionen festlegen
4. **Importfortschritt** — Fortschritt beim Import der Datensätze beobachten
5. **Berichtzusammenfassung** — Ergebnisbericht prüfen

## Schritt-für-Schritt-Anleitung

## Schritt 1 — Datei auswählen

Öffnen Sie den Importassistenten über **Datei > Readerware-Sicherung importieren…** oder die Symbolleiste.

Klicken Sie auf **Durchsuchen** und wählen Sie eines der folgenden:
- Eine Readerware **Sicherungs-ZIP** (.zip) — ein mit Readerwares *Sicherung*-Funktion erstelltes Archiv
- Einen Readerware **Sicherungsordner** — den entpackten Inhalt einer solchen ZIP-Datei

Klicken Sie auf **Weiter**, um zur Vorschauansicht zu gelangen.

## Schritt 2 — Vorschauansicht

Bevor Daten geschrieben werden, analysiert BookDB die Sicherungskopie und zeigt:
- **Datensatzanzahl** — wie viele Bücher gefunden wurden
- **Feldabdeckung** — welche Felder erkannt wurden und wie viele Datensätze jedes Feld ausgefüllt haben
- **Doppelte ISBNs** — ISBNs, die bereits in Ihrer Sammlung vorhanden sind
- **Kodierungsprobleme** — Zeichenkodierungsfehler in der Datei

Prüfen Sie die Vorschau sorgfältig. Es werden keine Daten importiert, bis Sie in Schritt 4 bestätigen.

Klicken Sie auf **Weiter**, um zu den Importeinstellungen zu gelangen.

## Schritt 3 — Importeinstellungen

**Zielsammlung** — wählen Sie, welcher Sammlung (Belletristik, Sachbuch, Comics usw.) die importierten Bücher zugewiesen werden sollen. Sie können dies später durch Bearbeitung einzelner Bücher ändern.

**Dublettenbehandlung** — wenn ein Buch mit derselben ISBN bereits in Ihrer Sammlung vorhanden ist, kann BookDB:
- Das Duplikat überspringen (Standard)
- Den vorhandenen Datensatz überschreiben
- Jedes Mal nachfragen

Klicken Sie auf **Weiter**, um den Import zu starten.

## Schritt 4 — Importfortschritt

BookDB importiert Datensätze in Stapeln. Der Fortschrittsbalken zeigt:
- Wie viele Datensätze verarbeitet wurden
- Datensätze, die übersprungen wurden oder fehlgeschlagen sind

Sie können den Import jederzeit abbrechen. Teilweise importierte Datensätze bleiben erhalten.

## Schritt 5 — Importbericht

Der abschließende Bericht zeigt:
- **Importierte Datensätze** — erfolgreich in der Datenbank gespeichert
- **Übersprungene Datensätze** — Duplikate oder Datensätze mit Fehlern
- **Fehlende Felder** — Felder, die in der Importdatei leer waren
- **Kodierungsprobleme** — Zeichenprobleme, die aufgetreten sind

Klicken Sie auf **Fertigstellen**, um den Assistenten zu schließen. Ihre Bücherliste wird automatisch aktualisiert.

## Unterstützte Dateiformate

| Format | Erstellt von | Hinweise |
|--------|-------------|----------|
| ZIP | Readerware > Sicherung | Sicherungsarchiv mit Buchdaten und Umschlagbildern |
| Ordner | ZIP entpacken | Der entpackte Inhalt einer Readerware-Sicherungskopie |

## Umschlagbilder

Im Sicherungsarchiv enthaltene Umschlagbilder werden automatisch importiert und den jeweiligen Büchern zugeordnet.

## Aus einer aktiven Readerware-Datenbank importieren

Wenn Sie keine Sicherung haben, aber noch Ihre aktive Readerware-Datenbank (den `.rw4`-Ordner, z. B. `MyBooks.rw4`), kann BookDB sie direkt lesen:

1. Öffnen Sie **Extras > Readerware-Datenbank importieren…**.
2. Klicken Sie auf **Durchsuchen** und wählen Sie Ihren `.rw4`-Datenbankordner.
3. Klicken Sie auf **Konvertieren**. BookDB kopiert die Datenbank zuerst — Ihr Original wird nie geöffnet oder verändert — und konvertiert sie in einen Sicherungsordner.
4. Klicken Sie nach Abschluss der Konvertierung auf **Importassistent öffnen**, um mit denselben oben beschriebenen Schritten (Vorschau, Einstellungen, Import) fortzufahren.

Dies erfordert eine einmalige Einrichtung: Legen Sie den HSQLDB- und Java-Tool-Ordner unter **Einstellungen > Import** fest. Dieser Ordner muss `jre\bin\java.exe` und `lib\hsqldb.jar` enthalten.

### Unterstützte Readerware-Version

Diese Funktion unterstützt **Readerware 4**-Datenbanken — das `DBCATALOG40`-Format, gespeichert als HSQLDB-1.8.x-Datenbank. Titel- und Miniaturbilder im Format **JPEG, PNG, GIF oder BMP** werden importiert.

## Fehlerbehebung

**„Keine Datensätze gefunden"** — Die Datei ist möglicherweise leer oder keine gültige Readerware-Sicherungskopie. Stellen Sie sicher, dass sie mit Readerwares Sicherungsfunktion und nicht mit einem Export erstellt wurde.

**„Kodierungsprobleme erkannt"** — BookDB verarbeitet die Zeichenkodierung automatisch. Wenn in der Vorschau unleserliche Zeichen erscheinen, ist die Sicherungskopie möglicherweise beschädigt — versuchen Sie, eine neue Sicherung in Readerware zu erstellen.

**Viele Duplikate werden angezeigt** — Wenn Sie bereits Bücher über die ISBN-Suche importiert haben, werden diese als Duplikate angezeigt. Wählen Sie „Überspringen", um das Überschreiben Ihrer manuell geprüften Datensätze zu vermeiden.
