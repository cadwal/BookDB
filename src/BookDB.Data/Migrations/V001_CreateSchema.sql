-- V001_CreateSchema.sql
-- Single authoritative create-from-scratch script.
-- Replaces the 14-script incremental chain (V001_InitialSchema through V014_LoanDateFieldsDateTime).
-- Produces the full current schema; no ALTER TABLE, RENAME, or DROP statements.

-- WAL mode must be the first statement so all subsequent writes use write-ahead logging.
PRAGMA journal_mode = WAL;
-- Enable FK enforcement for all subsequent connections via SqlitePragmaInterceptor.
PRAGMA foreign_keys = ON;

-- ============================================================
-- Lookup tables (no foreign keys — create first)
-- ============================================================

CREATE TABLE Collection (
    CollectionId    INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    SortOrder       INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Person (
    PersonId        INTEGER PRIMARY KEY AUTOINCREMENT,
    DisplayName     TEXT    NOT NULL,
    SortName        TEXT    NOT NULL,
    Bio             TEXT    NULL,
    BirthDate       TEXT    NULL,
    BirthPlace      TEXT    NULL,
    DeathDate       TEXT    NULL,
    DeathPlace      TEXT    NULL,
    Website         TEXT    NULL
);

CREATE TABLE ContributorRole (
    ContributorRoleId   INTEGER PRIMARY KEY AUTOINCREMENT,
    Code                TEXT    NOT NULL UNIQUE,
    DisplayName         TEXT    NOT NULL,
    ResourceKey         TEXT    NULL
);

CREATE TABLE Publisher (
    PublisherId     INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE
);

CREATE TABLE Series (
    SeriesId        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE
);

CREATE TABLE Category (
    CategoryId      INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL,
    SortOrder       INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Condition (
    ConditionId     INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    ResourceKey     TEXT    NULL
);

CREATE TABLE Edition (
    EditionId       INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    ResourceKey     TEXT    NULL
);

CREATE TABLE Format (
    FormatId        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    ResourceKey     TEXT    NULL
);

CREATE TABLE Language (
    LanguageId      INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    ResourceKey     TEXT    NULL
);

CREATE TABLE Location (
    LocationId      INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE
);

CREATE TABLE Owner (
    OwnerId         INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE
);

CREATE TABLE PurchasePlace (
    PurchasePlaceId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE
);

-- NumericValue allows ratings to be ordered numerically (e.g. 1.0-5.0) independent of display name.
CREATE TABLE Rating (
    RatingId        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    NumericValue    REAL    NULL,
    ResourceKey     TEXT    NULL
);

CREATE TABLE ReadingLevel (
    ReadingLevelId  INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    ResourceKey     TEXT    NULL
);

CREATE TABLE Source (
    SourceId        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE
);

CREATE TABLE Status (
    StatusId        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    ResourceKey     TEXT    NULL
);

CREATE TABLE BorrowerStatus (
    BorrowerStatusId  INTEGER PRIMARY KEY,
    StatusName        TEXT    NOT NULL,
    ResourceKey       TEXT    NULL
);

CREATE TABLE BookImageType (
    BookImageTypeId  INTEGER PRIMARY KEY,
    TypeName         TEXT    NOT NULL,
    ResourceKey      TEXT    NULL
);

-- ============================================================
-- Cross-reference tables that depend only on lookup tables
-- ============================================================

CREATE TABLE CategoryCollection (
    CategoryId      INTEGER NOT NULL REFERENCES Category(CategoryId) ON DELETE CASCADE,
    CollectionId    INTEGER NOT NULL REFERENCES Collection(CollectionId) ON DELETE CASCADE,
    PRIMARY KEY (CategoryId, CollectionId)
);

-- ============================================================
-- Core entity tables
-- ============================================================

CREATE TABLE Book (
    BookId              INTEGER PRIMARY KEY AUTOINCREMENT,
    CollectionId        INTEGER REFERENCES Collection(CollectionId),
    Title               TEXT    NOT NULL,
    Subtitle            TEXT    NULL,
    PublisherId         INTEGER REFERENCES Publisher(PublisherId),
    PubPlace            TEXT    NULL,
    PubDate             TEXT    NULL,
    CopyrightDate       TEXT    NULL,
    FormatId            INTEGER REFERENCES Format(FormatId),
    EditionId           INTEGER REFERENCES Edition(EditionId),
    Pages               INTEGER NULL,
    Copies              INTEGER NOT NULL DEFAULT 1,
    Isbn                TEXT    NULL,
    LanguageId          INTEGER REFERENCES Language(LanguageId),
    SeriesId            INTEGER REFERENCES Series(SeriesId),
    SeriesNumber        TEXT    NULL,
    ReadCount           INTEGER NOT NULL DEFAULT 0,
    RatingId            INTEGER REFERENCES Rating(RatingId),
    ConditionId         INTEGER REFERENCES Condition(ConditionId),
    LocationId          INTEGER REFERENCES Location(LocationId),
    OwnerId             INTEGER REFERENCES Owner(OwnerId),
    StatusId            INTEGER REFERENCES Status(StatusId),
    Signed              INTEGER NOT NULL DEFAULT 0,
    OutOfPrint          INTEGER NOT NULL DEFAULT 0,
    Favorite            INTEGER NOT NULL DEFAULT 0,
    Keywords            TEXT    NULL,
    Comments            TEXT    NULL,
    BookInfo            TEXT    NULL,
    PurchasePrice       REAL    NULL,
    PurchasePlaceId     INTEGER REFERENCES PurchasePlace(PurchasePlaceId),
    ListPrice           REAL    NULL,
    SourceId            INTEGER REFERENCES Source(SourceId),
    ExternalId          TEXT    NULL,
    MediaLink           TEXT    NULL,
    Display             INTEGER NOT NULL DEFAULT 1,
    ReadingLevelId      INTEGER REFERENCES ReadingLevel(ReadingLevelId),
    Added               DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    Updated             DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    PurchaseCurrency    TEXT    NULL,
    ListPriceCurrency   TEXT    NULL,
    AltTitle            TEXT    NULL,
    AmazonAsin          TEXT    NULL,
    PurchaseDate        DATETIME NULL,
    DateLastRead        DATETIME NULL,
    Issn                TEXT    NULL,
    Lccn                TEXT    NULL,
    DeweyDecimal        TEXT    NULL,
    CallNumber          TEXT    NULL,
    Dimensions          TEXT    NULL,
    Weight              REAL    NULL,
    ItemValue           REAL    NULL,
    ValuationDate       DATETIME NULL,
    AmazonNewValue          REAL    NULL,
    AmazonUsedValue         REAL    NULL,
    AmazonCollectibleValue  REAL    NULL,
    AmazonNewCount          INTEGER NULL,
    AmazonUsedCount         INTEGER NULL,
    AmazonCollectibleCount  INTEGER NULL,
    SalesRank               INTEGER NULL,
    LexileLevel             INTEGER NULL
);

CREATE TABLE BookContributor (
    BookContributorId   INTEGER PRIMARY KEY AUTOINCREMENT,
    BookId              INTEGER NOT NULL REFERENCES Book(BookId) ON DELETE CASCADE,
    PersonId            INTEGER NOT NULL REFERENCES Person(PersonId),
    ContributorRoleId   INTEGER NOT NULL REFERENCES ContributorRole(ContributorRoleId),
    SortOrder           INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE BookCategory (
    BookId      INTEGER NOT NULL REFERENCES Book(BookId) ON DELETE CASCADE,
    CategoryId  INTEGER NOT NULL REFERENCES Category(CategoryId),
    PRIMARY KEY (BookId, CategoryId)
);

CREATE TABLE Settings (
    Key     TEXT PRIMARY KEY,
    Value   TEXT NULL
);

CREATE TABLE SavedSearch (
    SavedSearchId   INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    QueryJson       TEXT    NOT NULL,
    CreatedAt       DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE TABLE BatchQueueItem (
    BatchQueueItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    Isbn             TEXT NOT NULL,
    BookId           INTEGER,
    Status           TEXT NOT NULL DEFAULT 'Pending',
    ResultJson       TEXT,
    CreatedAt        DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    UpdatedAt        DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE TABLE BookImage (
    BookImageId     INTEGER PRIMARY KEY AUTOINCREMENT,
    BookId          INTEGER NOT NULL REFERENCES Book(BookId) ON DELETE CASCADE,
    ImageData       BLOB    NOT NULL,
    MimeType        TEXT    NOT NULL DEFAULT 'image/jpeg',
    IsPrimary       INTEGER NOT NULL DEFAULT 0,
    DisplayOrder    INTEGER NOT NULL DEFAULT 0,
    Added           DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    BookImageTypeId INTEGER NOT NULL DEFAULT 0 REFERENCES BookImageType(BookImageTypeId)
);

CREATE TABLE BookVolume (
    BookVolumeId  INTEGER PRIMARY KEY AUTOINCREMENT,
    BookId        INTEGER NOT NULL REFERENCES Book(BookId) ON DELETE CASCADE,
    VolumeNumber  INTEGER NOT NULL
);

CREATE TABLE BookChapter (
    BookChapterId  INTEGER PRIMARY KEY AUTOINCREMENT,
    BookVolumeId   INTEGER NOT NULL REFERENCES BookVolume(BookVolumeId) ON DELETE CASCADE,
    ChapterNumber  INTEGER NOT NULL
);

CREATE TABLE Borrower (
    BorrowerId          INTEGER PRIMARY KEY AUTOINCREMENT,
    StatusId            INTEGER NOT NULL DEFAULT 0 REFERENCES BorrowerStatus(BorrowerStatusId),
    FirstName           TEXT NULL,
    LastName            TEXT NULL,
    BorrowerExternalId  TEXT NULL,
    Organization        TEXT NULL,
    Address1            TEXT NULL,
    Address2            TEXT NULL,
    City                TEXT NULL,
    State               TEXT NULL,
    PostalCode          TEXT NULL,
    Country             TEXT NULL,
    Phone1              TEXT NULL,
    Phone2              TEXT NULL,
    Email               TEXT NULL,
    Fax                 TEXT NULL
);

-- Loan records are historical: deleting a Book or Borrower with associated loans is blocked.
-- Delete all loan records for that book/borrower first before removing the parent row.
CREATE TABLE Loan (
    LoanId          INTEGER PRIMARY KEY AUTOINCREMENT,
    BookId          INTEGER NOT NULL REFERENCES Book(BookId) ON DELETE RESTRICT,
    BorrowerId      INTEGER NOT NULL REFERENCES Borrower(BorrowerId) ON DELETE RESTRICT,
    LoanedDate      DATETIME NULL,
    DueDate         DATETIME NULL,
    ReturnedDate    DATETIME NULL,
    LoanExternalId  TEXT NULL
);

-- ============================================================
-- FTS5 virtual table (external content, points at Book)
-- ============================================================

CREATE VIRTUAL TABLE IF NOT EXISTS fts_books USING fts5(
    Title,
    Subtitle,
    Keywords,
    Comments,
    BookInfo,
    ExternalId,
    content='Book',
    content_rowid='BookId'
);

-- ============================================================
-- Indexes
-- ============================================================

-- Book single-column indexes
CREATE INDEX IX_Book_CollectionId        ON Book(CollectionId);
CREATE INDEX IX_Book_PublisherId         ON Book(PublisherId);
CREATE INDEX IX_Book_SeriesId            ON Book(SeriesId);
CREATE INDEX IX_Book_Isbn                ON Book(Isbn);
CREATE INDEX IX_Book_StatusId            ON Book(StatusId);
CREATE INDEX IX_Book_FormatId            ON Book(FormatId);
CREATE INDEX IX_Book_LanguageId          ON Book(LanguageId);
CREATE INDEX IX_Book_LocationId          ON Book(LocationId);
CREATE INDEX IX_Book_OwnerId             ON Book(OwnerId);
CREATE INDEX IX_Book_RatingId            ON Book(RatingId);
CREATE INDEX IX_BookContributor_BookId   ON BookContributor(BookId);
CREATE INDEX IX_BookContributor_PersonId ON BookContributor(PersonId);
CREATE INDEX IX_BookCategory_CategoryId  ON BookCategory(CategoryId);

-- BookImage index
CREATE INDEX IX_BookImage_BookId ON BookImage(BookId);

-- BookVolume / BookChapter indexes
CREATE INDEX IX_BookVolume_BookId        ON BookVolume(BookId);
CREATE INDEX IX_BookChapter_BookVolumeId ON BookChapter(BookVolumeId);

-- BatchQueueItem index
CREATE INDEX IX_BatchQueueItem_Status ON BatchQueueItem(Status);

-- Compound indexes and ISBN uniqueness
CREATE INDEX IF NOT EXISTS IX_Book_Collection_Status
    ON Book(CollectionId, StatusId);

CREATE INDEX IF NOT EXISTS IX_Book_Collection_Format
    ON Book(CollectionId, FormatId);

-- Partial unique index: allows NULL ISBNs and empty strings (books without ISBNs)
CREATE UNIQUE INDEX IF NOT EXISTS UX_Book_Isbn
    ON Book(Isbn)
    WHERE Isbn IS NOT NULL AND Isbn != '';

-- ============================================================
-- FTS5 triggers
-- ============================================================

-- After-INSERT trigger
CREATE TRIGGER IF NOT EXISTS fts_books_ai AFTER INSERT ON Book BEGIN
    INSERT INTO fts_books(rowid, Title, Subtitle, Keywords, Comments, BookInfo, ExternalId)
    VALUES (new.BookId, new.Title, new.Subtitle, new.Keywords, new.Comments, new.BookInfo, new.ExternalId);
END;

-- After-DELETE trigger (FTS5 delete syntax required)
CREATE TRIGGER IF NOT EXISTS fts_books_ad AFTER DELETE ON Book BEGIN
    INSERT INTO fts_books(fts_books, rowid, Title, Subtitle, Keywords, Comments, BookInfo, ExternalId)
    VALUES ('delete', old.BookId, old.Title, old.Subtitle, old.Keywords, old.Comments, old.BookInfo, old.ExternalId);
END;

-- After-UPDATE trigger (delete old + insert new)
CREATE TRIGGER IF NOT EXISTS fts_books_au AFTER UPDATE ON Book BEGIN
    INSERT INTO fts_books(fts_books, rowid, Title, Subtitle, Keywords, Comments, BookInfo, ExternalId)
    VALUES ('delete', old.BookId, old.Title, old.Subtitle, old.Keywords, old.Comments, old.BookInfo, old.ExternalId);
    INSERT INTO fts_books(rowid, Title, Subtitle, Keywords, Comments, BookInfo, ExternalId)
    VALUES (new.BookId, new.Title, new.Subtitle, new.Keywords, new.Comments, new.BookInfo, new.ExternalId);
END;

-- ============================================================
-- Seed data
-- ============================================================

-- Seed: Collections
INSERT OR IGNORE INTO Collection (Name, SortOrder) VALUES ('Comics', 1);
INSERT OR IGNORE INTO Collection (Name, SortOrder) VALUES ('Fiction', 2);
INSERT OR IGNORE INTO Collection (Name, SortOrder) VALUES ('Non-Fiction', 3);

-- Seed: Contributor roles
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Author', 'Author', 'ContributorRole_Author');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Editor', 'Editor', 'ContributorRole_Editor');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Translator', 'Translator', 'ContributorRole_Translator');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Foreword', 'Foreword', 'ContributorRole_Foreword');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Introduction', 'Introduction', 'ContributorRole_Introduction');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Illustrator', 'Illustrator', 'ContributorRole_Illustrator');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Writer', 'Writer', 'ContributorRole_Writer');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Penciller', 'Penciller', 'ContributorRole_Penciller');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Inker', 'Inker', 'ContributorRole_Inker');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Colorist', 'Colorist', 'ContributorRole_Colorist');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Letterer', 'Letterer', 'ContributorRole_Letterer');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('CoverArtist', 'Cover Artist', 'ContributorRole_CoverArtist');
INSERT OR IGNORE INTO ContributorRole (Code, DisplayName, ResourceKey) VALUES ('Designer', 'Designer', 'ContributorRole_Designer');

-- Seed: Categories
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Adventure', 1);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Biography', 2);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Children', 3);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Classic', 4);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Crime', 5);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Fantasy', 6);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('History', 7);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Horror', 8);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Humor', 9);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Mystery', 10);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Poetry', 11);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Reference', 12);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Romance', 13);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Science', 14);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Science Fiction', 15);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Self-Help', 16);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Superhero', 17);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Thriller', 18);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Travel', 19);
INSERT OR IGNORE INTO Category (Name, SortOrder) VALUES ('Young Adult', 20);

-- Seed: CategoryCollection
INSERT OR IGNORE INTO CategoryCollection (CategoryId, CollectionId)
SELECT c.CategoryId, col.CollectionId
FROM Category c
CROSS JOIN Collection col;

-- Seed: Formats
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Hardcover', 'Format_Hardcover');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Paperback', 'Format_Paperback');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Trade Paperback', 'Format_TradePaperback');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Mass Market Paperback', 'Format_MassMarketPaperback');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('E-book', 'Format_Ebook');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Audiobook', 'Format_Audiobook');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Graphic Novel', 'Format_GraphicNovel');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Comic', 'Format_Comic');
INSERT OR IGNORE INTO Format (Name, ResourceKey) VALUES ('Magazine', 'Format_Magazine');

-- Seed: Conditions
INSERT OR IGNORE INTO Condition (Name, ResourceKey) VALUES ('New', 'Condition_New');
INSERT OR IGNORE INTO Condition (Name, ResourceKey) VALUES ('Like New', 'Condition_LikeNew');
INSERT OR IGNORE INTO Condition (Name, ResourceKey) VALUES ('Very Good', 'Condition_VeryGood');
INSERT OR IGNORE INTO Condition (Name, ResourceKey) VALUES ('Good', 'Condition_Good');
INSERT OR IGNORE INTO Condition (Name, ResourceKey) VALUES ('Fair', 'Condition_Fair');
INSERT OR IGNORE INTO Condition (Name, ResourceKey) VALUES ('Poor', 'Condition_Poor');

-- Seed: Editions
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('1st Edition', 'Edition_1stEdition');
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('2nd Edition', 'Edition_2ndEdition');
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('3rd Edition', 'Edition_3rdEdition');
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('Revised Edition', 'Edition_RevisedEdition');
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('Collector''s Edition', 'Edition_CollectorsEdition');
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('Limited Edition', 'Edition_LimitedEdition');
INSERT OR IGNORE INTO Edition (Name, ResourceKey) VALUES ('Signed Edition', 'Edition_SignedEdition');

-- Seed: Languages
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('English', 'Language_English');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Swedish', 'Language_Swedish');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('French', 'Language_French');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('German', 'Language_German');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Spanish', 'Language_Spanish');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Italian', 'Language_Italian');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Japanese', 'Language_Japanese');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Norwegian', 'Language_Norwegian');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Danish', 'Language_Danish');
INSERT OR IGNORE INTO Language (Name, ResourceKey) VALUES ('Finnish', 'Language_Finnish');

-- Seed: Ratings
INSERT OR IGNORE INTO Rating (Name, NumericValue, ResourceKey) VALUES ('1 Star', 1.0, 'Rating_1Star');
INSERT OR IGNORE INTO Rating (Name, NumericValue, ResourceKey) VALUES ('2 Stars', 2.0, 'Rating_2Stars');
INSERT OR IGNORE INTO Rating (Name, NumericValue, ResourceKey) VALUES ('3 Stars', 3.0, 'Rating_3Stars');
INSERT OR IGNORE INTO Rating (Name, NumericValue, ResourceKey) VALUES ('4 Stars', 4.0, 'Rating_4Stars');
INSERT OR IGNORE INTO Rating (Name, NumericValue, ResourceKey) VALUES ('5 Stars', 5.0, 'Rating_5Stars');

-- Seed: Statuses
INSERT OR IGNORE INTO Status (Name, ResourceKey) VALUES ('Owned', 'Status_Owned');
INSERT OR IGNORE INTO Status (Name, ResourceKey) VALUES ('Wishlist', 'Status_Wishlist');
INSERT OR IGNORE INTO Status (Name, ResourceKey) VALUES ('On Order', 'Status_OnOrder');
INSERT OR IGNORE INTO Status (Name, ResourceKey) VALUES ('Borrowed', 'Status_Borrowed');
INSERT OR IGNORE INTO Status (Name, ResourceKey) VALUES ('Sold', 'Status_Sold');
INSERT OR IGNORE INTO Status (Name, ResourceKey) VALUES ('Given Away', 'Status_GivenAway');

-- Seed: Reading levels
INSERT OR IGNORE INTO ReadingLevel (Name, ResourceKey) VALUES ('Easy Reader', 'ReadingLevel_EasyReader');
INSERT OR IGNORE INTO ReadingLevel (Name, ResourceKey) VALUES ('Middle Grade', 'ReadingLevel_MiddleGrade');
INSERT OR IGNORE INTO ReadingLevel (Name, ResourceKey) VALUES ('Young Adult', 'ReadingLevel_YoungAdult');
INSERT OR IGNORE INTO ReadingLevel (Name, ResourceKey) VALUES ('Adult', 'ReadingLevel_Adult');

-- Seed: Default single-value lookups
INSERT OR IGNORE INTO Location (Name) VALUES ('Default');
INSERT OR IGNORE INTO Owner (Name) VALUES ('Me');
INSERT OR IGNORE INTO Publisher (Name) VALUES ('Unknown');
INSERT OR IGNORE INTO PurchasePlace (Name) VALUES ('Unknown');
INSERT OR IGNORE INTO Series (Name) VALUES ('Standalone');
INSERT OR IGNORE INTO Source (Name) VALUES ('Manual Entry');

-- Seed: Settings
INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('PrimaryDisplayRole', 'Author');

-- Seed: BookImageType
INSERT OR IGNORE INTO BookImageType (BookImageTypeId, TypeName, ResourceKey) VALUES (0, 'Cover', 'BookImageType_Cover');
INSERT OR IGNORE INTO BookImageType (BookImageTypeId, TypeName, ResourceKey) VALUES (1, 'Thumbnail', 'BookImageType_Thumbnail');
INSERT OR IGNORE INTO BookImageType (BookImageTypeId, TypeName, ResourceKey) VALUES (2, 'BackCover', 'BookImageType_BackCover');
INSERT OR IGNORE INTO BookImageType (BookImageTypeId, TypeName, ResourceKey) VALUES (3, 'Spine', 'BookImageType_Spine');
INSERT OR IGNORE INTO BookImageType (BookImageTypeId, TypeName, ResourceKey) VALUES (4, 'DustJacket', 'BookImageType_DustJacket');

-- Seed: BorrowerStatus
INSERT OR IGNORE INTO BorrowerStatus VALUES (0, 'Active', 'BorrowerStatus_Active');
INSERT OR IGNORE INTO BorrowerStatus VALUES (1, 'Inactive', 'BorrowerStatus_Inactive');
