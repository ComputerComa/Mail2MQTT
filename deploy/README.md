# Deployment guide

This gateway is a native .NET Worker Service. It is not containerized and
has no runtime dependency beyond the .NET runtime (or nothing at all, if
published self-contained).

## 1. Build

```bash
dotnet build SmtpMqttGateway.sln -c Release
```

## 2. Run the tests

```bash
dotnet test tests/SmtpMqttGateway.Tests/SmtpMqttGateway.Tests.csproj
```

## 3. Publish

### Option A: framework-dependent (requires the .NET 10 runtime on the target host)

```bash
dotnet publish src/SmtpMqttGateway/SmtpMqttGateway.csproj \
  -c Release \
  -o ./publish/framework-dependent
```

This produces `SmtpMqttGateway.dll`, run with `dotnet SmtpMqttGateway.dll`.

### Option B: self-contained, single-file, linux-x64 (no .NET runtime required on the target host)

```bash
dotnet publish src/SmtpMqttGateway/SmtpMqttGateway.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o ./publish/self-contained-linux-x64
```

This produces a single native executable, `SmtpMqttGateway`, plus its
`appsettings*.json` files alongside it.

## 4. Create a dedicated service user

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin smtp-mqtt-gateway
```

## 5. Install the files

```bash
sudo mkdir -p /opt/smtp-mqtt-gateway
sudo cp -r ./publish/framework-dependent/* /opt/smtp-mqtt-gateway/
# or the self-contained-linux-x64 output, if you used Option B

# appsettings.json (defaults) is safe to leave world-readable.
# appsettings.Production.json holds the MQTT password - lock it down:
sudo cp deploy/appsettings.Production.example.json /opt/smtp-mqtt-gateway/appsettings.Production.json
sudo $EDITOR /opt/smtp-mqtt-gateway/appsettings.Production.json   # set Mqtt:Username / Mqtt:Password

sudo chown -R smtp-mqtt-gateway:smtp-mqtt-gateway /opt/smtp-mqtt-gateway
sudo chmod 750 /opt/smtp-mqtt-gateway
sudo chmod 640 /opt/smtp-mqtt-gateway/appsettings.Production.json
```

Prefer environment variables over the JSON file for the MQTT password where
possible (.NET's configuration system maps `Mqtt__Password` to
`Mqtt:Password`); the provided systemd unit shows how to wire that up via
`EnvironmentFile=`.

## 6. Install the systemd unit

```bash
sudo cp deploy/smtp-mqtt-gateway.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now smtp-mqtt-gateway
sudo systemctl status smtp-mqtt-gateway
journalctl -u smtp-mqtt-gateway -f
```

Edit the `ExecStart=` line in the unit file to match whichever publish
option you used (framework-dependent `dotnet ... .dll`, or the
self-contained single-file binary).

## 7. Mosquitto ACL

See `deploy/mosquitto-acl.example`. The gateway needs **publish-only**
access to its two topics:

- `homelab/alerts/raw`
- `homelab/gateways/smtp/status`

It never subscribes to anything, so do not grant it read access.

## 8. Postfix integration

This gateway only replaces the final hop from Postfix to your notification
backend - Postfix keeps handling external SMTP, STARTTLS, SMTP AUTH,
certificates, and the durable queue with retries. Once you've verified the
gateway independently (see below), point Postfix's existing
notification-relay transport at it instead of (or alongside, during
testing) your current destination.

A typical approach is a dedicated Postfix `transport_maps` entry so only
notification traffic is routed to the gateway, leaving other mail flows
untouched. In `/etc/postfix/transport`:

```
notify.example.invalid    smtp:[127.0.0.1]:10025
```

then in `main.cf`:

```
transport_maps = hash:/etc/postfix/transport
```

and `postmap /etc/postfix/transport` followed by `postfix reload`.

Because the gateway's SMTP listener binds only to `127.0.0.1` and neither
authenticates nor terminates TLS, it must never be reachable from anywhere
except Postfix on the same host - Postfix has already done both of those
checks before this hop. Do not expose the gateway's port beyond loopback.

**Keep your existing direct route to your current notification backend
working until you have confirmed, end-to-end, that alerts sent through this
gateway are correctly published to MQTT and consumed downstream.** Only
then cut Postfix over to the gateway for real traffic.

## 9. Graceful shutdown behaviour

On `systemctl stop`, the gateway:

1. Stops accepting new SMTP connections.
2. Lets in-flight SMTP transactions finish (bounded by systemd's stop
   timeout, `TimeoutStopSec` in the unit file).
3. Publishes a retained `offline` status to `homelab/gateways/smtp/status`.
4. Disconnects from the MQTT broker and disposes all resources.

If the process is killed ungracefully instead, the MQTT broker's Last Will
message (also a retained `offline` status) fires automatically.
