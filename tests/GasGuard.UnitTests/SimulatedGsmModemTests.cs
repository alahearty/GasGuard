using GasGuard.Contracts.Modem;
using GasGuard.Infrastructure.Modem;
using Microsoft.Extensions.Logging.Abstractions;

namespace GasGuard.UnitTests;

public sealed class SimulatedGsmModemTests
{
    [Fact]
    public async Task ConnectsAndReportsReadyStatus()
    {
        await using var modem = NewModem();
        var result = await modem.ConnectAsync(ModemConnectionOptions.Simulator);

        Assert.True(result.Success);
        Assert.True(modem.IsConnected);
        Assert.True(modem.Status.IsReadyForSms);
        Assert.True(modem.Status.IsSimulated);
        Assert.Contains("DEMONSTRATION", result.Message);
    }

    [Fact]
    public async Task InjectsMessageRaisesEventAndDeletesFromStorage()
    {
        await using var modem = NewModem();
        await modem.ConnectAsync(ModemConnectionOptions.Simulator);
        var received = new TaskCompletionSource<IncomingSms>(TaskCreationOptions.RunContinuationsAsynchronously);
        modem.SmsReceived += (_, args) => received.TrySetResult(args.Message);

        var result = await modem.InjectMessageAsync(
            new SimulatedSmsRequest("+2348031234567", "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1"));
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Success);
        Assert.True(message.IsSimulated);
        Assert.NotNull(message.StorageIndex);
        Assert.Equal(1, modem.PendingStoredMessageCount);

        var stored = await modem.ReadStoredMessagesAsync(true);
        Assert.Single(stored);
        Assert.Equal(message.Body, stored[0].Body);

        await modem.DeleteStoredMessageAsync(message.StorageIndex!.Value);
        Assert.Equal(0, modem.PendingStoredMessageCount);
    }

    [Fact]
    public async Task RetainsMessagesWhileDisconnectedForReconciliation()
    {
        await using var modem = NewModem();
        var injection = await modem.InjectMessageAsync(
            new SimulatedSmsRequest("+2348031234567", "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1"));
        Assert.True(injection.Success);
        Assert.Equal(1, modem.PendingStoredMessageCount);

        await modem.ConnectAsync(ModemConnectionOptions.Simulator);
        var stored = await modem.ReadStoredMessagesAsync(true);
        Assert.Single(stored);
    }

    [Fact]
    public async Task OutgoingSuccessReportsModemSubmissionOnly()
    {
        await using var modem = NewModem();
        await modem.ConnectAsync(ModemConnectionOptions.Simulator);

        var result = await modem.SendSmsAsync("+2348031234567", "GasGuard test");

        Assert.True(result.SubmittedToModem);
        Assert.Equal(SmsSendOutcome.SubmittedToModem, result.Outcome);
        Assert.False(result.DeliveryConfirmed);
        Assert.NotNull(result.ProviderReference);
    }

    [Fact]
    public async Task OutgoingRejectionModeReturnsFailure()
    {
        await using var modem = NewModem();
        await modem.ConnectAsync(ModemConnectionOptions.Simulator);
        await modem.SetSendModeAsync(SimulatedSendMode.ModemRejected);

        var result = await modem.SendSmsAsync("+2348031234567", "GasGuard test");

        Assert.False(result.SubmittedToModem);
        Assert.Equal(SmsSendOutcome.RejectedByModem, result.Outcome);
    }

    [Fact]
    public async Task OutgoingTimeoutModeReturnsTimeout()
    {
        await using var modem = NewModem();
        await modem.ConnectAsync(ModemConnectionOptions.Simulator);
        await modem.SetSendModeAsync(SimulatedSendMode.Timeout);

        var result = await modem.SendSmsAsync("+2348031234567", "GasGuard test");

        Assert.False(result.SubmittedToModem);
        Assert.Equal(SmsSendOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task RejectsNonAsciiOutgoingMessage()
    {
        await using var modem = NewModem();
        await modem.ConnectAsync(ModemConnectionOptions.Simulator);

        var result = await modem.SendSmsAsync("+2348031234567", "GasGuard \u00B0C");

        Assert.False(result.SubmittedToModem);
        Assert.Equal(SmsSendOutcome.RejectedByModem, result.Outcome);
    }

    [Fact]
    public async Task SimulatedDisconnectionRaisesFaultAndStopsSending()
    {
        await using var modem = NewModem();
        await modem.ConnectAsync(ModemConnectionOptions.Simulator);
        var faulted = new TaskCompletionSource<ModemFaultedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        modem.Faulted += (_, args) => faulted.TrySetResult(args);

        await modem.SimulateDisconnectionAsync("Cable removed (simulated).");
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await modem.SendSmsAsync("+2348031234567", "GasGuard test");

        Assert.Equal(ModemConnectionState.Faulted, modem.Status.ConnectionState);
        Assert.Equal(SmsSendOutcome.NotConnected, result.Outcome);
    }

    private static SimulatedGsmModem NewModem() =>
        new(new TestClock(), NullLogger<SimulatedGsmModem>.Instance);
}
