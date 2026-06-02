# Feldlexikon

Beschreibungen aller Felder in BookDB. Felder, die als *optional* markiert sind, müssen nicht ausgefüllt werden, um ein Buch zu speichern.

## Titelinformationen

| Feld | Beschreibung |
|------|--------------|
| Titel | Der Haupttitel des Buches. Pflichtfeld. |
| Untertitel | Eine sekundäre Titelzeile, die üblicherweise unter dem Haupttitel auf dem Einband erscheint. *Optional.* |
| Alternativtitel | Ein alternativer oder fremdsprachiger Titel (z. B. der englische Titel eines übersetzten Werkes). *Optional.* |

## Mitwirkende

| Feld | Beschreibung |
|------|--------------|
| Autoren / Mitwirkende | Die am Buch beteiligten Personen — Autor, Herausgeber, Illustrator, Designer und weitere Rollen. Jeder Mitwirkende ist ein Personen-Datensatz, der dem Buch mit einer Rolle zugeordnet ist. |

## Veröffentlichungsdetails

| Feld | Beschreibung |
|------|--------------|
| Verlag | Der Verlag, der das Buch veröffentlicht hat. *Optional.* |
| Erscheinungsort | Die Stadt oder das Land der Veröffentlichung. *Optional.* |
| Erscheinungsjahr | Das Erscheinungsjahr. Wird als Text gespeichert, um Teil- oder Näherungsangaben wie „ca. 1950" zu unterstützen. *Optional.* |
| Copyright-Jahr | Das Copyright-Jahr, das bei späteren Ausgaben vom Erscheinungsjahr abweichen kann. *Optional.* |
| Format | Das physische Format: Hardcover, Taschenbuch, Großdruck usw. *Optional.* |
| Ausgabe | Die Auflage des Buches: Erste, Zweite, Überarbeitete usw. *Optional.* |
| Seiten | Die Gesamtseitenzahl. *Optional.* |
| Sprache | Die Sprache des Textes im Buch. *Optional.* |

## Identifikatoren

| Feld | Beschreibung |
|------|--------------|
| ISBN | Die Internationale Standardbuchnummer (ISBN-10 oder ISBN-13). Wird für die Metadatensuche und Dublettenprüfung verwendet. *Optional.* |
| ISSN | Die Internationale Standardnummer für fortlaufende Sammelwerke, für Periodika. *Optional.* |
| LCCN | Kontrollnummer der Library of Congress. *Optional.* |
| Dewey-Dezimalklassifikation | Dewey-Dezimalklassifikationscode. *Optional.* |
| Signatur | Eine Bibliothekssignatur für den Stellplatz. *Optional.* |

## Reihe

| Feld | Beschreibung |
|------|--------------|
| Reihe | Die Reihe, zu der das Buch gehört, falls vorhanden. *Optional.* |
| Reihennummer | Die Position dieses Buches innerhalb der Reihe (z. B. „3" oder „3.5"). *Optional.* |

## Ihr Exemplar

| Feld | Beschreibung |
|------|--------------|
| Exemplare | Die Anzahl der physischen Exemplare, die Sie besitzen. Standard ist 1. |
| Zustand | Der physische Zustand Ihres Exemplars: Sehr gut, Gut, Befriedigend, Ausreichend, Schlecht usw. *Optional.* |
| Standort | Das Regal, der Raum oder der Aufbewahrungsort dieses Exemplars. *Optional.* |
| Eigentümer | Wem dieses Exemplar gehört (nützlich bei gemeinsamen Sammlungen). *Optional.* |
| Signiert | Ob es sich um ein signiertes Exemplar handelt. |
| Vergriffen | Ob das Buch als vergriffen markiert ist. |

## Leseverfolgung

| Feld | Beschreibung |
|------|--------------|
| Status | Ihr Lesestatus: Zu lesen, Lese gerade, Gelesen, Abgebrochen usw. *Optional.* |
| Leseanzahl | Wie oft Sie dieses Buch gelesen haben. |
| Zuletzt gelesen | Das Datum, an dem Sie dieses Buch zuletzt zu Ende gelesen haben. *Optional.* |
| Bewertung | Ihre persönliche Bewertung. *Optional.* |
| Favorit | Ob dieses Buch als Favorit markiert ist. |
| Lesestufe | Das angestrebte Leseniveau (Alter oder Klasse). *Optional.* |

## Kauf und Wert

| Feld | Beschreibung |
|------|--------------|
| Kaufpreis | Der Preis, den Sie für dieses Exemplar bezahlt haben. *Optional.* |
| Kaufwährung | Die Währung des Kaufpreises (z. B. EUR, USD, SEK). *Optional.* |
| Kaufort | Wo Sie das Buch gekauft haben. *Optional.* |
| Kaufdatum | Das Datum, an dem Sie das Buch gekauft haben. *Optional.* |
| Listenpreis | Der empfohlene Verkaufspreis des Verlags. *Optional.* |
| Listenpreiswährung | Die Währung des Listenpreises. *Optional.* |
| Artikelwert | Ihr geschätzter Geldwert dieses Exemplars (z. B. für Versicherungszwecke). *Optional.* |
| Bewertungsdatum | Das Datum der Wertermittlung. *Optional.* |

## Beschreibung und Notizen

| Feld | Beschreibung |
|------|--------------|
| Schlüsselwörter | Freitextmarkierungen für den eigenen Gebrauch. *Optional.* |
| Notizen | Ihre persönlichen Notizen zu diesem Buch. *Optional.* |
| Buchinfo | Eine erweiterte Beschreibung oder Zusammenfassung. *Optional.* |
| Abmessungen | Physische Abmessungen des Buches (z. B. „24 × 16 × 3 cm"). *Optional.* |
| Gewicht | Das physische Gewicht des Buches. *Optional.* |

## System- und Quellenfelder

| Feld | Beschreibung |
|------|--------------|
| Quelle | Woher der Katalogeintrag stammt (z. B. Importiert, Manuell, ISBN-Suche). *Optional.* |
| Medienlink | Eine URL zu verwandten Medien oder der Verlagsseite für dieses Buch. *Optional.* |
| Kategorien | Die Sammlungskategorien, zu denen dieses Buch gehört (z. B. Belletristik, Comics). Im Filterpanel verwaltet. |
| Hinzugefügt | Datum und Uhrzeit, zu der dieser Datensatz in BookDB angelegt wurde. Wird automatisch gesetzt. |
| Aktualisiert | Datum und Uhrzeit der letzten Änderung dieses Datensatzes. Wird beim Speichern automatisch aktualisiert. |
