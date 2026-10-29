# Test Results

## Environment

| Item | Value |
|---|---|
| Date | 2026-10-28 |
| OS | Windows 11 Pro, version 10.0.26100, x64 |
| SDK | .NET SDK 10.0.403 |
| Host/runtime | 10.0.14 |
| Test framework | xUnit 2.9.3; xUnit VSTest adapter 3.1.4 |
| Database tests | SQLite through EF Core 10.0.14, unique disposable files |
| Physical GSM hardware | **Not available; no hardware test performed** |

Repository note: the workspace is not a Git repository. Baseline and final results below were
observed directly from the solution; no test was modified merely to make a build pass.

## Commands executed

```powershell
dotnet --info
dotnet restore GasGuard.sln
dotnet build GasGuard.sln -c Release
dotnet test GasGuard.sln -c Release --no-build --logger "console;verbosity=minimal"
dotnet publish src/GasGuard.Desktop/GasGuard.Desktop.csproj -c Release -r win-x64 `
  --self-contained false -o artifacts/publish/win-x64
```

At the time of this run, the final canonical results were:

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| `GasGuard.UnitTests` | **101** | 0 | 0 |
| `GasGuard.IntegrationTests` | **46** | 0 | 0 |
| **Total** | **147** | **0** | **0** |

Release build: **succeeded, 0 warnings, 0 errors**.

The initial baseline before hardening was 89 unit + 32 integration = 121 passing tests. The final count
reflects added regression tests for the defects listed in
[PRODUCTION_READINESS_AUDIT.md](PRODUCTION_READINESS_AUDIT.md).

## Unit coverage highlights

- Protocol valid/status/critical/warning cases; embedded controls and non-ASCII rejection; unknown
  fields, malformed timestamps, duplicate fields, invalid numeric fields, device/event/level
  validation, sequence parsing, size limits.
- Nigerian number normalization, invalid lengths/prefixes, display formatting and fallback matching.
- Device freshness states, sequence-aware reset behavior policy tests, incident ack/resolve
  transitions.
- CSV escaping and spreadsheet-formula neutralization.
- Modem response parsing (`CPIN`, `CSQ`, `CREG`, `COPS`, identity) and status descriptions.
- AT pipeline: serialization, prompt/payload/Ctrl-Z, command reference, errors, timeout/desync,
  cancellation, unsolicited CMT/CMTI, transport fault, echo handling.
- Simulated modem: connection, retention, delivery, deletion, outgoing success/rejection/timeout,
  disconnection.
- Physical adapter with a scripted serial transport: initialization, SIM PIN state, network-not-
  registered send rejection, reading stored SMS, sending, deletion failure, status-refresh fault.

## Integration coverage highlights

- Fresh migration, singleton settings, foreign-key enforcement.
- Valid critical SMS → one message + incident + detector state + audit, transactionally.
- Duplicate raw body, duplicate sequence, and same modem storage reference behavior.
- Lower-sequence critical alert preservation.
- Repeated critical alert after resolution or after the duplicate window creates a new incident.
- Status sequence-reset handling with trusted device time; out-of-order status does not regress state.
- Invalid, unknown-device, unauthorized-sender and simulated-for-production messages retained but not
  trusted.
- Audit failure rolls back message/incident and device operations; audit failure on acknowledgement
  leaves the incident open.
- Database write failure rollback; coordinator end-to-end simulated workflow; retained stored SMS
  recovery; pause/resume; ingress drain; startup journal recovery.
- Backup verification, round-trip restore, fresh-install restore, live-source rejection, no WAL/SHM
  leftovers, corrupt backup rejection, safety backup.
- Retention preserves incidents, linked SMS and incident audit evidence while removing old
  non-incident records.
- Device/contact validation and deactivation; settings validation; dashboard staleness; test-SMS
  confirmation and outgoing logs; automatic notification bounds; report summaries/trends and CSV BOM;
  evidence query including invalid messages; simultaneous distinct alerts.

## Manual verification recorded

These are observations, not automated tests and not hardware validation:

- The Release and Debug desktop applications start without a physical modem, create a fresh local
  database, connect the simulated modem, and display `MODEM READY` after startup.
- A second desktop process is refused with "GasGuard already running"; the first process remains
  running. This was tested with the final build containing the single-instance mutex.
- The prior manual demo path was exercised: load demo detectors/contacts, inject a simulated critical
  SMS through the simulator, observe the alert banner, acknowledge, resolve with notes, inspect history
  and audit, and use the confirmed test SMS workflow.
- Backup creation from Settings produced a validated backup with no `.tmp-wal`/`.tmp-shm` residue.
- Runtime restore was exercised after the coordinator quiesce fix. The restore completed, displayed a
  completion dialog with the safety-backup path, and the application closed after the dialog was
  dismissed. The first drain implementation failed safely with `NotSupportedException`; it was fixed
  and the regression test was added.
- The WPF pages were navigated during the audit; startup and core busy/error/empty states render.

## Explicitly not tested

| Item | Reason/status |
|---|---|
| Physical USB modem AT behavior | No modem |
| Real SIM PIN/CPIN behavior | No SIM |
| Nigerian network registration/SMS | No SIM/network |
| Real SMS reception and deletion | No modem/SIM |
| Real outgoing SMS/delivery | Not possible; delivery receipts not implemented |
| Detector MCU message generation | No detector hardware |
| Local audible/visual alarm independent operation | No detector hardware; checklist only |
| Sensor calibration / gas exposure | Not performed; uncontrolled or unapproved gas testing is prohibited |
| USB removal/replug with actual hardware | Simulator/scripted transport only |
| Clean non-administrator Windows profile installation | Not performed |
| Signed installer / Windows Store packaging | Not produced |
| Multipart/UCS-2/PDU SMS | Not implemented/validated |
| Full WPF UI automation | Not implemented; manual smoke checks only |

## Test-to-defect traceability

| Defect | Regression evidence |
|---|---|
| Sequence-reset critical suppression | `LowerSequenceCriticalAlertIsNotSilentlySuppressed` |
| Audit outside safety transaction | `AuditWriteFailureRollsBackMessageIncidentAndAudit`, `AuditWriteFailureOnAcknowledgeLeavesIncidentOpen`, `AuditFailureRollsBackDeviceCreation` |
| Modem status overwritten after fault | `RequiredStatusCommandFailureMarksModemFaulted`, delete failure test |
| Restore quiesce/close race | `CoordinatorPauseQuiescesIngestionUntilResume`, `IngressDrainWaitReturnsAfterQueuedSmsIsProcessed`; manual restore |
| CSV formula injection | `CsvEscapingNeutralizesSpreadsheetFormulaStarts` |
| Live DB self-restore | `LiveDatabaseCannotBeRestoredOntoItself` |
| Content-only permanent dedup | `RepeatedCriticalAfterResolutionWithinWindowCreatesNewIncident`, `RepeatedCriticalAfterDuplicateWindowCreatesNewIncident` |
| Simulation impacting production device | `SimulatedMessageForProductionDetectorIsRejectedWithoutChangingState` |
| Direct-CMT crash durability | `JournaledSmsIsRecoveredOnCoordinatorStart`, retained-journal assertions in failed persistence test |
| Single instance | Manual second-process test |
| Control/non-ASCII input | Parser control and non-ASCII tests |
| Retention deleting incident audit | Retention test asserting `OldIncidentEvidence` survives |
| Privacy masking | Code review; no test assertions on rendered log content |

## Reproducibility notes

- Integration tests use disposable SQLite files and do not touch `%LOCALAPPDATA%` unless the test
  target root is constructed there; all test hosts use unique temporary roots.
- Automated tests do not require a modem, SIM, cloud service, internet database, or credentials.
- The simulator and scripted transport are software fakes. They cannot support a claim of physical
  modem, radio, network or gas-safety validation.

## Conclusion

All automated tests pass with a clean Release build. The software is suitable for controlled
laboratory validation. It is not certified, safety-rated, or physically hardware-validated.

## Final package verification

After the last source change, the Release build completed with 0 warnings and 0 errors and the full
suite passed: **101 unit + 46 integration = 147 tests**, 0 failed, 0 skipped.

Final packaged outputs:

| Output | Status |
|---|---|
| `artifacts\GasGuard-Desktop-win-x64.zip` | Framework-dependent package |
| `artifacts\GasGuard-Desktop-win-x64-selfcontained.zip` | Self-contained package; no separate .NET runtime required |
| `artifacts\GasGuard-source.zip` | Source archive excluding `bin`, `obj` and generated output |

Package hashes are recorded in `artifacts\SHA256SUMS.txt`. Physical modem, SIM, Nigerian network,
detector firmware/local alarm and clean-profile installations remain unvalidated as documented in the
hardware validation checklist.