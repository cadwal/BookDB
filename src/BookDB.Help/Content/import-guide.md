# Import Guide

BookDB can import your existing book collection from a Readerware backup — either the backup zip file itself, or the extracted backup folder.

## Import Wizard Flow

1. **File Select** — Choose a backup .zip file or extracted backup folder
2. **Dry-run Preview** — Preview: record count, field coverage, duplicates
3. **Settings** — Set target collection and import options
4. **Import Progress** — Watch progress as records are imported
5. **Report Summary** — Review the results report

## Step-by-Step Instructions

## Step 1 — Select a File

Open the import wizard from **File > Import Readerware backup…** or the toolbar.

Click **Browse** and select one of the following:
- A Readerware **backup zip** (.zip) — a Readerware backup archive created with Readerware's *Backup* function
- A Readerware **backup folder** — the extracted contents of such a zip

Click **Next** to proceed to the dry-run preview.

## Step 2 — Dry-Run Preview

Before any data is written, BookDB analyses the backup and shows:
- **Record count** — how many books were found
- **Field coverage** — which fields were detected and how many records have each field filled in
- **Duplicate ISBNs** — ISBNs that already exist in your collection
- **Encoding issues** — any character encoding problems found in the file

Review the preview carefully. No data is imported until you confirm on Step 4.

Click **Next** to proceed to import settings.

## Step 3 — Import Options

**Target collection** — choose which collection (Fiction, Non-Fiction, Comics, etc.) the imported books will be assigned to. You can change this later by editing individual books.

**Duplicate handling** — if a book with the same ISBN already exists in your collection, BookDB can:
- Skip the duplicate (default)
- Overwrite the existing record
- Ask you each time

Click **Next** to start the import.

## Step 4 — Import Progress

BookDB imports records in batches. The progress bar shows:
- How many records have been processed
- Any records that were skipped or failed

You can cancel the import at any time. Partially imported records are retained.

## Step 5 — Import Report

The final report shows:
- **Records imported** — successfully saved to the database
- **Records skipped** — duplicates or records with errors
- **Fields missing** — fields that were empty across the import file
- **Encoding issues** — any character problems encountered

Click **Finish** to close the wizard. Your book list refreshes automatically.

## Supported File Formats

| Format | Created by | Notes |
|--------|-----------|-------|
| Zip | Readerware > Backup | Backup archive containing book data and cover images |
| Folder | Extract the zip | The extracted contents of a Readerware backup zip |
| Live database | Readerware (the `.rw4` folder) | Converted on the fly — see *Importing from a Live Readerware Database* below |

## Cover Images

Cover images embedded in the backup archive are imported automatically and associated with each book. **JPEG, PNG, GIF, and BMP** covers are supported.

## Multiple Images of the Same Type

A book can end up with more than one image of the same type — Readerware often stores several cover or thumbnail images per book, and they may all import as the same type (for example, two *Front cover* images). BookDB keeps every image, but each type shows only one in the preview: the one with the lowest order.

Books like this are flagged in the book list with a **!** badge on the thumbnail ("Duplicate image types — check the Images tab").

To sort them out, open the book for editing and go to the **Images** tab. Whenever a type holds two or more images, a **Manage all images** section appears, listing every image. For each one you can:

- **Reassign it to a different image type** — for example, retype a second *Front cover* as *Back cover* or *Spine*.
- **Move it up or down within the type** — the top (lowest-order) image becomes that type's preview.
- **Remove the image** entirely.

Save the book to keep your changes. Once each type has at most one image, the **!** badge disappears.

## Importing from a Live Readerware Database

If you don't have a backup but still have your working Readerware database (the `.rw4` folder, e.g. `MyBooks.rw4`), BookDB can read it directly:

1. Open **Tools > Import Readerware database…**.
2. Click **Browse** and select your `.rw4` database folder.
3. Click **Convert**. BookDB copies the database first — your original is never opened or modified — and converts it into a backup folder.
4. When conversion finishes, click **Open import wizard** to continue through the same preview, settings, and import steps described above.

This requires a one-time setup: set the HSQLDB + Java tool folder in **Settings > Import**. That folder must contain `jre\bin\java.exe` and `lib\hsqldb.jar`.

### Supported Readerware Version

This feature supports **Readerware 4** databases — the `DBCATALOG40` format, stored as an HSQLDB 1.8.x database. Cover and thumbnail images in **JPEG, PNG, GIF, or BMP** format are imported.

## Troubleshooting

**"No records found"** — The file may be empty or not a valid Readerware backup. Verify it was created with Readerware's Backup function, not an export.

**"Encoding issues detected"** — BookDB handles character encoding automatically. If you see garbled characters in the preview, the backup file may be damaged — try creating a fresh backup from Readerware.

**Many duplicates shown** — If you have already imported some books by ISBN lookup, they will appear as duplicates. Choose "Skip" to avoid overwriting your manually reviewed records.
