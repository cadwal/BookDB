# Remote Databases

BookDB stores your library in a local SQLite file by default — no setup required. If you want to reach the same library from several computers, you can instead keep it on a database server: **PostgreSQL** or **MySQL / MariaDB**. Every BookDB feature works the same regardless of where the library is stored.

## Choosing a database backend

Open **Tools › Settings › Database**. Under **Database backend** you choose between:

- **Local file (SQLite)** — the default single-computer library
- **PostgreSQL server**
- **MySQL / MariaDB server**

The server options require an OS keyring (a secure credential store). BookDB keeps the server password **only** in the keyring — it is never written to a configuration file, and there is no plaintext fallback. If no keyring is available on your system, the server options are disabled.

For a server connection you fill in:

- **Host** and **port** — the port defaults to 5432 for PostgreSQL and 3306 for MySQL/MariaDB
- **Database** name
- **Username** and **password**
- **TLS / SSL mode** — the available options match the chosen engine

If a password is already saved for the connection, a hint says so and the password field can stay blank.

**Test connection** checks the settings before you save. On success it shows the server version and how many books the database contains. On failure it tells you what went wrong: wrong credentials, connection refused, timeout, TLS problem, or an unsupported server version.

Saving a backend change prompts you to restart — **the new backend only takes effect after BookDB restarts**. If the server can't be reached the next time BookDB starts, a dialog offers **Retry**, **Open settings**, or **Quit**.

## Server version requirements

- **PostgreSQL 12 or later** — needed for full-text search
- **MySQL 8.0 or later** / **MariaDB 10.6 or later**

The version is checked when you test the connection and again at startup; a server that is too old is rejected with a message stating the required version.

## Moving your library between backends

**Tools › Maintenance › Move library** copies the entire library between any two backends — for example from the local SQLite file to a new PostgreSQL server, or back again.

The move is designed to be safe:

- A **CSV safety backup of the source** is always taken before anything is copied.
- If the target database already contains data, BookDB backs up the target as well, and the move only starts after you explicitly tick **I understand — replace all data in the target database**.
- An empty target gets its schema created automatically.
- After copying, the row counts of source and target are compared; the move only counts as complete when they match.
- Optionally, BookDB switches the active database to the target and restarts.

## Using the library from multiple computers

A server library keeps track of connected BookDB clients, refreshed by a heartbeat every 60 seconds:

- If another client appears to be connected when you start, BookDB warns you. You can **Quit**, or choose **Connect anyway** — that button becomes available after a 3-second delay.
- A client that crashed without disconnecting stops counting as connected after about 3 minutes.

Separately from the server session, only one BookDB instance can run at a time per user on the same computer — starting a second one focuses the window that is already open.

If the server connection drops while you are working, BookDB tells you and offers **Keep waiting** (recommended) or **Quit**.

## Backups of a server library

File-based backup applies only to the local SQLite file. When the library is on a server, **Backup...** and automatic backups always produce the **CSV archive** — the backup dialog states this instead of failing. A SQLite file backup cannot be restored into a server library; use a CSV archive backup, or switch back to the local database first.
