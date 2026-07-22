-- V003_AddBatchReviewAndCleanupIgnore.sql
-- Appended after V002 (earlier scripts stay untouched). BatchQueueItem gains a per-item
-- force-review flag (a guided single-book add must always land in review, even when all sources
-- agree) and a failure code (a localizable reason instead of a bare failed status). EF supplies
-- FailureCode values, so it carries no DEFAULT; ForceReview defaults to 0 for pre-existing rows.
--
-- PersonCleanupIgnore persists dismissed person-name cleanup proposals. The unique key is the
-- proposal's content, not just the person: a scan skips a proposal only while it would re-derive
-- exactly the same suggestion, so changed person data surfaces a fresh proposal despite an old
-- ignore. Rows die with their person (split-apply deletes the source person).

ALTER TABLE BatchQueueItem ADD COLUMN ForceReview INTEGER NOT NULL DEFAULT 0;
ALTER TABLE BatchQueueItem ADD COLUMN FailureCode TEXT NULL;

CREATE TABLE PersonCleanupIgnore (
    PersonCleanupIgnoreId INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonId              INTEGER  NOT NULL REFERENCES Person(PersonId) ON DELETE CASCADE,
    Kind                  TEXT     NOT NULL,
    ProposedContent       TEXT     NOT NULL,
    CreatedAt             DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE UNIQUE INDEX UX_PersonCleanupIgnore_Fingerprint
    ON PersonCleanupIgnore (PersonId, Kind, ProposedContent);
