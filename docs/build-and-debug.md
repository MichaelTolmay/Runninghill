# Build and debug Runninghill

Use Debug for breakpoints and Release for deployment. Libraries build with their
callers: put a breakpoint in Application, Database, or Contracts and run the service
or client that calls it. A library is not a separate process.

## First-time setup

Install .NET 10, Python 3, and Docker with Compose. For the browser debugger install
Chrome or Edge. For MAUI install the target's .NET workload and platform tools:
Android SDK/JDK for Android, Xcode on macOS for Apple targets, and Windows tooling
for Windows. Build machines cannot compile every operating system's MAUI app.

```bash
python3 scripts/dev.py configure
python3 scripts/dev.py prepare --target core
```

In PowerShell use `python` instead of `python3`. `configure` writes the ignored
`Directory.Build.local.props`, using installed Android SDK and Java locations when
found. Review it if multiple Android versions or JDKs are installed. It never
replaces an existing local props file. Settings apply to command-line and IDE builds.

On this Linux host, use the following instead of the first command. These switches
address its unavailable Docker bridge interfaces and incomplete ASP.NET targeting
pack; they are not needed on normal installations:

```bash
python3 scripts/dev.py configure --host-network --allow-missing-prune-data
```

Docker uses the local `default` context, without changing your global Docker context.
Pass `--docker-context NAME` to `configure` to deliberately select another context.

`prepare` starts a separate PostgreSQL container, applies the EF Core collection migrations,
generates private Debug settings, and builds the selected projects. It leaves the existing release stack alone:

| Debug component | Local address |
| --- | --- |
| HTTP API | `http://localhost:5180` |
| gRPC (HTTP/2) | `http://localhost:5181` |
| Web development server | `http://localhost:5182` |
| PostgreSQL | `127.0.0.1:55433` |

`.run/` contains generated credentials and local tooling settings. The service's
`appsettings.Development.local.json` is also generated. These files are ignored by
Git and excluded from Docker build contexts. Only the explicit Debug launch profile
loads the service file, and only in Development. Environment variables/command-line
settings can override it. Do not delete `.run/credentials.json` while keeping the
database volume: the database would still have the old password.

## Build everything or one project

On Linux/macOS:

```bash
./build.sh build -c Debug
./build.sh build -c Release
./build.sh build --target core -c Debug
./build.sh build --target service -c Debug
./build.sh build --target web -c Debug
./build.sh build --target cli -c Release
./build.sh build --target maui -c Debug --framework net10.0-android36.1
./build.sh test -c Debug
```

On Windows the same arguments work with `./build.ps1`. Alternatively use
`python scripts/build.py ...` on any host. Targets are `all` (default), `core`
(everything except MAUI), `application`, `database`, `contracts`, `service`, `cli`,
`web`, `maui`, and `tests`. A single all-project build follows the solution dependency
graph. Use `--jobs 4` to permit four MSBuild workers; dependencies still build first.
Do not launch separate builds of the same project/output directory concurrently.

All builds include MAUI targets for the current host. `--framework` selects one
MAUI target when other platform workloads are unavailable. `--rid` on an all-project
build selects the MAUI runtime only, so an Android RID is never applied to the service:

```bash
./build.sh build -c Release --framework net10.0-android36.1 --rid android-arm64
```

`--property Name=Value` passes an additional MSBuild setting and can be repeated.
Use this for signing settings or unusual toolchain locations; keep secrets out of
command lines and committed files. Missing workloads cause a build failure rather
than silently skipping projects.

## Publish deployable Release output

A Release **build** compiles projects. **Publish** performs AOT, trimming, and
packaging where supported. Outputs go under `artifacts/Release/<project>/<RID>`
(or `artifacts/Release/web` for the static site).

```bash
./build.sh publish -c Release --target core
./build.sh publish -c Release --target service --rid linux-x64
./build.sh publish -c Release --target cli --rid win-x64
./build.sh publish -c Release --target web
./build.sh publish -c Release --target maui --framework net10.0-android36.1 --rid android-arm64
./build.sh publish -c Release --target maui --framework net10.0-ios --rid ios-arm64
```

Run Windows publishes on Windows and Apple publishes on macOS. iOS device packages
need valid signing settings. `--target all` publishing requires a MAUI `--framework`
and `--rid`; service/CLI then publish for the build host, and MAUI for the selected
runtime. `--target core` publishes service, CLI, and web. Their libraries are included;
test projects are built/run, not published as products.

Debug uses portable symbols and disables optimization. The service publishes with self-contained ReadyToRun in Release, with trimming disabled for EF Core. Client Release compilation modes are unchanged. See [service publishing](databases.md#release-publishing-with-readytorun) for details.

## VS Code: F5 and compound debugging

Open this repository folder. Install the recommended extensions from
`.vscode/extensions.json`: C#, C# Dev Kit, .NET MAUI, and Blazor debugging support.
Some alternative VS Code distributions do not support Microsoft's extensions;
use a supported VS Code installation for MAUI and Blazor C# debugging. Core CLR
profiles can also use a compatible installed `coreclr` adapter.

Select a profile in **Run and Debug**, then press **F5**:

- **Service** builds/starts its Debug dependencies and stops at server/library breakpoints.
- **CLI (service already running)** builds the CLI and stops at entry so the short-lived
  command does not finish before you inspect it. Start Service first. Debug settings
  load from `.run/cli.env`; no token needs to be committed to `launch.json`.
- **Web (service already running)** starts the Blazor development server and a C# browser
  debugger. Start Service first to exercise the API; UI-only debugging can run separately.
- **MAUI (selected device)** uses the MAUI extension. Select `Runninghill.Maui`, **Debug**,
  and a device/emulator in that extension before launching.
- **Service + Web**, **Service + CLI**, **Service + MAUI**, and **All applications** build
  their shared prerequisites once, then start multiple debug sessions. The CLI/web
  compound entries wait for the service's readiness endpoint before starting.
- **Attach to a managed process** attaches to a previously launched Debug process.

The profiles ending in **(compound)** are supporting entries without repeated build
steps. Prefer the named compounds for a multi-process run. Switch the active process
in the debug toolbar to inspect a different application. Stopping a compound stops
its debugger sessions; its database stays available for the next run.

Use **Terminal > Run Build Task** for the default all-project Debug build. Other
Debug/Release build, test, and Release publish tasks are listed under **Run Task**.
Use Test Explorer's **Debug Test** for test breakpoints. In Visual Studio on Windows,
open `Runninghill.slnx`, select Debug/Release, and choose multiple startup projects
(Service/Web/CLI/MAUI as needed) in solution properties after running `prepare`.

## Running without an IDE

After `prepare`, run these in separate terminals:

```bash
python3 scripts/dev.py run --target service --no-build
python3 scripts/dev.py run --target web --no-build
python3 scripts/dev.py run --target cli --no-build
```

For manual `dotnet run`, use each project's `Debug` launch profile. The web launch
profile includes the Blazor `inspectUri` needed by the browser debugging proxy.
The Debug browser client calls port 5180 directly; a Development-only CORS policy
allows just the two local web origins and exposes the request reference header.
Release web builds still use the nginx same-origin API proxy.

Generate a fresh 15-minute token for the web or MAUI input:

```bash
python3 scripts/dev.py token
```

This Debug token belongs to the Debug service. The existing `scripts/dev-token.py`
generates tokens for the separate release container stack. Debug CLI preparations
refresh their token automatically; repeat prepare or the wait task if it expires.

## MAUI devices and breakpoint timing

Android emulators reach the host through `http://10.0.2.2:5180/`. Desktop and Apple
simulators use `http://localhost:5180/`. Debug builds prefill that address and permit
HTTP only for loopback/the Android emulator address. Android and Apple local-network
permissions are included only in Debug. Release continues to require HTTPS.

Physical devices need a reachable HTTPS service and a certificate trusted by the
device. Configure the endpoint/certificate explicitly; no TLS-validation bypass is
provided. Linux supports the Android MAUI target, not native Linux desktop MAUI.

Clients allow five minutes per request in Debug, giving you time to inspect a server
breakpoint; Release remains ten seconds. gRPC's internal ten-second timer is disabled
only in a Debug build with an attached debugger; the caller can still cancel. Browser
startup breakpoints may be too early for its debug proxy: use an event handler such
as `CheckStatusAsync` for the first breakpoint.

## Stop the Debug database

```bash
python3 scripts/dev.py database-down
```

This preserves its data volume. It does not stop the release stack or delete data.

Debugger references: [C# configuration](https://code.visualstudio.com/docs/csharp/debugger-settings),
[compound launches](https://code.visualstudio.com/docs/debugtest/debugging-configuration),
[Blazor debugging](https://learn.microsoft.com/aspnet/core/blazor/debug?view=aspnetcore-10.0),
and [MAUI tooling](https://learn.microsoft.com/dotnet/maui/get-started/installation?view=net-maui-10.0).
