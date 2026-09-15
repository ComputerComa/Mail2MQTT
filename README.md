# Mail2MQTT

A generic SMTP-to-MQTT notification gateway written in native .NET.

It sits behind an existing SMTP frontend (e.g. Postfix) that already handles
public listening, STARTTLS, SMTP authentication, certificates, and durable
queuing/retries. This gateway only does the last hop: it accepts messages
over **loopback** SMTP, parses them with MimeKit, converts them into a
normalized, versioned JSON event, and publishes that event to an MQTT broker
at QoS 1 - only returning SMTP success once the broker has acknowledged the
publish. Downstream automation (Node-RED, Home Assistant, etc.) then decides
what to do with the alert.

```text
Sending applications/appliances
    -> Existing SMTP frontend (auth, TLS, queuing, retries)
    -> Mail2MQTT (loopback SMTP -> MQTT, QoS 1, ack-gated)
    -> MQTT broker
    -> whatever consumes homelab/alerts/raw
```

## Why SMTP succeeds only after MQTT acknowledges

Reliability is the core design goal. If the MQTT broker is disconnected,
times out, or rejects the publish, the gateway returns a temporary SMTP
failure (451) so your SMTP frontend keeps the message queued and retries it
later - it never returns success unless MQTT has confirmed the message.
Because QoS 1 is at-least-once delivery, every event carries a
deterministic SHA-256 event ID (computed from the raw message bytes plus
the envelope sender/recipients) so retries can be deduplicated downstream.

## Project layout

```text
SmtpMqttGateway.slnx
src/SmtpMqttGateway/     - the gateway (Worker Service)
  Configuration/         - typed, validated options (Smtp, Mqtt)
  Models/                - the versioned AlertEnvelopeV1 event schema
  Mqtt/                  - IAlertPublisher / MqttAlertPublisher (MQTTnet)
  Smtp/                  - GatewayMessageStore / SmtpHostedService (SmtpServer)
  Services/              - MIME parsing, HTML-to-text, event construction
tests/SmtpMqttGateway.Tests/
deploy/                  - systemd unit, example configs, Mosquitto ACL, docs
```

See `deploy/README.md` for build, publish, and integration instructions,
including how to point an existing Postfix (or similar) install at this
gateway's loopback listener.

## Quick start (development)

```bash
dotnet build SmtpMqttGateway.slnx
dotnet test tests/SmtpMqttGateway.Tests/SmtpMqttGateway.Tests.csproj
dotnet run --project src/SmtpMqttGateway
```

Copy `src/SmtpMqttGateway/appsettings.example.json` to
`appsettings.Development.json` (or set environment variables such as
`Mqtt__Password`) to point the gateway at your own MQTT broker before
running it for real.

## Topics

Every alert is published twice, both at QoS 1 and not retained:

- A fixed fan-out topic (`Mqtt:RawTopic`, default `homelab/alerts/raw`) -
  every alert, regardless of sender.
- A per-sender topic resolved from `Mqtt:TopicTemplate` (default
  `homelab/alerts/{senderLocal}`), so different senders can be routed to
  different downstream flows without any classification logic in the
  gateway itself. See `deploy/README.md` for the available template
  variables. Both publishes must be acknowledged for the SMTP transaction
  to succeed.

The retained gateway status (`Mqtt:StatusTopic`, default
`homelab/gateways/smtp/status`) is `online`/`offline`, backed by an MQTT
Last Will for ungraceful disconnects.

## Event schema (alert payload)

```json
{
  "schemaVersion": 1,
  "eventId": "<sha256 hex>",
  "receivedAtUtc": "2026-01-01T00:00:00Z",
  "messageId": "<original-message-id@example>",
  "envelope": { "from": "...", "recipients": ["..."] },
  "headers": { "from": "...", "to": ["..."], "subject": "...", "dateUtc": "..." },
  "content": { "text": "...", "html": null },
  "attachments": [{ "fileName": "report.txt", "mediaType": "text/plain", "size": 1234 }]
}
```

Attachment **contents** are never published - metadata only (filename,
media type, size), bounded by configurable limits. See
`src/SmtpMqttGateway/appsettings.example.json` for all configuration knobs.
