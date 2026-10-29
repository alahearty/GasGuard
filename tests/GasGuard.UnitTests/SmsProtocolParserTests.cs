using GasGuard.Application.Services;
using GasGuard.Contracts.Protocol;
using GasGuard.Domain;

namespace GasGuard.UnitTests;

public sealed class SmsProtocolParserTests
{
    private readonly SmsProtocolParser _parser = new();

    [Fact]
    public void ParsesValidCriticalMessage()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=1042");

        Assert.True(result.IsValid);
        var message = Assert.IsType<ParsedGasMessage>(result.Message);
        Assert.Equal("GAS-001", message.DeviceCode);
        Assert.Equal(GasEventCode.GasCritical, message.EventCode);
        Assert.Equal(Severity.Critical, message.Severity);
        Assert.Equal(850, message.Reading);
        Assert.Equal("raw", message.Unit);
        Assert.Equal(1042, message.SequenceNumber);
        Assert.DoesNotContain(result.Issues, x => x.IsError);
    }

    [Fact]
    public void ParsesStatusAndTimestampWithOffset()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=gas-002|event=STATUS|level=NORMAL|reading=120|unit=raw|seq=1043|ts=2026-10-28T13:00:00+01:00");

        Assert.True(result.IsValid);
        Assert.Equal("GAS-002", result.Message!.DeviceCode);
        Assert.Equal(Severity.Normal, result.Message.Severity);
        Assert.Equal(TimeSpan.FromHours(1), result.Message.DeviceTimestamp!.Value.Offset);
        Assert.True(result.Message.HasExplicitTimestampOffset);
        Assert.True(SmsProtocolParserFixture.HasNoErrors(result));
    }

    [Fact]
    public void DefaultsUnitToRawWhenOmitted()
    {
        var result = _parser.Parse("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=12");

        Assert.True(result.IsValid);
        Assert.Equal("raw", result.Message!.Unit);
    }

    [Theory]
    [InlineData("", ProtocolErrorCodes.EmptyMessage)]
    [InlineData("hello", ProtocolErrorCodes.MissingMarker)]
    [InlineData("GASALERT", ProtocolErrorCodes.MissingMarker)]
    [InlineData("OTHER|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1", ProtocolErrorCodes.MissingMarker)]
    public void RejectsMalformedEnvelope(string body, string expectedCode)
    {
        var result = _parser.Parse(body);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsUnsupportedVersion()
    {
        var result = _parser.Parse(
            "GASALERT|v=2|device=GAS-001|event=STATUS|level=NORMAL|reading=1");

        Assert.Equal(SmsParseStatus.UnsupportedVersion, result.Status);
        Assert.Equal(ProtocolErrorCodes.UnsupportedVersion, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsMissingVersion()
    {
        var result = _parser.Parse("GASALERT|device=GAS-001|event=STATUS|level=NORMAL|reading=1");

        Assert.Equal(ProtocolErrorCodes.MissingVersion, result.PrimaryErrorCode);
    }

    [Theory]
    [InlineData("AB", ProtocolErrorCodes.InvalidDeviceCode)]
    [InlineData("GAS 001", ProtocolErrorCodes.InvalidDeviceCode)]
    [InlineData("GAS@001", ProtocolErrorCodes.InvalidDeviceCode)]
    [InlineData("-GAS001", ProtocolErrorCodes.InvalidDeviceCode)]
    public void RejectsInvalidDeviceCodes(string device, string expectedCode)
    {
        var result = _parser.Parse(
            $"GASALERT|v=1|device={device}|event=STATUS|level=NORMAL|reading=1");

        Assert.Equal(expectedCode, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsUnknownEvent()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=FIRE|level=CRITICAL|reading=1");

        Assert.Equal(ProtocolErrorCodes.UnsupportedEvent, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsLevelThatDoesNotMatchEvent()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=NORMAL|reading=1");

        Assert.Equal(ProtocolErrorCodes.EventLevelMismatch, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsUnknownLevel()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=SAFE|reading=1");

        Assert.Equal(ProtocolErrorCodes.UnsupportedLevel, result.PrimaryErrorCode);
    }

    [Theory]
    [InlineData("8.5", ProtocolErrorCodes.InvalidReading)]
    [InlineData("-1", ProtocolErrorCodes.InvalidReading)]
    [InlineData("abc", ProtocolErrorCodes.InvalidReading)]
    [InlineData("1000001", ProtocolErrorCodes.ReadingOutOfRange)]
    public void RejectsInvalidReadings(string reading, string expectedCode)
    {
        var result = _parser.Parse(
            $"GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading={reading}");

        Assert.Equal(expectedCode, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsMissingReading()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL");

        Assert.Equal(ProtocolErrorCodes.MissingReading, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsInvalidSequence()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|seq=-4");

        Assert.Equal(ProtocolErrorCodes.InvalidSequence, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsDuplicateFields()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|device=GAS-002|event=STATUS|level=NORMAL|reading=1");

        Assert.Equal(ProtocolErrorCodes.DuplicateField, result.PrimaryErrorCode);
    }

    [Fact]
    public void IgnoresTimestampWithoutOffsetWithWarning()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|ts=2026-10-28T13:00:00");

        Assert.True(result.IsValid);
        Assert.Null(result.Message!.DeviceTimestamp);
        Assert.Contains(result.Issues, x => x.Code == ProtocolErrorCodes.TimestampWithoutOffset && !x.IsError);
    }

    [Fact]
    public void UnknownFieldsAreWarningsNotFailures()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|future=thing");

        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, x => x.Code == ProtocolErrorCodes.UnknownField && !x.IsError);
    }

    [Fact]
    public void RejectsOverlongBody()
    {
        var body = "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|pad=" + new string('x', 500);
        var result = _parser.Parse(body);

        Assert.Equal(ProtocolErrorCodes.MessageTooLong, result.PrimaryErrorCode);
    }

    [Fact]
    public void RejectsInvalidUnit()
    {
        var result = _parser.Parse(
            "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|unit=ppm<script>");

        Assert.Equal(ProtocolErrorCodes.InvalidUnit, result.PrimaryErrorCode);
    }

    [Fact]
    public void AcceptsWhitespaceAroundSegments()
    {
        var result = _parser.Parse(
            "  GASALERT | v = 1 | device = GAS-001 | event = GAS_WARNING | level = WARNING | reading = 42  ");

        Assert.True(result.IsValid);
        Assert.Equal(Severity.Warning, result.Message!.Severity);
    }

    [Theory]
    [InlineData("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|note=line\r\nbreak")]
    [InlineData("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|note=tab\tvalue")]
    [InlineData("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|note=null\u0000value")]
    public void RejectsEmbeddedControlCharacters(string body)
    {
        var result = _parser.Parse(body);

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolErrorCodes.ControlCharacter, result.PrimaryErrorCode);
    }

    [Theory]
    [InlineData("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|note=température")]
    [InlineData("GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=1|note=☃")]
    public void RejectsNonAsciiProtocolContent(string body)
    {
        var result = _parser.Parse(body);

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolErrorCodes.NonAsciiContent, result.PrimaryErrorCode);
    }
}

internal static class SmsProtocolParserFixture
{
    public static bool HasNoErrors(SmsParseResult result) => result.Issues.All(x => !x.IsError);
}
