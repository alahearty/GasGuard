# Release, Upgrade, Backup and Recovery

## Release baseline and verification

Release output is generated only after a clean Release build and full test run. The pre-hardening
package was preserved under `artifacts\baseline\` before the hardened package was generated.

Final verification command set:

```powershell
dotnet restore GasGuard.sln
dotnet build GasGuard.sln -c Release
dotnet test GasGuard.sln -c Release --no-build
& .\scripts\publish.ps1
```

Observed final software evidence is in [TEST_RESULTS.md](TEST_RESULTS.md): 101 unit + 46 integration
tests pass; Release build has 0 warnings and 0 errors. A physical modem was not available, so no
hardware result is claimed.

## Target framework and runtime

- WPF desktop project: `net10.0-windows`.
- Publish RID: `win-x64`.
- Current `artifacts\publish\win-x64` is **framework-dependent**: target PCs need the .NET 10 Desktop
  Runtime (x64).
- A self-contained package can be produced with `scripts\package.ps1 -SelfContained`; it includes the
  .NET runtime and does not require a separate runtime installation.
- The packaged outputs are:
  - `artifacts\GasGuard-Desktop-win-x64.zip` — framework-dependent;
  - `artifacts\GasGuard-Desktop-win-x64-selfcontained.zip` — self-contained;
  - `artifacts\GasGuard-source.zip` — source archive.
- Native SQLite (`e_sqlite3.dll`) and `System.IO.Ports.dll` are included in the publish output.
- `scripts\publish.ps1` disables PDB/debug-symbol generation for the release package so developer
  source paths and symbols are not distributed. Keep symbols internally if line-number diagnostics are
  required.
- No installer or code signing is included. An institution should package/sign the publish directory
  using its approved installer and signing process.

## Build/publish commands

```powershell
# Framework-dependent win-x64
& .\scripts\publish.ps1

# Self-contained win-x64 (larger)
& .\scripts\publish.ps1 -SelfContained

# Or directly
dotnet publish src\GasGuard.Desktop\GasGuard.Desktop.csproj `
  -c Release -r win-x64 --self-contained false `
  -o artifacts\publish\win-x64
```

The ZIP package is created from the publish folder plus `README.md` and `docs/`. Verify archive
integrity before distribution:

```powershell
Get-FileHash artifacts\GasGuard-Desktop-win-x64.zip -Algorithm SHA256
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path artifacts\GasGuard-Desktop-win-x64.zip))
$zip.Entries | Select-Object FullName, Length
$zip.Dispose()
```

## First launch

1. Install the .NET 10 Desktop Runtime if using framework-dependent output.
2. If a physical modem is used, install its signed USB serial driver and connect it.
3. Run `GasGuard.Desktop.exe` as a standard Windows user (no elevation required by the app).
4. The app creates:
   - `%LOCALAPPDATA%\GasGuardDesktop\gasguard.db`
   - `backups\`
   - `logs\`
   - `inbox\` (durable incoming-SMS journal)
5. EF Core migrations are applied. If an existing database has pending migrations, a verified
   pre-migration backup is attempted first; migration stops if that backup cannot be verified.
6. Default settings use the simulated modem and auto-connect. The DEMO banner is visible. No detector
   or incident data is invented on a first run.
7. Load demo data explicitly if demonstrating; simulated data is only accepted for DEMO detectors.

A second instance is refused by a local single-instance mutex to prevent concurrent database/modem
access.

## Upgrade

1. Create a verified backup from the current version and copy it outside the application folder.
2. Close the app gracefully.
3. Replace the application files with the verified new publish folder; do not delete the user data
   directory.
4. Start the new version. Pending migrations are processed with an automatic pre-migration backup.
5. Check **Settings → Database health**, device/contact counts, incident history and reports.
6. Keep the prior application release folder until the upgraded version has been verified.

The singleton settings rows are preserved; startup only repairs missing rows. An ordinary upgrade does
not reset user configuration or history. Never overwrite a newer database with an older application
binary without testing the migration path.

## Backup

- Use **Settings → Create verified backup**. The SQLite online backup API is used; the result is
  integrity-checked, finalised to a self-contained `.db`, and WAL/SHM sidecars are removed.
- A backup copy should be moved off the machine according to institutional policy.
- The in-app backup folder alone is not disaster recovery.
- The incoming-SMS journal is separate from the database backup; pending journal entries survive a
  restore and are replayed on restart. Successful entries are removed automatically.

## Restore

**Restore is destructive to the current database; a pre-restore safety backup is created.**

Runtime flow:

1. Settings validates the selected backup (integrity, core tables, foreign keys), rejects the live
   database as its own source and asks for confirmation.
2. The modem is disconnected; queued ingress is drained; coordinator pause gates are acquired.
3. A blocking maintenance overlay appears. The window cannot be closed while the operation guard is
   active.
4. A verified pre-restore safety backup is created.
5. The backup is restored and validated. On validation/copy failure the utility attempts rollback from
   the safety backup.
6. On success, the app displays the safety-backup path and closes. Restart it to load the restored
   database. On failure, processing and modem connection are resumed and an error is shown.

Offline/manual restore is also possible by an administrator using the validated `.db` and the normal
application backup mechanism, but the in-app path is safer because it performs validation, pause and
safety backup. Do not copy a live SQLite file (including its WAL sidecars) while the app is running
except through the built-in online backup API.

## Rollback

- If an **upgrade** fails: stop the app, retain the logs and failed database, then restore the
  pre-migration backup through the Settings restore path or by an approved offline recovery procedure
  using the previous application version. Do not repeatedly start a failing migration.
- If a **restore** fails: the current utility attempts automatic rollback from the safety backup. If
  that also fails, preserve both files and the logs; do not delete anything. Use an administrator and
  a validated copy.
- Keep the previous release folder and a copy of the pre-upgrade backup until the new release is
  accepted.

## Data and privacy

All operational data is under the per-user LocalAppData profile. The database, journal and backups are
not application-encrypted; rely on Windows account ACLs, BitLocker/device encryption, and institutional
handling rules. Logs mask phone numbers; the database/communication exports contain operational
numbers and SMS evidence. Do not place backups on shared folders without access control.

## Operational recovery

| Symptom | Action |
|---|---|
| App says database cannot open/migrate | Read the error/log, preserve the DB and any pre-migration backup, run Database health if possible; do not delete the database. |
| Modem port missing | Check driver/Device Manager, refresh ports, close vendor tools holding the port. |
| AT timeout / modem fault | Reconnect; confirm baud/port; verify with modem documentation. The app marks the pipeline faulted rather than pretending it is healthy. |
| SIM PIN required | Unlock/disable the PIN outside GasGuard; the app does not enter or store SIM PINs. |
| No network registration | Check SIM, antenna, coverage and operator; receiving/SMS readiness will not be claimed until registered. |
| SMS not deleted after processing | Check communication log/DB health; it is retained for reconciliation. Do not manually delete database rows. |
| Unread SMS after crash | On restart, coordinator replays the inbox journal and polls modem storage; duplicates are deduplicated. |
| CSV spreadsheet warning | The exporter neutralizes formula-leading cells; it does not mean the file is free of sensitive operational data. |
| Restore interrupted | Close the app only after the maintenance overlay clears; if forced off, use the pre-restore safety backup and logs. |
| Power/network unstable | Use a UPS, keep the independent local detector alarm, and record outages; do not rely on the desktop for safety. |

## Release package verification checklist

- [ ] Release build/tests passed after the last source change.
- [ ] Published executable launches with the intended runtime.
- [ ] Publish directory contains SQLite native and serial-port dependencies.
- [ ] ZIP opens, has expected app/docs contents and a recorded SHA-256.
- [ ] No database, logs, inbox journals, secrets, or developer credentials are inside the ZIP.
- [x] First launch on a clean local profile tested with the framework-dependent `win-x64` publish on
      2026-10-28: database created, DEMO banner visible, simulated modem reached `MODEM READY`.
      Second start reached the window in 5.7 s in this environment; first start was slower due to
      one-time runtime/antivirus work.
- [ ] Physical modem/SIM/Nigerian-network checklist completed separately (not performed).
- [ ] Local detector alarm independently verified (not performed).
- [ ] Previous release and pre-upgrade backup retained.
