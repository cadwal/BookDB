# BookDB

Source code and releases for a book database application.

The purpose is specifically to allow me, and any other users, to continue to maintain book data from the **Readerware Books** application — which is no longer maintained since the developer passed.

## Getting started

 - Download the application (WinGet / Github Releases)
 - Decide on the collections you want (Lookups | Collections)
 - Import Readerware backups into your collections (File | Import ...)
 - Or, but this depends on the availability of the JRE and HSQLDB from a Readerware 4 installation on your platform, import from a Readerware database.

### Installing and starting via winget (Windows)

```powershell
winget install cadwal.BookDB
```

The winget package is a portable install, so no Start Menu entry is created. To start the
application, open a **new** terminal (winget adds the install folder to your user `PATH`, and
already-open terminals don't see the update) and run:

```powershell
BookDB.Desktop
```

Once the application is running, open **Settings → Application access** to create Start Menu
and desktop shortcuts, so future launches don't need the terminal.

### Installing the companion app (Android)

The companion is an **APK on the release page**, not a Play Store listing:
`BookDB-Companion-vX.Y.Z.apk` under [Releases](https://github.com/cadwal/BookDB/releases). Download it on the
phone or tablet and open it; Android will ask once for permission to install apps from that source. It needs
**Android 8.0 or later**.

The companion does nothing on its own — it talks only to your own computer, over your own network. Turn it on
in **Settings → Companion** on the desktop, then pair the device from **Tools → Maintenance → Devices**; the
desktop's Help has a **Companion** topic covering pairing and firewalls.

> Keep the app updated from the same source. Android ties an installed app to the key it was signed with, so
> an APK from anywhere else cannot update this one.

## What it does

With this application you can maintain a book database. It supports:

- ISBN-based cataloguing via metadata sources
- Print support
- Statistics and searching
- **Import from a Readerware Books backup** — preserving all data from the final version
- **An Android companion app** for cataloguing away from the desk: scan a book's barcode, photograph its
  covers, and send the batch to your computer over your own network — plus a read-only browse of the library
  to settle "do I already own this?" in a shop

The application uses a **SQLite3 database** by default, so tooling exists in almost any programming language on almost any platform to let you use the data in any form you like. Optionally, it can keep the library on a **PostgreSQL server** instead, so the same catalog is reachable from more than one machine — SQLite stays the default and needs no setup, and you can move the library between the two in either direction. A PostgreSQL backend requires **PostgreSQL 12 or later**.

## What it does not do

- A standalone mobile app — the companion is a companion: it holds no library of its own and needs your
  computer to be reachable for anything but scanning
- No cloud, no account, no sync service — the companion talks to your computer directly, over your own network
- No telemetry, analytics, or "phone-home" — see [Privacy](PRIVACY.md)

Compared to Readerware, the metadata sources supported are limited.

## Translations

Translations are available for a small set of languages. None of them especially good, I assume.

## Contributions

PRs, comments, and feature ideas are welcome — but the amount of time and effort available is limited. The core goal was to let me keep maintaining my own database.

## Platform notes

As a Windows user, the backup import has been tested against what the Windows version of Readerware generated. It should work with other platforms since Readerware was a Java application, but this has not been verified. Also the import from database is only tested on Windows.

The macOS version is of the "it builds, ship it" variety — I do not have a Mac to test with.

The companion app is Android only. It is built to work on tablets as well as phones, but it has been tested
on rather few devices.

## Privacy

BookDB runs entirely on your machine, has no telemetry, and collects nothing about
you. It only reaches the internet when you explicitly look up a book or cover by ISBN.
Full details: [Privacy Policy](PRIVACY.md).

---

[Behind the scenes](BACKSTORY.md)
