#!/usr/bin/env python3
"""Print a short-lived DEVELOPMENT bearer token; never use this issuer in production."""
from pathlib import Path
import base64
import hashlib
import hmac
import json
import time

env_path = Path(__file__).resolve().parents[1] / ".env"
settings = dict(line.split("=", 1) for line in env_path.read_text().splitlines() if "=" in line and not line.startswith("#"))
if settings.get("ASPNETCORE_ENVIRONMENT") != "Development":
    raise SystemExit("Development tokens are only available for a Development environment.")
key = settings["DEVELOPMENT_SIGNING_KEY"]
def encode(value):
    return base64.urlsafe_b64encode(value).rstrip(b"=")
header = encode(json.dumps({"alg": "HS256", "typ": "JWT"}).encode())
payload = encode(json.dumps({"iss": "runninghill-development", "aud": "runninghill", "sub": "local-developer",
                            "scope": "status.read", "exp": int(time.time()) + 900}).encode())
message = header + b"." + payload
print((message + b"." + encode(hmac.new(key.encode(), message, hashlib.sha256).digest())).decode())
