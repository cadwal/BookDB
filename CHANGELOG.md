# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/semver-spec/semver-spec.html).

## [Unreleased]

## [2.2.0] - 2026-07-07

### Added
- In-app help for remote databases: a new **Remote Databases** topic in the Help window (Help → Remote Databases) covering how to choose a database backend and why an OS keyring is required, the server version requirements, moving your library between backends, using the same library from several computers, and how backups behave on a server library — translated into all supported languages.
- "Help: remote databases" links in Settings → Database and Tools → Maintenance → Move library jump straight to that topic. The Move library link keeps working while a move is running.

### Changed
- The book list's right-click menu now shows icons, and several menu icons were replaced with clearer ones: Backup (archive box), Import (import arrow), Catalog by ISBN (barcode scanner — menu and toolbar now match), Re-catalog (book with a refresh arrow), and Full details / Open in window (opening panel). Every icon follows the active theme's colours.

### Fixed
- Opening a book in its own window (Full details / Open in window) no longer asks to save changes when nothing was edited — the window used to count its own start-up as an edit for any book with a publisher, format, or series set.
- Portuguese help topics now load their Brazilian and European translations; previously every Help tab fell back to English for Portuguese users.
- The Help menu items and Help window tab titles are now translated in German, Spanish, French, Italian, Dutch, and Portuguese — they previously appeared in English in those languages.
- European Portuguese texts modernised to post-reform (Acordo Ortográfico de 1990) spelling, and assorted translation fixes in Swedish, Dutch, German, and Spanish (register and compound-word corrections).

## [2.1.0] - 2026-06-30

### Added
- MySQL and MariaDB database support: BookDB can now keep your library on a MySQL or MariaDB server, alongside the existing PostgreSQL server and local SQLite options. **MySQL 8.0 or later** / **MariaDB 10.6 or later** are required (the schema relies on InnoDB full-text search, `utf8mb4`, and microsecond `datetime`); Test connection reports a clear message and refuses to proceed against an older server.
- Maintenance (Tools → Maintenance) now reports how many tables — and which — each integrity check and optimize pass covered, on every backend.
- Settings → Database now offers MySQL / MariaDB as a third backend, using the same connection editor (host, port, database, user, password, TLS mode) and Test connection that reports the server version and book count. As with PostgreSQL, the password is kept in your operating system's secret store; on a system without one, the server backends are disabled and SQLite remains available.
- Move library (Tools → Maintenance): copy your entire catalog to or from a MySQL/MariaDB server in any direction, choosing the target backend explicitly. It takes the same safety backups (source and, if it already holds data, target), checks the target before writing, shows per-table progress, verifies every row count matches, and can switch the active database to the target when it finishes.
- Restore from a CSV archive can restore directly into a MySQL/MariaDB server named by the backup, and the restore-confirmation dialog correctly recognises and names a MySQL/MariaDB archive and detects when its connection differs from the live one.
- The Maintenance tools and multi-client detection work against MySQL/MariaDB the same way they do against PostgreSQL — a server-side sanity check and statistics refresh, and the "another client is connected" warning — since both ride engine-neutral seams.

### Changed
- The note shown when no OS credential store is available now refers to a server database generally, rather than PostgreSQL specifically, because every server backend requires it.

### Fixed
- Clear filter (above the facets) no longer hides every book. It had started an empty advanced search — matching nothing — instead of clearing the filters; it now clears them and shows the full list again.
- Importing from a Readerware backup now untangles messy author fields: a single field holding several authors — a bracketed or comma/"och"-separated list — is split into separate authors, and stray brackets, "by"/"av" prefixes, and "role:" labels are cleaned off.

## [2.0.0] - 2026-06-26

### Added
- PostgreSQL database support: BookDB can now keep your library on a PostgreSQL server instead of the local file, so the same catalog is reachable from more than one machine. SQLite stays the default and needs no setup — remote databases are entirely opt-in. PostgreSQL **12 or later** is required (the full-text search column uses a stored generated column added in PostgreSQL 12); Test connection reports a clear message and refuses to proceed against an older server.
- Settings → Database tab to choose the backend and enter the server details (host, port, database, user, password, TLS mode), with a Test connection button that reports the server version and book count before you commit. Switching the active database restarts the app to load it.
- Passwords for remote databases are stored in your operating system's secret store (Windows Credential Manager, macOS Keychain, or Linux Secret Service) — never in plain text. On a system with no secret store available, the PostgreSQL option is disabled with an explanation and SQLite remains available.
- Move library (Tools → Maintenance): copy your entire catalog between SQLite and PostgreSQL in either direction. It takes a safety backup first, checks the target isn't about to be overwritten unknowingly, shows per-table progress, verifies every row count matches, and can switch the active database to the target when it finishes.
- Restore from a CSV archive backup: it replaces the current library, always taking a safety backup of the live database first, and runs the whole restore as one transaction so a failure leaves your data untouched. When the backup names a different PostgreSQL server, you can restore directly into that server so the data and its connection settings land together.
- Multi-client warning: when using a remote database, BookDB notices if another client appears to be connected and offers to quit rather than risk two clients writing at once (with a "connect anyway" override). A crashed client ages out automatically and never locks you out.
- Connection-loss handling for remote databases: a clear dialog if the server can't be reached at startup (with Retry / open Settings / Quit), automatic reconnection if the connection drops mid-session, and a Retry / Discard prompt if a save can't reach the server — so your edits are never silently lost.
- The Maintenance tools (Tools → Maintenance) now adapt to the active database: on PostgreSQL they run a server-side connectivity and sanity check, VACUUM (ANALYZE), and report the database size, in place of the SQLite-only integrity check and file repair.

### Changed
- Auto-backup on close now uses the engine-neutral CSV archive when the active database is remote (the file-copy backup remains for local SQLite).

## [1.2.1] - 2026-06-22

### Security
- Updated the bundled SQLite native library to a patched version, resolving a known high-severity advisory (GHSA-2m69-gcr7-jv3q) in the version pulled in transitively by the database layer

## [1.2.0] - 2026-06-15

### Fixed
- Launching BookDB while it is already running no longer opens a second copy — the window that's already open comes to the front instead (Windows and Linux, including Raspberry Pi)
- Start Menu and desktop shortcuts created from Settings → Application access now show the BookDB icon instead of a blank one (Windows)

## [1.1.0] - 2026-06-12

### Added
- Colour themes: a new Appearance tab in Settings to choose Default, Vibrant, High contrast, or Dark — applied on the next launch, with WCAG AA contrast across every theme and translated theme names in all supported languages
- Colourful toolbar and menu icons that follow the theme — semantic colours (add = green, edit/open = blue, delete = red), with tinted "chips" behind the coloured actions in the Vibrant theme
- Windows ARM64 and Linux ARM64 (Raspberry Pi, 64-bit) release builds — self-contained archives, a Windows ARM64 winget installer, and a Linux ARM64 AppImage
- Settings → Application access tab: create Start Menu and desktop shortcuts (Windows) or an applications-menu entry (Linux), since the portable/winget install doesn't add one
- Database maintenance: a new Tools → Maintenance dialog that checks your library database for problems (integrity and foreign-key checks) and can optimize and repair it — rebuilding indexes and compacting the file, with an automatic safety backup taken first

### Changed
- Refreshed the default colour palette with deeper contrast across text, borders, and status badges
- Auto-backup on close now runs only when it's worthwhile — when library data changed during the session, or it's been more than 7 days since the last backup — instead of backing up on every exit

### Fixed
- Collection filter button no longer clips the descenders of collection names (e.g. the tail of a "g")
- CSV archive backups now include every table — previously loans, borrowers, saved searches, settings, volumes/chapters, all lookup lists, and the image metadata (which book each image belongs to, and its type) were missing from the archive

## [1.0.0] - 2026-06-02

### Added
- Initial public release of BookDB — a personal book catalog desktop app (Windows, with Linux and macOS builds available)
- Import from Readerware: CSV, XML, and zip backup files, plus direct import from a live Readerware database
- Three-pane browse layout (filter / list / detail) with user-defined collections
- Full-text search across all fields (FTS5) and advanced multi-criteria search
- Cover and image management: multiple image types per book, thumbnail gallery, reordering, and a hover preview
- Lending: check books out to borrowers, track due dates, and view loan history
- Statistics: totals and breakdowns by format, collection, language, and published year, with a books-added-per-year chart
- Bulk edit, plus duplicate detection and merge
- CSV export and PDF list printing
- Backup and restore (SQLite or CSV archive), with optional auto-backup on close
- Multi-language UI: EN, SV, DE, ES, FR, NL, IT, PT-BR, PT-PT
- Help system with per-screen contextual help

[Unreleased]: https://github.com/cadwal/BookDB/compare/v2.2.0...HEAD
[2.1.0]: https://github.com/cadwal/BookDB/releases/tag/v2.1.0
[1.1.0]: https://github.com/cadwal/BookDB/releases/tag/v1.1.0
[1.0.0]: https://github.com/cadwal/BookDB/releases/tag/v1.0.0
[1.2.0]: https://github.com/cadwal/BookDB/releases/tag/v1.2.0
[1.2.1]: https://github.com/cadwal/BookDB/releases/tag/v1.2.1
[2.0.0]: https://github.com/cadwal/BookDB/releases/tag/v2.0.0
[2.2.0]: https://github.com/cadwal/BookDB/releases/tag/v2.2.0