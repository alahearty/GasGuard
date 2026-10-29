# Windows Deployment

## Supported baseline

- Windows 10 or Windows 11, x64.
- .NET 10 Desktop Runtime for framework-dependent publication, or use self-contained publication.
- Optional physical modem driver and an available COM port.
- Local write access to `%LOCALAPPDATA%`.

GasGuard Desktop is a local desktop application. It does not require IIS, a database server,
firewall rules, internet access or a cloud account.

## Build and test before publishing

```powershell
dotnet restore GasGuard.sln
dotnet build GasGuard.sln -c Release
dotnet test GasGuard.sln -c Release
```

Do not publish a build with failing tests. The current canonical results are **101 unit + 46
integration tests passing** in [TEST_RESULTS.md](TEST_RESULTS.md); the historical baseline is in
[TEST_REPORT.md](TEST_REPORT.md). Release and recovery guidance is in
[RELEASE_AND_RECOVERY.md](RELEASE_AND_RECOVERY.md).

## Framework-dependent publish (recommended for a managed academic PC)

```powershell
dotnet publish src/GasGuard.Desktop/GasGuard.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishReadyToRun=false `
  -o artifacts/publish/win-x64
```

Copy the whole `artifacts/publish/win-x64` folder, not just the `.exe`.

Prerequisite on the target machine: **.NET 10 Desktop Runtime (x64)**. Confirm with:

```powershell
dotnet --list-runtimes
```

## Self-contained publish (no .NET install on target)

```powershell
dotnet publish src/GasGuard.Desktop/GasGuard.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o artifacts/publish/win-x64-selfcontained
```

Self-contained output is larger. Do not use single-file trimming for WPF; test any single-file package
on the target because native SQLite/port libraries can be affected.

## Installation

1. Copy the publish folder to a local path such as `C:\Program Files\GasGuard` or a user folder.
2. Ensure the Windows user can read the application folder and write to their own
   `%LOCALAPPDATA%`.
3. If using a physical modem, install the manufacturer's USB serial driver as required. No
   administrator rights are needed by GasGuard itself after installation.
4. Start `GasGuard.Desktop.exe`.
5. On first launch, if delivering a pre-verified backup, restore it from Settings after confirming
   the database path and integrity. Runtime restore pauses monitoring, drains queued SMS, creates a
   pre-restore safety backup, shows a maintenance overlay and closes the app after completion.
6. Start only one GasGuard instance per Windows session; a second process is refused to protect the
   database, journal and COM port.

There is no bundled MSI/MSIX. If an installer is required by the institution, wrap the publish
directory with the approved packaging tool and test installation, upgrade and uninstallation.
Code signing should be applied if the institution distributes the executable.

## Runtime data and permissions

```text
%LOCALAPPDATA%\GasGuardDesktop\
  gasguard.db
  gasguard.db-wal / gasguard.db-shm   (normal SQLite WAL sidecars, not backups)
  backups\
  logs\
  inbox\                              (unprocessed SMS crash-recovery journal)
```

- The application creates the folder and initial database automatically.
- Back up the database with the in-app **Create verified backup**, not by copying a live `.db`
  without its WAL sidecars.
- The local data contains SMS content, contact numbers, incident history, audit details and a raw
  crash-recovery journal. Protect the Windows account and disk with ACLs, device encryption and
  institutional controls. The application does not encrypt these files itself.
- No cloud credentials, SIM PINs or APNs are stored. There is no in-app login/role model; the Windows
  account is the audit actor.
- If a user profile is deleted, local history is lost unless a verified backup was exported.

## Upgrading

1. Close GasGuard Desktop.
2. Create a verified backup from the current version (or confirm an automatic pre-migration backup
   directory exists).
3. Replace the application files with the new publish folder.
4. Start the application. `StartupService` checks pending migrations and creates a verified
   pre-migration backup before applying them.
5. Verify database health and incident counts.
6. If migration fails, keep the pre-migration backup and do not continue using the new binary.

Never copy an older binary over a newer database without testing migrations. Schema evolution is
documented in [DATABASE.md](DATABASE.md).

## Restore/decommission

- **Restore** is inside Settings: select a validated backup, confirm, and allow the maintenance
  operation to finish and the application to close. The live database is rejected as its own source;
  a pre-restore safety backup is created. Start the app again to load the restored database.
- **Decommission:** export required reports, create a verified backup if history must be retained,
  then remove the application folder and the user data folder under the institution's data
  retention policy.

## Modem/COM port considerations

- The modem may re-enumerate to a different COM port after being unplugged. Refresh ports or enter
  the new port, then reconnect.
- If another application holds the port, GasGuard cannot open it. Close vendor dashboards/terminal
  tools.
- A physical modem must be tested with the actual SIM and Nigerian network; see
  [HARDWARE_INTEGRATION.md](HARDWARE_INTEGRATION.md).
- Do not use a shared/kiosk Windows account without considering SMS content and contact data
  exposure.

## Diagnostics for support

- Logs: `%LOCALAPPDATA%\GasGuardDesktop\logs\gasguard-YYYYMMDD.log`.
- Database health and integrity: Settings → Database health.
- SMS security limits: [SMS_SECURITY_AND_PROTOCOL.md](SMS_SECURITY_AND_PROTOCOL.md).
- Hardware validation record: [GSM_HARDWARE_VALIDATION.md](GSM_HARDWARE_VALIDATION.md).
- Modem diagnostics: GSM Modem workspace (current session) and persistent communication logs.
- Include modem model/firmware, SIM operator, screenshot of connection state and log excerpts when
  reporting a hardware issue. Do **not** include SIM PINs or sensitive content.

## Verification checklist after deployment

- [ ] Application starts and the database path is correct.
- [ ] DEMO/simulation banner is visible when simulated mode is configured.
- [ ] A critical simulated SMS creates exactly one incident.
- [ ] Duplicate injection does not create a second incident.
- [ ] Acknowledgement does not resolve; resolution requires notes.
- [ ] Incident survives restart.
- [ ] Backup validates and restore works on a test copy; window close is blocked during maintenance.
- [ ] A second app instance is refused.
- [ ] Simulated injection is refused for a production detector.
- [ ] Physical modem checklist completed separately where hardware exists.
- [ ] Local detector alarm independently confirmed by qualified hardware testing.
- [ ] No untrusted data/backups or development credentials are present in the deployed package.
