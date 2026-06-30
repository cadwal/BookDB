-- V001_CreateSchema.sql (MySQL / MariaDB)
-- Provider-specific create-from-scratch script. Mirrors the SQLite/PostgreSQL schema with native MySQL types:
-- tinyint(1) for bools, datetime(6) for dates, LONGBLOB for images, AUTO_INCREMENT identity keys, and a
-- FULLTEXT index in place of SQLite FTS5 / PostgreSQL tsvector. Identifiers are backtick-quoted to preserve the
-- PascalCase names the shared EF model maps to. Tables are utf8mb4 / utf8mb4_unicode_ci so lookup-name
-- uniqueness and search are case-insensitive natively (no ILIKE plumbing).
--
-- Two MySQL-specific shapes worth noting:
--   * InnoDB silently ignores column-level inline REFERENCES, so every foreign key is declared as a
--     table-level constraint (the FK delete-guard depends on these being real).
--   * datetime DEFAULTs use UTC_TIMESTAMP(6); the EF model customizer stores the UTC wall-clock and re-tags
--     it Utc on read, so the stored instant is timezone-independent across clients.

-- ============================================================
-- Lookup tables (referenced by foreign keys — create first)
-- ============================================================

CREATE TABLE `Collection` (
    `CollectionId`  INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `SortOrder`     INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (`CollectionId`),
    UNIQUE KEY `UX_Collection_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Person` (
    `PersonId`      INT           NOT NULL AUTO_INCREMENT,
    `DisplayName`   VARCHAR(255)  NOT NULL,
    `SortName`      VARCHAR(255)  NOT NULL,
    `Bio`           TEXT          NULL,
    `BirthDate`     VARCHAR(255)  NULL,
    `BirthPlace`    VARCHAR(255)  NULL,
    `DeathDate`     VARCHAR(255)  NULL,
    `DeathPlace`    VARCHAR(255)  NULL,
    `Website`       VARCHAR(2048) NULL,
    PRIMARY KEY (`PersonId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ContributorRole` (
    `ContributorRoleId` INT          NOT NULL AUTO_INCREMENT,
    `Code`              VARCHAR(255) NOT NULL,
    `DisplayName`       VARCHAR(255) NOT NULL,
    `ResourceKey`       VARCHAR(255) NULL,
    PRIMARY KEY (`ContributorRoleId`),
    UNIQUE KEY `UX_ContributorRole_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Publisher` (
    `PublisherId`   INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    PRIMARY KEY (`PublisherId`),
    UNIQUE KEY `UX_Publisher_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Series` (
    `SeriesId`      INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    PRIMARY KEY (`SeriesId`),
    UNIQUE KEY `UX_Series_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Category.Name is intentionally not unique (mirrors the other providers).
CREATE TABLE `Category` (
    `CategoryId`    INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `SortOrder`     INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (`CategoryId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Condition` (
    `ConditionId`   INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `ResourceKey`   VARCHAR(255) NULL,
    PRIMARY KEY (`ConditionId`),
    UNIQUE KEY `UX_Condition_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Edition` (
    `EditionId`     INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `ResourceKey`   VARCHAR(255) NULL,
    PRIMARY KEY (`EditionId`),
    UNIQUE KEY `UX_Edition_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Format` (
    `FormatId`      INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `ResourceKey`   VARCHAR(255) NULL,
    PRIMARY KEY (`FormatId`),
    UNIQUE KEY `UX_Format_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Language` (
    `LanguageId`    INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `ResourceKey`   VARCHAR(255) NULL,
    PRIMARY KEY (`LanguageId`),
    UNIQUE KEY `UX_Language_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Location` (
    `LocationId`    INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    PRIMARY KEY (`LocationId`),
    UNIQUE KEY `UX_Location_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Owner` (
    `OwnerId`       INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    PRIMARY KEY (`OwnerId`),
    UNIQUE KEY `UX_Owner_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `PurchasePlace` (
    `PurchasePlaceId` INT          NOT NULL AUTO_INCREMENT,
    `Name`            VARCHAR(255) NOT NULL,
    PRIMARY KEY (`PurchasePlaceId`),
    UNIQUE KEY `UX_PurchasePlace_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- NumericValue allows ratings to be ordered numerically (e.g. 1.0-5.0) independent of display name.
CREATE TABLE `Rating` (
    `RatingId`      INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `NumericValue`  DOUBLE       NULL,
    `ResourceKey`   VARCHAR(255) NULL,
    PRIMARY KEY (`RatingId`),
    UNIQUE KEY `UX_Rating_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ReadingLevel` (
    `ReadingLevelId` INT          NOT NULL AUTO_INCREMENT,
    `Name`           VARCHAR(255) NOT NULL,
    `ResourceKey`    VARCHAR(255) NULL,
    PRIMARY KEY (`ReadingLevelId`),
    UNIQUE KEY `UX_ReadingLevel_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Source` (
    `SourceId`      INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    PRIMARY KEY (`SourceId`),
    UNIQUE KEY `UX_Source_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Status` (
    `StatusId`      INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `ResourceKey`   VARCHAR(255) NULL,
    PRIMARY KEY (`StatusId`),
    UNIQUE KEY `UX_Status_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Fixed enum-like lookups with explicit ids: plain integer keys (never inserted by the app).
CREATE TABLE `BorrowerStatus` (
    `BorrowerStatusId` INT          NOT NULL,
    `StatusName`       VARCHAR(255) NOT NULL,
    `ResourceKey`      VARCHAR(255) NULL,
    PRIMARY KEY (`BorrowerStatusId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BookImageType` (
    `BookImageTypeId` INT          NOT NULL,
    `TypeName`        VARCHAR(255) NOT NULL,
    `ResourceKey`     VARCHAR(255) NULL,
    PRIMARY KEY (`BookImageTypeId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- Cross-reference tables that depend only on lookup tables
-- ============================================================

CREATE TABLE `CategoryCollection` (
    `CategoryId`    INT NOT NULL,
    `CollectionId`  INT NOT NULL,
    PRIMARY KEY (`CategoryId`, `CollectionId`),
    KEY `IX_CategoryCollection_CollectionId` (`CollectionId`),
    CONSTRAINT `FK_CategoryCollection_Category`   FOREIGN KEY (`CategoryId`)   REFERENCES `Category`(`CategoryId`)     ON DELETE CASCADE,
    CONSTRAINT `FK_CategoryCollection_Collection` FOREIGN KEY (`CollectionId`) REFERENCES `Collection`(`CollectionId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- Core entity tables
-- ============================================================

CREATE TABLE `Book` (
    `BookId`            INT           NOT NULL AUTO_INCREMENT,
    `CollectionId`      INT           NULL,
    `Title`             TEXT          NOT NULL,
    `Subtitle`          TEXT          NULL,
    `PublisherId`       INT           NULL,
    `PubPlace`          VARCHAR(255)  NULL,
    `PubDate`           VARCHAR(255)  NULL,
    `CopyrightDate`     VARCHAR(255)  NULL,
    `FormatId`          INT           NULL,
    `EditionId`         INT           NULL,
    `Pages`             INT           NULL,
    `Copies`            INT           NOT NULL DEFAULT 1,
    `Isbn`              VARCHAR(255)  NULL,
    `LanguageId`        INT           NULL,
    `SeriesId`          INT           NULL,
    `SeriesNumber`      VARCHAR(255)  NULL,
    `ReadCount`         INT           NOT NULL DEFAULT 0,
    `RatingId`          INT           NULL,
    `ConditionId`       INT           NULL,
    `LocationId`        INT           NULL,
    `OwnerId`           INT           NULL,
    `StatusId`          INT           NULL,
    `Signed`            TINYINT(1)    NOT NULL DEFAULT 0,
    `OutOfPrint`        TINYINT(1)    NOT NULL DEFAULT 0,
    `Favorite`          TINYINT(1)    NOT NULL DEFAULT 0,
    `Keywords`          TEXT          NULL,
    `Comments`          TEXT          NULL,
    `BookInfo`          TEXT          NULL,
    `PurchasePrice`     DECIMAL(18,2) NULL,
    `PurchasePlaceId`   INT           NULL,
    `ListPrice`         DECIMAL(18,2) NULL,
    `SourceId`          INT           NULL,
    `ExternalId`        VARCHAR(255)  NULL,
    `MediaLink`         VARCHAR(2048) NULL,
    `Display`           TINYINT(1)    NOT NULL DEFAULT 1,
    `ReadingLevelId`    INT           NULL,
    `Added`             DATETIME(6)   NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    `Updated`           DATETIME(6)   NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    `PurchaseCurrency`  VARCHAR(255)  NULL,
    `ListPriceCurrency` VARCHAR(255)  NULL,
    `AltTitle`          VARCHAR(255)  NULL,
    `AmazonAsin`        VARCHAR(255)  NULL,
    `PurchaseDate`      DATETIME(6)   NULL,
    `DateLastRead`      DATETIME(6)   NULL,
    `Issn`              VARCHAR(255)  NULL,
    `Lccn`              VARCHAR(255)  NULL,
    `DeweyDecimal`      VARCHAR(255)  NULL,
    `CallNumber`        VARCHAR(255)  NULL,
    `Dimensions`        VARCHAR(255)  NULL,
    `Weight`            DECIMAL(18,3) NULL,
    `ItemValue`         DECIMAL(18,2) NULL,
    `ValuationDate`     DATETIME(6)   NULL,
    `AmazonNewValue`         DECIMAL(18,2) NULL,
    `AmazonUsedValue`        DECIMAL(18,2) NULL,
    `AmazonCollectibleValue` DECIMAL(18,2) NULL,
    `AmazonNewCount`         INT NULL,
    `AmazonUsedCount`        INT NULL,
    `AmazonCollectibleCount` INT NULL,
    `SalesRank`              INT NULL,
    `LexileLevel`           INT NULL,
    -- Reproduces the partial-unique Isbn of the other providers (unique among non-empty values, NULLs allowed):
    -- MySQL has no filtered index, so a virtual column maps '' to NULL and the unique key sits on it.
    `IsbnUnique`        VARCHAR(255)  GENERATED ALWAYS AS (NULLIF(`Isbn`, '')) VIRTUAL,
    PRIMARY KEY (`BookId`),
    KEY `IX_Book_CollectionId`      (`CollectionId`),
    KEY `IX_Book_PublisherId`       (`PublisherId`),
    KEY `IX_Book_SeriesId`          (`SeriesId`),
    KEY `IX_Book_Isbn`              (`Isbn`),
    KEY `IX_Book_StatusId`          (`StatusId`),
    KEY `IX_Book_FormatId`          (`FormatId`),
    KEY `IX_Book_LanguageId`        (`LanguageId`),
    KEY `IX_Book_LocationId`        (`LocationId`),
    KEY `IX_Book_OwnerId`           (`OwnerId`),
    KEY `IX_Book_RatingId`          (`RatingId`),
    KEY `IX_Book_Collection_Status` (`CollectionId`, `StatusId`),
    KEY `IX_Book_Collection_Format` (`CollectionId`, `FormatId`),
    UNIQUE KEY `UX_Book_Isbn` (`IsbnUnique`),
    FULLTEXT KEY `IX_Book_SearchVector` (`Title`, `Subtitle`, `Keywords`, `Comments`, `BookInfo`, `ExternalId`),
    CONSTRAINT `FK_Book_Collection`    FOREIGN KEY (`CollectionId`)    REFERENCES `Collection`(`CollectionId`),
    CONSTRAINT `FK_Book_Publisher`     FOREIGN KEY (`PublisherId`)     REFERENCES `Publisher`(`PublisherId`),
    CONSTRAINT `FK_Book_Format`        FOREIGN KEY (`FormatId`)        REFERENCES `Format`(`FormatId`),
    CONSTRAINT `FK_Book_Edition`       FOREIGN KEY (`EditionId`)       REFERENCES `Edition`(`EditionId`),
    CONSTRAINT `FK_Book_Language`      FOREIGN KEY (`LanguageId`)      REFERENCES `Language`(`LanguageId`),
    CONSTRAINT `FK_Book_Series`        FOREIGN KEY (`SeriesId`)        REFERENCES `Series`(`SeriesId`),
    CONSTRAINT `FK_Book_Rating`        FOREIGN KEY (`RatingId`)        REFERENCES `Rating`(`RatingId`),
    CONSTRAINT `FK_Book_Condition`     FOREIGN KEY (`ConditionId`)     REFERENCES `Condition`(`ConditionId`),
    CONSTRAINT `FK_Book_Location`      FOREIGN KEY (`LocationId`)      REFERENCES `Location`(`LocationId`),
    CONSTRAINT `FK_Book_Owner`         FOREIGN KEY (`OwnerId`)         REFERENCES `Owner`(`OwnerId`),
    CONSTRAINT `FK_Book_Status`        FOREIGN KEY (`StatusId`)        REFERENCES `Status`(`StatusId`),
    CONSTRAINT `FK_Book_PurchasePlace` FOREIGN KEY (`PurchasePlaceId`) REFERENCES `PurchasePlace`(`PurchasePlaceId`),
    CONSTRAINT `FK_Book_Source`        FOREIGN KEY (`SourceId`)        REFERENCES `Source`(`SourceId`),
    CONSTRAINT `FK_Book_ReadingLevel`  FOREIGN KEY (`ReadingLevelId`)  REFERENCES `ReadingLevel`(`ReadingLevelId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BookContributor` (
    `BookContributorId` INT NOT NULL AUTO_INCREMENT,
    `BookId`            INT NOT NULL,
    `PersonId`          INT NOT NULL,
    `ContributorRoleId` INT NOT NULL,
    `SortOrder`         INT NOT NULL DEFAULT 0,
    PRIMARY KEY (`BookContributorId`),
    KEY `IX_BookContributor_BookId`   (`BookId`),
    KEY `IX_BookContributor_PersonId` (`PersonId`),
    KEY `IX_BookContributor_ContributorRoleId` (`ContributorRoleId`),
    CONSTRAINT `FK_BookContributor_Book`            FOREIGN KEY (`BookId`)            REFERENCES `Book`(`BookId`) ON DELETE CASCADE,
    CONSTRAINT `FK_BookContributor_Person`          FOREIGN KEY (`PersonId`)          REFERENCES `Person`(`PersonId`),
    CONSTRAINT `FK_BookContributor_ContributorRole` FOREIGN KEY (`ContributorRoleId`) REFERENCES `ContributorRole`(`ContributorRoleId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BookCategory` (
    `BookId`      INT NOT NULL,
    `CategoryId`  INT NOT NULL,
    PRIMARY KEY (`BookId`, `CategoryId`),
    KEY `IX_BookCategory_CategoryId` (`CategoryId`),
    CONSTRAINT `FK_BookCategory_Book`     FOREIGN KEY (`BookId`)     REFERENCES `Book`(`BookId`) ON DELETE CASCADE,
    CONSTRAINT `FK_BookCategory_Category` FOREIGN KEY (`CategoryId`) REFERENCES `Category`(`CategoryId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Settings` (
    `Key`     VARCHAR(255) NOT NULL,
    `Value`   TEXT         NULL,
    PRIMARY KEY (`Key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `SavedSearch` (
    `SavedSearchId` INT          NOT NULL AUTO_INCREMENT,
    `Name`          VARCHAR(255) NOT NULL,
    `QueryJson`     TEXT         NOT NULL,
    `CreatedAt`     DATETIME(6)  NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    PRIMARY KEY (`SavedSearchId`),
    UNIQUE KEY `UX_SavedSearch_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BatchQueueItem` (
    `BatchQueueItemId` INT          NOT NULL AUTO_INCREMENT,
    `Isbn`             VARCHAR(255) NOT NULL,
    `BookId`           INT          NULL,
    `Status`           VARCHAR(50)  NOT NULL DEFAULT 'Pending',
    `ResultJson`       TEXT         NULL,
    `CreatedAt`        DATETIME(6)  NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    `UpdatedAt`        DATETIME(6)  NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    PRIMARY KEY (`BatchQueueItemId`),
    KEY `IX_BatchQueueItem_Status` (`Status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BookImage` (
    `BookImageId`     INT          NOT NULL AUTO_INCREMENT,
    `BookId`          INT          NOT NULL,
    `ImageData`       LONGBLOB     NOT NULL,
    `MimeType`        VARCHAR(255) NOT NULL DEFAULT 'image/jpeg',
    `IsPrimary`       TINYINT(1)   NOT NULL DEFAULT 0,
    `DisplayOrder`    INT          NOT NULL DEFAULT 0,
    `Added`           DATETIME(6)  NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    `BookImageTypeId` INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (`BookImageId`),
    KEY `IX_BookImage_BookId` (`BookId`),
    KEY `IX_BookImage_BookImageTypeId` (`BookImageTypeId`),
    CONSTRAINT `FK_BookImage_Book`          FOREIGN KEY (`BookId`)          REFERENCES `Book`(`BookId`) ON DELETE CASCADE,
    CONSTRAINT `FK_BookImage_BookImageType` FOREIGN KEY (`BookImageTypeId`) REFERENCES `BookImageType`(`BookImageTypeId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BookVolume` (
    `BookVolumeId`  INT NOT NULL AUTO_INCREMENT,
    `BookId`        INT NOT NULL,
    `VolumeNumber`  INT NOT NULL,
    PRIMARY KEY (`BookVolumeId`),
    KEY `IX_BookVolume_BookId` (`BookId`),
    CONSTRAINT `FK_BookVolume_Book` FOREIGN KEY (`BookId`) REFERENCES `Book`(`BookId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `BookChapter` (
    `BookChapterId`  INT NOT NULL AUTO_INCREMENT,
    `BookVolumeId`   INT NOT NULL,
    `ChapterNumber`  INT NOT NULL,
    PRIMARY KEY (`BookChapterId`),
    KEY `IX_BookChapter_BookVolumeId` (`BookVolumeId`),
    CONSTRAINT `FK_BookChapter_BookVolume` FOREIGN KEY (`BookVolumeId`) REFERENCES `BookVolume`(`BookVolumeId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Borrower` (
    `BorrowerId`          INT          NOT NULL AUTO_INCREMENT,
    `StatusId`            INT          NOT NULL DEFAULT 0,
    `FirstName`           VARCHAR(255) NULL,
    `LastName`            VARCHAR(255) NULL,
    `BorrowerExternalId`  VARCHAR(255) NULL,
    `Organization`        VARCHAR(255) NULL,
    `Address1`            VARCHAR(255) NULL,
    `Address2`            VARCHAR(255) NULL,
    `City`                VARCHAR(255) NULL,
    `State`               VARCHAR(255) NULL,
    `PostalCode`          VARCHAR(255) NULL,
    `Country`             VARCHAR(255) NULL,
    `Phone1`              VARCHAR(255) NULL,
    `Phone2`              VARCHAR(255) NULL,
    `Email`               VARCHAR(255) NULL,
    `Fax`                 VARCHAR(255) NULL,
    PRIMARY KEY (`BorrowerId`),
    KEY `IX_Borrower_StatusId` (`StatusId`),
    CONSTRAINT `FK_Borrower_BorrowerStatus` FOREIGN KEY (`StatusId`) REFERENCES `BorrowerStatus`(`BorrowerStatusId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Loan records are historical: deleting a Book or Borrower with associated loans is blocked (RESTRICT).
CREATE TABLE `Loan` (
    `LoanId`          INT          NOT NULL AUTO_INCREMENT,
    `BookId`          INT          NOT NULL,
    `BorrowerId`      INT          NOT NULL,
    `LoanedDate`      DATETIME(6)  NULL,
    `DueDate`         DATETIME(6)  NULL,
    `ReturnedDate`    DATETIME(6)  NULL,
    `LoanExternalId`  VARCHAR(255) NULL,
    PRIMARY KEY (`LoanId`),
    KEY `IX_Loan_BookId`     (`BookId`),
    KEY `IX_Loan_BorrowerId` (`BorrowerId`),
    CONSTRAINT `FK_Loan_Book`     FOREIGN KEY (`BookId`)     REFERENCES `Book`(`BookId`)         ON DELETE RESTRICT,
    CONSTRAINT `FK_Loan_Borrower` FOREIGN KEY (`BorrowerId`) REFERENCES `Borrower`(`BorrowerId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- Seed data
-- ============================================================

INSERT IGNORE INTO `Collection` (`Name`, `SortOrder`) VALUES ('Comics', 1);
INSERT IGNORE INTO `Collection` (`Name`, `SortOrder`) VALUES ('Fiction', 2);
INSERT IGNORE INTO `Collection` (`Name`, `SortOrder`) VALUES ('Non-Fiction', 3);

INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Author', 'Author', 'ContributorRole_Author');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Editor', 'Editor', 'ContributorRole_Editor');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Translator', 'Translator', 'ContributorRole_Translator');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Foreword', 'Foreword', 'ContributorRole_Foreword');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Introduction', 'Introduction', 'ContributorRole_Introduction');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Illustrator', 'Illustrator', 'ContributorRole_Illustrator');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Writer', 'Writer', 'ContributorRole_Writer');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Penciller', 'Penciller', 'ContributorRole_Penciller');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Inker', 'Inker', 'ContributorRole_Inker');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Colorist', 'Colorist', 'ContributorRole_Colorist');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Letterer', 'Letterer', 'ContributorRole_Letterer');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('CoverArtist', 'Cover Artist', 'ContributorRole_CoverArtist');
INSERT IGNORE INTO `ContributorRole` (`Code`, `DisplayName`, `ResourceKey`) VALUES ('Designer', 'Designer', 'ContributorRole_Designer');

INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Adventure', 1);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Biography', 2);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Children', 3);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Classic', 4);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Crime', 5);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Fantasy', 6);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('History', 7);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Horror', 8);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Humor', 9);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Mystery', 10);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Poetry', 11);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Reference', 12);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Romance', 13);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Science', 14);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Science Fiction', 15);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Self-Help', 16);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Superhero', 17);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Thriller', 18);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Travel', 19);
INSERT IGNORE INTO `Category` (`Name`, `SortOrder`) VALUES ('Young Adult', 20);

INSERT IGNORE INTO `CategoryCollection` (`CategoryId`, `CollectionId`)
SELECT c.`CategoryId`, col.`CollectionId`
FROM `Category` c
CROSS JOIN `Collection` col;

INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Hardcover', 'Format_Hardcover');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Paperback', 'Format_Paperback');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Trade Paperback', 'Format_TradePaperback');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Mass Market Paperback', 'Format_MassMarketPaperback');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('E-book', 'Format_Ebook');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Audiobook', 'Format_Audiobook');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Graphic Novel', 'Format_GraphicNovel');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Comic', 'Format_Comic');
INSERT IGNORE INTO `Format` (`Name`, `ResourceKey`) VALUES ('Magazine', 'Format_Magazine');

INSERT IGNORE INTO `Condition` (`Name`, `ResourceKey`) VALUES ('New', 'Condition_New');
INSERT IGNORE INTO `Condition` (`Name`, `ResourceKey`) VALUES ('Like New', 'Condition_LikeNew');
INSERT IGNORE INTO `Condition` (`Name`, `ResourceKey`) VALUES ('Very Good', 'Condition_VeryGood');
INSERT IGNORE INTO `Condition` (`Name`, `ResourceKey`) VALUES ('Good', 'Condition_Good');
INSERT IGNORE INTO `Condition` (`Name`, `ResourceKey`) VALUES ('Fair', 'Condition_Fair');
INSERT IGNORE INTO `Condition` (`Name`, `ResourceKey`) VALUES ('Poor', 'Condition_Poor');

INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('1st Edition', 'Edition_1stEdition');
INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('2nd Edition', 'Edition_2ndEdition');
INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('3rd Edition', 'Edition_3rdEdition');
INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('Revised Edition', 'Edition_RevisedEdition');
INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('Collector''s Edition', 'Edition_CollectorsEdition');
INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('Limited Edition', 'Edition_LimitedEdition');
INSERT IGNORE INTO `Edition` (`Name`, `ResourceKey`) VALUES ('Signed Edition', 'Edition_SignedEdition');

INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('English', 'Language_English');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Swedish', 'Language_Swedish');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('French', 'Language_French');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('German', 'Language_German');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Spanish', 'Language_Spanish');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Italian', 'Language_Italian');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Japanese', 'Language_Japanese');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Norwegian', 'Language_Norwegian');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Danish', 'Language_Danish');
INSERT IGNORE INTO `Language` (`Name`, `ResourceKey`) VALUES ('Finnish', 'Language_Finnish');

INSERT IGNORE INTO `Rating` (`Name`, `NumericValue`, `ResourceKey`) VALUES ('1 Star', 1.0, 'Rating_1Star');
INSERT IGNORE INTO `Rating` (`Name`, `NumericValue`, `ResourceKey`) VALUES ('2 Stars', 2.0, 'Rating_2Stars');
INSERT IGNORE INTO `Rating` (`Name`, `NumericValue`, `ResourceKey`) VALUES ('3 Stars', 3.0, 'Rating_3Stars');
INSERT IGNORE INTO `Rating` (`Name`, `NumericValue`, `ResourceKey`) VALUES ('4 Stars', 4.0, 'Rating_4Stars');
INSERT IGNORE INTO `Rating` (`Name`, `NumericValue`, `ResourceKey`) VALUES ('5 Stars', 5.0, 'Rating_5Stars');

INSERT IGNORE INTO `Status` (`Name`, `ResourceKey`) VALUES ('Owned', 'Status_Owned');
INSERT IGNORE INTO `Status` (`Name`, `ResourceKey`) VALUES ('Wishlist', 'Status_Wishlist');
INSERT IGNORE INTO `Status` (`Name`, `ResourceKey`) VALUES ('On Order', 'Status_OnOrder');
INSERT IGNORE INTO `Status` (`Name`, `ResourceKey`) VALUES ('Borrowed', 'Status_Borrowed');
INSERT IGNORE INTO `Status` (`Name`, `ResourceKey`) VALUES ('Sold', 'Status_Sold');
INSERT IGNORE INTO `Status` (`Name`, `ResourceKey`) VALUES ('Given Away', 'Status_GivenAway');

INSERT IGNORE INTO `ReadingLevel` (`Name`, `ResourceKey`) VALUES ('Easy Reader', 'ReadingLevel_EasyReader');
INSERT IGNORE INTO `ReadingLevel` (`Name`, `ResourceKey`) VALUES ('Middle Grade', 'ReadingLevel_MiddleGrade');
INSERT IGNORE INTO `ReadingLevel` (`Name`, `ResourceKey`) VALUES ('Young Adult', 'ReadingLevel_YoungAdult');
INSERT IGNORE INTO `ReadingLevel` (`Name`, `ResourceKey`) VALUES ('Adult', 'ReadingLevel_Adult');

INSERT IGNORE INTO `Location` (`Name`) VALUES ('Default');
INSERT IGNORE INTO `Owner` (`Name`) VALUES ('Me');
INSERT IGNORE INTO `Publisher` (`Name`) VALUES ('Unknown');
INSERT IGNORE INTO `PurchasePlace` (`Name`) VALUES ('Unknown');
INSERT IGNORE INTO `Series` (`Name`) VALUES ('Standalone');
INSERT IGNORE INTO `Source` (`Name`) VALUES ('Manual Entry');

INSERT IGNORE INTO `Settings` (`Key`, `Value`) VALUES ('PrimaryDisplayRole', 'Author');

INSERT IGNORE INTO `BookImageType` (`BookImageTypeId`, `TypeName`, `ResourceKey`) VALUES (0, 'Cover', 'BookImageType_Cover');
INSERT IGNORE INTO `BookImageType` (`BookImageTypeId`, `TypeName`, `ResourceKey`) VALUES (1, 'Thumbnail', 'BookImageType_Thumbnail');
INSERT IGNORE INTO `BookImageType` (`BookImageTypeId`, `TypeName`, `ResourceKey`) VALUES (2, 'BackCover', 'BookImageType_BackCover');
INSERT IGNORE INTO `BookImageType` (`BookImageTypeId`, `TypeName`, `ResourceKey`) VALUES (3, 'Spine', 'BookImageType_Spine');
INSERT IGNORE INTO `BookImageType` (`BookImageTypeId`, `TypeName`, `ResourceKey`) VALUES (4, 'DustJacket', 'BookImageType_DustJacket');

INSERT IGNORE INTO `BorrowerStatus` (`BorrowerStatusId`, `StatusName`, `ResourceKey`) VALUES (0, 'Active', 'BorrowerStatus_Active');
INSERT IGNORE INTO `BorrowerStatus` (`BorrowerStatusId`, `StatusName`, `ResourceKey`) VALUES (1, 'Inactive', 'BorrowerStatus_Inactive');
