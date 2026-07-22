# About Data Sources

When you catalog a book by ISBN (Ctrl+I or the toolbar button), BookDB fetches metadata from four public sources simultaneously.

## Lookup Flow

1. You enter an ISBN
2. BookDB fetches all four sources simultaneously — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. The **Merge Review** dialog opens — you choose which fields to accept from each source
4. Book record is saved

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

Google Books is the largest general-purpose book database, with broad coverage of English-language and popular international titles.

**Fields typically provided:**
- Title, Subtitle, Authors
- Publisher, Published Date
- Description (Book Info)
- Page count
- Language
- ISBN-10 and ISBN-13
- Cover image (thumbnail and large)
- Categories

**Notes:**
- Works without a key, but unauthenticated requests share a small daily quota and are frequently rate-limited (429). Add a personal API key (see below) to use your own quota
- Coverage is strongest for post-1980 commercial publications
- Author names may not always match your preferred format

**Getting a Google Books API key (optional)**

Without a key, BookDB shares a small anonymous daily quota with every other unauthenticated caller, so Google Books is frequently rate-limited — a banner in the merge-review dialog names the sources that were dropped. A free personal key moves your lookups onto your own quota:

1. Sign in to the **Google Cloud Console** at https://console.cloud.google.com.
2. Create a new project, or select an existing one.
3. Open **APIs & Services → Library**, search for **Books API**, and click **Enable**.
4. Open **APIs & Services → Credentials**, click **Create credentials → API key**, and copy the key.
5. Recommended: edit the key and, under **API restrictions**, restrict it to the **Books API**.
6. In BookDB, open **Settings → Lookup**, paste the key under **Google Books**, and click **Save**.

The key takes effect on the next lookup — no restart needed. Clear the field and save to go back to the shared quota.

## Open Library

**URL:** https://openlibrary.org

Open Library is an open-access catalog maintained by the Internet Archive. It emphasises completeness over polish — records may have more fields but less consistent formatting.

**Fields typically provided:**
- Title, Authors
- Publisher, Publish Date, Publish Places
- Number of pages
- ISBNs, LCCN, Dewey Decimal
- Cover image

**Notes:**
- Community-maintained — data quality varies
- Particularly good for older or out-of-print books
- Often provides identifiers (LCCN, Dewey) that Google Books does not

## Libris KB

**URL:** https://libris.kb.se

Libris is the Swedish national library catalogue, maintained by the National Library of Sweden (Kungliga biblioteket). It has excellent coverage of Swedish publications and translations into Swedish.

**Fields typically provided:**
- Title, Authors
- Publisher, Publication year
- Language
- ISBN
- Series information
- Dewey Decimal, Call Number

**Notes:**
- Best source for books published in Sweden or translated into Swedish
- Descriptions and summaries may be in Swedish
- Coverage of non-Swedish titles is limited

## IsbnSearch.org

**URL:** https://isbnsearch.org

IsbnSearch.org is a free ISBN lookup service that provides basic bibliographic data parsed from its web pages. It serves as a useful supplementary source for ISBNs that return no results from the API-based sources.

**Fields typically provided:**
- Title, Authors
- Publisher, Published Date
- Cover image

**Notes:**
- Data is extracted by HTML parsing — formatting may be less consistent than API-based sources
- Best used as a supplementary source alongside Google Books, Open Library, and Libris KB
## Merge Review

After BookDB fetches results from all available sources, the **Merge Review** dialog shows all retrieved fields side by side:

| Field | Current | Google Books | Open Library | Libris KB |
|-------|---------|-------------|--------------|-----------|
| Title | — | The Great... | The Great... | — |
| Author | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Publisher | — | Scribner | — | — |
| Pages | — | 180 | 172 | — |

For each field you can:
- **Accept** a value from a source (click the value to select it)
- **Keep** your current value
- **Accept all** to take every incoming value at once

When you click **Save**, only the fields you accepted are updated. Your existing data is never overwritten automatically.

## When a Source Returns No Results

If a source returns no results for an ISBN:
- The source column is simply absent from the Merge Review table
- Other sources are unaffected
- This is normal for newer books, regional publications, or unusual ISBNs

## Rate Limits

BookDB respects each API's rate limits automatically. During batch re-cataloging (Tools > Re-catalog), requests are spaced out so you are never blocked from any source.
