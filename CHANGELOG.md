# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/semver-spec/semver-spec.html).

## [Unreleased]

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

[Unreleased]: https://github.com/cadwal/BookDB/compare/v1.2.0...HEAD
[1.1.0]: https://github.com/cadwal/BookDB/releases/tag/v1.1.0
[1.0.0]: https://github.com/cadwal/BookDB/releases/tag/v1.0.0
[1.2.0]: https://github.com/cadwal/BookDB/releases/tag/v1.2.0