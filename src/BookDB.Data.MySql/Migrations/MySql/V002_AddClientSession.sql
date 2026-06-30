-- V002_AddClientSession.sql (MySQL / MariaDB)
-- Appended after V001 (which stays untouched): the multi-client heartbeat/session table. It exists in every
-- provider's schema but is only written when the active backend is remote. EF always supplies the timestamp
-- values, so the columns carry no DEFAULT.

CREATE TABLE `ClientSession` (
    `SessionId`   VARCHAR(255) NOT NULL,
    `Hostname`    VARCHAR(255) NOT NULL,
    `UserName`    VARCHAR(255) NOT NULL,
    `AppVersion`  VARCHAR(255) NOT NULL,
    `StartedAt`   DATETIME(6)  NOT NULL,
    `LastSeenAt`  DATETIME(6)  NOT NULL,
    PRIMARY KEY (`SessionId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
