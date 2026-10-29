# SMS Security and Protocol

This document consolidates the v1 protocol, the hardened validation/deduplication behavior, and the
security assessment. For field-level examples see [SMS_PROTOCOL.md](SMS_PROTOCOL.md).

## 1. Security summary

| Property | Current state |
|---|---|
| Message confidentiality | SMS is not confidential end-to-end; cellular/SMS providers may see content |
| Sender authenticity | Number allow-list only; **not cryptographic** |
| Message integrity | No signature/MAC; any field can be altered in transit or spoofed |
| Replay protection | Time-window fingerprint/dedup plus sequence checks; heuristic, not cryptographic |
| Sensitive SMS commands | Not supported; the app accepts telemetry event codes only |
| Secrets at rest | No SIM PIN/production key stored; database/journal/backups are unencrypted at the application layer |
| Access control | Windows account identity in audit; no in-app roles/login |
| Local alarm safety | Independent hardware requirement; not controlled by this app |

The current v1 protocol must be treated as **unauthenticated telemetry**. It is suitable for an
academic demonstration and controlled lab validation, not for a real gas-safety deployment without
detector-side authentication and an independent safety review.

## 2. v1 format

```text
GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042
```

| Field | Required | Validation |
|---|---|---|
| `v` | Yes | Exactly `1` |
| `device` | Yes | 3–32 ASCII alphanumerics, `-`, `_`; uppercase normalized |
| `event` | Yes | `STATUS`, `GAS_WARNING`, `GAS_CRITICAL` |
| `level` | Yes | `NORMAL`, `WARNING`, `CRITICAL`, mapped to the event |
| `reading` | Yes | Non-negative integer 0–1,000,000; no decimal/sign |
| `unit` | No | 1–16 ASCII safe characters; default `raw` |
| `seq` | No | Non-negative 64-bit integer |
| `ts` | No | ISO-8601 with explicit `Z`/offset; otherwise ignored with a warning |

Hardened parser rules:

- marker/field separator/`key=value` syntax;
- duplicate known fields rejected;
- unknown fields ignored with an advisory warning;
- embedded control characters (`< 0x20`, DEL) rejected;
- any non-ASCII character rejected (`NON_ASCII_CONTENT`);
- maximum 480-character body and explicit field limits;
- invariant integer/sequence parsing;
- event/level mismatch rejected;
- malformed/optional timestamp becomes a warning and fallback time source, not a trusted
  occurrence time.

Malformed messages are persisted as evidence with `Invalid`, never as a reading or incident.

## 3. Sender trust

Detector registry matching:

1. Device code exists.
2. Sender number matches `ExpectedSenderNumber` after Nigerian mobile normalization where possible.
3. Sender numbers can be `0803…`, `+234803…`, `234803…`, or the 10-digit subscriber form.

This is an allow-list filter. SMS sender IDs can be spoofed by attackers or altered by intermediaries.
An allow-listed sender does not prove that the physical detector sent the message, nor that the
detector is functioning. Sender mismatch/unknown devices are retained and audited. No remote
configuration or actuation command is accepted via SMS, which limits the consequence of spoofing to
false telemetry/incidents and false status displays.

## 4. Duplicate and replay behavior

- SMS fingerprint: SHA-256 of normalized sender + trimmed raw body.
- Duplicate window: **15 minutes** for current duplicate replay suppression.
- Exact duplicates within the window are retained as `Duplicate` messages but do not create another
  incident or regress device state.
- Alerts are not suppressed solely because their sequence is lower/equal. A possible sequence reset
  is recorded and the alert can create a new incident. A resolved incident followed by a repeated
  alert creates a new incident.
- Status messages with a lower/equal sequence are normally duplicate/out-of-order. A status reset is
  accepted only if a trusted device timestamp is newer than the last successful report; without that
  evidence, old statuses are retained as out-of-order rather than allowed to regress state.
- Alert incident keys are time-bucketed, with an occurrence suffix after a resolved incident, so a
  detector reset that reuses a sequence number does not permanently consume the key.
- A replay after the 15-minute window may create a new incident. This is intentionally safety-first:
  false positives are preferable to silently discarding a possible new hazard. It is **not** a
  cryptographic anti-replay guarantee.

The complete duplicate/rejection policy is additionally recorded in the communication log, message
processing detail, and incident/rejection audit where applicable.

## 5. Authentication limitation and proposed v2

The current detector hardware was unavailable and has no provisioned key material. Therefore no HMAC
implementation is claimed end-to-end. A secure v2 protocol should include:

```text
GASALERT|v=2|keyid=DEVICE_KEY_1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|
reading=850|unit=raw|seq=1042|ts=2026-10-28T15:27:26+01:00|nonce=...|mac=<hex>
```

Proposed requirements:

1. Canonicalize all authenticated fields in a documented byte-exact order.
2. Device computes `HMAC-SHA-256(device_secret, canonical_payload)`.
3. Receiver looks up `keyid` (only non-secret key identifier stored in app config/DB), computes the
   MAC, and compares with `CryptographicOperations.FixedTimeEquals` in constant time.
4. Authenticated sequence/nonce/time windows prevent replay; store last accepted sequence/nonce per
   key.
5. Provision keys outside the repository (secure manufacturing/field process), never hard-code them,
   never log them, and support rotation/revocation.
6. Do not create incidents or update detector state until authentication/authorization succeeds.
7. A policy setting marks v1 as unauthenticated/disabled where authentication is required.

None of these key-management steps can be completed with the available hardware. Until a detector
can generate v2 messages, any statement that GasGuard provides end-to-end SMS authentication would be
false.

## 6. Encoding and multipart

- The physical transport uses text mode and ASCII; non-ASCII bytes trigger a diagnostic warning.
- v1 parser rejects non-ASCII content.
- UCS-2/Unicode, PDU mode, and concatenated/multipart SMS are not implemented.
- The app does not attempt unsafe multipart reassembly. A split/incomplete message is rejected and
  retained as invalid evidence rather than merged with unrelated SMS.
- The intended detector/network must be validated with actual SMS; see
  [GSM_HARDWARE_VALIDATION.md](GSM_HARDWARE_VALIDATION.md).

## 7. Simulation security

Simulated messages are accepted only for detectors created by the demo scenario (`IsDemo=true`).
Simulator UI lists only demo detectors. Simulated data cannot update a production detector's state or
create a production incident. Automated notifications are suppressed for simulated incidents and for
demo devices. Rejected simulated messages are retained/audited as `SimulationRejected`.

## 8. Privacy and retention

- SMS bodies/sender numbers are operational evidence and are stored in SQLite; they may contain
  personal data (phone numbers).
- Communication logs/CSV reports can include phone numbers and are available to the local desktop
  operator; protect exports appropriately.
- File logs mask phone numbers and do not log SMS bodies. Audit entries mask phone numbers where
  practical.
- The incoming-SMS journal stores raw messages temporarily under the user's LocalAppData profile for
  crash recovery; successful processing removes entries. Protect the Windows profile and use device
  encryption.
- Retention cleanup removes old communication logs and unlinked SMS messages, and non-incident audit
  rows, but preserves incidents, linked SMS evidence, and incident audit history.
- Backups contain the same sensitive data as the database. Store/copy them under institutional
  controls.

## 9. Threat model highlights

| Threat | Effect | Current mitigation | Residual |
|---|---|---|---|
| Spoofed sender + valid body | False alert/status | Number allow-list; no commands; audit | Still possible; needs HMAC/secure gateway |
| Replay of old alert | False incident | 15-minute dedup/sequence; audit | Replay after window can create incident |
| Malformed SMS | Crash/false safe state | Strict parser; raw evidence; rejects state changes | None known in tested inputs |
| Unknown/unauthorized sender | Unauthorized telemetry | Reject/retain/audit | No crypto identity |
| Modem compromise | Malicious modem input | Parse/validate; raw evidence; no SMS commands | Modem is trusted local hardware |
| DB corruption/write failure | Lost/misleading state | Transactions; health checks; backups; journal | Power loss during direct-receive-to-journal window |
| Unauthorized local user | Acknowledge/resolve/config/restore | Windows account identity/audit | No in-app roles; use OS access controls |
| CSV export opened in Excel | Formula execution | Formula-leading cells neutralized | File still contains operational PII |

## 10. Security acceptance criteria

Before treating the integration as authenticated:

- detector firmware generates signed v2 messages with a provisioned key;
- receiver verifies with constant-time comparison;
- keys are provisioned/rotated outside source control;
- replay/nonce/sequence acceptance tests pass;
- v1 is explicitly disabled or marked unauthenticated by policy;
- physical modem/network validation is complete.

Until then, the correct security status is **unauthenticated telemetry with allow-list filtering and
heuristic replay controls**, not a secure authenticated gas-alarm link.
