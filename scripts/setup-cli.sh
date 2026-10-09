#!/usr/bin/env bash
# Run this file, rather than sourcing it: credentials stay in the CLI child process.
# Usage: ./scripts/setup-cli.sh [status | words list | ...]
if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then
    printf '%s\n' 'Run this script directly: bash scripts/setup-cli.sh [command]. Do not source it.' >&2
    return 2
fi
set -euo pipefail

# Resolve from the script location, not the terminal's current folder.
runninghill_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
runninghill_certificate="$runninghill_root/.run/tls/localhost.crt"
if ! command -v python3 >/dev/null 2>&1; then
    printf '%s\n' 'Python 3 is required. Install it and run this script again.' >&2
    exit 2
fi
if [[ ! -f "$runninghill_root/.env" ]]; then
    printf '%s\n' 'Missing .env. Run scripts/dev-setup.py from this checkout to configure the local Docker stack.' >&2
    exit 2
fi
if [[ ! -f "$runninghill_certificate" ]]; then
    printf '%s\n' 'Missing HTTPS certificate. Run scripts/dev-certificate.py and start the HTTPS containers first.' >&2
    exit 2
fi

case "$(uname -s)" in
    Linux) runninghill_os=linux ;;
    Darwin) runninghill_os=osx ;;
    *) printf '%s\n' 'Use scripts/setup-cli.ps1 on Windows. This script supports Linux and macOS.' >&2; exit 2 ;;
esac
case "$(uname -m)" in
    x86_64|amd64) runninghill_arch=x64 ;;
    aarch64|arm64) runninghill_arch=arm64 ;;
    *) printf '%s\n' 'This CLI setup supports x64 and arm64 computers.' >&2; exit 2 ;;
esac
runninghill_rid="$runninghill_os-$runninghill_arch"
runninghill_binary="$runninghill_root/artifacts/Release/cli/$runninghill_rid/Runninghill.Cli"
if [[ ! -x "$runninghill_binary" ]]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        printf '%s\n' 'The published CLI is missing. Install the .NET 10 SDK and native AOT build tools, then retry.' >&2
        exit 2
    fi
    printf 'Publishing the CLI for %s…\n' "$runninghill_rid"
    if ! python3 "$runninghill_root/scripts/build.py" publish --target cli --rid "$runninghill_rid"; then
        printf '%s\n' 'CLI publishing failed. Check the build output and install the native AOT tools for your operating system.' >&2
        exit 2
    fi
fi

if [[ "$runninghill_os" == osx ]]; then
    # .NET on macOS uses Keychain, rather than OpenSSL's certificate file setting.
    # Trust only the public localhost certificate in this user's login keychain.
    printf '%s\n' 'Trusting the local HTTPS certificate in your login keychain (macOS may ask for permission).'
    security add-trusted-cert -r trustRoot -k "$HOME/Library/Keychains/login.keychain-db" "$runninghill_certificate"
fi

# Generate just before the call so a first-time build cannot consume the token's lifetime.
# Capture the token: never print it, save it, or put it in process arguments.
runninghill_token="$(python3 "$runninghill_root/scripts/dev-token.py")"
if [[ -z "$runninghill_token" ]]; then
    printf '%s\n' 'No access token was generated. Check the development settings in .env.' >&2
    exit 2
fi
if [[ $# -eq 0 ]]; then set -- status; fi
printf '%s\n' 'Using https://localhost:5443/ with a fresh development token.' >&2
# These settings affect this command only, not the calling shell or other programs.
# SSL_CERT_FILE keeps normal hostname/expiry validation and trusts our local certificate.
RUNNINGHILL_SERVICE_URL='https://localhost:5443/' \
RUNNINGHILL_ACCESS_TOKEN="$runninghill_token" \
SSL_CERT_FILE="$runninghill_certificate" \
    "$runninghill_binary" "$@"
