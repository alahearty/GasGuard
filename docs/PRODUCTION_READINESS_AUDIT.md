# Production Readiness Audit

## Scope and evidence basis

Repository: the audited GasGuard solution in the current workspace.

This audit began by independently inspecting the actual repository on 2026-10-28. The directory is
not a Git repository (`git status` reports "not a git repository"), so no commit history or unrelated
user changes could be distinguished through Git. No files outside `GasGuard/` were modified.

Baseline observed before changes:

| Item | Baseline |
|---|---|
| SDK | .NET SDK 10.0.403; host/runtime 10.0.14 |
| Solution | 7 projects (5 source, 2 test) |
| Release build | Succeeded, 0 warnings, 0 errors |
| Unit tests | 89 passed |
| Integration tests | 32 passed |
| Published artifact | Existing `artifacts\publish\win-x64` and ZIP present |
| Physical hardware | No modem, SIM, detector, or GSM network available |

Final verified state after remediation:

| Item | Final |
|---|---|
| Release build | Succeeded, 0 warnings, 0 errors |
| Unit tests | 101 passed |
| Integration tests | 46 passed |
| Total automated tests | 147 passed |
| Fixed material findings | 16 listed below (10 high, 6 medium) |
| New regression tests | Added for every fixed safety/transaction defect listed |
| Physical hardware tests | **None performed** |
| Certification / safety rating | **None claimed** |

Canonical test evidence is in [TEST_RESULTS.md](TEST_RESULTS.md). This audit records engineering
observations, not a certification.

## Method

1. Inspected solution/project files, package references, migrations, domain/application services,
   SQLite/EF Core code, modem adapters, AT pipeline, WPF ViewModels/views, tests, and published ZIP.
2. Ran `dotnet --info`, `dotnet restore`, Release build, and all tests.
3. Reviewed failure and recovery paths rather than only happy-path UI behavior.
4. Added focused regression tests before or alongside each material fix.
5. Rebuilt and reran the complete test suite.
6. Regenerated the Release package only after tests passed; a copy of the pre-hardening package was
   preserved separately before replacing the release output.
7. Verified first launch, single-instance behavior, demo workflow, test-SMS path, backup, and
   runtime restore manually on the development machine.

## Findings and remediation

### H-1 — Sequence reset could silently suppress a legitimate critical alert

**Severity:** High  
**Component:** `SmsIngestionService`, deduplication/sequence logic  
**Root cause:** Any incoming alert with a sequence number lower than or equal to the detector's last
sequence was classified as out-of-order and returned without creating an incident. A detector reboot
or reflashed firmware resets its sequence counter; a real critical alert after a reset could therefore
be discarded as "old".  
**Evidence:** Code path previously checked sequence before event/incident dedup; no regression test
covered lower-sequence alerts.  
**Fix:** Critical/warning alerts are no longer suppressed solely by sequence ordering. Alert
deduplication is based on a bounded, recent-incident query (device, severity, sequence/fingerprint,
simulation mode) plus a unique time-bucketed dedup key. A lower sequence alert creates a new incident
and records the possible-reset condition. Status messages retain conservative ordering; a status
sequence reset is accepted only when a trusted device timestamp advances beyond the last successful
report.  
**Regression:** `LowerSequenceCriticalAlertIsNotSilentlySuppressed`; existing out-of-order status
test remains.

### H-2 — Audit entries were outside the safety transaction

**Severity:** High  
**Component:** Ingestion, incident acknowledgement/resolution, device/contact/settings writes  
**Root cause:** Message + incident commits succeeded first; audit writes used a separate context and
were best-effort. A crash or audit-table failure could leave a committed incident without its opening
audit entry.  
**Evidence:** Separate `IAuditLogService.RecordAsync` writes after `CommitAsync`; no audit-failure
transaction test.  
**Fix:** Added `IAuditLogRepository` to the unit of work. Incident/message/audit and
acknowledgement/resolution/audit writes now share a SQLite transaction; device, contact, settings and
demo-data writes also add audit rows before their commit. Retention cleanup is transactional.  
**Regression:** `AuditWriteFailureRollsBackMessageIncidentAndAudit`,
`AuditWriteFailureOnAcknowledgeLeavesIncidentOpen`, `AuditFailureRollsBackDeviceCreation`.

### H-3 — Modem status could recover to "Connected" after a failed AT refresh

**Severity:** High  
**Component:** `PhysicalGsmModemAdapter`, `AtCommandQueue`  
**Root cause:** `RefreshStatusAsync` continued after a required command failed or timed out and then
overwrote the faulted status with a newly constructed Connected status. Queue desynchronization could
also leave the UI claiming the modem was connected.  
**Evidence:** Reconnect/fault paths lacked status-refresh regression tests.  
**Fix:** Required command failures now mark the modem faulted and return without writing a healthy
status. Queue desynchronization detected during send, read, delete, refresh or connection is surfaced
as a modem fault; reconnect is required.  
**Regression:** `RequiredStatusCommandFailureMarksModemFaulted`,
`DeleteFailureDoesNotReportSuccessOrFabricateConnectionLoss`, existing timeout/error tests.

### H-4 — Runtime restore was not quiesced and the window could close mid-operation

**Severity:** High  
**Component:** Settings/restore, SMS coordinator, WPF window lifecycle  
**Root cause:** Restore copied over the live SQLite database while coordinator processing and UI
actions could still write. Restore was previously offered in the running UI without pausing ingestion,
and the window could be closed while the file replacement was in progress.  
**Evidence:** Restore flow directly called `IBackupService` without pausing the coordinator; no
operation guard existed.  
**Fix:** Restore now disconnects the modem, drains the ingress counter, pauses message processing and
reconciliation through coordinator gates, and displays a blocking maintenance overlay. Closing is
refused while the operation guard is active. On failure the coordinator and modem are resumed; on
success the app closes before normal use resumes. A 30-second quiesce timeout prevents an indefinite
hang.  
**Regression/verification:** `CoordinatorPauseQuiescesIngestionUntilResume`,
`IngressDrainWaitReturnsAfterQueuedSmsIsProcessed`; manual restore closed the application only after
the completion dialog.

**Additional defect found during the UI test:** the first drain implementation called
`ChannelReader<T>.Count`, which throws `NotSupportedException` for the channel implementation. The
restore failed safely and the guard released; the drain now uses an explicit processed/pending counter.
This was fixed and the drain regression test added.

### H-5 — CSV formula injection in exported reports

**Severity:** High for data handling, medium for this local deployment  
**Component:** `CsvBuilder`/all CSV exports  
**Root cause:** Cell escaping only handled commas/quotes/newlines. A value beginning with `=`, `+`,
`-`, `@` (or leading whitespace then one) could be interpreted as a spreadsheet formula when opened in
Excel. Contact/device/notes/snapshot fields are operator or SMS influenced.  
**Fix:** CSV values whose first non-space/tab character is a spreadsheet formula trigger are prefixed
with a single quote before normal quoting.  
**Regression:** `CsvEscapingNeutralizesSpreadsheetFormulaStarts` (including `+234…` phone numbers).

### H-6 — Live database could be selected as its own restore source

**Severity:** High  
**Component:** `BackupService`  
**Root cause:** Restore validation accepted any readable SQLite database, including the live file;
copying a database onto itself is unsafe.  
**Fix:** Restore rejects the live database path before validation. The UI performs the same check
before confirmation.  
**Regression:** `LiveDatabaseCannotBeRestoredOntoItself`.

### H-7 — Permanent content-only deduplication could hide legitimate repeats

**Severity:** High  
**Component:** `SmsIngestionService` dedup policy  
**Root cause:** A fingerprint match anywhere in history suppressed a new alert, even if the earlier
incident was resolved or the device legitimately repeated the same reading later. The unique incident
key could also be permanently consumed by a sequence value reused after reset.  
**Fix:** Dedup is now bounded to a 15-minute window and considers unresolved vs resolved recent
incidents; a new alert after resolution or after the window creates a new incident. Alert incidents
use a time-bucketed dedup key with a unique occurrence suffix when a resolved incident is followed by
a new event. Exact repeats within the window are still retained and marked `Duplicate`. This is a
safety-first false-positive trade-off documented in the security document.  
**Regression:** `RepeatedCriticalAfterResolutionWithinWindowCreatesNewIncident`,
`RepeatedCriticalAfterDuplicateWindowCreatesNewIncident`; existing duplicate tests remain.

### H-8 — Simulated messages could influence a production detector

**Severity:** High  
**Component:** Ingestion + simulator UI/demo service  
**Root cause:** The simulator accepted injection for any registered detector code; simulated status or
alerts would update that device's last state and could create/affect incidents. This could leave a
production record showing simulated state when switching back to physical hardware.  
**Fix:** Simulated messages are accepted only for detectors created by the demo scenario
(`IsDemo = true`). Simulator UI lists only demo detectors; the demo service rejects non-demo devices.
A rejected simulated message is retained as `SimulationRejected` evidence and audited, with no device
state change. Dedup queries/keys also distinguish simulated and real messages.  
**Regression:** `SimulatedMessageForProductionDetectorIsRejectedWithoutChangingState`; updated demo
tests.

### H-9 — Crash between direct modem delivery and database persistence

**Severity:** High for durability  
**Component:** SMS coordinator / incoming message pipeline  
**Root cause:** Stored modem SMS was intentionally retained until persistence, but a direct `+CMT`
notification had no disk journal; a process crash before the database commit could lose it.  
**Fix:** Added an atomic incoming-SMS journal under `%LOCALAPPDATA%\GasGuardDesktop\inbox`. Messages
are journaled before processing; entries are replayed at coordinator start and removed only after
successful persistence. Journal writes are deterministic by modem source reference where present.
Persistence remains idempotent through source-reference/fingerprint checks.  
**Regression:** `JournaledSmsIsRecoveredOnCoordinatorStart`; failed-persistence test asserts the
journal is retained and later removed after successful reconciliation; `WaitForIngressDrain`.
**Residual:** A very small receive-to-journal-write window still exists for direct `+CMT`; physical
modem storage retention is the hardware-level defence. This is documented, not claimed eliminated.

### H-10 — Multiple application instances could race on one database/modem

**Severity:** High for data integrity  
**Component:** Desktop application startup  
**Root cause:** No single-instance lock existed. Two instances could use the same LocalAppData database
and COM port, and write journal files concurrently.  
**Fix:** Added a named local mutex. A second instance displays an explicit message and exits before
database or modem initialization. The mutex is released after provider shutdown.  
**Verification:** Manual second-instance launch showed "GasGuard already running"; the first instance
remained active and the second exited.

### M-1 — Control characters and non-ASCII content were not strictly rejected

**Severity:** Medium  
**Component:** `SmsProtocolParser`  
**Root cause:** Unknown fields were ignored; control/Unicode content could exist without influencing
trusted fields. This weakened input hygiene and made encoding behavior unclear.  
**Fix:** Parser rejects embedded control characters and any non-ASCII content with explicit error
codes. Physical transport logs a warning on non-ASCII bytes because text-mode decoding may be
lossy.  
**Regression:** Control-character and non-ASCII tests.

### M-2 — Device timestamps older than the last successful report were treated as trusted

**Severity:** Medium  
**Component:** Ingestion occurrence-time logic  
**Root cause:** Trust checked only age/future skew, not ordering relative to the last successful
message. A delayed or clock-regressed message could supply an old occurrence time.  
**Fix:** A device timestamp must also be no older than the last successful communication; otherwise
receipt time is used and the fallback source is recorded.  
**Verification:** Existing timestamp tests plus sequence-reset status test.

### M-3 — Retention deleted incident audit evidence

**Severity:** Medium  
**Component:** `RetentionService`  
**Root cause:** Audit retention removed all old audit rows, including incident opening/ack/resolution
history, while incidents themselves are preserved indefinitely.  
**Fix:** Retention removes old non-incident audit rows; incident audit evidence is preserved.  
**Regression:** retention test asserts old incident audit survives while a non-incident audit row is
removed.

### M-4 — Logs exposed full phone numbers

**Severity:** Medium privacy  
**Component:** Ingestion failure logging, simulated outgoing SMS log  
**Root cause:** Some diagnostic log messages used the full sender/recipient number.  
**Fix:** Logs mask phone numbers; audit writes for test SMS and rejected incoming messages also mask
numbers. Raw operational data remains in the access-controlled database/communication logs where
required.  
**Verification:** Manual code review; no log captures are part of the repository.

### M-5 — UI/operation success and maintenance semantics

**Severity:** Medium  
**Component:** WPF settings/restore and operation lifecycle  
**Fix:** Maintenance overlay, operation guard, 30-second quiesce timeout, explicit shutdown after
restore, and audit-committed status messages. Existing pages already use async commands, loading
states and confirmations.  
**Verification:** Manual restore workflow; full UI smoke tests are not automated.

### M-6 — Ingress drain used an unsupported channel API

**Severity:** Medium (runtime failure in a high-risk path)  
**Component:** `SmsProcessingCoordinator.WaitForIngressDrainAsync`  
**Root cause:** `UnboundedChannelOptions` channel reader does not support `Count` in this runtime. The
restore path failed safely with an error but could not proceed.  
**Fix:** Added an explicit `_ingressPending` counter incremented before enqueue and decremented after
processing; drain waits on that volatile counter.  
**Regression:** `IngressDrainWaitReturnsAfterQueuedSmsIsProcessed`; manual restore then succeeded.

### L-1 — Legacy protocol has no cryptographic authentication or anti-replay proof

**Severity:** High residual security risk (unresolved by design)  
**Component:** SMS protocol v1 / sender allow-list  
**Details:** Sender-number matching is a filter only. A spoofed SMS from the allowed number, or a
replay outside the dedup window, can create a false incident or state update. No HMAC or device
signature exists because the detector hardware and key provisioning are unavailable. No sensitive
SMS command is accepted. A v2 authenticated protocol is specified as future work in
[SMS_SECURITY_AND_PROTOCOL.md](SMS_SECURITY_AND_PROTOCOL.md); it is not implemented or claimed.  
**Recommended action:** Implement device-side HMAC-SHA-256 over canonical fields with a provisioned
per-device key and sequence/nonce, constant-time verification, key rotation, and policy to reject v1
where authentication is required.

### L-2 — Physical hardware and local alarm are unverified

**Severity:** High if presented as operational  
**Details:** No modem, SIM, Nigerian network, detector, or local alarm was available. Software tests
with fakes/simulator do not prove physical compatibility or safety function. The local detector alarm
must operate independently; a hardware checklist is provided but not performed.  
**Action:** Complete [GSM_HARDWARE_VALIDATION.md](GSM_HARDWARE_VALIDATION.md) and a documented local
alarm test with qualified personnel before any pilot.

### L-3 — No in-app authentication/role model, encryption at rest, or signed installer

**Severity:** Medium operational security  
**Details:** Audit records the Windows user, but any user of the desktop account can acknowledge,
resolve, configure, export, or restore. The database, journal and backups are not application-
encrypted; they rely on the per-user Windows profile ACL. No signed installer or clean-profile
installation test was performed.  
**Action:** Use dedicated Windows accounts, NTFS/BitLocker controls, code signing, and a packaging
process; add application-level operator roles if shared access is required.

### L-4 — Encoding/multipart and delivery facts remain unsupported

**Severity:** Medium integration constraint  
**Details:** v1 is ASCII-only; UCS-2/PDU, multipart reassembly and SMS delivery reports are not
implemented. This fails safely (invalid/accepted-as-evidence handling), but it is not a general SMS
gateway.  
**Action:** Validate that the intended modem/network delivers single-line GSM-7/ASCII messages; add a
versioned encoding/multipart layer only after hardware validation and strict reassembly design.

### L-5 — Residual durability windows and maintenance assumptions

**Severity:** Low/Medium  
**Details:** The journal reduces crash loss but cannot eliminate the direct-receive-to-journal window.
Restore is intentionally destructive to the current database state (a pre-restore safety backup is
created). The UI pauses monitoring; a forced OS power loss or Task Manager kill can still interrupt
operations. Audit is best-effort for external actions (report file writes, backup files, notifications)
where a database/audit write cannot be rolled back with the external side effect.  
**Action:** Operate restore during maintenance windows, use a UPS, verify backups, and treat incident
history/backups as evidence rather than a live redundant control system.

## Defects checked and found acceptable

- SQLite foreign keys enabled and tested.
- UTC timestamp conversion tested; `Africa/Lagos` display conversion tested.
- Transactional message + incident creation; write-failure rollback tested.
- Duplicate source/modem reference handling and reconciliation retention tested.
- Acknowledgement cannot resolve; resolution requires notes; NORMAL cannot auto-resolve.
- Backup validation includes integrity, required tables and foreign-key checks; restore validates and
  creates a safety backup; fresh-install restore tested.
- AT command serialization, prompt/payload, timeout, cancellation, echo suppression, unsolicited CMT,
  CMTI, transport fault, and outgoing failure handling tested.
- SIM PIN-required and network-not-registered states do not claim SMS readiness.
- CSV formula injection addressed.
- Single-instance and maintenance guard manually verified.
- App starts with no physical modem; simulator workflow works; app closes cleanly in manual tests.

## Security assessment summary

| Topic | Assessment |
|---|---|
| Sender allow-list | Implemented, not cryptographic authentication |
| Spoofing | Residual; no HMAC/signature; no sensitive SMS commands accepted |
| Replay | Time-window dedup and sequence handling; bounded, not cryptographic |
| Secrets | No SIM PIN/API secrets stored; no production secrets in source/package |
| Data at rest | SQLite/journal/backups unencrypted; per-user Windows ACL; recommend BitLocker |
| Access control | Windows account identity for audit; no in-app roles/login |
| CSV export | Formula injection neutralised; full numbers may be present for operational use |
| Logging privacy | SMS bodies not logged; phone numbers masked in file logs/audit where practical |
| Audit integrity | Core safety actions transactional; external-effect audit remains best-effort |

## Hardware assessment summary

| Layer | Software verified | Simulator/fake verified | Physical hardware verified |
|---|---|---|---|
| Serial open/dispose | Code review + fake transport | Yes | No |
| AT identity/SIM/network/signal | Parser tests + scripted transport | Partially | No |
| SMS read/delete/send | Scripted transport; read/delete logic | Yes | No |
| Modem fault/reconnect | Fault tests; simulator disconnection | Yes | No |
| Nigerian SIM/network | Not possible here | No | No |
| Detector/local alarm | Not hardware controlled | No | No |

## Final assessment

**Status: Ready for further laboratory validation — not ready for an operational pilot or any
safety-critical deployment.**

The software has been materially hardened and independently re-tested at 147 passing automated tests,
with a clean Release build and a verified simulated workflow. The remaining blockers are evidence
gaps, not software test failures: physical GSM/SIM/Nigerian-network compatibility, detector/local
alarm validation, cryptographic SMS authentication (requires detector/key-provisioning work), and
institutional access/encryption/packaging controls. None of these may be represented as completed.
