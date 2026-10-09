# Desktop monitoring dashboard

`Runninghill.Dashboard` is a separate **Photino.Blazor** application for Windows,
macOS and Linux. It contains one monitoring dashboard. The existing MAUI word
collection application is still available separately.

## Build and run

Install the .NET 10 SDK. The native window also needs:

- **Windows:** Microsoft Edge WebView2 Runtime.
- **macOS:** the system WebKit runtime.
- **Linux:** a graphical session, GTK 3, WebKitGTK 4.1 and libnotify. On Ubuntu
  24.04: `sudo apt install libgtk-3-0t64 libwebkit2gtk-4.1-0 libnotify4`.
  On Alpine the packages are different; the shipped Photino Linux library uses
  glibc, so this desktop application is intended for a compatible glibc desktop,
  not the Alpine nginx container.

From the repository root:

```sh
python3 scripts/build.py build --target dashboard
dotnet run --project src/Clients/Runninghill.Dashboard
```

On Windows, use `python` instead of `python3`. VS Code also has a
**Photino monitoring dashboard** debug configuration and a
**build: dashboard (Debug)** task.

When launched from this checkout (including its build/publish folders), the dashboard
automatically finds `.run/tls/localhost.crt`. This public development certificate
adds trust **only for localhost/loopback connections**. No system trust settings are
changed. A token is checked only after a secure HTTPS connection succeeds.

For a dashboard copied outside the checkout, or to choose a different certificate,
explicitly trust its public certificate for this process before running:

```sh
# Bash, from the repository root
export RUNNINGHILL_DASHBOARD_CA_CERT="$PWD/.run/tls/localhost.crt"
dotnet run --project src/Clients/Runninghill.Dashboard
```

```powershell
# PowerShell, from the repository root
$env:RUNNINGHILL_DASHBOARD_CA_CERT = (Resolve-Path .run/tls/localhost.crt).Path
dotnet run --project src/Clients/Runninghill.Dashboard
```

Normally trusted HTTPS certificates work without extra configuration. An explicit
variable overrides discovery; a missing or incorrect file is never silently ignored.
The supplied certificate adds trust only for its chain; certificate dates and
hostnames are still checked. Relative paths are resolved from the launch directory;
an absolute path is recommended. The debugger configuration sets this variable for
the repository's local certificate; remove it if using another setup. Copy only the
public `.crt` file to another computer, never the private `.key` file.

### If you see `HttpRequestError.SecureConnectionError`

This is a certificate error, not a rejected token. Trusting a browser's certificate
warning does not make the desktop application's HTTP client trust it.

1. Close and reopen the **rebuilt dashboard**. Local checkout launches now discover
   the certificate automatically. If running a copied application, set the variable
   above in the same shell before launching it.
2. Confirm the addresses are `https://localhost:5443/` and that
   `.run/tls/localhost.crt` exists. If it does not, follow the [HTTPS guide](https.md).
3. If the certificate was renewed, recreate the nginx web container using the HTTPS
   guide, then restart the dashboard so both load the same certificate. Check the
   certificate's expiry date and the computer's clock if the error persists.
4. When reporting this error, include the error code, endpoint hostname/port and
   whether you used automatic discovery or an explicit certificate path. Do not
   include your token or private key. For remote servers, ask the administrator to
   check the certificate's hostname, validity and complete trust chain.

## Connect

1. Start the API and website using the [HTTPS guide](https.md). **Rebuild and
   redeploy the service after this update**: the dashboard needs the new
   `GET /api/statistics` endpoint. No database schema migration is needed for counts.
2. Open **Connection settings**. Both addresses default to
   `https://localhost:5443/`. They may be different hosts. Service monitoring calls
   `/health/ready`; website monitoring calls `/health/live` on the respective host.
   HTTPS is required except for local development HTTP addresses.
3. Paste a valid bearer token into **Access token**. The value stays in memory
   only; it is never saved with preferences or sent to the website health endpoint.
4. Enter a whole-number interval from **1 to 3,600 seconds** at the top and select
   **Start monitoring**. Select **Stop monitoring** before changing connection
   settings, replacing an expired token, or changing the interval.

Health checks do not need a token. Additional features require these permissions:

| Feature | Token scopes |
| --- | --- |
| Word and sentence totals | `words.read sentences.read` |
| Service logs | `logs.read` |
| Load test | `status.read` |

For this local development stack, generate a fresh token with the existing
`python3 scripts/dev-token.py` command. Its development token already includes
all the scopes above. Production tokens must come from your configured identity provider.

## Reading the dashboard

- **Health checks** counts completed service and website endpoint checks during
  this window's lifetime. Each completed cycle contributes two checks.
- **Failures** counts failed health checks, including timeouts, TLS failures,
  unexpected health responses and non-success HTTP statuses. It is not a count
  of every error in the service logs. Closing the window resets both counters.
- **Saved words / saved sentences** are scalar database counts, not page sizes.
  A failed refresh shows `—` with a reason instead of an old number or a false zero.
  The counts are read separately and may reflect writes between the two reads.
- The interval is a pause **after a cycle finishes**. Slow checks never overlap
  or accumulate a queue. Each HTTP request times out after eight seconds.
- **Run load test** sends up to 24 authenticated, read-only `/api/status` requests,
  with at most four in flight. The test has a 20-second deadline and stops scheduling
  requests on HTTP 401, 403 or 429. **Stop monitoring** also cancels the test.
- The gauge shows **95th-percentile response latency**, alongside throughput and
  failed requests. Its scale is 0–2,000 milliseconds; the text shows the actual
  value even above that range. This measures responsiveness under a small synthetic
  load, **not server CPU or memory utilisation**, and is not a capacity benchmark.

## Logs, languages and themes

The log list below the cards supports **Dashboard logs** and authenticated
**Service logs**, severity/search filters, refresh and older pages. Choose filters
and select **Refresh logs** to apply them. Newest pages refresh during monitoring;
older pages stay in place while you read them. Each source retains up to 1,000
events in memory, displayed 20 at a time. These are retained application events,
not an archive of Docker, nginx or operating-system files. Service restart clears
its buffer. Technical log messages and user data are displayed as received.

All dashboard controls and messages use the existing English (ZA), Afrikaans (ZA),
isiXhosa (ZA), isiZulu (ZA) and Setswana (ZA) catalogues. Theme and language changes
keep monitoring active. **System theme** supports Light, Dark and System;
**App theme** uses the palettes in the root `themes` folder. Only these appearance
preferences are saved locally.

On startup failure, `RH-DASHBOARD-STARTUP` and the exception type are written to
`Runninghill/Dashboard/startup-error.log` beneath the current user's local
application-data folder. `DllNotFoundException` usually means a native webview
dependency is missing. `RH-DASHBOARD-RENDER` appears inside the window if the UI
cannot be created; check the certificate path and share the displayed error type.
HTTP 401 means the token needs replacing; HTTP 403 means a required scope is
missing. HTTP 404 for totals means the service needs this update or its URL is wrong.

## Publish

Publish on the target operating system and distribute the **whole output folder**,
including `wwwroot` and native libraries:

```sh
python3 scripts/build.py publish --target dashboard --rid linux-x64
# Windows: --rid win-x64
# Intel Mac: --rid osx-x64
# Apple Silicon: --rid osx-arm64
```

Output: `artifacts/Release/dashboard/<rid>/`. Run `Runninghill.Dashboard.exe` on
Windows or `./Runninghill.Dashboard` on Linux/macOS. The .NET runtime is included;
the native webview requirements above still apply. Release uses ReadyToRun with
trimming disabled to preserve Blazor component metadata. This is a portable
application folder, not a signed installer or notarised macOS app bundle.

GitHub publishes dashboard builds on Linux, Windows and macOS after merged PRs.
The managed tests cover monitoring behaviour and count endpoint permissions;
after publishing, `python3 tests/desktop/dashboard.py` exercises the real desktop window against
isolated mock endpoints. On headless Linux, use `xvfb-run -a` after installing the
native dependencies. The window test never accesses the user's collection.
With an existing `.run/tls` certificate pair, add `--https` to exercise automatic
certificate discovery against an isolated HTTPS fixture server. Linux CI runs this
mode; managed TLS tests also verify that expired, misnamed and unrelated
certificates are rejected.

Photino references: [Photino.Blazor](https://github.com/tryphotino/photino.Blazor),
[package and native dependencies](https://www.nuget.org/packages/Photino.Blazor/4.0.13).
