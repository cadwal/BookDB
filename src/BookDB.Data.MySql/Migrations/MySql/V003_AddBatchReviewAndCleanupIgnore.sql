-- V003_AddBatchReviewAndCleanupIgnore.sql (MySQL / MariaDB)
-- Appended after V002 (earlier scripts stay untouched). BatchQueueItem gains a per-item
-- force-review flag (a guided single-book add must always land in review, even when all sources
-- agree) and a failure code (a localizable reason instead of a bare failed status). EF supplies
-- FailureCode values, so it carries no DEFAULT; ForceReview defaults to 0 for pre-existing rows.
--
-- PersonCleanupIgnore persists dismissed person-name cleanup proposals. The unique key is the
-- proposal's content, not just the person: a scan skips a proposal only while it would re-derive
-- exactly the same suggestion, so changed person data surfaces a fresh proposal despite an old
-- ignore. Rows die with their person (split-apply deletes the source person).
-- ProposedContent is VARCHAR(700), not TEXT, because it sits in the unique key and InnoDB caps
-- composite index keys at 3072 bytes (4 bytes/char under utf8mb4).

ALTER TABLE `BatchQueueItem`
    ADD COLUMN `ForceReview` TINYINT(1)  NOT NULL DEFAULT 0,
    ADD COLUMN `FailureCode` VARCHAR(50) NULL;

CREATE TABLE `PersonCleanupIgnore` (
    `PersonCleanupIgnoreId` INT          NOT NULL AUTO_INCREMENT,
    `PersonId`              INT          NOT NULL,
    `Kind`                  VARCHAR(20)  NOT NULL,
    `ProposedContent`       VARCHAR(700) NOT NULL,
    `CreatedAt`             DATETIME(6)  NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    PRIMARY KEY (`PersonCleanupIgnoreId`),
    UNIQUE KEY `UX_PersonCleanupIgnore_Fingerprint` (`PersonId`, `Kind`, `ProposedContent`),
    CONSTRAINT `FK_PersonCleanupIgnore_Person` FOREIGN KEY (`PersonId`)
        REFERENCES `Person`(`PersonId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
