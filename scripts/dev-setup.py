#!/usr/bin/env python3
"""Create local development credentials without printing them or replacing existing files."""
from pathlib import Path
import os
import secrets

root = Path(__file__).resolve().parents[1]
path = root / ".env"
try:
    # Exclusive creation prevents two runs from replacing each other's passwords.
    # Set owner-only permissions BEFORE writing any secrets, not afterwards.
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w") as output:
        output.write("POSTGRES_PASSWORD=" + secrets.token_urlsafe(32) + "\n")
        output.write("DEVELOPMENT_SIGNING_KEY=" + secrets.token_urlsafe(48) + "\n")
        output.write("ASPNETCORE_ENVIRONMENT=Development\n")
except FileExistsError:
    raise SystemExit(".env already exists; left unchanged.") from None
except OSError:
    raise SystemExit("Could not create .env. Check write permissions and free disk space. If an incomplete .env exists, review it before trying again.") from None
print("Created .env for local development. Do not commit it.")
