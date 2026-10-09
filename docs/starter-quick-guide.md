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

If a newer Docker builder asks for `--allow=network.host`, replace the build
command with these two commands (the file contains build settings, not runtime credentials):

```sh
docker --context default compose -f compose.yaml -f deploy/compose.host-network.yaml build --print > .run/docker-build.json
docker --context default buildx bake --allow=network.host -f .run/docker-build.json
```

Create `.run` first if it does not exist; the certificate setup also creates it.

When using this mode, include `-f compose.yaml -f deploy/compose.host-network.yaml`
in later Compose commands too. The website and API addresses remain the same.

## 4. Open the website and connect

For **self-signed HTTPS on https://localhost:5443**, follow [HTTPS and nginx reverse proxy](https.md) after the build/migration steps above. It includes certificate generation, the Compose overrides for this Linux host, and browser trust instructions. Without those overrides, use HTTP below.

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

### Choose your appearance

The centered controls at the top appear in this order: **System theme → App theme → Logs**.
**System theme** offers **Light**, **Dark**, and **System**. System follows your device's
appearance and updates when the device changes between light and dark. Explicit Light
or Dark choices stay fixed. Your preferences are remembered per browser or native app;
changing them keeps your connection, filters, and sentence draft.

**App theme** lists company palettes matching the current brightness. The initial pair
is `Runninghill_light` and `Runninghill_dark`. Changing brightness automatically uses the
same company's other variant. The Logs page uses the same palette.

The collection page displays the company image below the header and above the
introductory text. Place images in `themes/<companyname>_light_icon.webp` and
`themes/<companyname>_dark_icon.webp`. Both Runninghill files initially contain the
supplied white artwork unchanged; `IconBackground` gives it a charcoal backing.
Images switch with the theme and fit inside a centered 256 × 128 box, shrinking on
narrow screens without stretching or cropping. The web packages the original WebP;
native builds generate compatible PNG resources automatically. Rebuild after replacing
an image. A company without an image uses the matching Runninghill image.

Company palettes live in the root **`themes/`** directory. Copy both Runninghill JSON
files to `<companyname>_light.json` and `<companyname>_dark.json`, keep all colour-role
names, and replace the `#RRGGBB` values. Company names may contain ASCII letters,
digits, underscores, and hyphens. Filenames remain stable company identifiers;
selector labels and brightness choices are translated into all five languages.

Run `python3 scripts/check-themes.py`, then rebuild the web and native clients. The
build embeds every `themes/*.json` file, so new company pairs appear without changing
C# or Razor code. If you modify the built-in Runninghill palettes, first run
`python3 scripts/check-themes.py --generate` to refresh the web's startup colours.
Incomplete or invalid optional pairs are omitted with a translated message. Invalid
theme packages fail the CI check. Theme JSON accepts colours only, with no executable CSS
or remote URLs.

### Choose your language

Use the **Language** selector on the website or native app. English (ZA),
Afrikaans (ZA), isiXhosa (ZA), isiZulu (ZA), and Setswana (ZA) are available.
Your selection is remembered on that browser or device. Changing it keeps your
connection and sentence draft. Labels, word-type names, instructions, feedback,
accessibility descriptions, and the Logs interface use the selected language.

Saved words and sentences keep the spelling you entered. API word types and CLI
commands retain their English identifiers so integrations keep working. Raw
diagnostic records also retain their original text, category, and reference for
support; the surrounding log controls and severity labels are translated.

For the CLI, pass `--language` anywhere in the command:

If you already published an older CLI, update it first with
`python3 scripts/build.py publish --target cli`. The setup scripts reuse an
existing binary.

```sh
./scripts/setup-cli.sh --language af-ZA words list
```

```powershell
.\scripts\setup-cli.ps1 --language zu-ZA words list
```

The available codes are `en-ZA`, `af-ZA`, `xh-ZA`, `zu-ZA`, and `tn-ZA`.
Alternatively, set `RUNNINGHILL_LANGUAGE` before running the CLI. The command-line
option takes precedence. Native CLI builds now require the normal ICU runtime on
Linux; invariant globalization cannot format these cultures.

Each client sends `Accept-Language` to the service. HTTP validation and status
messages, and gRPC status/error descriptions, use that request's language;
unsupported languages fall back to `en-ZA`. Changing languages does not require
a database migration or another token. Rebuild existing container images to use
the new interface and translated service responses.

Translations are compiled from `src/Runninghill.Contracts/Resources/Text*.resx`.
All five dictionaries ship together, so switching languages needs no translation
download. When changing a message, update every dictionary, retain its numbered
placeholders, and run:

```sh
python3 scripts/check-localization.py --generate
python3 scripts/check-localization.py
```

The check also runs in CI. Translation completeness and formatting are tested;
native-speaker review is still recommended for terminology and natural phrasing.

| Component | Address |
| --- | --- |
| Website | `http://localhost:5082` |
| SSL/TLS | 'https://localhost:5443' |
| HTTP/JSON API | `http://localhost:5080` |
| gRPC endpoint | `localhost:5081` |
| Database readiness | `http://localhost:5080/health/ready` |

## Use the CLI with HTTPS

With the HTTPS containers running, these scripts use `https://localhost:5443/`,
publish the native CLI if it is missing, generate a fresh development token, and
run your command. No arguments means `status`.

Linux/macOS (Bash):

```sh
./scripts/setup-cli.sh
./scripts/setup-cli.sh words list
./scripts/setup-cli.sh words add hello Noun
```

Windows (PowerShell):

```powershell
.\scripts\setup-cli.ps1
.\scripts\setup-cli.ps1 words list
.\scripts\setup-cli.ps1 words add hello Noun
```

Run the scripts directly, **not by sourcing/dot-sourcing them**. You can run them
from any folder using their full path. Use the script for each command: its token
is not printed or saved and the caller's environment is left unchanged. Tokens
are generated again on every invocation, so you do not need to paste or refresh
one manually. The commands still enforce the API's token permissions.

The scripts require Python 3, this checkout's `.env`, and
`.run/tls/localhost.crt`. Follow [HTTPS setup](https.md) first if these are missing.
If no published CLI exists, .NET 10 and the platform's Native AOT build tools are
needed. After changing CLI code, publish it again with
`python3 scripts/build.py publish --target cli`; the setup scripts reuse an existing binary.

On Linux, certificate trust applies only to the CLI process. On Windows, the
PowerShell script imports the public localhost certificate into **Current User →
Trusted Root Certification Authorities** if it is not already there. On macOS,
the scripts trust it in your login keychain; macOS may ask for permission. These
Windows/macOS trust entries persist until removed in certificate/keychain settings.
Certificate validation stays enabled. Only trust a certificate generated for your
own local service. The scripts do not start containers or reset the database.

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

## 6. View logs and stop safely

Choose **Logs** in the website or native app. Select local app events or **Service**.
Service logs need `logs.read` permission, which newly generated development tokens
include. Search, filter severity and refresh to inspect recent events. Use the
container log command below for server output.

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
| The normal browser window fails but a private window works | Use **Ctrl+Shift+R** once after upgrading. This reloads cached files without deleting your preferences or database. Startup scripts have content-based versioned names, and nginx revalidates the entry page to prevent mixing releases. |

### Report an error

The browser's error panel explains recovery and offers **Copy diagnostic details**.
If clipboard access is blocked, expand **Diagnostic details** and copy the text.
Send the code, reference, time, browser version, and steps that caused the error.
Never send an access token. Reloading can discard an unsaved draft; check the
collection before repeating a save that may have reached the service.

HTTP codes and .NET error names below are standard identifiers you can search online.
`RH-` codes are specific to Runninghill; this table is their reference, not a claim
that a public internet error catalogue contains them.

| Code | Meaning and next step |
| --- | --- |
| HTTP 401 (Unauthorized) | The token is missing, expired, or invalid. Generate a new token for the running service. |
| HTTP 403 (Forbidden) | The token lacks permission. Ask the administrator for the required scope. |
| HTTP 404 (NotFound) | Check the service URL and whether the app and service versions match. |
| HTTP 409 (Conflict) | Follow the supplied validation message, then refresh before retrying. |
| HTTP 429 (TooManyRequests) | Wait before trying again. |
| HTTP 500 / 502 / 503 / 504 | The service or proxy could not complete the request. Report the HTTP code and request reference; administrators should check service health and logs. |
| HttpRequestError.SecureConnectionError | TLS verification failed. Check the device clock and certificate trust. |
| HttpRequestError.NameResolutionError | The hostname could not be resolved. Check the address and network connection. |
| RH-NETWORK / RH-NETWORK-TIMEOUT | No usable reply arrived. Check connectivity; no HTTP status is invented when none was received. |
| RH-REPLY-INVALID | The service reply is incompatible or incomplete. Report the app and service versions. |
| RH-APP-UNEXPECTED | A client action failed. Report the exception type and reference; refresh before repeating a save. |
| RH-WEB-ASSET | A startup script or stylesheet failed to load. Check the connection, reload, and report the file name. |
| RH-WEB-START | The browser runtime did not finish starting. Hard-refresh, then try a private window and report the diagnostic details. |
| RH-WEB-UNSUPPORTED | Update the browser and allow JavaScript and WebAssembly. |
| RH-WEB-RENDER / RH-WEB-RUNTIME | The page could not render or stopped working. Report the exception type and reference before reloading. |

Service request references match service logs. Client references match local app
logs or the browser console; copy them before reloading. Diagnostic reports exclude
tokens, form values, raw exception messages, response bodies, and URL query strings.

For available build targets and platform options, run `python3 scripts/build.py --help`.
Debug services use ports `5180`–`5182` and tokens from `scripts/dev.py token`;
do not mix those tokens with the Docker setup above.
