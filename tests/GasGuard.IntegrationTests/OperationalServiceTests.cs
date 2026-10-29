using System.Text;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.IntegrationTests;

public sealed class OperationalServiceTests
{
    [Fact]
    public async Task DeviceValidationEnforcesUniqueCodeAndSender()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(code: "GAS-001", sender: "+2348031234567");
        var service = host.Get<IDeviceService>();

        var duplicateCode = await service.CreateAsync(new DeviceInput(
            "gas-001",
            "Duplicate code",
            "Elsewhere",
            "+2348031234568",
            DetectorType.CombustibleGas,
            null,
            null,
            true,
            null));
        var duplicateSender = await service.CreateAsync(new DeviceInput(
            "GAS-003",
            "Duplicate sender",
            "Elsewhere",
            "08031234567",
            DetectorType.CombustibleGas,
            null,
            null,
            true,
            null));
        var invalidSender = await service.CreateAsync(new DeviceInput(
            "GAS-004",
            "Invalid sender",
            "Elsewhere",
            "12345",
            DetectorType.CombustibleGas,
            null,
            null,
            true,
            null));

        Assert.False(duplicateCode.Success);
        Assert.False(duplicateSender.Success);
        Assert.False(invalidSender.Success);
        Assert.Equal(1, await host.CountAsync<GasDevice>());
    }

    [Fact]
    public async Task DeactivatingDetectorPreservesIncidentsAndUpdatesDashboardCounts()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var incident = await ingestion.ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=1",
            host.Clock.UtcNow,
            null,
            "DEACT-1",
            false));

        var deactivate = await host.Get<IDeviceService>().SetActiveAsync(device.Id, false);
        var snapshot = await host.Get<IDashboardService>().GetSnapshotAsync();
        var details = await host.Get<IIncidentService>().GetDetailsAsync(incident.IncidentId!.Value);

        Assert.True(deactivate.Success);
        Assert.Equal(1, await host.CountAsync<Incident>());
        Assert.NotNull(details);
        Assert.Equal(0, snapshot.ActiveDetectorCount);
        Assert.Equal(1, snapshot.UnresolvedCriticalCount);
        Assert.Empty(snapshot.Detectors);
    }

    [Fact]
    public async Task ContactValidationAndDeactivationWork()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedContactAsync(phone: "+2348030000010");
        var service = host.Get<IContactService>();

        var duplicate = await service.CreateAsync(new ContactInput(
            "Duplicate",
            "08030000010",
            ContactRole.Operator,
            true,
            true,
            false,
            false,
            null));
        var invalid = await service.CreateAsync(new ContactInput(
            "Invalid",
            "12345",
            ContactRole.Operator,
            true,
            true,
            false,
            false,
            null));

        var contacts = await service.GetContactsAsync(true);
        var deactivate = await service.SetActiveAsync(contacts.Single().Id, false);
        var active = await service.GetContactsAsync(false);

        Assert.False(duplicate.Success);
        Assert.False(invalid.Success);
        Assert.True(deactivate.Success);
        Assert.Empty(active);
        Assert.Equal(1, await host.CountAsync<Contact>());
    }

    [Fact]
    public async Task SettingsValidationAndPersistenceAreEnforced()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var settings = host.Get<ISettingsService>();
        var snapshot = await settings.GetSnapshotAsync();

        var invalid = await settings.SavePreferencesAsync(snapshot.Preferences with { StaleAfterMinutes = 0 });
        var validPreferences = await settings.SavePreferencesAsync(snapshot.Preferences with
        {
            StaleAfterMinutes = 3,
            DataRetentionDays = 90,
            AutomaticContactNotifications = true,
            NotificationMaxAttempts = 2
        });
        var validModem = await settings.SaveModemSettingsAsync(snapshot.Modem with
        {
            UseSimulatedModem = false,
            PortName = "COM9",
            BaudRate = 9600
        });
        var updated = await settings.GetSnapshotAsync();

        Assert.False(invalid.Success);
        Assert.True(validPreferences.Success);
        Assert.True(validModem.Success);
        Assert.Equal(3, updated.Preferences.StaleAfterMinutes);
        Assert.Equal(90, updated.Preferences.DataRetentionDays);
        Assert.True(updated.Preferences.AutomaticContactNotifications);
        Assert.False(updated.Modem.UseSimulatedModem);
        Assert.Equal("COM9", updated.Modem.PortName);
    }

    [Fact]
    public async Task ModemSettingsRejectSimulatorPortWhenPhysicalModeSelected()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var settings = host.Get<ISettingsService>();
        var snapshot = await settings.GetSnapshotAsync();

        var result = await settings.SaveModemSettingsAsync(snapshot.Modem with
        {
            UseSimulatedModem = false,
            PortName = "SIMULATOR"
        });

        Assert.False(result.Success);
        Assert.Contains(result.ValidationErrors!, x => x.Contains("SIMULATOR", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaleDetectorIsShownOfflineEvenWhenModemIsConnected()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var ingestion = host.Get<ISmsIngestionService>();
        var modem = host.Get<IGsmModemService>();

        await ingestion.ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=120|seq=1",
            host.Clock.UtcNow,
            null,
            "FRESH-1",
            false));
        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);

        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(45);
        var snapshot = await host.Get<IDashboardService>().GetSnapshotAsync();

        Assert.True(snapshot.ModemStatus.IsSimulated);
        Assert.True(snapshot.ModemStatus.IsReadyForSms);
        var detector = snapshot.Detectors.Single();
        Assert.Equal(DeviceOperationalState.Offline, detector.OperationalState);
        Assert.Equal(DeviceReportedState.Normal, detector.LastReportedState);
        Assert.NotNull(snapshot.LastSuccessfulCommunicationUtc);
    }

    [Fact]
    public async Task TestSmsRequiresConfirmationAndLogsSubmission()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var modem = host.Get<IGsmModemService>();
        var notifications = host.Get<INotificationService>();
        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);

        var denied = await notifications.SendTestMessageAsync(
            "+2348031234567",
            "GasGuard test SMS",
            operatorConfirmed: false);
        var sent = await notifications.SendTestMessageAsync(
            "+2348031234567",
            "GasGuard test SMS",
            operatorConfirmed: true);
        var outgoing = await host.Get<ICommunicationLogService>().GetOutgoingAsync(10);

        Assert.False(denied.SubmittedToModem);
        Assert.Equal(SmsSendOutcome.Cancelled, denied.Outcome);
        Assert.True(sent.SubmittedToModem);
        Assert.False(sent.DeliveryConfirmed);
        Assert.Single(outgoing);
        Assert.Equal(CommunicationOutcome.Success, outgoing[0].Outcome);
        Assert.Equal("+2348031234567", outgoing[0].RecipientNumber);
    }

    [Fact]
    public async Task AutomaticCriticalNotificationIsBoundedAndAudited()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync(sender: "+2348030000001");
        await host.SeedContactAsync(phone: "+2348030000010", notifyCritical: true);
        var settings = host.Get<ISettingsService>();
        var snapshot = await settings.GetSnapshotAsync();
        Assert.True((await settings.SavePreferencesAsync(snapshot.Preferences with
        {
            AutomaticContactNotifications = true,
            NotificationMaxAttempts = 1
        })).Success);

        var modem = host.Get<IGsmModemService>();
        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);

        var incident = await host.Get<ISmsIngestionService>().ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=500",
            host.Clock.UtcNow,
            null,
            "NOTIFY-1",
            false));
        var dispatch = await host.Get<INotificationService>().DispatchIncidentNotificationsAsync(incident.IncidentId!.Value);

        Assert.Equal(1, dispatch.Attempted);
        Assert.Equal(1, dispatch.Submitted);
        Assert.Equal(0, dispatch.Failed);

        await using var context = await host.CreateDbContextAsync();
        Assert.Contains(
            await context.AuditLogs.ToListAsync(),
            x => x.Action == "IncidentNotificationsDispatched");
        Assert.Contains(
            await context.CommunicationLogs.ToListAsync(),
            x => x.Direction == CommunicationDirection.Outbound && x.Outcome == CommunicationOutcome.Success);
    }

    [Fact]
    public async Task SimulatedIncidentsDoNotTriggerRealSmsNotifications()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync(sender: "+2348030000001", demo: true);
        await host.SeedContactAsync(notifyCritical: true);
        var settings = host.Get<ISettingsService>();
        var snapshot = await settings.GetSnapshotAsync();
        await settings.SavePreferencesAsync(snapshot.Preferences with { AutomaticContactNotifications = true });
        Assert.True((await host.Get<IGsmModemService>().ConnectAsync(ModemConnectionOptions.Simulator)).Success);

        var incident = await host.Get<ISmsIngestionService>().ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=600",
            host.Clock.UtcNow,
            null,
            "SIM-NOTIFY-1",
            true));
        var dispatch = await host.Get<INotificationService>().DispatchIncidentNotificationsAsync(incident.IncidentId!.Value);

        Assert.Equal(0, dispatch.Attempted);
        Assert.Empty(await host.Get<ICommunicationLogService>().GetOutgoingAsync(10));
    }

    [Fact]
    public async Task ReportsSummarizeAndExportIncidentsWithSimulationLabels()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync(demo: true);
        var ingestion = host.Get<ISmsIngestionService>();

        await ingestion.ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=1",
            host.Clock.UtcNow,
            null,
            "REPORT-1",
            false));
        await ingestion.ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_WARNING|level=WARNING|reading=300|seq=2",
            host.Clock.UtcNow,
            null,
            "REPORT-2",
            true));
        await ingestion.ProcessAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=100|seq=3",
            host.Clock.UtcNow,
            null,
            "REPORT-3",
            false));

        var criteria = new ReportCriteria(
            host.Clock.UtcNow.AddDays(-1),
            host.Clock.UtcNow.AddDays(1));
        var reports = host.Get<IReportService>();
        var summary = await reports.GetSummaryAsync(criteria);
        var exportPath = Path.Combine(host.Paths.RootDirectory, "incidents.csv");
        var export = await reports.ExportIncidentsCsvAsync(criteria, exportPath);

        Assert.Equal(2, summary.TotalIncidents);
        Assert.Equal(1, summary.SimulatedIncidentCount);
        Assert.Equal(1, summary.IncidentsBySeverity.Single(x => x.Severity == Severity.Critical).Count);
        Assert.Equal(1, summary.IncidentsBySeverity.Single(x => x.Severity == Severity.Warning).Count);
        Assert.Single(summary.IncidentsByDevice);
        Assert.Equal(3, summary.DetectorActivity.Single().ProcessedMessageCount);
        Assert.True(export.Success, export.Message);
        Assert.True(File.Exists(exportPath));
        var bytes = await File.ReadAllBytesAsync(exportPath);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        var text = await File.ReadAllTextAsync(exportPath);
        Assert.Contains("Severity", text);
        Assert.Contains("GAS-001", text);
        Assert.Contains("Yes", text);
        Assert.Contains("ReportExported", (await host.ListAsync<AuditLog>()).Select(x => x.Action));
    }

    [Fact]
    public async Task InvalidSmsIsRetainedAndAvailableForModemEvidenceReview()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync();
        var invalidBody = "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=not-a-number";

        await host.Get<ISmsIngestionService>().ProcessAsync(new IncomingSms(
            "+2348031234567",
            invalidBody,
            host.Clock.UtcNow,
            null,
            "EVIDENCE-1",
            false));

        var evidence = await host.Get<ISmsEvidenceService>().GetRecentAsync(10);
        var item = Assert.Single(evidence);

        Assert.Equal(SmsProcessingStatus.Invalid, item.ProcessingStatus);
        Assert.Equal(invalidBody, item.BodyRaw);
        Assert.Equal("GAS-001", item.DeviceCode);
        Assert.Equal(0, await host.CountAsync<Incident>());
    }

    [Fact]
    public async Task AuditFailureRollsBackDeviceCreation()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await using (var context = await host.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER FailDeviceAudit BEFORE INSERT ON AuditLogs WHEN NEW.Action = 'DeviceCreated' BEGIN SELECT RAISE(ABORT, 'simulated audit failure'); END;");
        }

        var result = await host.Get<IDeviceService>().CreateAsync(new DeviceInput(
            "GAS-010",
            "Audit failure detector",
            "Test location",
            "+2348031234599",
            DetectorType.CombustibleGas,
            null,
            null,
            true,
            null));

        Assert.False(result.Success);
        Assert.Equal(0, await host.CountAsync<GasDevice>());
    }

    [Fact]
    public async Task SimultaneousDistinctAlertsCreateTwoIncidents()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var firstDevice = await host.SeedDeviceAsync("GAS-001", "+2348031234567");
        var secondDevice = await host.SeedDeviceAsync("GAS-002", "+2348031234568");
        var ingestion = host.Get<ISmsIngestionService>();

        var first = ingestion.ProcessAsync(new IncomingSms(
            firstDevice.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=500|seq=10",
            host.Clock.UtcNow,
            null,
            "CONCURRENT-1",
            false));
        var second = ingestion.ProcessAsync(new IncomingSms(
            secondDevice.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-002|event=GAS_CRITICAL|level=CRITICAL|reading=600|seq=10",
            host.Clock.UtcNow,
            null,
            "CONCURRENT-2",
            false));

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.True(result.Persisted));
        Assert.Equal(2, await host.CountAsync<Incident>());
        Assert.Equal(2, await host.CountAsync<SmsMessage>());
    }

    [Fact]
    public async Task AuditLogCanBeQueriedByEntityAndOutcome()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync();
        var audit = host.Get<IAuditLogService>();
        await audit.RecordAsync(new AuditEntryDraft(
            "tester",
            "SecurityTest",
            "GasDevice",
            device.Id.ToString(),
            AuditOutcome.Denied,
            "A denied action was recorded."));

        var byEntity = await audit.GetForEntityAsync("GasDevice", device.Id.ToString());
        var query = await audit.QueryAsync(new AuditQuery(Outcome: AuditOutcome.Denied));

        Assert.Contains(byEntity, x => x.Action == "SecurityTest");
        Assert.Contains(query.Items, x => x.Action == "SecurityTest");
    }
}
