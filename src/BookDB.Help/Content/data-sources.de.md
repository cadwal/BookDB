# Über Datenquellen

Wenn Sie ein Buch per ISBN katalogisieren (Strg+I oder die Schaltfläche in der Symbolleiste), ruft BookDB gleichzeitig Metadaten von drei öffentlichen APIs ab.

## Suchablauf

1. Sie geben die ISBN ein
2. BookDB fragt alle vier Quellen parallel ab — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. Der Dialog **Zusammenführungsprüfung** öffnet sich — Sie wählen, welche Felder aus welcher Quelle übernommen werden sollen
4. Buchdatensatz gespeichert

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

Google Books ist die größte allgemeine Buchdatenbank mit breiter Abdeckung englischsprachiger und populärer internationaler Titel.

**Typisch bereitgestellte Felder:**
- Titel, Untertitel, Autoren
- Verlag, Erscheinungsdatum
- Beschreibung (Buchinfo)
- Seitenanzahl
- Sprache
- ISBN-10 und ISBN-13
- Umschlagbild (Miniatur und groß)
- Kategorien

**Hinweise:**
- Für einfache Suchen wird kein API-Schlüssel benötigt
- Die Abdeckung ist am stärksten bei kommerziellen Veröffentlichungen nach 1980
- Autorennamen entsprechen möglicherweise nicht immer Ihrem bevorzugten Format

## Open Library

**URL:** https://openlibrary.org

Open Library ist ein offener Katalog, der vom Internet Archive gepflegt wird. Er legt den Schwerpunkt auf Vollständigkeit statt auf Qualität — Datensätze können mehr Felder, aber weniger konsistente Formatierung aufweisen.

**Typisch bereitgestellte Felder:**
- Titel, Autoren
- Verlag, Erscheinungsdatum, Erscheinungsort
- Seitenanzahl
- ISBN, LCCN, Dewey-Dezimal
- Umschlagbild

**Hinweise:**
- Gemeinschaftsgepflegt — die Datenqualität schwankt
- Besonders gut für ältere oder vergriffene Bücher
- Liefert oft Identifikatoren (LCCN, Dewey), die Google Books nicht bereitstellt

## Libris KB

**URL:** https://libris.kb.se

Libris ist der schwedische Nationalkatalog, der von der Königlichen Bibliothek Schwedens (Kungliga biblioteket) gepflegt wird. Er bietet ausgezeichnete Abdeckung schwedischer Veröffentlichungen und Übersetzungen ins Schwedische.

**Typisch bereitgestellte Felder:**
- Titel, Autoren
- Verlag, Erscheinungsjahr
- Sprache
- ISBN
- Reiheninformationen
- Dewey-Dezimal, Signatur

**Hinweise:**
- Beste Quelle für in Schweden veröffentlichte oder ins Schwedische übersetzte Bücher
- Beschreibungen und Zusammenfassungen können auf Schwedisch sein
- Die Abdeckung nicht-schwedischer Titel ist begrenzt

## IsbnSearch.org

**URL:** https://isbnsearch.org

IsbnSearch.org ist ein kostenloser ISBN-Suchdienst, der grundlegende bibliografische Daten von seinen Webseiten bereitstellt. Er dient als nützliche ergänzende Quelle für ISBNs, die bei den API-basierten Quellen kein Ergebnis liefern.

**Typisch bereitgestellte Felder:**
- Titel, Autoren
- Verlag, Erscheinungsdatum
- Umschlagbild

**Hinweise:**
- Daten werden per HTML-Analyse extrahiert — die Formatierung kann weniger konsistent sein als bei API-basierten Quellen
- Am besten als ergänzende Quelle neben Google Books, Open Library und Libris KB verwendet
## Zusammenführungsprüfung

Nachdem BookDB die Ergebnisse aller verfügbaren Quellen abgerufen hat, zeigt der Dialog **Zusammenführungsprüfung** alle abgerufenen Felder nebeneinander:

| Feld | Aktuell | Google Books | Open Library | Libris KB |
|------|---------|-------------|--------------|-----------|
| Titel | — | The Great... | The Great... | — |
| Autor | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Verlag | — | Scribner | — | — |
| Seiten | — | 180 | 172 | — |

Für jedes Feld können Sie:
- Einen Wert aus einer Quelle **übernehmen** (auf den Wert klicken, um ihn auszuwählen)
- Ihren aktuellen Wert **behalten**
- **Alle übernehmen**, um alle eingehenden Werte auf einmal zu akzeptieren

Wenn Sie auf **Speichern** klicken, werden nur die von Ihnen akzeptierten Felder aktualisiert. Ihre vorhandenen Daten werden niemals automatisch überschrieben.

## Wenn eine Quelle keine Ergebnisse liefert

Falls eine Quelle für eine ISBN keine Ergebnisse liefert:
- Die Quellspalte fehlt einfach in der Zusammenführungstabelle
- Andere Quellen sind nicht betroffen
- Dies ist normal bei neueren Büchern, regionalen Veröffentlichungen oder ungewöhnlichen ISBNs

## Ratenbegrenzungen

BookDB respektiert automatisch die Ratenbegrenzungen jeder API. Bei der Massen-Neukatalogisierung (Extras > Neukatalogisierung) werden Anfragen zeitlich gestaffelt, damit Sie von keiner Quelle blockiert werden.
