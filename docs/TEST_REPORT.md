# Test Report (baseline and historical verification)

> **Canonical post-hardening evidence is in [TEST_RESULTS.md](TEST_RESULTS.md).**
>
> This file preserves the original baseline/historical test record. It is not the current test count;
> the production-readiness remediation added regression tests and the final suite is **101 unit + 46
> integration = 147 tests passing** with a clean Release build.

## Original baseline

| Suite | Result |
|---|---|
| `GasGuard.UnitTests` | **89 passed, 0 failed, 0 skipped** |
| `GasGuard.IntegrationTests` | **32 passed, 0 failed, 0 skipped** |
| Total automated tests | **121 passed** |
| Physical modem / SIM / GSM network tests | **NOT PERFORMED** — no hardware available |

Environment:

- Windows 11 Pro (10.0.26100)
- .NET SDK 10.0.403
- EF Core / Microsoft.Data.Sqlite 10.0.14
- xUnit 2.9.3 and xUnit VSTest Adapter 3.1.4
- Baseline execution date: 2026-10-28

The suite requires no physical GSM modem.

## Original coverage summary

The baseline tested the SMS parser, Nigerian number handling, domain/incident rules, UTC/Lagos time,
modem response parsing, fingerprinting, CSV quoting, the AT command pipeline with a scripted/fake
transport, the simulated modem, the physical adapter with a scripted serial transport, and SQLite
integration paths: migrations, foreign keys, ingestion/rollback, duplicate/replay basics, evidence
retention, restart recovery, coordinator persistence/deletion/reconciliation, backup/restore,
retention, device/contact/settings CRUD, staleness, test SMS, notifications, reports and audit.

The hardening audit then added transaction/audit-failure, sequence-reset, repeated-alert,
simulation-isolation, CSV formula-injection, journal recovery, pause/drain, retention-evidence and
restore-safety tests. See [TEST_RESULTS.md](TEST_RESULTS.md) for the exact final traceability.

## Historical manual UI verification

The WPF application was launched during the original project verification and the following were
observed manually:

- The app started, applied migrations, connected the simulated modem and showed the DEMO banner.
- All nine navigation workspaces rendered and accepted input.
- Demo data loaded with two labelled detectors and two contacts.
- A simulated critical SMS produced an alert banner, Live Alert, device state/sequence update and
  stored-message deletion.
- Acknowledgement, resolution notes and resolution completed; history/audit entries appeared.
- Header unresolved count updated when the incident was resolved.
- A contact test SMS used the simulator and logged modem submission with no delivery claim.
- A detector add dialog opened with expected controls/validation.
- Recent SMS evidence was present on the GSM Modem workspace.
- Backup creation produced a verified backup with no WAL/SHM sidecars.
- The app closed promptly and cleanly.

Later production-readiness manual checks (on the hardened build) additionally covered single-instance
refusal, maintenance/restore completion, and startup after the drain fix. These remain manual
observations, not automated UI tests or hardware validation.

## Historical defects resolved before or during the baseline

| Defect | Resolution |
|---|---|
| Reconciliation status feedback caused a tight status-to-reconcile loop | Reconcile on transition to connected; integration/manual verification |
| Backup `.db` left SQLite `-wal`/`-shm` sidecars | Checkpoint/finalise, switch temp backup to DELETE journal, remove sidecars |
| Invalid SMS did not show best-effort device code in evidence review | Extract device field only for non-trusted evidence; no reading/incident |
| Header unresolved count remained stale after resolve | Incident-state event; refresh counts |
| Contact test panel hid its send action | Compact layout/rebalanced form |
| Health badge used "ACTIVE" instead of health wording | Dedicated health text/brush converter |

## Defects resolved during the later production-readiness audit

The audit found and fixed 16 material items, including sequence-reset suppression, audit writes outside
safety transactions, modem status overwriting faults, unsafe runtime restore, CSV formula injection,
live-DB self-restore, permanent content deduplication, simulation affecting production devices, the
direct-CMT durability gap, missing single-instance enforcement, and a runtime channel-count exception
in the restore drain. Full evidence is in
[PRODUCTION_READINESS_AUDIT.md](PRODUCTION_READINESS_AUDIT.md).

## Not tested / not possible here

- Physical USB modem, SIM, Nigerian network and real SMS in either direction.
- Detector MCU message generation and independent local alarm.
- Actual gas exposure/calibration and any safety response.
- Clean non-administrator Windows profile / signed installer.
- Multipart/UCS-2/PDU SMS and delivery receipts.
- Full automated UI suite.

## Residual risks (historical, still applicable)

- SMS sender identity is not cryptographically authenticated; spoofed telemetry is possible. No
  sensitive SMS commands are accepted. See
  [SMS_SECURITY_AND_PROTOCOL.md](SMS_SECURITY_AND_PROTOCOL.md).
- Text-mode/ASCII/single-line SMS assumptions must be validated against the actual modem/network.
- Hardware remains unvalidated until the checklist is evidenced.
- The independent local alarm was not tested here.
- The database, journal and backups are not application-encrypted and there is no in-app user role
  model; use Windows/institutional controls.

## Conclusion

Historical baseline: 121 automated tests passed. Final hardened state: **147 automated tests passed**;
physical hardware remains unvalidated. See [TEST_RESULTS.md](TEST_RESULTS.md) for the current report.
