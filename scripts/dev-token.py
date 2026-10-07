#!/usr/bin/env python3
"""Print a short-lived DEVELOPMENT bearer token; never use this issuer in production."""
from pathlib import Path
import base64
import hashlib
import hmac
import json
import time

env_path = Path(__file__).resolve().parents[1] / ".env"
try:
    settings = dict(line.split("=", 1) for line in env_path.read_text().splitlines()
                    if "=" in line and not line.startswith("#"))
except (OSError, UnicodeError):
    raise SystemExit("Could not read .env. Run python3 scripts/dev-setup.py first and check file permissions.") from None
if settings.get("ASPNETCORE_ENVIRONMENT") != "Development":
    raise SystemExit("Development tokens are only available for a Development environment.")
key = settings.get("DEVELOPMENT_SIGNING_KEY", "")
if len(key.encode()) < 32:
    raise SystemExit("DEVELOPMENT_SIGNING_KEY must contain at least 32 bytes. Check your local .env settings.")

# JWT has three URL-safe pieces: who signed it, what it allows, and a tamper-check signature.
# Removing '=' padding is part of the JWT format; it does not encrypt the token.
def encode(value):
    return base64.urlsafe_b64encode(value).rstrip(b"=")
header = encode(json.dumps({"alg": "HS256", "typ": "JWT"}).encode())
# Expire in 15 minutes so a copied development token does not remain useful indefinitely.
payload = encode(json.dumps({"iss": "runninghill-development", "aud": "runninghill", "sub": "local-developer",
                            "scope": "status.read", "exp": int(time.time()) + 900}).encode())
message = header + b"." + payload
print((message + b"." + encode(hmac.new(key.encode(), message, hashlib.sha256).digest())).decode())
