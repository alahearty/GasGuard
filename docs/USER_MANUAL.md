# GasGuard Desktop — User Manual

## 1. Starting the application

Run `GasGuard.Desktop.exe` or `run.cmd`. On first start GasGuard creates its local database and shows
the main window. The default configuration connects the **simulated modem** and displays a warning
that demonstration data must not be treated as live gas safety information.

The application can start without a physical modem. If a physical modem is configured but not
available, the connection failure is shown; detector and incident history remains usable.

The window is maximised by default. Use normal Windows resizing; content areas scroll where needed.

## 2. Reading the screen

### Header

The top bar shows:

- current workspace title;
- unresolved incident summary (neutral when zero, warning/critical colour plus text when open);
- modem connection/readiness state with SIM/network summary;
- current `Africa/Lagos` time.

### Sidebar

Navigation: Dashboard, Live Alerts, GSM Modem, Gas Detectors, Incident History, Contacts, Reports,
Audit Log, Settings.

The bottom badge says **SIMULATED MODE** or **PHYSICAL MODE**. In simulation mode, demo data remains
visibly labelled everywhere.

### Notification banner

New incidents and communication failures produce a top banner with severity text, message, local
time and a SIMULATED tag when applicable. Use **Review alerts** to go to Live Alerts or **Dismiss** to
close the banner. Dismissing the banner does not delete an incident.

## 3. Dashboard

The dashboard shows:

- **Active detectors** — registry count of active devices, not a count of devices proven online.
- **Open critical / open warnings** — incidents not yet resolved (acknowledged incidents remain
  unresolved).
- **Modem** — actual connection/readiness/SIM/network/signal summary.
- **Detector status** — code, name, location, derived state, last report time, age and raw reading.
- **System health** — last successful detector message, freshness, database health, backup state and
  snapshot time.
- **Recent alerts** and **Recent communication failures**.

### State meanings

| Label | Meaning |
|---|---|
| NORMAL | The latest accepted report was NORMAL and is inside the freshness window. |
| WARNING | Latest accepted report was WARNING and is inside the freshness window. |
| CRITICAL | Latest accepted report was CRITICAL and is inside the freshness window. |
| UNKNOWN | No successful detector report has been received. |
| OFFLINE / STALE | No successful report within the configured stale window, or the device is inactive. |
| COMMUNICATION ERROR | A newer message was rejected/unauthorised or the device reported a communication error. |

A stale NORMAL is **not** current safety evidence. The dashboard shows the last-reported time and age
so it cannot be mistaken for a live reading.

### Demo data

**Load demo data** creates two labelled DEMO detectors and contacts. It does not overwrite existing
records. Demo messages are generated only when you explicitly inject them from the GSM Modem page.

## 4. Live Alerts

The list shows unresolved incidents in severity/time order. Select one to inspect:

- severity/status/simulation tags;
- device and location;
- occurrence and receipt times, including time source;
- raw reading and sequence;
- sender and SMS processing status;
- the original SMS;
- acknowledgement and resolution details;
- history/audit events.

### Acknowledge

1. Enter an optional acknowledgement note.
2. Click **Acknowledge incident**.
3. Confirm the dialog.

Acknowledgement records that an operator has seen the incident. It **does not** mean the hazard is
cleared and does not resolve the incident.

### Resolve

1. Acknowledge the incident first.
2. Enter required resolution notes documenting verification/corrective action.
3. Click **Resolve incident** and confirm.

Resolution is an explicit operator decision. A later NORMAL SMS does not resolve an incident
automatically, and the original alert remains in history.

## 5. GSM Modem

### Connection settings

Choose mode:

- **Simulated modem (DEMO)** — no hardware; all generated data is labelled.
- **Physical USB GSM modem** — enter/select a COM port and serial settings.

Use **Refresh COM ports** to enumerate physical ports. Click **Save settings** to persist; **Connect
modem** saves the current values and connects. Connection changes apply immediately when connecting.

The status card shows SIM readiness, network registration, operator, signal, text mode,
new-message indication, last poll and errors. A connected modem is not proof that detectors are
online.

### Polling and reconciliation

**Poll now** requests unread stored SMS. Messages are deleted from modem storage only after successful
database persistence when that setting is enabled. A failed save leaves the message available for a
later reconciliation.

### Simulator controls

Available in simulated mode:

- **Load demo detectors & contacts**.
- **Inject structured demo message** — detector, event (`STATUS`, `GAS_WARNING`,
  `GAS_CRITICAL`), raw reading.
- **Inject custom raw SMS** — sender, raw body, optional delivery delay. Malformed bodies are allowed
  for demonstrating rejection.
- **Outgoing SMS outcome** — accept/submit, reject, or timeout.
- **Simulate modem disconnection**.

All demo messages are marked SIMULATED. Simulated injection is restricted to detectors created by
the demo scenario; a simulated message for a production detector is rejected, retained as evidence
and audited without changing detector state. Unread simulated messages remain in simulated storage
for reconciliation when delivery/persistence fails.

### Recent SMS evidence

This table lists every retained SMS, including invalid, unknown-device, unauthorised-sender and
out-of-order messages. Select a row to inspect the original raw body and validation detail. Rejected
messages never create a trusted reading or incident, but they remain available for review. Use
**Refresh SMS evidence** after an injection if processing has just completed.

### Diagnostics

The current-session diagnostic table shows AT/system events and errors. **Clear** clears only the
in-memory table. Persistent communication failures are in the database/dashboard and never include
message bodies or secrets.

## 6. Gas Detectors

Fields:

- device code (unique, 3–32 chars);
- display name;
- installation location;
- expected sender number (normalised Nigerian mobile);
- detector type;
- description/installation date;
- active/inactive;
- maintenance notes;
- last communication/state fields.

Use **Add detector**, **Edit**, **Deactivate detector**/**Reactivate detector**. Deactivation
preserves every incident and message. There is no permanent delete action. Changing a device code or
expected sender requires confirmation because it changes which SMS sender is trusted for that
detector.

## 7. Incident History

Filters: text search, from/to local dates, severity, status, detector and location. Use **Apply
filters**, **Reset**, **Previous/Next**, and select an incident for original SMS and history.

**Export filtered CSV** writes all matching incidents, not just the visible page. The CSV includes
UTC and Lagos times, severity/status, snapshots, raw reading, sequence, acknowledgement/resolution
details and a simulation column.

## 8. Contacts & Notifications

Contacts include name, Nigerian mobile number, role, active state and notification preferences.

- Add/edit/deactivate contacts; deactivation preserves the record.
- The list shows normalised `+234` numbers and preferences.
- Select a contact to send a **confirmed test SMS**.
- Review the message and confirm the dialog.
- Status text says modem submission only; delivery is not confirmed. The outgoing log retains the
  attempt, outcome, provider reference and simulation flag.

Automatic contact notifications are off by default. Enable them in Settings if required; they use
bounded attempts and only target active contacts with matching preferences. Simulated incidents and
incidents on DEMO devices do not trigger real SMS notifications. Message delivery is not confirmed;
the UI only claims modem submission.

## 9. Reports

Choose the local date range, detector, severity and whether to include simulated data, then
**Generate report**. Available analytics:

- totals by severity;
- counts by detector;
- daily trend (bars show counts, not a prediction);
- communication-failure groups;
- detector message/incident activity.

CSV buttons export incidents, communication failures or detector activity. Raw readings are
explicitly labelled as device values, not calibrated ppm. Simulated incident counts are shown.

## 10. Audit Log

Search details/actor/entity, filter by action text, outcome and date range, and page through entries.
Typical actions: device/contact changes, modem configuration, incident acknowledgement/resolution,
report export, backup/restore, test SMS, retention, demo actions and security rejections.

No SIM PIN, credentials or SMS bodies are stored in the audit log. Phone numbers are masked where
practical in audit details and file logs; raw SMS evidence in the database/communication logs still
contains operational sender details. The Windows account is recorded as the actor; there is no in-app
login or role model.

## 11. Settings & Maintenance

### Preferences

- stale-data threshold in minutes (detector freshness);
- retention days (0 disables cleanup; minimum effective value is 7);
- minimum diagnostic log level;
- automatic contact notification toggle;
- notification attempts per contact (1–3).

Save preferences to apply them.

### Database health

Shows integrity result, foreign-key enforcement, size and last check time. Use **Refresh health** for a
new check.

### Backups

- **Create verified backup** writes a self-contained, integrity-checked `.db` to the backup folder.
- **Validate selected** checks integrity, required tables and foreign keys.
- **Restore selected** disconnects the modem, drains queued messages, pauses processing, creates a
  pre-restore safety backup, restores and then closes the application so it restarts against the
  restored database. A maintenance overlay is shown and the window cannot be closed while the
  operation is active.
- **Restore from file** allows a backup outside the default folder.

Never restore an untrusted file. The live database cannot be restored onto itself. A restore is
consequential; confirm the dialog. Keep an independent copy of important backups.

### Retention and folders

**Run retention cleanup** uses the saved retention period and removes old communication logs,
unlinked SMS messages and old **non-incident** audit rows. Incidents, linked SMS evidence and incident
audit history are preserved. The cleanup is transactional and audited. Use the folder buttons to open
data, backup and log folders.

### Protocol information

Displays protocol version, supported events, supported levels and display time zone. The protocol is
fixed in this academic release; changing it requires firmware and parser work, not an arbitrary UI
edit.

## 12. Keyboard and accessibility

- `Tab`/`Shift+Tab` move between controls.
- ComboBoxes and lists support arrow keys.
- Data grids support keyboard selection and sorting where enabled.
- Status is shown with words plus colour; colour is not the only signal.
- Confirmation dialogs protect destructive actions.
- Text labels accompany form inputs.

## 13. Data, shutdown and restart

- Database: `%LOCALAPPDATA%\GasGuardDesktop\gasguard.db`.
- Backups, logs and the unprocessed-SMS journal (`inbox\`) are beside it.
- Use the window close button for graceful shutdown; the app stops polling and disconnects the modem.
  Only one desktop instance may run at a time.
- Incidents and messages survive restart. Pending journaled SMS is replayed after a crash or restart.
- The database, journal and backups are not application-encrypted; protect the Windows account and
  use device encryption.
- If a physical modem is removed, the fault is visible; reconnect from the GSM Modem page.

## 14. Troubleshooting quick answers

| Problem | Answer |
|---|---|
| Dashboard shows OFFLINE but modem is connected | Detector freshness is independent of modem state. Check last-report time. An old NORMAL is stale. |
| NORMAL message did not close a critical alert | This is intentional. Resolve the incident explicitly with notes. |
| Test SMS says submitted but phone has no SMS | Submission is not delivery. Check recipient, network, SMSC, airtime and the modem error log. |
| Invalid SMS not visible as an incident | Invalid messages intentionally never become incidents. Open **GSM Modem → Recent SMS evidence** to inspect every retained raw SMS, including invalid/unknown-device/unauthorised messages. |
| Backup restore asks to close | The running app pauses monitoring, restores, shows the safety-backup path and must restart against the restored database. Do not force-close while the maintenance overlay is visible. |
| SIM PIN required | This app does not store or enter SIM PINs. Disable/unlock the PIN in a phone first. |
| Physical hardware not validated | Use the hardware checklist; simulator results do not prove modem compatibility. |

## 15. Safety reminder

GasGuard Desktop does not replace the detector's local alarm. If gas is suspected, follow site
emergency procedures, evacuate, ventilate only under approved supervision, and use calibrated,
approved detection equipment. Never use the app's simulated/raw values as a safety clearance.
