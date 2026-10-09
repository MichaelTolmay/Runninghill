# Local HTTPS and nginx reverse proxy

nginx can serve the website and proxy the API through **https://localhost:5443**.
The certificate is self-signed for `localhost`, `127.0.0.1` and `::1`, valid for
365 days. The published listeners use IPv4 loopback; use `localhost` or
`127.0.0.1` to connect. This configuration is for this computer, not LAN access.

The existing three containers are reused:

```text
Browser / client -- HTTPS :5443 --> nginx web container
                                      |-- serves Blazor files
                                      |-- /api/* --> API :5080 (HTTP)
                                      |-- /runninghill.v1.Application/* --> API :5081 (gRPC)
                                                           |-- PostgreSQL
```

TLS ends at nginx. Internal API traffic uses the private Docker network, or
loopback in host-network mode. The original API ports remain bound to loopback
for local tools. nginx forwards bearer tokens and preserves API paths, query
parameters, status codes and request references. Native gRPC uses HTTP/2; this
does not add gRPC-Web support to the browser.

## Generate the certificate

From the repository root, with Python 3 and OpenSSL available:

```sh
python3 scripts/dev-certificate.py
```

This creates `.run/tls/localhost.crt` and `.run/tls/localhost.key`. Repeating the
command keeps an existing pair. `.run` is excluded from Git and Docker build
contexts: certificates are mounted at runtime, never copied into an image.

On Unix, the containing directory has mode `0700` so only its owner can access
it. The individual files have mode `0644` so nginx's unprivileged container user
can read their separate read-only bind mounts. Keep the directory private; do
not copy the private key into a shared directory. On Windows, keep the directory
under your user account with restricted filesystem access. Only the public `.crt`
file should be shared for client trust.

## Start HTTPS with ordinary Docker networking

First complete the build and migration steps in the [Starter quick guide](starter-quick-guide.md).
Then use the HTTPS override:

```sh
docker --context default compose -f compose.yaml -f deploy/compose.https.yaml up -d --wait --no-build
```

## Start HTTPS on this Linux host

This machine needs host networking. Use all four files in this order:

```sh
docker --context default compose -f compose.yaml -f deploy/compose.host-network.yaml -f deploy/compose.https.yaml -f deploy/compose.https.host-network.yaml up -d --wait --no-build
```

Use the same file list for future `up`, `stop` and `logs` commands. HTTPS configuration
is mounted, so enabling it does not require recompiling the web app. If you later
run `up` without the HTTPS overrides, Compose restores the ordinary HTTP setup.

Open **https://localhost:5443**. Visiting `http://localhost:5082` redirects to that
address (except the local health check). API clients should use HTTPS directly,
including when sending tokens or POST requests; a redirect cannot encrypt the
original HTTP request.

A self-signed certificate is **not automatically trusted** by browsers. No system
trust store is changed by the script. Trust the public certificate using your
browser/OS certificate settings where supported, or approve a local browser
certificate exception after checking its fingerprint:

```sh
openssl x509 -in .run/tls/localhost.crt -noout -fingerprint -sha256 -dates
```

Access tokens still come from `python3 scripts/dev-token.py` and still expire
after 15 minutes. HTTPS certificates and JWT signing keys serve different purposes;
neither the certificate nor its private key belongs in the access-token box.

## Verify the proxy

These checks explicitly trust this certificate and still verify the hostname:

```sh
curl --cacert .run/tls/localhost.crt https://localhost:5443/health/ready
python3 scripts/smoke-stack.py --service https://localhost:5443 --web https://localhost:5443 --ca-cert .run/tls/localhost.crt
```

The second command checks unauthorized and authorized API calls through nginx,
PostgreSQL readiness and web assets. Certificate validation remains enabled.
nginx offers TLS 1.2/1.3, HTTP/2 and shared TLS session caching. API connections
are reused, requests have bounded timeouts, and nginx logs omit query strings.

## Renew or troubleshoot

- **Browser certificate warning:** trust the generated certificate or approve the
  local exception; the certificate cannot be verified by a public certificate authority.
- **Missing certificate file:** run `scripts/dev-certificate.py` before Compose.
- **Certificate expired or needs replacing:** stop the web container using the
  same Compose file list, run `python3 scripts/dev-certificate.py --renew`, then
  recreate it with `up -d --wait --no-build --force-recreate web`. File bind mounts
  need recreation to see replaced files. Clients must trust the new certificate.
- **502 / service unavailable:** check the API and database containers and their
  health checks. The certificate does not replace starting the backend.
- **Want a public or LAN deployment:** supply a certificate for the actual hostname,
  adjust listeners and ports, and use a trusted issuer for public deployment.
  This localhost configuration intentionally does not expose the machine to the LAN.

nginx references: [HTTPS configuration](https://nginx.org/en/docs/http/configuring_https_servers.html),
[HTTP proxy](https://nginx.org/en/docs/http/ngx_http_proxy_module.html),
and [gRPC proxy](https://nginx.org/en/docs/http/ngx_http_grpc_module.html).
