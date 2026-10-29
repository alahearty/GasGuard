# GasGuard Desktop

**Design and Implementation of a Gas Detection System Using a GSM Network**

GasGuard Desktop is a Windows WPF monitoring application that receives detector telemetry as SMS
through a USB GSM modem, validates a versioned text protocol, preserves the original messages,
creates and tracks incidents, and supports explicit operator acknowledgement and resolution.

It is an undergraduate/academic project and demonstration system. It is **not** a certified gas
safety controller and it must never replace a detector's independent local alarm or an engineered
safety system.

## Current status

| Item | Status |
|---|---|
| .NET 10 solution and WPF application | Builds successfully |
| Unit tests | 101 passed |
| Integration tests | 46 passed |
| Simulated modem workflow | Verified end to end |
| Physical USB GSM modem | **Not physically tested** on the development machine |
| Local detector alarm | Must be independent hardware; documented and not tested here |
| Real gas response | Not tested; values are raw device readings, not calibrated ppm |

The latest verified test command and results are in [docs/TEST_RESULTS.md](docs/TEST_RESULTS.md).
Physical hardware compatibility must be validated on the actual modem, SIM, and Nigerian network
before making any operational claim.

## What the application does

- **Dashboard** showing active detectors, unresolved critical/warning incidents, last detector
  report and freshness, modem readiness, signal, database health, backup status, recent alerts and
  communication failures.
- **Live Alerts** with explicit acknowledgement and resolution, mandatory resolution notes, original
  SMS inspection, receipt/occurrence times and a full audit history.
- **GSM Modem** workspace with COM-port discovery, manual port selection, serial settings, AT
  command connection, SIM/networking/signal status, SMS polling, reconciliation, diagnostics,
  confirmed test messages, bounded reconnection and a full DEMO simulator.
- **Gas Detectors** registry with unique device codes, expected SMS senders, installation data and
  deactivation that preserves historical incidents.
- **Incident History** with search, dates, severity/status/device/location filters, pagination,
  inspection and filtered CSV export.
- **Contacts & Notifications** with Nigerian mobile-number normalisation, notification preferences,
  confirmation-gated test SMS and outgoing SMS audit.
- **Reports** for incident counts by severity/detector, trends, communication failures and detector
  activity, with CSV exports.
- **Audit Log** covering configuration, operator actions, exports, backups and security rejections.
- **Settings & Maintenance** with freshness and retention settings, logging level, database health,
  verified backups/restore, cleanup and protocol information.
- **Simulation mode** with clearly labelled demo detectors, sample/custom SMS injection, delayed
  delivery, duplicate/out-of-order/invalid messages, outgoing failure outcomes and simulated
  disconnection.
- **Durable SMS intake**: each received SMS is journaled to `%LOCALAPPDATA%\GasGuardDesktop\inbox` before processing; journal entries are replayed after a crash and removed only after persistence.
- **Safety-oriented deduplication**: duplicate/replay suppression is time-bounded; a lower sequence or repeated critical alert is never silently suppressed solely because it resembles an earlier message.
- **Transactional audit**: message, incident and audit writes for ingestion and operator state changes share one SQLite transaction.
- **Hardened operations**: single-instance lock, restore quiescing/maintenance overlay, CSV formula-injection protection, strict ASCII/control-character protocol checks, and physical modem fault-state handling.
- **Recent SMS evidence** showing every retained raw message, including invalid, unknown-device and
  unauthorised-sender messages, without treating them as trusted readings.

## Safety position

The application is a monitoring and evidence-recording layer. It does not control gas valves,
suppress alarms, or override a local controller.

- A detector's local alarm must work when Windows, SQLite, USB, SMS and the internet are unavailable.
- A connected modem does not mean a detector is online.
- An old NORMAL reading is never represented as current safety.
- A later NORMAL SMS never automatically resolves a CRITICAL incident.
- Acknowledgement records operator awareness; it is not a hazard clearance.
- Raw ADC values are displayed as raw readings, not calibrated gas concentrations.
- No remote valve or safety-actuation command is implemented.
- Do not test with uncontrolled gas, naked flames, or unapproved equipment.

## Technology

- C# / .NET 10 (`net10.0` and `net10.0-windows`)
- WPF with MVVM via `CommunityToolkit.Mvvm`
- Entity Framework Core 10 + SQLite
- `System.IO.Ports` for the physical modem
- xUnit for unit and integration tests
- Dependency injection and structured file logging

## Solution structure

```
GasGuard.sln
src/
  GasGuard.Domain          Entities, enums, incident transitions, device-state rules
  GasGuard.Contracts       SMS protocol models, modem models and shared contracts
  GasGuard.Application     Use cases, validation, parser, ingestion, coordinator, interfaces
  GasGuard.Infrastructure  EF Core/SQLite, repositories, modem adapters, reports, backups
  GasGuard.Desktop         WPF shell, all views/ViewModels, dialogs, local logging
tests/
  GasGuard.UnitTests           Parser, phone, domain, AT queue, simulator, physical adapter
  GasGuard.IntegrationTests    SQLite, transactions, ingestion, coordinator, backup, reports
docs/
  ARCHITECTURE.md, DATABASE.md, SMS_PROTOCOL.md, HARDWARE_INTEGRATION.md,
  USER_MANUAL.md, TEST_REPORT.md, DEPLOYMENT.md, DEMO_SCRIPT.md,
  PRODUCTION_READINESS_AUDIT.md, GSM_HARDWARE_VALIDATION.md,
  SMS_SECURITY_AND_PROTOCOL.md, TEST_RESULTS.md, RELEASE_AND_RECOVERY.md
```

## Build and run

Requirements:

- Windows 10 or Windows 11
- .NET SDK 10.0.4xx (verified with 10.0.403)
- Optional: a GSM modem and SIM for physical testing

```powershell
dotnet restore GasGuard.sln
dotnet build GasGuard.sln -c Debug
dotnet test GasGuard.sln -c Debug
dotnet run --project src/GasGuard.Desktop/GasGuard.Desktop.csproj
```

Or use `run.cmd` / `run.ps1` from the solution root.

## Release packages

```powershell
# Framework-dependent package (target PC needs .NET 10 Desktop Runtime)
.\scripts\package.ps1

# Self-contained package (target PC does not need a separate .NET runtime)
.\scripts\package.ps1 -SelfContained

# Source archive
.\scripts\source-archive.ps1
```

The final self-contained package is `artifacts\GasGuard-Desktop-win-x64-selfcontained.zip`. The
framework-dependent package remains available for environments that already have the .NET 10 Desktop
Runtime. The source archive is `artifacts\GasGuard-source.zip`.

On first start the application:

1. Creates `%LOCALAPPDATA%\GasGuardDesktop\gasguard.db`.
2. Applies the EF Core initial migration (with a pre-migration backup when an existing database
   needs an upgrade).
3. Seeds safe singleton settings (simulated modem, auto-connect on, no automatic contact SMS).
4. Starts the SMS coordinator and connects the **simulated modem** by default.
5. Shows the DEMO banner and an empty dashboard. No detector or incident data is fabricated.

## First demonstration

1. Open **Dashboard** and click **Load demo data** (or use **GSM Modem -> Load demo detectors &
   contacts**). This creates two clearly labelled DEMO detectors and two DEMO contacts.
2. Open **GSM Modem**. The simulator is connected. Under *Simulator controls* choose a detector,
   `GAS_CRITICAL`, raw reading `850`, then **Inject demo message**.
3. The in-app alert banner appears. Open **Live Alerts**, inspect the original SMS, acknowledge it,
   enter resolution notes, and resolve it.
4. Open **Incident History** to see the preserved message, timestamps and audit trail.
5. Open **Contacts**, select the demo safety officer, confirm and send a test SMS. The outgoing log
   says submitted to modem; it never claims delivery.
6. Open **Settings** and create a verified backup; optionally validate/restore it.

A complete script is in [docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md).

## Database and logs

| Data | Default location |
|---|---|
| Database | `%LOCALAPPDATA%\GasGuardDesktop\gasguard.db` |
| Backups | `%LOCALAPPDATA%\GasGuardDesktop\backups` |
| Diagnostic logs | `%LOCALAPPDATA%\GasGuardDesktop\logs` |
| Unprocessed SMS journal | `%LOCALAPPDATA%\GasGuardDesktop\inbox` |

Timestamps are stored in UTC and displayed in `Africa/Lagos` (UTC+01:00). The app is single-user and
local; no cloud service is used.

## Physical modem

The physical integration uses a serialized AT-command pipeline with text-mode SMS
(`AT+CMGF=1`), new-message indications, unread-message polling, and deletion only after successful
database persistence. The exact implementation, supported command sequence, SIM preparation,
network checks, troubleshooting and **explicitly unverified** hardware status are described in
[docs/HARDWARE_INTEGRATION.md](docs/HARDWARE_INTEGRATION.md).

The development machine had no physical GSM modem attached. There are no claimed hardware test
results. Use `docs/HARDWARE_INTEGRATION.md` as a validation checklist, not as a compatibility
certificate.

## SMS protocol

The documented v1 protocol expects messages such as:

```text
GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042
GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=120|unit=raw|seq=1043
```

See [docs/SMS_PROTOCOL.md](docs/SMS_PROTOCOL.md) for fields, validation, sender checking,
duplicate/out-of-order handling and the SMS authentication limitations. SMS never carries executable
commands in this implementation.

## Tests

```powershell
dotnet test tests/GasGuard.UnitTests/GasGuard.UnitTests.csproj
dotnet test tests/GasGuard.IntegrationTests/GasGuard.IntegrationTests.csproj
```

At the time of writing: **101/101 unit tests and 46/46 integration tests pass**. The suite does not
require a physical modem. Hardware-dependent checks are listed separately in the test report and
have not been performed. The canonical post-hardening evidence is in [docs/TEST_RESULTS.md](docs/TEST_RESULTS.md).

## Known limitations

- No physical modem or Nigerian network has been validated in this environment.
- SMS text mode is assumed; PDU/UCS2, concatenated/multipart SMS and delivery reports are not
  implemented. Message bodies are expected to be single-line printable ASCII.
- The SMS sender number is validated against the detector registry, but SMS sender identity is not
  cryptographically authenticated and can be spoofed. Sensitive SMS commands are deliberately not
  accepted. See the protocol document.
- Acknowledgement/resolution are local operator records; there is no remote command back to the
  detector.
- Automatic contact notifications require an enabled setting, send through the same modem, use at
  most three attempts and only ever claim modem submission.
- Restore replaces the live database, pauses monitoring, creates a safety backup and requires an
  application restart. Only one app instance may run.
- Simulated messages are accepted only for DEMO detectors; a rejected simulated message cannot change
  a production detector.
- Raw readings are uncalibrated device values.
- The application has no in-app login or role model; Windows user identity is recorded in audit
  entries. Deployment in a shared environment should use Windows account controls and an approved
  access policy.
- SMS sender allow-listing is not cryptographic authentication. A documented v2 HMAC design is
  proposed in the security document but is not implemented because the detector hardware and key
  provisioning mechanism were unavailable.
- Duplicate/replay suppression is heuristic and time-bounded. A replay outside the duplicate window
  can create a new incident; this is a deliberate safety-first trade-off and does not stop a
  determined SMS spoofer.
- Direct modem notifications are journaled before database processing, but a crash in the very small
  interval between receiving a direct +CMT and writing the journal remains possible. Stored modem
  messages are retained until persistence succeeds.
- PDU modes, UCS-2/Unicode SMS, concatenated/multipart reassembly and delivery receipts are not
  implemented. Non-ASCII protocol content is rejected and raw evidence may be text-decoded
  best-effort by the modem transport.
- The local database, SMS journal and backups are not application-encrypted; they rely on the
  Windows user-profile ACL. Use BitLocker/device encryption and institutional access controls.
- No signed installer, clean-profile installation test or physical hardware validation was
  performed.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Database and schema evolution](docs/DATABASE.md)
- [SMS protocol](docs/SMS_PROTOCOL.md)
- [Hardware integration and validation checklist](docs/HARDWARE_INTEGRATION.md)
- [User manual](docs/USER_MANUAL.md)
- [Production readiness audit](docs/PRODUCTION_READINESS_AUDIT.md)
- [Hardware validation checklist](docs/GSM_HARDWARE_VALIDATION.md)
- [SMS security and protocol](docs/SMS_SECURITY_AND_PROTOCOL.md)
- [Test results](docs/TEST_RESULTS.md)
- [Release and recovery](docs/RELEASE_AND_RECOVERY.md)
- [UI text audit](docs/UI_TEXT_AUDIT.md)
- [Test report (original)](docs/TEST_REPORT.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Academic demonstration script](docs/DEMO_SCRIPT.md)

## Licence and project use

This is an academic project deliverable. No warranty is provided. Do not use it as a certified
life-safety system.

