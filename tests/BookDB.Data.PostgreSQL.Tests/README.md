# BookDB.Data.PostgreSQL.Tests — running the PostgreSQL tests

These tests run the PostgreSQL provider against a disposable `postgres:16-alpine`
container started by [Testcontainers](https://testcontainers.com/). They need a
reachable Docker daemon.

- **No Docker?** The tests **skip** automatically and the rest of the suite stays
  green — you only need Docker to actually exercise the PostgreSQL provider.
- **This project uses Docker Engine inside WSL, not Docker Desktop.**

## Starting Docker (Docker Engine in WSL)

The WSL distro `Ubuntu-26.04` has `docker.io` installed with the daemon exposed on
TCP 2375 — systemd override `ExecStart=/usr/bin/dockerd -H fd:// -H tcp://0.0.0.0:2375`,
bound to `0.0.0.0` (not `127.0.0.1`) so WSL's localhost forwarding relays it. Nothing
is exposed on the LAN; only loopback connects are intercepted.

WSL shuts the distro down when its last process exits, which kills `dockerd`. So:

1. **Keep the distro alive** — run this once (PowerShell; it stays hidden in the background):

   ```powershell
   Start-Process wsl -ArgumentList '-d','Ubuntu-26.04','--exec','sleep','infinity' -WindowStyle Hidden
   ```

   `dockerd` starts with the distro (systemd) and becomes `active` a few seconds later.
   Check: `wsl -d Ubuntu-26.04 --exec docker version`.

2. **Point Testcontainers at it** — set `DOCKER_HOST` for the test run:

   ```powershell
   $env:DOCKER_HOST = 'tcp://localhost:2375'
   dotnet test BookDB.slnx
   ```

   Or persist it in `~/.testcontainers.properties`:

   ```
   docker.host=tcp://localhost:2375
   ```

Verify reachability from Windows: `curl http://localhost:2375/_ping` → `OK`.

The first run pulls `postgres:16-alpine` (and the Ryuk reaper image); later runs are fast.
