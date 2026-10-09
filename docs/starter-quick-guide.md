# Starter quick guide

Run the website, API and PostgreSQL database locally with Docker. This is the
development setup; you do not need to install .NET or PostgreSQL on your computer
for this route. Docker builds the applications and runs the database for you.

## 1. Prepare your computer

- Install Docker with Compose v2 and start its engine. On Windows, select Linux containers.
- Install Python 3. On Windows, use `python` wherever this guide says `python3`.
- Open a terminal in the repository folder containing `compose.yaml`.

Check the tools:

```sh
docker --context default info
docker --context default compose version
python3 --version
```

The first build downloads dependencies and compiles the web application ahead of
time, so it can take several minutes.

## 2. Create local settings

If `.env` does not exist, run:

```sh
python3 scripts/dev-setup.py
```

This creates a random PostgreSQL password and development token signing key.
If `.env` already exists, keep it; the script leaves existing settings unchanged.
Do not commit this file or replace its password while keeping the existing database.

## 3. Build, create the tables, and start

Run each command in order and continue only when it succeeds:

```sh
docker --context default compose build
docker --context default compose up -d --wait database
python3 scripts/migrate.py
docker --context default compose up -d --wait
python3 scripts/smoke-stack.py
```

The database starts first. The migration command creates or upgrades its tables
through Entity Framework Core 10. The API does not create tables automatically.
The smoke check verifies the API, database readiness and web connection.

For an existing installation, back up the database and stop `service` and `web`
before applying migrations. Keep the database running and run only one migration
process at a time.

### Linux hosts without Docker bridge networking

If Docker reports that it cannot create a bridge interface, use this sequence
**instead of** the commands above. This is the workaround used on the current
development Linux machine:

```sh
docker --context default compose -f compose.yaml -f deploy/compose.host-network.yaml build
docker --context default compose -f compose.yaml -f deploy/compose.host-network.yaml up -d --wait database
python3 scripts/migrate.py --host-network
docker --context default compose -f compose.yaml -f deploy/compose.host-network.yaml up -d --wait
python3 scripts/smoke-stack.py
```

When using this mode, include `-f compose.yaml -f deploy/compose.host-network.yaml`
in later Compose commands too. The website and API addresses remain the same.

## 4. Open the website and connect

1. Open **http://localhost:5082**.
2. Generate a development access token:

   ```sh
   python3 scripts/dev-token.py
   ```

3. Copy the printed token into **Service connection → Access token**.
4. Select **Connect / refresh**.
5. Add a word and choose its type. Add more words, edit or delete them, then add
   words to your sentence draft and select **Save sentence**.

Tokens expire after 15 minutes. Generate another when needed. The access token
is different from the PostgreSQL password. Refreshing the browser clears the
in-memory token; it does not delete saved words or sentences.

| Component | Address |
| --- | --- |
| Website | `http://localhost:5082` |
| HTTP/JSON API | `http://localhost:5080` |
| gRPC endpoint | `localhost:5081` |
| Database readiness | `http://localhost:5080/health/ready` |

## 5. PostgreSQL is included

The default stack runs **PostgreSQL 17** using `Dockerfile.database` and the
`database` service in `compose.yaml`. The API uses the EF Core PostgreSQL provider.
No additional database project or manual table creation is needed.

| Setting | Value |
| --- | --- |
| Database name | `runninghill` |
| Database user | `runninghill` |
| Password | `POSTGRES_PASSWORD` in your local `.env` |
| API connection inside ordinary Compose networking | `database:5432` |
| Storage | Named volume `runninghill_database`, mounted at `/var/lib/postgresql/data` |

Ordinary Compose networking keeps PostgreSQL internal to Docker. In the Linux
host-network workaround it listens at `127.0.0.1:55432`. Client applications
connect to the API, not directly to PostgreSQL. The separate IDE Debug setup uses
its own database volume, credentials and port `55433`.

To check the database or inspect its tables while it is running:

```sh
docker --context default compose exec database pg_isready -U runninghill -d runninghill
docker --context default compose exec database psql -U runninghill -d runninghill -c '\dt'
```

In host-network mode, also add `-h 127.0.0.1 -p 55432` to both database commands.
See [database configuration](databases.md) for other providers and migration details.

## 6. View logs and stop safely

Choose **Logs** in the website or native app. Select local app events or **Service**.
Service logs need `logs.read` permission, which newly generated development tokens
include. Search, filter severity and refresh to inspect recent events. See
[logging](logging.md) for retained files and console output.

```sh
docker --context default compose ps
docker --context default compose logs --tail 100 service database web
docker --context default compose stop
```

`stop` keeps containers and saved data. To resume an unchanged installation, run
`docker --context default compose up -d --wait`. After code updates, repeat the
build and migration steps. Do not use `down --volumes` unless you intend to delete
the saved database.

## If something does not work

| Problem | What to do |
| --- | --- |
| Cannot connect to Docker | Start the engine and check that the `default` context points to your local engine. |
| Database or API remains unhealthy | Check container logs. Confirm `.env` matches the existing database, then check the migration step. |
| A port is already in use | Stop the other application using that port before starting this stack. |
| Token rejected or expired | Generate a fresh token with `scripts/dev-token.py` for this Docker stack and reconnect. |
| Service logs report missing permission | Generate a new development token; for an external identity provider, ask its administrator for `logs.read`. |
| Docker cannot create a bridge on Linux | Use the host-network sequence in step 3. |

For debugger setup and separate project builds, see [build and debug](build-and-debug.md).
For native clients, see [Android](android.md) and [Windows desktop](windows-desktop.md).
Debug services use ports `5180`–`5182` and tokens from `scripts/dev.py token`;
do not mix those tokens with the Docker setup above.
