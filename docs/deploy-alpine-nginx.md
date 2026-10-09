# Deploy Runninghill to your existing Alpine nginx container

Your existing shared folder is enough to serve the website. You also need the
Runninghill API and PostgreSQL containers for saving and loading words.

This guide assumes **Docker with Compose**, a normal Docker bridge network, and
all containers on the **same Alpine server**. Run the commands on that server as
an account with Docker and folder permissions. No .NET installation is needed on
the Alpine host: the supplied Dockerfiles build the applications inside containers.

## 1. Put the project on the server

Copy the project to `/opt/runninghill`, including its `src`, `themes`, `scripts`,
Dockerfiles and Compose file. Keep source code and credentials outside the web folder.
Do not copy your development machine's `.env`, `.run`, `bin`, or `obj` folders.

```sh
cd /opt/runninghill
docker compose version
python3 --version
docker ps --format '{{.Names}}'
```

Note the name of your existing nginx container. Replace **`YOUR_NGINX`** below
with that name. If Python or Compose is missing, Alpine provides them through
`apk add python3 docker-cli-compose` (Compose is in the `community` repository).
[Alpine package reference](https://pkgs.alpinelinux.org/package/v3.23/community/x86_64/docker-cli-compose).

## 2. Create settings and start the backend

For a new installation, create the server's settings:

```sh
python3 scripts/dev-setup.py
```

This creates `.env` with a database password and a development signing key. Keep
an existing `.env` if this server already has a Runninghill database.

- **Private demo:** the generated Development settings support the token command
  in step 6. Keep this setup restricted to your private network or VPN.
- **Public deployment:** before starting, edit `.env` to set
  `ASPNETCORE_ENVIRONMENT=Production`, `AUTHENTICATION_AUTHORITY=https://YOUR-IDENTITY-PROVIDER`,
  and `AUTHENTICATION_AUDIENCE=YOUR-API-AUDIENCE`. Use real values from your identity
  provider. Production requires its JWT access tokens; the development token script
  does not work in Production. Use HTTPS with a certificate for your site's hostname.

Run these commands in order; continue only when each succeeds:

```sh
docker compose build
docker compose up -d --wait database
python3 scripts/migrate.py
docker compose up -d --wait service
docker network connect runninghill_default YOUR_NGINX
```

The build can take several minutes. Migrations create the database tables. These
commands start only the database and API; **your existing nginx serves the website**.
Do not run a bare `docker compose up`, which would also start the project's nginx.

The shared Docker network lets nginx reach the API by its service name.
[Docker networking reference](https://docs.docker.com/compose/how-tos/networking/).
If nginx is already attached, skip `network connect`. If you recreate nginx later,
reattach it or declare `runninghill_default` as an external network in its Compose file.

## 3. Copy the built website into your shared folder

Use a subfolder so your current demo files stay untouched:

```sh
mkdir -p /var/www/html/runninghill
docker create --name runninghill-site-export runninghill-web
docker cp runninghill-site-export:/usr/share/nginx/html/. /var/www/html/runninghill/
docker rm runninghill-site-export
```

The temporary container only supplies the published files; it is never started.
Copy **all** files, including `_framework`, JavaScript, CSS, and configuration.

| Location | Published website directory |
| --- | --- |
| Alpine server | `/var/www/html/runninghill/` |
| Inside your existing nginx container | `/usr/share/nginx/html/runninghill/` |

Check the shared folder:

```sh
docker exec YOUR_NGINX ls /usr/share/nginx/html/runninghill/index.html
```

## 4. Point your existing nginx site at Runninghill

Back up the site's nginx configuration. Edit the **persistent configuration file**
mounted into your container; changes made only inside a container disappear when it
is recreated. `docker exec YOUR_NGINX nginx -T` shows the active configuration files.

Keep your existing `listen`, `server_name`, and HTTPS certificate settings. Inside
that site's `server { ... }` block, replace its `root`, `index`, and old `location /`
with the following. Replace any conflicting locations instead of adding duplicates.

```nginx
root /usr/share/nginx/html/runninghill;
index index.html;

location /api/ {
    # No trailing slash: the API must receive the full /api/... path.
    proxy_pass http://service:5080;
    proxy_http_version 1.1;
    proxy_set_header Host $http_host;
    proxy_set_header Authorization $http_authorization;
    proxy_connect_timeout 3s;
    proxy_read_timeout 15s;
    client_max_body_size 16k;
}

location = /health/ready {
    proxy_pass http://service:5080;
}

location /_framework/ {
    try_files $uri =404;
}

location / {
    # Recheck the entry page and styles after deployment instead of using stale files.
    add_header Cache-Control "no-cache" always;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy no-referrer always;
    try_files $uri $uri/ /index.html;
}
```

Keep nginx's existing `include /etc/nginx/mime.types;` in the `http` configuration.
Its `types` table should contain `application/wasm wasm;`; add that entry if absent.
Preserve any other site-specific headers in `location /`, since nginx header
inheritance can change when that location defines its own `add_header` directives.

`service:5080` refers to the API container. **Do not use `localhost:5080` inside nginx**:
that would point back to nginx's own container. The proxy preserves the request path.
[nginx proxy reference](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_pass).

The app is served at **`https://YOUR-SITE/`**, not `/runninghill/`. The subfolder is
only a disk location; the application currently expects the root URL.

## 5. Check and reload nginx

```sh
docker exec YOUR_NGINX nginx -t
```

Only if that succeeds:

```sh
docker exec YOUR_NGINX nginx -s reload
```

Open `https://YOUR-SITE/health/ready`: it should show `Healthy`.
Then open `https://YOUR-SITE/` and press **Ctrl+Shift+R** once.

## 6. Connect to your collection

For the **private Development demo**, generate a token on this server:

```sh
cd /opt/runninghill
python3 scripts/dev-token.py
```

Paste it into **Service connection → Access token**, then select **Connect / refresh**.
It expires after 15 minutes. For Production, obtain an access token from your identity
provider with the configured audience and `status.read`, `words.read`, `words.write`,
`sentences.read`, and `sentences.write` scopes. `logs.read` is needed for service logs.
The browser automatically calls `/api/` on your site's address; no localhost URL
needs to be entered in the web app.

## Later updates and quick fixes

For an update, back up the database and current website files, copy the new source
into `/opt/runninghill`, and keep `.env`. Build again. Stop the API before applying
migrations, keep PostgreSQL running, then start the API again:

```sh
docker compose build
docker compose stop service
python3 scripts/migrate.py
docker compose up -d --wait service
```

Repeat steps 3 and 5 during your deployment window. Reload nginx after replacing
the API container so it resolves the service's current address. Do not use
`docker compose down -v`: that removes the database volume.

| Problem | What to check |
| --- | --- |
| Old demo still appears | Check the active site's `root`, confirm the new `index.html` is visible inside nginx, reload nginx, then hard-refresh the browser. |
| HTTP 502 / “host not found in upstream” | Check `docker compose ps`, nginx's network attachment, and `docker compose logs --tail 50 service`. Reload nginx after fixing the API connection. |
| Page has no styling or fails to load | Copy the complete published directory, including `_framework` and both CSS files. Confirm the MIME types and inspect browser diagnostics. |
| HTTP 401 | Use a fresh token from this server's settings or the configured Production identity provider. |
| Readiness is unhealthy | Check PostgreSQL, the connection settings, and whether migrations completed. |

To restore the original demo page, restore your previous nginx configuration and
reload nginx. The original demo files remain in `/var/www/html/`.
