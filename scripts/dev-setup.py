#!/usr/bin/env python3
"""Create local development credentials without printing them or replacing existing files."""
from pathlib import Path
import secrets

root = Path(__file__).resolve().parents[1]
path = root / ".env"
if path.exists():
    raise SystemExit(".env already exists; left unchanged.")
with path.open("x") as output:
    output.write("POSTGRES_PASSWORD=" + secrets.token_urlsafe(32) + "\n")
    output.write("DEVELOPMENT_SIGNING_KEY=" + secrets.token_urlsafe(48) + "\n")
    output.write("ASPNETCORE_ENVIRONMENT=Development\n")
path.chmod(0o600)
print("Created .env for local development. Do not commit it.")
