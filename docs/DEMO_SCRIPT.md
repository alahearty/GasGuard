# Academic Demonstration Script

## Demonstration goal

Show that GasGuard Desktop:

1. starts independently of a physical modem;
2. clearly distinguishes simulated data from live safety data;
3. validates and preserves SMS evidence;
4. creates one durable incident per critical alert and handles duplicates safely;
5. requires explicit operator acknowledgement and resolution;
6. survives restart and supports audit, reports, backup and restore.

**Do not use gas or flames for this demonstration.** Everything below uses the built-in simulator.

## Preconditions

- Windows PC with the published app or source checkout and .NET 10 SDK/runtime.
- No physical modem required.
- Use an empty or disposable development database for a clean first-run story. Optional: back up any
  existing data and delete `%LOCALAPPDATA%\GasGuardDesktop\gasguard.db*` **only on a demo machine**.
- Screen resolution at least 1366×768; the app window is maximised by default.

## Demonstration flow

### 1. Start with no physical hardware (1 minute)

Launch `GasGuard.Desktop.exe`.

Point out:

- the app started successfully without a USB modem;
- the **SIMULATED MODE** badge;
- the dashboard's yellow DEMO banner;
- the modem card shows a simulated ready state, not a live detector status;
- “No detector status has ever been received” and the warning that an empty dashboard is not proof
  of safety.

### 2. Load explicitly labelled demo data (1 minute)

On the Dashboard click **Load demo data**. Alternatively use **GSM Modem → Load demo detectors &
contacts**. Only these DEMO detectors can be targeted by the simulator.

Point out:

- two detectors `GAS-001` and `GAS-002`, marked **DEMO**;
- two demo contacts with DEMO notes;
- active detector count becomes 2;
- detector states remain UNKNOWN because no detector telemetry has been received yet.

### 3. Inspect modem/network readiness (1 minute)

Open **GSM Modem**.

Point out:

- mode = Simulated modem (DEMO);
- SIM ready (simulated), registered on a demo network, signal 25/31;
- there is no claim that a detector is online;
- simulator controls and diagnostics;
- the setting to delete modem SMS only after database persistence.

### 4. Inject a critical SMS (2 minutes)

In **Simulator controls → Inject structured demo message**:

- detector: `GAS-001`;
- event: `GAS_CRITICAL`;
- raw reading: `850`;
- click **Inject demo message**.

Point out:

- the red in-app notification banner with **SIMULATED** tag;
- header unresolved count becomes `1 critical`;
- the simulated SMS is immediately stored/reconciled and shown in **Recent SMS evidence**;
- dashboard detector state becomes CRITICAL with raw reading 850 and a current last-report age.

Emphasise: 850 is an illustrative raw device value, **not calibrated ppm** and not a safety limit.

### 5. Inspect the incident and original SMS (1 minute)

Open **Live Alerts**.

Show:

- severity, open status and SIMULATED tag;
- device/location, occurrence and receipt times/time source;
- sender, sequence and parsing status;
- the exact original SMS;
- the statement that a later NORMAL does not clear the alert.

### 6. Acknowledge – not resolve (1 minute)

Enter an acknowledgement note such as `Operator observed the simulated alert.` and click
**Acknowledge incident**; confirm.

Point out:

- status becomes ACKNOWLEDGED but the incident remains in the unresolved list;
- acknowledgement records awareness only;
- **Resolve** now becomes available.

### 7. Resolve explicitly with notes (1 minute)

Enter `Demo area checked; simulated scenario acknowledged and closed.` and click **Resolve incident**;
confirm.

Point out:

- incident moves out of Live Alerts because it is resolved;
- the header unresolved count returns to zero;
- Incident History still contains the full record, original SMS and audit history.

### 8. Demonstrate duplicate safety (2 minutes)

Return to **GSM Modem** and inject the same structured event for `GAS-001` again with the same
reading. Then use **Live Alerts** to show one new incident; optionally inject the exact same custom
raw body twice to show duplicate handling.

Better controlled duplicate demonstration:

- in **Inject custom raw SMS**, use sender `+2348030000001` and the same raw body twice, separated by
  a few seconds;
- open **Recent SMS evidence** and show one processed message plus a retained duplicate;
- show that only one incident was created for the duplicate sequence/body.

### 9. Demonstrate malformed evidence rejection (2 minutes)

Use **Inject custom raw SMS** with sender `+2348030000001` and:

```text
GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=NORMAL|reading=850
```

Inject. Then:

- open **Recent SMS evidence** and select the new row;
- show processing status `Invalid`, the preserved raw body and the validation detail;
- confirm no new trusted incident/reading was created by the malformed SMS.

Optional second message with an unauthorised sender:

```text
sender: +2348039999999
body: GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=900|seq=777
```

Show it retained/rejected as an unauthorised sender and no incident.

### 10. Demonstrate contact/test SMS audit (2 minutes)

Open **Contacts**:

- select **Demo Safety Officer**;
- review the test message;
- click **Send test SMS** and confirm the dialog;
- show the green status: submitted to modem, delivery not confirmed;
- show the outgoing SMS log row and reference.

Emphasise that a network delivery receipt is not implemented and is never claimed.

### 11. Reports and CSV export (2 minutes)

Open **Reports**, keep the last 30 days, click **Generate report**.

Point out:

- total incidents, critical/warning counts, simulated count;
- trend and detector breakdown;
- raw readings are not ppm.

Click **Incidents CSV**, save to a known folder, and open the file to show UTC/Lagos timestamps,
simulation column and incident detail. Optionally export detector activity.

### 12. History, audit and restart persistence (2 minutes)

- Open **Incident History**, filter by status CRITICAL, open the record and show history.
- Open **Audit Log** and show `IncidentOpened`, `IncidentAcknowledged`, `IncidentResolved`,
  `DemoMessageInjected`, `TestSmsSent`, `ReportExported`.
- Close GasGuard Desktop (window close).
- Start it again.
- Open Incident History and show the incident persists; inject/buffer state carried by the DB, not
  memory.

### 13. Backup and database health (2 minutes)

Open **Settings**:

- point out database health = HEALTHY, integrity ok, foreign keys enabled;
- click **Create verified backup**;
- show the new row in Verified backups;
- optionally select it, **Validate selected**;
- explain that restore validates the file, rejects the live database as a source, drains/pauses
  monitoring, creates a safety backup, restores and then restarts the app; the maintenance overlay
  prevents closing the window mid-operation.

### 14. Closing safety statement (1 minute)

State clearly:

- The detector's local hardware alarm is independent and must continue working with no PC, no
  Windows, no USB, no SMS and no internet.
- The desktop is a monitoring/evidence layer, not a safety controller and not a valve controller.
- The simulator and automated tests prove software behavior only. No physical modem/network/gas
  result is claimed.
- Simulated injection is restricted to DEMO detectors; a rejected simulated message is retained as
  evidence and cannot update a production detector.
- Sender-number matching is not cryptographic authentication; SMS spoofing remains a documented
  limitation. See the security and protocol document.
- Raw readings are not calibrated gas concentrations.
- SMS sender numbers are not cryptographic identity; payload authentication and professional
  safety engineering are future work.

## Optional advanced demonstrations

### Simulated modem disconnection

In **GSM Modem**, click **Simulate modem disconnection**. Show the modem fault status and notification;
explain that no detector is marked online because of the modem, and that reconnecting restores
simulation.

### Outgoing failure

Set **Outgoing SMS outcome** to *Reject at modem*, apply, then send a test SMS. Show the failure and
the outgoing log. Reset to *Accept and submit* afterward.

### Duplicate/out-of-order sequence

Using custom raw messages, send `seq=100`, then `seq=99`. Show that the older sequence is retained as
out-of-order and does not regress the detector's last state or create a duplicate incident.

### Database failure retention

This requires the developer test trigger or a temporarily unavailable database; it is covered in
automated integration tests rather than the standard UI demo. Explain that the message is not deleted
from modem storage until persistence succeeds; a later reconciliation retries.

## Expected questions and concise answers

**Is this the gas alarm itself?** No. The detector/local alarm is independent hardware. This is the
GSM monitoring and incident management layer.

**Can it shut off a valve?** No. Remote actuation is deliberately excluded without a separate,
approved safety design and certified hardware.

**Does a connected modem mean detectors are online?** No. Detector state uses last successful
telemetry and a freshness threshold.

**Why doesn't NORMAL close the critical incident?** Because a later message must not erase evidence
and an operator must explicitly verify and resolve the incident.

**Does an SMS acknowledgement mean gas is gone?** No. It only records that an operator saw the alert.

**Can SMS be spoofed?** Yes; sender-number matching is not cryptographic. The app accepts no control
commands over SMS and the limitations are documented. Payload authentication is future work.

**Are readings ppm?** No. They are raw device values; no calibration has been performed.

**Is the physical modem tested?** No. The development environment had no modem. The hardware guide is
a validation checklist, not a certificate.

## Reset after demonstration

On a demo-only machine only: close the app and remove
`%LOCALAPPDATA%\GasGuardDesktop\gasguard.db`, `gasguard.db-wal`, `gasguard.db-shm` if a clean first-run
is required. Keep verified backups if the history is needed. Never delete a real user's database as a
demo step.
