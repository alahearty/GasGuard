using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.IntegrationTests;

public sealed class BackupAndMaintenanceTests
{
    [Fact]
    public async Task BackupIsVerifiedAndRestoreRoundTrips()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(code: "GAS-001", sender: "+2348031234567");
        var backups = host.Get<IBackupService>();

        var created = await backups.CreateBackupAsync();

        Assert.True(created.Success, created.Message);
        Assert.True(created.Verified);
        Assert.NotNull(created.BackupPath);
        Assert.True(File.Exists(created.BackupPath));
        Assert.False(File.Exists(created.BackupPath + "-wal"));
        Assert.False(File.Exists(created.BackupPath + "-shm"));
        Assert.False(File.Exists(created.BackupPath + ".tmp-wal"));
        Assert.False(File.Exists(created.BackupPath + ".tmp-shm"));

        var validation = await backups.ValidateBackupAsync(created.BackupPath!);
        Assert.True(validation.IsValid);

        await host.SeedDeviceAsync(code: "GAS-002", sender: "+2348031234568", name: "Second Detector");
        Assert.Equal(2, await host.CountAsync<GasDevice>());

        var restored = await backups.RestoreBackupAsync(created.BackupPath!);

        Assert.True(restored.Success, restored.Message);
        Assert.True(restored.RestartRequired);
        Assert.True(restored.Verified);
        Assert.NotNull(restored.PreRestoreBackupPath);
        Assert.True(File.Exists(restored.PreRestoreBackupPath));

        Assert.Equal(1, await host.CountAsync<GasDevice>());
        Assert.Equal("GAS-001", (await host.ListAsync<GasDevice>()).Single().DeviceCode);

        var health = await backups.GetDatabaseHealthAsync();
        Assert.True(health.IsHealthy, health.Message);
        Assert.True(health.ForeignKeysEnabled);
    }

    [Fact]
    public async Task LiveDatabaseCannotBeRestoredOntoItself()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var backups = host.Get<IBackupService>();

        var result = await backups.RestoreBackupAsync(host.Paths.DatabasePath);

        Assert.False(result.Success);
        Assert.Contains("live database", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await host.CountAsync<GasDevice>());
    }

    [Fact]
    public async Task VerifiedBackupRestoresOnAFreshInstallation()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "GasGuardTests", Guid.NewGuid().ToString("N"));
        var targetRoot = Path.Combine(Path.GetTempPath(), "GasGuardTests", Guid.NewGuid().ToString("N"));
        string backupPath;
        try
        {
            await using (var source = await IntegrationTestHost.CreateAsync(sourceRoot, deleteOnDispose: false))
            {
                await source.SeedDeviceAsync("GAS-001", "+2348031234567");
                var created = await source.Get<IBackupService>().CreateBackupAsync();
                Assert.True(created.Success, created.Message);
                backupPath = created.BackupPath!;
            }

            await using var target = await IntegrationTestHost.CreateAsync(targetRoot);
            var restore = await target.Get<IBackupService>().RestoreBackupAsync(backupPath);

            Assert.True(restore.Success, restore.Message);
            Assert.Equal(1, await target.CountAsync<GasDevice>());
            Assert.Equal("GAS-001", (await target.ListAsync<GasDevice>()).Single().DeviceCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, true);
            }

            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, true);
            }
        }
    }

    [Fact]
    public async Task CorruptOrMissingBackupIsRejected()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var backups = host.Get<IBackupService>();
        var badPath = Path.Combine(host.Paths.BackupsDirectory, "bad-backup.db");
        await File.WriteAllTextAsync(badPath, "this is not a SQLite database");

        var validation = await backups.ValidateBackupAsync(badPath);
        var restore = await backups.RestoreBackupAsync(badPath);
        var missing = await backups.ValidateBackupAsync(Path.Combine(host.Paths.BackupsDirectory, "missing.db"));

        Assert.False(validation.IsValid);
        Assert.False(restore.Success);
        Assert.False(missing.IsValid);
    }

    [Fact]
    public async Task RetentionPreservesIncidentsAndLinkedSmsEvidence()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var created = await ingestion.ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=1",
            host.Clock.UtcNow,
            null,
            "RETAIN-1",
            false));

        await using (var context = await host.CreateDbContextAsync())
        {
            var linked = await context.SmsMessages.SingleAsync(x => x.Id == created.MessageId);
            linked.ReceivedAtUtc = host.Clock.UtcNow.AddDays(-60);
            context.CommunicationLogs.Add(new CommunicationLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = host.Clock.UtcNow.AddDays(-60),
                Direction = CommunicationDirection.System,
                Kind = CommunicationKind.Connection,
                Outcome = CommunicationOutcome.Information,
                Summary = "Old communication log",
                IsSimulated = false
            });
            context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = host.Clock.UtcNow.AddDays(-60),
                Actor = "test",
                Action = "OldAction",
                EntityType = "Test",
                Outcome = AuditOutcome.Success,
                Details = "Old audit row"
            });
            context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = host.Clock.UtcNow.AddDays(-60),
                Actor = "test",
                Action = "OldIncidentEvidence",
                EntityType = "Incident",
                EntityId = created.IncidentId?.ToString(),
                Outcome = AuditOutcome.Success,
                Details = "Old incident audit must be retained."
            });
            context.SmsMessages.Add(new SmsMessage
            {
                Id = Guid.NewGuid(),
                SenderNumberRaw = "+2348031234567",
                BodyRaw = "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1",
                Fingerprint = new string('F', 64),
                ReceivedAtUtc = host.Clock.UtcNow.AddDays(-60),
                StoredAtUtc = host.Clock.UtcNow.AddDays(-60),
                DeviceId = device.Id,
                DeviceCode = "GAS-001",
                ProcessingStatus = SmsProcessingStatus.Processed
            });
            await context.SaveChangesAsync();
        }

        var settings = host.Get<ISettingsService>();
        var snapshot = await settings.GetSnapshotAsync();
        var save = await settings.SavePreferencesAsync(snapshot.Preferences with { DataRetentionDays = 30 });
        Assert.True(save.Success);

        var result = await host.Get<IRetentionService>().ApplyRetentionAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, await host.CountAsync<Incident>());

        await using var verify = await host.CreateDbContextAsync();
        var messages = await verify.SmsMessages.ToListAsync();
        Assert.Single(messages);
        Assert.Equal(created.MessageId, messages[0].Id);
        Assert.False(await verify.CommunicationLogs.AnyAsync(x => x.Summary == "Old communication log"));
        Assert.False(await verify.AuditLogs.AnyAsync(x => x.Action == "OldAction"));
        Assert.True(await verify.AuditLogs.AnyAsync(x => x.Action == "OldIncidentEvidence"));
        Assert.True(await verify.AuditLogs.AnyAsync(x => x.Action == "RetentionCleanup"));
    }

    [Fact]
    public async Task BackupListAndLastBackupMetadataAreUpdated()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var backups = host.Get<IBackupService>();

        var created = await backups.CreateBackupAsync();
        var list = await backups.ListBackupsAsync();
        var overview = await host.Get<ISettingsService>().GetOverviewAsync();

        Assert.True(created.Success);
        Assert.NotEmpty(list);
        Assert.NotNull(overview.Settings.LastBackupAtUtc);
        Assert.Equal(created.BackupPath, overview.Settings.LastBackupPath);
    }
}
