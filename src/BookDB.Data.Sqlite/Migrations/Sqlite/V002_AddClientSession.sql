-- V002_AddClientSession.sql
-- Appended after V001 (which stays untouched): the multi-client heartbeat/session table. It exists in both
-- providers' schemas but is only written when the active backend is remote. EF always supplies the timestamp
-- values, so the columns carry no DEFAULT.

CREATE TABLE ClientSession (
    SessionId   TEXT     NOT NULL PRIMARY KEY,
    Hostname    TEXT     NOT NULL,
    UserName    TEXT     NOT NULL,
    AppVersion  TEXT     NOT NULL,
    StartedAt   DATETIME NOT NULL,
    LastSeenAt  DATETIME NOT NULL
);
