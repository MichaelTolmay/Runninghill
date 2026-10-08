#!/usr/bin/env sh
# Resolve paths relative to this file, so callers can start from any directory.
exec python3 "$(dirname "$0")/scripts/build.py" "$@"
