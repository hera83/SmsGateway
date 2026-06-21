# SmsGateway — Docker deployment (Ubuntu 24.04)

This document describes how to run the full stack — `api` (talks to the Teltonika
TRM240 modem) and `web` (the dashboard) — in Docker on Ubuntu 24.04, including
serial-port passthrough, non-root permissions, and the `.env`-driven configuration.

- `api` is exposed on host port **5000** (container-internal port 8080).
- `web` is exposed on host port **8080** (container-internal port 8080).
- `web` talks to `api` over the internal Docker network (`http://smsapp:8080`), not
  through the host port mapping.
- Both containers serve **plain HTTP only** — no TLS certificate is configured inside
  either container, and `UseHttpsRedirection()`/`UseHsts()` are skipped outside
  Development for exactly that reason. This setup assumes the stack runs on a
  trusted/internal network (VPN, internal LAN). If you need TLS for external access,
  put a reverse proxy (Nginx/Traefik/Caddy) in front to terminate it — don't expose
  these containers' ports directly to the internet as-is.

All host ports, the serial device, and the shared secret are configured in a single
`.env` file — changing them never requires editing `docker-compose.yml` or rebuilding
images.

## 1. One-time setup: `.env`

```bash
cp .env.example .env
```

Edit `.env`:

```dotenv
SERIAL_DEVICE=/dev/ttyUSB2     # host-side modem device, see section 3
SERIAL_PORT=/dev/COM1          # fixed in-container path the app reads from
API_PORT=5000                  # host port -> api
WEB_PORT=8080                  # host port -> web
MASTER_KEY=...                 # shared secret between api and web (see section 2)
ASPNETCORE_ENVIRONMENT=Production
```

After changing `.env`, apply it with `docker compose up -d` (no `--build` needed —
environment/port/device changes don't require a rebuild).

## 2. How configuration is wired

### Serial port (api only)

The serial port is **never hardcoded**. `api` resolves it in this order:

1. `SERIAL_PORT` environment variable (what `.env`/compose sets).
2. `SmsService:PortName` in `appsettings.json` / `appsettings.Development.json`.
3. OS-based default if neither of the above is set:
   - Windows: `COM1`
   - Linux: `/dev/COM1`

Other serial parameters live under the `SmsService` section of `api/appsettings.json`
(`BaudRate`, `Parity`, `DataBits`, `StopBits`, `Handshake`, timeouts) and can also be
overridden via the standard ASP.NET Core double-underscore convention, e.g.
`SmsService__BaudRate=115200`.

If the configured port does not exist when the modem is accessed, `api` logs a clear
error (`Serial port '<name>' was not found on this host...`, including the list of
ports it *did* find) and the failing operation returns an error to the caller — **the
application keeps running** rather than crashing. The background workers
(`SmsQueueWorker`, `SmsInboxWorker`) just log and retry on their normal polling
interval until the port becomes available.

### Shared secret between `web` and `api`

`web` has no direct database/modem access — it calls `api` over HTTP using an API
key. In Docker, `MASTER_KEY` in `.env` is injected as both:

- `Security__MasterKey` on the `smsapp` (api) container — the master key `api` accepts.
- `SmsService__MasterKey` on the `web` container — the key `web` sends as `x-api-key`.

Both must be the same value, which `.env` already guarantees since both services read
the one `MASTER_KEY` variable. Rotate it for production (e.g. `uuidgen`) before going
live — anyone with this key bypasses subscription/balance checks.

### `web` → `api` URL

`web`'s container has `SmsService__Url=http://smsapp:8080` set in
`docker-compose.yml`. `smsapp` is the api service's Compose service name, resolved via
Docker's internal DNS; `8080` is the container-internal port, independent of
`API_PORT`/`WEB_PORT` (those only affect what's reachable from the host).

## 3. Identify the modem's serial device

Plug in the Teltonika TRM240 (or its USB-serial adapter) and find its device node:

```bash
ls -l /dev/ttyUSB*       # or /dev/ttyACM* depending on the adapter
dmesg | tail -n 20        # confirms which ttyUSBx was just attached
udevadm info -a -n /dev/ttyUSB2 | grep -E 'SUBSYSTEM|GROUP'
```

Put that path in `.env` as `SERIAL_DEVICE` (the examples below assume `/dev/ttyUSB2`).

## 4. Running with Docker Compose (recommended)

```bash
docker compose up -d --build
```

This builds and starts both `smsapp` (api) and `web`. Check status/logs with:

```bash
docker compose ps
docker compose logs -f smsapp
docker compose logs -f web
```

## 5. Running with `docker run` (manual, single container)

Equivalent to the `smsapp` service alone, useful for testing the api in isolation:

```bash
docker build -f api/Dockerfile -t sms-gateway-api .

docker run -d \
  --name sms-gateway-api \
  --device=/dev/ttyUSB2:/dev/COM1 \
  -e SERIAL_PORT=/dev/COM1 \
  -e Security__MasterKey=<your-master-key> \
  -p 5000:8080 \
  -v api_data:/app/App_dbs \
  sms-gateway-api
```

And for `web`:

```bash
docker build -f web/Dockerfile -t sms-gateway-web .

docker run -d \
  --name sms-gateway-web \
  -e SmsService__Url=http://<api-host>:5000 \
  -e SmsService__MasterKey=<your-master-key> \
  -p 8080:8080 \
  -v web_data:/app/App_dbs \
  sms-gateway-web
```

(`-v api_data:/app/App_dbs` is a named volume, created automatically on first run — see
section 6 for why this matters more than it sounds like it should.)

(With plain `docker run`, the two containers aren't on the same Compose network, so
`web` must reach `api` via a routable host/IP, not the `smsapp` DNS name Compose
provides — this is one reason Compose is the recommended path.)

## 6. Persisted data (SQLite databases)

The SQLite databases (`api/App_dbs/smsgateway.db`, `web/App_dbs/webgateway.db`) live
in **named Docker volumes** (`api_data`, `web_data`), not host bind-mounts. This is
deliberate, not just a style choice:

Both containers run as a non-root user (see section 7). If `App_dbs` were a host
bind-mount (`./api/App_dbs:/app/App_dbs`) instead, Docker creates that host directory
owned by `root` the first time it doesn't already exist — and the non-root container
user then can't write to it, so EF Core's `Database.Migrate()`/SQLite fails with
`SQLite Error 14: 'unable to open database file'` and the app exits immediately
(this is exactly what restart-looping with no useful `docker ps` info usually means;
check `docker compose logs` for the real exception, not just the exit code). This
bites on every host, but the Windows+WSL2 file-sharing layer makes it especially
unpredictable. A named volume avoids the problem entirely: Docker seeds a new, empty
named volume from the mount point's existing content/ownership *in the image*
(`/app/App_dbs`, `chown`'d to the app user in the Dockerfile), so permissions are
correct from the first run with zero manual steps.

Useful commands:

```bash
# List/inspect the volumes
docker volume ls
docker volume inspect sms-gateway_api_data

# Back up a volume to a tarball on the host
docker run --rm -v sms-gateway_api_data:/data -v "$(pwd)":/backup alpine \
  tar czf /backup/api_data_backup.tar.gz -C /data .

# Inspect the database file without leaving Docker
docker compose exec smsapp ls -la /app/App_dbs
```

(The `sms-gateway_` prefix on the volume name comes from the Compose project name —
run `docker volume ls` to confirm the exact name on your machine.)

If you'd rather have the `.db` file directly browsable on the host (e.g. to open it
with a SQLite GUI tool), you can switch back to a bind mount, but you must pre-create
and `chown` the directory to match the container's UID/GID **before** the first
`docker compose up`:

```bash
mkdir -p api/App_dbs web/App_dbs
sudo chown -R 10001:10001 api/App_dbs web/App_dbs
```

then change `api_data:/app/App_dbs` / `web_data:/app/App_dbs` back to
`./api/App_dbs:/app/App_dbs` / `./web/App_dbs:/app/App_dbs` in `docker-compose.yml`.

## 7. Non-root execution and serial port permissions

Both containers run as non-root users (UID/GID `10001` — deliberately not `1000`,
since current Ubuntu/Debian base images already ship a built-in non-root user/group
at GID `1000`), not `root`, per the principle of least privilege. `api`'s image
additionally adds its user to the `dialout` group,
which is the group that normally owns TTY/USB-serial devices (`/dev/ttyUSB*`,
`/dev/ttyACM*`) on Debian/Ubuntu — including inside the `--device`/`devices` mapping,
since Docker preserves the device's original ownership/group inside the container.

On most Ubuntu 24.04 hosts this "just works": `ls -l /dev/ttyUSB2` will show group
`dialout` with `rw` permissions for that group, and the container's `dialout` GID
(inherited from the base image, normally `20`) matches the host's.

**If `smsapp` still gets "Permission denied" opening the port**, the host's `dialout`
GID differs from the one baked into the image, or the device belongs to a different
group entirely. Diagnose and fix with one of the following:

1. Check the device's actual owning group/GID on the host:

   ```bash
   stat -c '%G %g' /dev/ttyUSB2
   ```

2. Make the container join that GID as a supplementary group (no image rebuild
   needed) — add to the `smsapp` service in `docker-compose.yml`:

   ```yaml
   group_add:
     - "20"   # replace with the GID from step 1
   ```

   or with `docker run`: `--group-add 20`.

3. As a last resort (not recommended), relax the device permissions on the host
   (`sudo chmod 660 /dev/ttyUSB2` is usually already the default; `666` would let any
   user read/write it) or run the container with `--user root` / `privileged: true`.
   Prefer option 2.

## 8. Verifying it's running

```bash
curl http://localhost:5000/api/Health/Get   # api
curl -I http://localhost:8080/               # web dashboard
```

Swagger UI for the api is at `http://localhost:5000/swagger` (not disabled in
Production).

## 9. Upgrading / reconfiguring

```bash
# Config-only change (ports, serial device, secret) — edit .env, then:
docker compose up -d

# Code change — rebuild:
docker compose up -d --build
```

The SQLite databases in the `api_data`/`web_data` volumes (section 6), and EF Core
migrations for both projects, are applied automatically on startup
(`dbContext.Database.Migrate()` in each `Program.cs`), so no manual migration step is
needed when upgrading.
