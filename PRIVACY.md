# BookDB Privacy Policy

BookDB is a desktop application that runs entirely on your own machine. It has **no
telemetry and no analytics**, and the author collects no data about you or your usage
whatsoever. The only thing BookDB does automatically over the network is a weekly check
for a newer version (see below); everything else happens only when you ask for it.

## Where your data lives

- Your catalog is stored **locally** in a SQLite database by default.
- Optionally, you may point BookDB at a database server (**PostgreSQL, MySQL, or
  MariaDB**) that **you** host and control. Connection details and credentials stay on
  your machine; BookDB never transmits them anywhere else.

## Outbound network requests

Apart from the weekly update check described below, BookDB contacts the internet **only
when you explicitly ask it to** — for example when you look up a book by ISBN or fetch a
cover image while adding or scanning books. In
those cases it sends the ISBN (or a cover-image URL you provide) to the third-party
metadata service you are using:

- Google Books
- Open Library
- Libris (National Library of Sweden / Kungliga biblioteket)
- isbnsearch.org

These requests contain only the search term needed for the lookup. BookDB sends them
no personal information and no identifiers. Each of these services has its own privacy
policy governing how it handles requests it receives. If you never perform a lookup,
the only outbound request BookDB makes is the weekly update check below (plus connecting
to a remote database backend, if you have configured one).

## Phone or tablet companion

BookDB can optionally accept books scanned on a **phone or tablet** running the BookDB
companion app on the same local network. This feature is **off by default**; it does
nothing until you enable it and pair a device.

- All communication between the companion device and BookDB happens **directly over your
  local network** (your Wi-Fi/LAN). It never travels over the internet and never passes
  through the author or any third-party server.
- The connection is **encrypted**, and a device can connect only after you have **paired**
  it — a one-time exchange of security certificates that you initiate from this computer.
  Only devices you have approved can send anything, and you can remove a device at any time.
- The scanned data (ISBNs and any cover or page photos) is added to **your** library on
  your machine, exactly as if you had typed it in. Nothing about it is sent to the author.
- When your library is on a remote database server **you** control, companion scans are
  written to that library the same way the desktop writes to it; the connection details
  still never leave your machine.

### The companion app and Google Play services

The companion app uses two pieces of **Google's ML Kit**, and they differ in what they need:

- **Reading the barcode** uses ML Kit's barcode scanner with its model **built into the app**.
  It works entirely on the device, with no Google Play services and no download — including on
  a device with no Google services at all.
- **Photographing covers** uses ML Kit's **document scanner**, which is delivered by **Google
  Play services**. The first time you photograph a cover, Play services may download that
  component from Google; that request goes to Google and is governed by Google's own privacy
  policy. A device without Play services can still scan barcodes and send books by their ISBN —
  the app says so rather than failing.

Neither sends your photos anywhere: both run on the device, and the pictures go only to your
own computer. Book metadata lookups are still performed by the desktop copy of BookDB (see
*Outbound network requests* above), not by the device.

## Update check

About once a week, when you open BookDB, it checks whether a newer version is available
and shows a small indicator if so. This is the one request BookDB makes without being
asked:

- It reads only the **public list of BookDB releases** — the GitHub Releases page for
  this project on most installs; on a winget install it asks winget, and on an
  AM/AppImage install it asks the AM updater.
- The request carries **no personal information and no identifiers** — it is an ordinary
  request for a public page, like opening that page in a web browser. BookDB sends no
  account, device id, or usage data, and gets back only the latest version number.
- Nothing about you or your library is uploaded.

## Contact

Questions: <https://github.com/cadwal/BookDB/issues>
