using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Infrastructure.Modem;
using Microsoft.Extensions.Logging.Abstractions;

namespace GasGuard.UnitTests;

public sealed class PhysicalGsmModemAdapterTests
{
    private static readonly ModemConnectionOptions Options = new(
        false,
        "COM_TEST",
        115200,
        8,
        SerialParity.None,
        SerialStopBits.One,
        SerialHandshake.None,
        1000,
        30,
        true);

    [Fact]
    public async Task ConnectsReadsStoredSmsAndSendsThroughAtPipeline()
    {
        var transport = new ScriptedAtTransport();
        await using var adapter = NewAdapter(transport);

        var connection = await adapter.ConnectAsync(Options);

        Assert.True(connection.Success, connection.Message);
        Assert.Equal(ModemConnectionState.Connected, adapter.Status.ConnectionState);
        Assert.True(adapter.Status.IsReadyForSms);
        Assert.True(adapter.Status.SmsTextModeEnabled);
        Assert.Equal(21, adapter.Status.SignalQuality);
        Assert.Equal("MTN", adapter.Status.NetworkOperator);
        Assert.Contains("SIM800C", adapter.Status.Model);

        var messages = await adapter.ReadStoredMessagesAsync(true);
        var message = Assert.Single(messages);
        Assert.Equal("+2348031234567", message.From);
        Assert.Equal(7, message.StorageIndex);
        Assert.StartsWith("GASALERT", message.Body);
        Assert.StartsWith("MODEM:7:", message.SourceReference);

        var send = await adapter.SendSmsAsync("+2348031234567", "GasGuard test");
        Assert.True(send.SubmittedToModem);
        Assert.Equal("42", send.ProviderReference);
        Assert.False(send.DeliveryConfirmed);

        var deletion = await adapter.DeleteStoredMessageAsync(7);
        Assert.True(deletion.Success);
    }

    [Fact]
    public async Task OutgoingSmsIsRejectedWhenNetworkIsNotRegistered()
    {
        var transport = new ScriptedAtTransport(cregResponse: "+CREG: 0,2");
        await using var adapter = NewAdapter(transport);

        await adapter.ConnectAsync(Options);
        var send = await adapter.SendSmsAsync("+2348031234567", "GasGuard test");

        Assert.Equal(NetworkRegistrationState.Searching, adapter.Status.RegistrationState);
        Assert.False(adapter.Status.IsReadyForSms);
        Assert.False(send.SubmittedToModem);
        Assert.Equal(SmsSendOutcome.RejectedByModem, send.Outcome);
        Assert.Contains("not ready", send.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequiredStatusCommandFailureMarksModemFaulted()
    {
        var transport = new ScriptedAtTransport();
        await using var adapter = NewAdapter(transport);
        Assert.True((await adapter.ConnectAsync(Options)).Success);

        transport.FailCsq = true;
        var status = await adapter.RefreshStatusAsync();

        Assert.Equal(ModemConnectionState.Faulted, status.ConnectionState);
        Assert.False(status.IsReadyForSms);
        Assert.NotNull(status.LastError);
    }

    [Fact]
    public async Task DeleteFailureDoesNotReportSuccessOrFabricateConnectionLoss()
    {
        var transport = new ScriptedAtTransport();
        await using var adapter = NewAdapter(transport);
        Assert.True((await adapter.ConnectAsync(Options)).Success);

        transport.FailDelete = true;
        var deletion = await adapter.DeleteStoredMessageAsync(7);

        Assert.False(deletion.Success);
        Assert.Equal(ModemConnectionState.Connected, adapter.Status.ConnectionState);
    }

    [Fact]
    public async Task SimPinRequiredIsVisibleButModemIsNotReadyForSms()
    {
        var transport = new ScriptedAtTransport("+CPIN: SIM PIN");
        await using var adapter = NewAdapter(transport);

        var result = await adapter.ConnectAsync(Options);

        Assert.True(result.Success);
        Assert.Equal(SimReadiness.PinRequired, adapter.Status.SimReadiness);
        Assert.False(adapter.Status.IsReadyForSms);
        Assert.Contains("readiness is incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PhysicalGsmModemAdapter NewAdapter(IAtTransport transport) =>
        new(
            Options,
            new ScriptedTransportFactory(transport),
            new TestClock(),
            NullLoggerFactory.Instance,
            NullLogger<PhysicalGsmModemAdapter>.Instance);
}
