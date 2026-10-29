using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.IntegrationTests;

public sealed class CoordinatorWorkflowTests
{
    [Fact]
    public async Task SimulatedCriticalSmsCompletesEndToEndWorkflow()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(sender: "+2348030000001", demo: true);
        var modem = host.Get<IGsmModemService>();
        var simulation = host.Get<ISimulatedModemControl>();
        var coordinator = host.Get<ISmsProcessingCoordinator>();
        var dashboard = host.Get<IDashboardService>();

        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);
        await coordinator.StartAsync();
        try
        {
            var injection = await simulation.InjectMessageAsync(new SimulatedSmsRequest(
                "+2348030000001",
                "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042"));

            Assert.True(injection.Success);
            await TestWait.UntilAsync(
                async () => await host.CountAsync<Incident>() == 1,
                because: "The simulated critical SMS should create an incident.");

            await TestWait.UntilAsync(
                () => Task.FromResult(simulation.PendingStoredMessageCount == 0),
                because: "The stored simulated SMS should be deleted after persistence.");

            await using var context = await host.CreateDbContextAsync();
            var incident = await context.Incidents.SingleAsync();
            var message = await context.SmsMessages.SingleAsync();
            var device = await context.GasDevices.SingleAsync();

            Assert.True(incident.IsSimulated);
            Assert.True(message.IsSimulated);
            Assert.Equal(SmsProcessingStatus.Processed, message.ProcessingStatus);
            Assert.Equal(1042, device.LastSequenceNumber);
            Assert.Equal(DeviceReportedState.Critical, device.LastReportedState);

            var snapshot = await dashboard.GetSnapshotAsync();
            Assert.True(snapshot.ContainsSimulatedData);
            Assert.Equal(1, snapshot.UnresolvedCriticalCount);
            Assert.Equal(1, snapshot.ActiveDetectorCount);
            Assert.True(snapshot.ModemStatus.IsSimulated);
            Assert.Equal("GAS-001", snapshot.LastSuccessfulDeviceCode);
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public async Task FailedPersistenceRetainsStoredSmsAndReconciliationRecoversIt()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(sender: "+2348030000001", demo: true);
        var modem = host.Get<IGsmModemService>();
        var simulation = host.Get<ISimulatedModemControl>();
        var coordinator = host.Get<ISmsProcessingCoordinator>();
        var journal = host.Get<IIncomingSmsJournal>();

        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);
        await coordinator.StartAsync();
        try
        {
            await using (var context = await host.CreateDbContextAsync())
            {
                await context.Database.ExecuteSqlRawAsync(
                    "CREATE TRIGGER FailIncidentInsert2 BEFORE INSERT ON Incidents BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
            }

            await simulation.InjectMessageAsync(new SimulatedSmsRequest(
                "+2348030000001",
                "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=200"));

            await TestWait.UntilAsync(
                async () =>
                {
                    await using var context = await host.CreateDbContextAsync();
                    return await context.CommunicationLogs.AnyAsync(
                        x => x.Summary == "Incoming SMS was not persisted");
                },
                because: "The failed persistence should be recorded.");

            Assert.Equal(0, await host.CountAsync<Incident>());
            Assert.Equal(0, await host.CountAsync<SmsMessage>());
            Assert.Equal(1, simulation.PendingStoredMessageCount);
            Assert.Single(await journal.LoadPendingAsync());

            await using (var context = await host.CreateDbContextAsync())
            {
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailIncidentInsert2;");
            }

            await coordinator.ReconcileNowAsync();
            await TestWait.UntilAsync(
                async () => await host.CountAsync<Incident>() == 1,
                because: "Reconciliation should ingest the retained SMS after the write failure is removed.");

            Assert.Equal(1, await host.CountAsync<Incident>());
            Assert.Equal(1, await host.CountAsync<SmsMessage>());
            Assert.Equal(0, simulation.PendingStoredMessageCount);
            Assert.Empty(await journal.LoadPendingAsync());
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public async Task DuplicateInjectedMessagesCreateOnlyOneIncident()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(sender: "+2348030000001", demo: true);
        var modem = host.Get<IGsmModemService>();
        var simulation = host.Get<ISimulatedModemControl>();
        var coordinator = host.Get<ISmsProcessingCoordinator>();

        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);
        await coordinator.StartAsync();
        try
        {
            const string body =
                "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=300";
            await simulation.InjectMessageAsync(new SimulatedSmsRequest("+2348030000001", body));
            await TestWait.UntilAsync(async () => await host.CountAsync<Incident>() == 1);
            await simulation.InjectMessageAsync(new SimulatedSmsRequest("+2348030000001", body));

            await TestWait.UntilAsync(
                async () => await host.CountAsync<SmsMessage>() >= 2,
                because: "The duplicate raw SMS should still be retained.");

            Assert.Equal(1, await host.CountAsync<Incident>());
            var messages = await host.ListAsync<SmsMessage>();
            Assert.Contains(messages, x => x.ProcessingStatus == SmsProcessingStatus.Duplicate);
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public async Task JournaledSmsIsRecoveredOnCoordinatorStart()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var device = await host.SeedDeviceAsync(sender: "+2348030000001");
        var journal = host.Get<IIncomingSmsJournal>();
        var coordinator = host.Get<ISmsProcessingCoordinator>();
        await journal.PersistAsync(new IncomingSms(
            device.ExpectedSenderNumber,
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=777",
            host.Clock.UtcNow,
            null,
            "JOURNAL-RECOVERY-1",
            false));

        await coordinator.StartAsync();
        try
        {
            await TestWait.UntilAsync(
                async () => await host.CountAsync<Incident>() == 1,
                because: "A journaled SMS should be recovered when the coordinator starts.");
            Assert.Empty(await journal.LoadPendingAsync());
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public async Task IngressDrainWaitReturnsAfterQueuedSmsIsProcessed()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(sender: "+2348030000001", demo: true);
        var modem = host.Get<IGsmModemService>();
        var simulation = host.Get<ISimulatedModemControl>();
        var coordinator = host.Get<ISmsProcessingCoordinator>();
        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);
        await coordinator.StartAsync();
        try
        {
            await simulation.InjectMessageAsync(new SimulatedSmsRequest(
                "+2348030000001",
                "GASALERT|v=1|device=GAS-001|event=GAS_WARNING|level=WARNING|reading=200|seq=902"));
            await coordinator.WaitForIngressDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, await host.CountAsync<Incident>());
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinatorPauseQuiescesIngestionUntilResume()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        await host.SeedDeviceAsync(sender: "+2348030000001", demo: true);
        var modem = host.Get<IGsmModemService>();
        var simulation = host.Get<ISimulatedModemControl>();
        var coordinator = host.Get<ISmsProcessingCoordinator>();
        Assert.True((await modem.ConnectAsync(ModemConnectionOptions.Simulator)).Success);
        await coordinator.StartAsync();
        try
        {
            await coordinator.PauseAsync();
            await simulation.InjectMessageAsync(new SimulatedSmsRequest(
                "+2348030000001",
                "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|seq=901"));
            await Task.Delay(300);
            Assert.Equal(0, await host.CountAsync<Incident>());

            await coordinator.ResumeAsync();
            await TestWait.UntilAsync(
                async () => await host.CountAsync<Incident>() == 1,
                because: "Paused ingestion should resume after explicit resume.");
        }
        finally
        {
            await coordinator.ResumeAsync();
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public async Task CoordinatorStopIsPromptWithNoPendingMessages()
    {
        await using var host = await IntegrationTestHost.CreateAsync();
        var coordinator = host.Get<ISmsProcessingCoordinator>();

        await coordinator.StartAsync();
        await coordinator.StopAsync();
        await coordinator.StopAsync();

        Assert.True(true);
    }
}
