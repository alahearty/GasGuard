# GasGuard SMS Protocol v1

## Scope

This protocol carries detector telemetry from a microcontroller/GSM transmitter to GasGuard Desktop.
It does **not** carry configuration commands, valve commands, or executable content. Incoming SMS is
parsed as data only.

The detector's local alarm and safety decision remain in the detector hardware and must be
independent of this application.

## Envelope

```
GASALERT|v=1|field=value|field=value...
```

- Marker: `GASALERT` (case-insensitive).
- Separator: `|`.
- Maximum body length: 480 characters.
- Keys are case-insensitive; repeated known keys are rejected.
- Unknown keys are ignored with an advisory warning (forward compatibility).
- The complete body must be printable ASCII; embedded control characters, line breaks, NUL, tabs (inside values) and any non-ASCII character are rejected.
- The SMS is expected to be a single line in physical text mode.

## Fields

| Field | Required | Rules |
|---|---|---|
| `v` | Yes | Protocol version. Only `1` is accepted. |
| `device` | Yes | 3–32 characters, letters/digits/`_`/`-`, must start with a letter or digit. Normalised to uppercase. |
| `event` | Yes | `STATUS`, `GAS_WARNING`, or `GAS_CRITICAL`. |
| `level` | Yes | `NORMAL`, `WARNING`, or `CRITICAL`; must match the event mapping below. |
| `reading` | Yes | Non-negative integer, 0–1,000,000. Raw device value, not calibrated ppm. |
| `unit` | No | 1–16 chars from letters/digits/`%`, `.`, `_`, `/`, `-`; default `raw`. The UI never treats an arbitrary unit as calibration evidence. |
| `seq` | No | Non-negative 64-bit integer sequence number per detector. |
| `ts` | No | ISO-8601 with explicit offset, e.g. `2026-10-28T15:27:26+01:00` or `...Z`. A timestamp without an offset is ignored with a warning. |

### Event/level mapping

| Event | Required level | Resulting severity | Incident? |
|---|---|---|---|
| `STATUS` | `NORMAL` | Normal | No |
| `GAS_WARNING` | `WARNING` | Warning | Yes, while unresolved |
| `GAS_CRITICAL` | `CRITICAL` | Critical | Yes, while unresolved |

Severity is derived from the event mapping, not trusted solely from the `level` text.

## Examples

Critical alert:

```text
GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042
```

Normal status:

```text
GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=120|unit=raw|seq=1043
```

Warning with device time:

```text
GASALERT|v=1|device=GAS-002|event=GAS_WARNING|level=WARNING|reading=300|unit=raw|seq=88|ts=2026-10-28T15:30:00+01:00
```

Malformed example retained for review:

```text
GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=NORMAL|reading=850
```

The malformed message never creates a trusted reading or incident. It is stored with its raw body.

## Validation order

1. Empty body
2. Maximum length
3. Control characters and non-ASCII rejection
4. Marker
5. Segment `key=value` syntax and duplicate fields
6. Required/recognised version
7. Device code syntax
8. Event code
9. Level syntax and event/level consistency
10. Reading syntax and range
11. Unit syntax
12. Sequence syntax
13. Timestamp syntax/offset (warning only, timestamp ignored if invalid)

Error codes include: `EMPTY_MESSAGE`, `MESSAGE_TOO_LONG`, `MISSING_MARKER`, `MALFORMED_FIELD`,
`DUPLICATE_FIELD`, `MISSING_VERSION`, `UNSUPPORTED_VERSION`, `MISSING_DEVICE`,
`INVALID_DEVICE_CODE`, `MISSING_EVENT`, `UNSUPPORTED_EVENT`, `MISSING_LEVEL`,
`UNSUPPORTED_LEVEL`, `EVENT_LEVEL_MISMATCH`, `MISSING_READING`, `INVALID_READING`,
`READING_OUT_OF_RANGE`, `INVALID_UNIT`, `INVALID_SEQUENCE`, `INVALID_TIMESTAMP`,
`TIMESTAMP_WITHOUT_OFFSET`, `CONTROL_CHARACTER`, `NON_ASCII_CONTENT` and `UNKNOWN_FIELD`.

## Sender validation and authentication

After parsing, GasGuard checks:

1. The device code exists in the detector registry.
2. The SMS sender number matches that detector's `ExpectedSenderNumber`.

Nigerian formats such as `0803...`, `+234803...`, `234803...` and the 10-digit subscriber form are
normalised to `+234` E.164 where possible.

This is sender-number filtering, **not cryptographic authentication**. SMS sender IDs can be spoofed
or altered by network systems. Consequences:

- No sensitive control command is accepted over SMS, so spoofing cannot reconfigure or actuate
  hardware through this application.
- A forged telemetry message can still create a false alert or a false NORMAL reading if it comes from
  a spoofed sender and contains a valid payload. Operators must treat SMS as one evidence source and
  use independent verification.
- A production deployment should add payload signing/MAC with per-device keys or a secure gateway.
  That is not implemented in this academic version.
- Sender mismatch and unknown-device messages are retained and audited as security-relevant
  rejections.

Raw SMS is preserved even when parsing or sender validation fails.

## Deduplication and out-of-order handling

The full security rationale is in [SMS_SECURITY_AND_PROTOCOL.md](SMS_SECURITY_AND_PROTOCOL.md).

- SMS fingerprint: SHA-256 of normalised sender + trimmed raw body.
- Exact duplicate suppression is bounded to a **15-minute** window. A repeat within the window is
  retained as `Duplicate` but does not create another incident or regress device state.
- Critical/warning alerts are not discarded solely because their sequence number is lower/equal. A
  lower sequence is recorded as a possible device reset and the alert can create a new incident. An
  alert repeated after its prior incident was resolved, or after the duplicate window, creates a new
  incident. This is a safety-first false-positive trade-off, not cryptographic replay protection.
- Incident dedup keys are time-bucketed and include the device, event, sequence/fingerprint and
  simulated/real mode. A resolved occurrence is followed by a unique occurrence suffix so a detector
  reset reusing a sequence does not permanently suppress a new alert.
- For STATUS messages, an equal sequence is duplicate; a lower sequence is normally duplicate or
  `OutOfOrder`. A status reset is accepted only when a trusted device timestamp is newer than the
  last successful report. Without that evidence, an old status is retained without regressing
  detector state.
- With no `seq`, the fingerprint window is the fallback for status duplicates.
- The detector sequence is updated for accepted messages, including a lower alert sequence that is
  treated as a probable reset.

## Time

- `ts` is used as the occurrence time only if it has an explicit offset, lies within a plausible
  window relative to desktop receipt (not older than 7 days, not more than 10 minutes in the future),
  and is not older than the last successful report for that detector.
- Otherwise the occurrence time is the desktop receipt time. The source is recorded and displayed:
  `DeviceReported`, `DesktopReceipt`, or `DesktopReceiptFallback`.
- Receipt time is always the desktop/modem receive time and is stored separately in UTC.
- All times display in `Africa/Lagos`.

## Demonstration data

Simulator-generated messages carry `IsSimulated = true` through the message and incident records.
The UI and exports label them. Simulated messages are accepted **only for detectors marked as DEMO**;
they cannot change a production detector or trigger automatic real SMS notifications. The sample raw
reading `850` is illustrative; it is not a validated gas concentration and must never be used as an
alarm threshold or calibration value.

## Changing the protocol

Version 1 is fixed in the current application. A future version requires:

1. A new documented version number.
2. Backward-compatible parser behaviour or a migration/compatibility decision.
3. Unit tests for both versions.
4. A detector firmware deployment plan.
5. A safety review; never silently reinterpret an existing event/level mapping.
