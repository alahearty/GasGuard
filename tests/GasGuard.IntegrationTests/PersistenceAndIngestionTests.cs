using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.IntegrationTests;

public sealed class PersistenceAndIngestionTests
{
    [Fact]
    public async Task StartupAppliesMigrationsSeedsSettingsAndEnablesForeignKeys()
    {
        await using var host = await IntegrationTestHost.CreateAsync();

        await using var context = await host.CreateDbContextAsync();
        var applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
        Assert.Single(await context.ModemConfigurations.ToListAsync());
        Assert.Single(await context.Preferences.ToListAsync());

        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ForeignKeyEnforcementRejectsOrphanMessage()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await using var context = await host.CreateDbContextAsync();

        context.SmsMessages.Add(new SmsMessage
        {
            Id = Guid.NewGuid(),
            SenderNumberRaw = "+2348031234567",
            BodyRaw = "GASALERT|v=1",
            Fingerprint = new string('A', 64),
            ReceivedAtUtc = host.Clock.UtcNow,
            StoredAtUtc = host.Clock.UtcNow,
            DeviceId = Guid.NewGuid(),
            DeviceCode = "GAS-999"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ValidCriticalSmsCreatesOneIncidentAndUpdatesDetector()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();

        var result = await host.Get<ISmsIngestionService>().ProcessAsync(NewIncoming(
            host,
            "08031234567",
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042",
            sourceReference: "TEST-1"));

        Assert.True(result.Persisted);
        Assert.True(result.IncidentCreated);
        Assert.Equal(SmsProcessingStatus.Processed, result.Status);

        await using var context = await host.CreateDbContextAsync();
        var incident = await context.Incidents.SingleAsync();
        var message = await context.SmsMessages.SingleAsync();
        var device = await context.GasDevices.SingleAsync();

        Assert.Equal(Severity.Critical, incident.Severity);
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(850, incident.Reading);
        Assert.Equal("raw", incident.Unit);
        Assert.Equal(1042, incident.SequenceNumber);
        Assert.False(incident.IsSimulated);
        Assert.Equal(IncidentTimeSource.DesktopReceipt, incident.OccurredTimeSource);
        Assert.Equal(host.Clock.UtcNow, incident.ReceivedAtUtc);
        Assert.Equal(message.Id, incident.TriggeringSmsMessageId);
        Assert.Equal(SmsProcessingStatus.Processed, message.ProcessingStatus);
        Assert.Equal("+2348031234567", message.SenderNumberNormalized);
        Assert.StartsWith("GASALERT", message.BodyRaw);
        Assert.Equal(DeviceReportedState.Critical, device.LastReportedState);
        Assert.Equal(1042, device.LastSequenceNumber);
        Assert.Equal(850, device.LastReading);
        Assert.Equal(host.Clock.UtcNow, device.LastSuccessfulCommunicationUtc);

        var audit = await host.Get<IAuditLogService>().GetForEntityAsync("Incident", incident.Id.ToString());
        Assert.Contains(audit, x => x.Action == "IncidentOpened");
    }

    [Fact]
    public async Task DuplicateRawMessageAndDuplicateSequenceDoNotCreateDuplicateIncidents()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        const string body =
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042";

        var first = await ingestion.ProcessAsync(NewIncoming(host, "+2348031234567", body, "TEST-1"));
        var duplicateBody = await ingestion.ProcessAsync(NewIncoming(host, "+2348031234567", body, "TEST-2"));
        var sameSequenceDifferentBody = await ingestion.ProcessAsync(NewIncoming(
            host,
            "+2348031234567",
            body.Replace("reading=850", "reading=851", StringComparison.Ordinal),
            "TEST-3"));

        Assert.True(first.IncidentCreated);
        Assert.True(duplicateBody.Duplicate);
        Assert.True(sameSequenceDifferentBody.Duplicate);
        Assert.Equal(1, await host.CountAsync<Incident>());
        Assert.Equal(3, await host.CountAsync<SmsMessage>());
    }

    [Fact]
    public async Task SameModemStorageReferenceIsNotPersistedTwice()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        const string body =
            "GASALERT|v=1|device=GAS-001|event=GAS_WARNING|level=WARNING|reading=100|seq=10";

        var first = await ingestion.ProcessAsync(NewIncoming(host, "+2348031234567", body, "STORAGE-1"));
        var second = await ingestion.ProcessAsync(NewIncoming(host, "+2348031234567", body, "STORAGE-1"));

        Assert.True(first.Persisted);
        Assert.True(second.Persisted);
        Assert.True(second.Duplicate);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await host.CountAsync<SmsMessage>());
        Assert.Equal(1, await host.CountAsync<Incident>());
    }

    [Fact]
    public async Task NormalStatusDoesNotResolveOrEraseCriticalIncident()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var incidents = host.Get<IIncidentService>();

        var critical = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=1042"));
        var normal = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=120|seq=1043"));

        Assert.True(normal.Persisted);
        var active = await incidents.GetActiveAsync();
        Assert.Single(active);
        Assert.Equal(IncidentStatus.Open, active[0].Status);
        Assert.Equal(1043, (await host.ListAsync<GasDevice>()).Single().LastSequenceNumber);

        var ack = await incidents.AcknowledgeAsync(critical.IncidentId!.Value, "Operator saw the alert.");
        Assert.True(ack.Success);
        Assert.Equal(IncidentStatus.Acknowledged, (await incidents.GetActiveAsync()).Single().Status);

        var resolution = await incidents.ResolveAsync(critical.IncidentId.Value, "Area ventilated and gas reading confirmed normal by operator.");
        Assert.True(resolution.Success);
        Assert.Empty(await incidents.GetActiveAsync());

        var details = await incidents.GetDetailsAsync(critical.IncidentId.Value);
        Assert.NotNull(details);
        Assert.Equal("Operator saw the alert.", details!.AcknowledgementNotes);
        Assert.Equal("Area ventilated and gas reading confirmed normal by operator.", details.ResolutionNotes);
        Assert.Contains(details.History, x => x.Action == "IncidentAcknowledged");
        Assert.Contains(details.History, x => x.Action == "IncidentResolved");
    }

    [Fact]
    public async Task InvalidMessageIsPreservedAndCannotCreateTrustedReading()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        const string raw = "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=NORMAL|reading=abc|seq=1";

        var result = await ingestion.ProcessAsync(NewIncoming(host, "+2348031234567", raw, "INVALID-1"));

        Assert.True(result.Persisted);
        Assert.Equal(SmsProcessingStatus.Invalid, result.Status);
        var message = (await host.ListAsync<SmsMessage>()).Single();
        Assert.Equal(raw, message.BodyRaw);
        Assert.Equal(SmsProcessingStatus.Invalid, message.ProcessingStatus);
        Assert.Empty(await host.ListAsync<Incident>());
        var device = (await host.ListAsync<GasDevice>()).Single();
        Assert.Null(device.LastSuccessfulCommunicationUtc);
        Assert.Equal(DeviceReportedState.Unknown, device.LastReportedState);
    }

    [Fact]
    public async Task UnknownDeviceAndUnauthorizedSenderAreRejectedAndRetained()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();

        var unknown = await ingestion.ProcessAsync(NewIncoming(
            host,
            "+2348039999999",
            "GASALERT|v=1|device=GAS-999|event=GAS_CRITICAL|level=CRITICAL|reading=900|seq=1"));
        var unauthorized = await ingestion.ProcessAsync(NewIncoming(
            host,
            "+2348039999999",
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=900|seq=2"));

        Assert.Equal(SmsProcessingStatus.UnknownDevice, unknown.Status);
        Assert.Equal(SmsProcessingStatus.UnauthorizedSender, unauthorized.Status);
        Assert.Equal(2, await host.CountAsync<SmsMessage>());
        Assert.Equal(0, await host.CountAsync<Incident>());
        var device = (await host.ListAsync<GasDevice>()).Single();
        Assert.NotNull(device.LastCommunicationErrorUtc);
        Assert.Equal(DeviceReportedState.Unknown, device.LastReportedState);
    }

    [Fact]
    public async Task OutOfOrderMessageDoesNotRegressDetectorState()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();

        await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=100"));
        var stale = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=100|seq=99"));

        Assert.Equal(SmsProcessingStatus.OutOfOrder, stale.Status);
        var currentDevice = (await host.ListAsync<GasDevice>()).Single();
        Assert.Equal(DeviceReportedState.Critical, currentDevice.LastReportedState);
        Assert.Equal(100, currentDevice.LastSequenceNumber);
        Assert.Equal(1, await host.CountAsync<Incident>());
    }

    [Fact]
    public async Task DeviceTimestampIsUsedWhenPlausibleAndFallsBackWhenNot()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();

        var trusted = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_WARNING|level=WARNING|reading=200|seq=1|ts=2026-10-28T12:30:00+01:00"));
        var untrusted = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_WARNING|level=WARNING|reading=210|seq=2|ts=2026-11-30T12:30:00+01:00"));

        await using var context = await host.CreateDbContextAsync();
        var incidents = await context.Incidents.OrderBy(x => x.SequenceNumber).ToListAsync();
        Assert.Equal(new DateTime(2026, 10, 28, 11, 30, 0, DateTimeKind.Utc), incidents[0].OccurredAtUtc);
        Assert.Equal(IncidentTimeSource.DeviceReported, incidents[0].OccurredTimeSource);
        Assert.Equal(host.Clock.UtcNow, incidents[1].OccurredAtUtc);
        Assert.Equal(IncidentTimeSource.DesktopReceiptFallback, incidents[1].OccurredTimeSource);
        Assert.True(trusted.Persisted && untrusted.Persisted);
    }

    [Fact]
    public async Task DatabaseWriteFailureRollsBackMessageAndIncident()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        await using (var context = await host.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER FailIncidentInsert BEFORE INSERT ON Incidents BEGIN SELECT RAISE(ABORT, 'simulated incident insert failure'); END;");
        }

        var result = await host.Get<ISmsIngestionService>().ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=777|seq=55",
            "FAIL-1"));

        Assert.False(result.Persisted);
        Assert.Equal(SmsProcessingStatus.Failed, result.Status);
        Assert.Equal(0, await host.CountAsync<SmsMessage>());
        Assert.Equal(0, await host.CountAsync<Incident>());

        var currentDevice = (await host.ListAsync<GasDevice>()).Single();
        Assert.Null(currentDevice.LastSuccessfulCommunicationUtc);
    }

    [Fact]
    public async Task IncidentsSurviveApplicationRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "GasGuardTests", Guid.NewGuid().ToString("N"));
        Guid incidentId;
        await using (var firstHost = await IntegrationTestHost.CreateAsync(root, deleteOnDispose: false))
        {
            var device = await firstHost.SeedDeviceAsync();
            var result = await firstHost.Get<ISmsIngestionService>().ProcessAsync(NewIncoming(
                firstHost,
                device.ExpectedSenderNumber,
                "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=1042"));
            incidentId = result.IncidentId!.Value;
        }

        await using var secondHost = await IntegrationTestHost.CreateAsync(root);
        var details = await secondHost.Get<IIncidentService>().GetDetailsAsync(incidentId);

        Assert.NotNull(details);
        Assert.Equal(850, details!.Summary.Reading);
        Assert.Equal(IncidentStatus.Open, details.Summary.Status);
    }

    [Fact]
    public async Task LowerSequenceCriticalAlertIsNotSilentlySuppressed()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();

        var first = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=100"));
        var afterReset = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=900|seq=1"));

        Assert.True(first.IncidentCreated);
        Assert.True(afterReset.IncidentCreated);
        Assert.Equal(2, await host.CountAsync<Incident>());
        var messages = await host.ListAsync<SmsMessage>();
        Assert.Contains(messages, x => x.ProcessingStatus == SmsProcessingStatus.Processed &&
                                       x.ProcessingDetail is not null &&
                                       x.ProcessingDetail.Contains("possible device reset", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, (await host.ListAsync<GasDevice>()).Single().LastSequenceNumber);
    }

    [Fact]
    public async Task SimulatedMessageForProductionDetectorIsRejectedWithoutChangingState()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();

        var result = await host.Get<ISmsIngestionService>().ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=7",
            simulated: true));

        Assert.True(result.Persisted);
        Assert.Equal(SmsProcessingStatus.SimulationRejected, result.Status);
        Assert.False(result.IncidentCreated);
        Assert.Equal(0, await host.CountAsync<Incident>());
        var current = (await host.ListAsync<GasDevice>()).Single();
        Assert.Null(current.LastSuccessfulCommunicationUtc);
        Assert.Equal(DeviceReportedState.Unknown, current.LastReportedState);
    }

    [Fact]
    public async Task RepeatedCriticalAfterResolutionWithinWindowCreatesNewIncident()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var incidents = host.Get<IIncidentService>();
        const string body =
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=5";

        var first = await ingestion.ProcessAsync(NewIncoming(host, device.ExpectedSenderNumber, body));
        Assert.True(first.IncidentCreated);
        Assert.True((await incidents.AcknowledgeAsync(first.IncidentId!.Value, "Seen.")).Success);
        Assert.True((await incidents.ResolveAsync(first.IncidentId.Value, "Verified and closed.")).Success);

        var repeated = await ingestion.ProcessAsync(NewIncoming(host, device.ExpectedSenderNumber, body));

        Assert.True(repeated.IncidentCreated);
        Assert.NotEqual(first.IncidentId, repeated.IncidentId);
        var stored = await host.ListAsync<Incident>();
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, x => x.Status == IncidentStatus.Resolved);
        Assert.Contains(stored, x => x.Status == IncidentStatus.Open);
    }

    [Fact]
    public async Task RepeatedCriticalAfterDuplicateWindowCreatesNewIncident()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var incidents = host.Get<IIncidentService>();
        const string body =
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw";

        var first = await ingestion.ProcessAsync(NewIncoming(host, device.ExpectedSenderNumber, body));
        Assert.True(first.IncidentCreated);
        Assert.True((await incidents.AcknowledgeAsync(first.IncidentId!.Value, "Seen.")).Success);
        Assert.True((await incidents.ResolveAsync(first.IncidentId.Value, "Closed after verification.")).Success);

        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(16);
        var repeated = await ingestion.ProcessAsync(NewIncoming(host, device.ExpectedSenderNumber, body));

        Assert.True(repeated.IncidentCreated);
        Assert.Equal(2, await host.CountAsync<Incident>());
    }

    [Fact]
    public async Task SequenceResetStatusWithTrustedDeviceTimeIsAccepted()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var timestamp = host.Clock.UtcNow
            .AddMinutes(1)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        var first = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=120|seq=100"));
        var reset = await ingestion.ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            $"GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=130|seq=2|ts={timestamp}"));

        Assert.True(first.Persisted);
        Assert.Equal(SmsProcessingStatus.Processed, reset.Status);
        var current = (await host.ListAsync<GasDevice>()).Single();
        Assert.Equal(2, current.LastSequenceNumber);
        Assert.Equal(130, current.LastReading);
        var resetMessage = (await host.ListAsync<SmsMessage>())
            .Single(x => x.SequenceNumber == 2);
        Assert.Contains("probable reset", resetMessage.ProcessingDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuditWriteFailureRollsBackMessageIncidentAndAudit()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        await using (var context = await host.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER FailIncidentAudit BEFORE INSERT ON AuditLogs WHEN NEW.Action = 'IncidentOpened' BEGIN SELECT RAISE(ABORT, 'simulated audit failure'); END;");
        }

        var result = await host.Get<ISmsIngestionService>().ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=777|seq=56",
            "AUDIT-FAIL-1"));

        Assert.False(result.Persisted);
        Assert.Equal(0, await host.CountAsync<SmsMessage>());
        Assert.Equal(0, await host.CountAsync<Incident>());
        await using var verify = await host.CreateDbContextAsync();
        Assert.False(await verify.AuditLogs.AnyAsync(x => x.Action == "IncidentOpened"));
    }

    [Fact]
    public async Task AuditWriteFailureOnAcknowledgeLeavesIncidentOpen()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var incident = await host.Get<ISmsIngestionService>().ProcessAsync(NewIncoming(
            host,
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=777|seq=57"));
        await using (var context = await host.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER FailAcknowledgeAudit BEFORE INSERT ON AuditLogs WHEN NEW.Action = 'IncidentAcknowledged' BEGIN SELECT RAISE(ABORT, 'simulated audit failure'); END;");
        }

        var acknowledge = await host.Get<IIncidentService>().AcknowledgeAsync(
            incident.IncidentId!.Value,
            "Attempted acknowledgement.");

        Assert.False(acknowledge.Success);
        var active = await host.Get<IIncidentService>().GetActiveAsync();
        Assert.Single(active);
        Assert.Equal(IncidentStatus.Open, active[0].Status);
    }

    private static IncomingSms NewIncoming(
        IntegrationTestHost host,
        string sender,
        string body,
        string? sourceReference = null,
        bool simulated = false) =>
        new(
            sender,
            body,
            host.Clock.UtcNow,
            sourceReference is null ? null : 1,
            sourceReference,
            simulated);
}
