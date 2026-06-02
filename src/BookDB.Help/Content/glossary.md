# Field Glossary

Descriptions of all fields in BookDB. Fields marked as *optional* do not need to be filled in to save a book.

## Title Information

| Field | Description |
|-------|-------------|
| Title | The primary title of the book. Required. |
| Subtitle | A secondary title line, typically shown below the main title on the cover. *Optional.* |
| Alt Title | An alternative or original-language title (e.g. the English title of a translated work). *Optional.* |

## Contributors

| Field | Description |
|-------|-------------|
| Authors / Contributors | The people involved in creating the book — Author, Editor, Illustrator, Designer, and other roles. Each contributor is a person record linked to the book with a role. |

## Publication Details

| Field | Description |
|-------|-------------|
| Publisher | The publishing house that released the book. *Optional.* |
| Pub Place | The city or country of publication. *Optional.* |
| Pub Date | The year of publication. Stored as text to support partial or approximate dates such as "ca. 1950". *Optional.* |
| Copyright Date | The copyright year, which may differ from the publication date in later editions. *Optional.* |
| Format | The physical format: Hardcover, Paperback, Large Print, etc. *Optional.* |
| Edition | The edition of the book: First, Second, Revised, etc. *Optional.* |
| Pages | The total page count. *Optional.* |
| Language | The language of the text in the book. *Optional.* |

## Identifiers

| Field | Description |
|-------|-------------|
| ISBN | The International Standard Book Number (ISBN-10 or ISBN-13). Used for metadata lookup and duplicate detection. *Optional.* |
| ISSN | The International Standard Serial Number, for periodicals. *Optional.* |
| LCCN | Library of Congress Control Number. *Optional.* |
| Dewey Decimal | Dewey Decimal Classification code. *Optional.* |
| Call Number | A library call number for shelf location. *Optional.* |

## Series

| Field | Description |
|-------|-------------|
| Series | The series the book belongs to, if any. *Optional.* |
| Series Number | The position of this book within the series (e.g. "3" or "3.5"). *Optional.* |

## Your Copy

| Field | Description |
|-------|-------------|
| Copies | The number of physical copies you own. Defaults to 1. |
| Condition | The physical condition of your copy: Fine, Very Good, Good, Fair, Poor, etc. *Optional.* |
| Location | The shelf, room, or storage location where this copy is kept. *Optional.* |
| Owner | Who owns this copy (useful for shared collections). *Optional.* |
| Signed | Whether this is an autographed copy. |
| Out of Print | Whether the book is marked as out of print. |

## Reading Tracking

| Field | Description |
|-------|-------------|
| Status | Your reading status: To Read, Reading, Read, Abandoned, etc. *Optional.* |
| Read Count | How many times you have read this book. |
| Date Last Read | The date you most recently finished reading this book. *Optional.* |
| Rating | Your personal rating. *Optional.* |
| Favorite | Whether this book is marked as a favourite. |
| Reading Level | The intended reading level (age or grade). *Optional.* |

## Purchase & Value

| Field | Description |
|-------|-------------|
| Purchase Price | The price you paid for this copy. *Optional.* |
| Purchase Currency | The currency of the purchase price (e.g. SEK, USD, EUR). *Optional.* |
| Purchase Place | Where you bought the book. *Optional.* |
| Purchase Date | The date you bought the book. *Optional.* |
| List Price | The publisher's retail list price. *Optional.* |
| List Price Currency | The currency of the list price. *Optional.* |
| Item Value | Your assessed monetary value of this copy (for insurance purposes, etc.). *Optional.* |
| Valuation Date | The date the item value was assessed. *Optional.* |

## Description & Notes

| Field | Description |
|-------|-------------|
| Keywords | Free-text tags for your own use. *Optional.* |
| Comments | Your personal notes about this book. *Optional.* |
| Book Info | An extended description or synopsis. *Optional.* |
| Dimensions | Physical dimensions of the book (e.g. "24 × 16 × 3 cm"). *Optional.* |
| Weight | The physical weight of the book. *Optional.* |

## System & Source Fields

| Field | Description |
|-------|-------------|
| Source | Where the catalog record originated (e.g. Imported, Manual, ISBN Lookup). *Optional.* |
| Media Link | A URL to related media or the publisher's page for this book. *Optional.* |
| Categories | The collection categories this book belongs to (e.g. Fiction, Comics). Managed in the filter panel. |
| Added | The date and time this record was created in BookDB. Set automatically. |
| Updated | The date and time this record was last modified. Updated automatically on save. |
