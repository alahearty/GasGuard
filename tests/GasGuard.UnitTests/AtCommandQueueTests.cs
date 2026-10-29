using GasGuard.Contracts.Modem;
using GasGuard.Infrastructure.Modem;

namespace GasGuard.UnitTests;

public sealed class AtCommandQueueTests
{
    [Fact]
    public async Task ExecutesCommandAndReturnsSuccess()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var execution = queue.ExecuteAsync(new AtCommandRequest("AT"));
        var written = await transport.NextWriteAsync();
        Assert.Equal("AT\r", written);

        transport.Emit("OK");
        var response = await execution;

        Assert.True(response.Success);
        Assert.Equal("AT_TIMEOUT", AtCommandResponse.Timeout("x").ErrorCode);
    }

    [Fact]
    public async Task SerializesCommandsAndPreservesResponses()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var first = queue.ExecuteAsync(new AtCommandRequest("AT+CSQ"));
        Assert.Equal("AT+CSQ\r", await transport.NextWriteAsync());

        var second = queue.ExecuteAsync(new AtCommandRequest("AT+CPIN?"));
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        transport.Emit("+CSQ: 18,99");
        transport.Emit("OK");
        var firstResponse = await first;
        Assert.True(firstResponse.Success);
        Assert.Contains("+CSQ: 18,99", firstResponse.Lines);

        Assert.Equal("AT+CPIN?\r", await transport.NextWriteAsync());
        transport.Emit("+CPIN: READY");
        transport.Emit("OK");
        var secondResponse = await second;
        Assert.True(secondResponse.Success);
        Assert.Contains("+CPIN: READY", secondResponse.Lines);
    }

    [Fact]
    public async Task HandlesSmsPayloadPromptAndCmsReference()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var execution = queue.ExecuteAsync(new AtCommandRequest("AT+CMGS=\"+2348031234567\"", "Hello"));
        Assert.Equal("AT+CMGS=\"+2348031234567\"\r", await transport.NextWriteAsync());

        transport.Emit(">");
        var payload = await transport.NextWriteAsync();
        Assert.Equal("Hello\u001A", payload);

        transport.Emit("+CMGS: 77");
        transport.Emit("OK");
        var response = await execution;

        Assert.True(response.Success);
        Assert.Equal("77", AtResponseParsing.ParseCmgsReference(response.Lines));
        Assert.DoesNotContain("Hello", response.RawResponse);
    }

    [Fact]
    public async Task ReportsTimeoutAndDesynchronizesQueue()
    {
        var transport = new FakeAtTransport();
        await using var queue = new AtCommandQueue(transport, 500, new TestClock());
        await queue.OpenAsync();

        var response = await queue.ExecuteAsync(new AtCommandRequest("AT"));
        Assert.True(response.TimedOut);
        Assert.False(queue.IsOpen);
    }

    [Fact]
    public async Task ReportsCancellation()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();
        using var cts = new CancellationTokenSource();

        var execution = queue.ExecuteAsync(new AtCommandRequest("AT"), cts.Token);
        Assert.Equal("AT\r", await transport.NextWriteAsync());
        cts.Cancel();
        var response = await execution;

        Assert.True(response.Cancelled);
    }

    [Fact]
    public async Task RoutesUnsolicitedCmtMessageToEvent()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var received = new TaskCompletionSource<IncomingSms>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.UnsolicitedSmsReceived += (_, sms) => received.TrySetResult(sms);

        transport.Emit("+CMT: \"+2348031234567\",,\"26/10/28,13:00:00+04\"");
        transport.Emit("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1");

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("+2348031234567", message.From);
        Assert.StartsWith("GASALERT", message.Body);
    }

    [Fact]
    public async Task RoutesMessageIndicationToNotificationEvent()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var notification = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.UnsolicitedNotificationReceived += (_, raw) => notification.TrySetResult(raw);

        transport.Emit("+CMTI: \"SM\",12");
        Assert.Equal("+CMTI: \"SM\",12", await notification.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task MapsCmsErrorToFailure()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var execution = queue.ExecuteAsync(new AtCommandRequest("AT+CMGS=\"+2348031234567\"", "Hello"));
        await transport.NextWriteAsync();
        transport.Emit(">");
        await transport.NextWriteAsync();
        transport.Emit("+CMS ERROR: 500");
        var response = await execution;

        Assert.False(response.Success);
        Assert.Equal("CMS_ERROR", response.ErrorCode);
    }

    [Fact]
    public async Task TransportFaultFailsPendingCommand()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var execution = queue.ExecuteAsync(new AtCommandRequest("AT"));
        await transport.NextWriteAsync();
        transport.RaiseFault(new IOException("Cable removed."));
        var response = await execution;

        Assert.False(response.Success);
        Assert.Equal("TRANSPORT_FAULTED", response.ErrorCode);
    }

    [Fact]
    public async Task IgnoresEchoedCommandLine()
    {
        var transport = new FakeAtTransport();
        await using var queue = NewQueue(transport);
        await queue.OpenAsync();

        var execution = queue.ExecuteAsync(new AtCommandRequest("AT"));
        await transport.NextWriteAsync();
        transport.Emit("AT");
        transport.Emit("OK");
        var response = await execution;

        Assert.True(response.Success);
    }

    private static AtCommandQueue NewQueue(FakeAtTransport transport) =>
        new(transport, 1000, new TestClock());
}
