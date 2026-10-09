# Release builds

From the repository root on this Linux development machine:

```sh
python3 scripts/build.py publish --target all -c Release \
  --framework net10.0-android36.1 --rid android-arm64 --output-root release
```

The command creates `release/` and publishes each application separately:

| Folder | Application |
| --- | --- |
| `release/service/linux-x64/` | Self-contained Linux API service, ReadyToRun |
| `release/cli/linux-x64/` | Linux CLI, Native AOT |
| `release/web/wwwroot/` | Static web app, WebAssembly AOT; serve this folder with nginx |
| `release/dashboard/linux-x64/` | Self-contained Photino desktop dashboard, ReadyToRun |
| `release/maui/android-arm64/` | Android ARM64 app with full Mono AOT |

Distribute the entire service or desktop folder, not just its executable. The desktop
still needs GTK 3, WebKitGTK 4.1 and libnotify; see [desktop setup](desktop-dashboard.md).
These Linux outputs use glibc. For the Alpine service container, use the existing
Docker build described in [deployment](deploy-alpine-nginx.md).

Android packages signed with the SDK's development key are for local installation.
A store release needs your own signing key and store configuration. No private keys,
access tokens or database credentials should be copied into this directory.

To publish one application, replace `--target all` with `service`, `cli`, `web`,
`dashboard` or `maui`. Only `maui` needs `--framework`; other native applications use
`--rid linux-x64`. Without `--output-root`, the existing `artifacts/Release/` layout
is preserved. Generated release files are excluded from Git.

## Performance settings

Release explicitly enables compiler optimisation and Native AOT's
[`OptimizationPreference=Speed`](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/optimizing).
The web build uses [`-O3`](https://emscripten.org/docs/tools_reference/emcc.html)
for native compilation, assembly bitcode and linking. These favour execution speed
and may increase build time, memory use and package/download size. They are not a
guarantee of the fastest result on every device; measure representative workloads.

The service and dashboard retain ReadyToRun with tiered compilation and dynamic
PGO. The service uses server garbage collection. The desktop retains workstation
GC to avoid applying a server memory policy to a small UI application. Android
retains full AOT, disables profiled-only AOT and the interpreter, and uses generated
XAML. Apple MAUI retains Native AOT and Windows MAUI retains ReadyToRun.

Keep EF/Photino reflection support, exception handling, diagnostics, Unicode data
and all five languages. Disabling these to shrink a release would break application
features or make production faults harder to diagnose.

## Other operating systems

This Linux command builds all five applications supported by the installed tools.
It does not produce Windows WinUI, iOS or Mac Catalyst packages. Build those on
Windows or macOS with the appropriate .NET MAUI SDKs, Xcode and signing settings.
Publish Photino on each target OS for a validated native distribution. The GitHub
workflow has Windows/macOS desktop jobs; a local publish does not run those jobs.

Examples on their respective build hosts:

```sh
python scripts/build.py publish --target dashboard --rid win-x64 --output-root release
python scripts/build.py publish --target maui --framework net10.0-windows10.0.19041.0 --rid win-x64 --output-root release
python3 scripts/build.py publish --target dashboard --rid osx-arm64 --output-root release
python3 scripts/build.py publish --target maui --framework net10.0-ios --rid ios-arm64 --output-root release
python3 scripts/build.py publish --target maui --framework net10.0-maccatalyst --rid maccatalyst-arm64 --output-root release
```
