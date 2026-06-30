# BookDB.Data.MySql.Tests — running the MySQL/MariaDB tests

These tests run the MySQL provider against disposable `mysql:8.0` **and** `mariadb:11` containers started by
[Testcontainers](https://testcontainers.com/) — the provider must work on both engines. They need a reachable
Docker daemon.

- **No Docker?** The tests **skip** automatically and the rest of the suite stays green.
- **This project uses Docker Engine inside WSL, not Docker Desktop.**

Make a local run turnkey:

```powershell
./scripts/start-test-databases.ps1
$env:DOCKER_HOST = 'tcp://localhost:2375'   # the script also sets this for its own session
dotnet test BookDB.slnx
```

See [docs/testing-with-containers.md](../../docs/testing-with-containers.md) for the full recipe, the
capability gate, and CI behaviour.
