using GasGuard.Application.Exporting;
using GasGuard.Application.Services;
using GasGuard.Application.Time;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;

namespace GasGuard.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void DeviceWithNoCommunicationIsUnknown()
    {
        var device = ActiveDevice();
        Assert.Equal(
            DeviceOperationalState.Unknown,
            device.GetOperationalState(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void DeviceWithStaleCommunicationIsOffline()
    {
        var device = ActiveDevice();
        device.LastSuccessfulCommunicationUtc = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
        device.LastReportedState = DeviceReportedState.Normal;

        Assert.Equal(
            DeviceOperationalState.Offline,
            device.GetOperationalState(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void RecentNormalStateIsNormal()
    {
        var device = ActiveDevice();
        device.LastSuccessfulCommunicationUtc = new DateTime(2026, 10, 28, 11, 55, 0, DateTimeKind.Utc);
        device.LastReportedState = DeviceReportedState.Normal;

        Assert.Equal(
            DeviceOperationalState.Normal,
            device.GetOperationalState(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void NewerCommunicationErrorTakesPriority()
    {
        var device = ActiveDevice();
        device.LastSuccessfulCommunicationUtc = new DateTime(2026, 10, 28, 11, 55, 0, DateTimeKind.Utc);
        device.LastReportedState = DeviceReportedState.Normal;
        device.LastCommunicationErrorUtc = new DateTime(2026, 10, 28, 11, 58, 0, DateTimeKind.Utc);

        Assert.Equal(
            DeviceOperationalState.CommunicationError,
            device.GetOperationalState(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void InactiveDeviceIsOfflineEvenWithRecentData()
    {
        var device = ActiveDevice();
        device.IsActive = false;
        device.LastSuccessfulCommunicationUtc = new DateTime(2026, 10, 28, 11, 59, 0, DateTimeKind.Utc);
        device.LastReportedState = DeviceReportedState.Critical;

        Assert.Equal(
            DeviceOperationalState.Offline,
            device.GetOperationalState(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void IncidentMustBeAcknowledgedBeforeResolution()
    {
        var incident = NewIncident();
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        Assert.Throws<DomainException>(() => incident.Resolve("operator", "cleared", now));

        incident.Acknowledge("operator", "seen", now);
        Assert.Equal(IncidentStatus.Acknowledged, incident.Status);
        Assert.NotNull(incident.AcknowledgedAtUtc);

        incident.Resolve("operator", "gas cleared and area ventilated", now.AddMinutes(5));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal("gas cleared and area ventilated", incident.ResolutionNotes);
    }

    [Fact]
    public void AcknowledgementDoesNotResolveIncident()
    {
        var incident = NewIncident();
        incident.Acknowledge("operator", null, DateTime.UtcNow);

        Assert.Equal(IncidentStatus.Acknowledged, incident.Status);
        Assert.Null(incident.ResolvedAtUtc);
        Assert.Null(incident.ResolvedBy);
    }

    [Fact]
    public void ResolvingRequiresNotes()
    {
        var incident = NewIncident();
        incident.Acknowledge("operator", null, DateTime.UtcNow);

        Assert.Throws<DomainException>(
            () => incident.Resolve("operator", "   ", DateTime.UtcNow));
    }

    [Fact]
    public void ResolvedIncidentCannotBeAcknowledged()
    {
        var incident = NewIncident();
        incident.Acknowledge("operator", null, DateTime.UtcNow);
        incident.Resolve("operator", "closed", DateTime.UtcNow);

        Assert.Throws<DomainException>(() => incident.Acknowledge("operator2", null, DateTime.UtcNow));
    }

    [Fact]
    public void LagosTimeConversionHandlesDateRollover()
    {
        var utc = new DateTime(2026, 10, 28, 23, 30, 0, DateTimeKind.Utc);
        var local = AfricaLagosTime.ToLocal(utc);

        Assert.Equal(new DateTime(2026, 10, 29, 0, 30, 0), local);
        Assert.Equal(new DateOnly(2026, 10, 29), AfricaLagosTime.ToLocalDate(utc));
    }

    [Fact]
    public void CsvEscapingQuotesAndSeparators()
    {
        Assert.Equal("plain", CsvBuilder.Escape("plain"));
        Assert.Equal("\"a,b\"", CsvBuilder.Escape("a,b"));
        Assert.Equal("\"say \"\"hello\"\"\"", CsvBuilder.Escape("say \"hello\""));
        Assert.Equal("\"line1\nline2\"", CsvBuilder.Escape("line1\nline2"));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+2348031234567", "'+2348031234567")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1:A2)", "'@SUM(A1:A2)")]
    [InlineData("  =CMD", "'  =CMD")]
    public void CsvEscapingNeutralizesSpreadsheetFormulaStarts(string value, string expected)
    {
        Assert.Equal(expected, CsvBuilder.Escape(value));
    }

    [Fact]
    public void FingerprintIsStableAndSenderAware()
    {
        var first = SmsFingerprint.Create("+2348031234567", "GASALERT|x");
        var second = SmsFingerprint.Create("+2348031234567", "GASALERT|x");
        var differentSender = SmsFingerprint.Create("+2348031234568", "GASALERT|x");

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentSender);
        Assert.Equal(64, first.Length);
    }

    [Theory]
    [InlineData("+CPIN: READY", SimReadiness.Ready)]
    [InlineData("+CPIN: SIM PIN", SimReadiness.PinRequired)]
    [InlineData("+CPIN: SIM PUK", SimReadiness.PukRequired)]
    [InlineData("ERROR", SimReadiness.Error)]
    public void ParsesSimReadiness(string response, SimReadiness expected)
    {
        Assert.Equal(expected, ModemStatusParser.ParseSimReadiness(response));
    }

    [Fact]
    public void ParsesSignalQuality()
    {
        Assert.Equal(20, ModemStatusParser.ParseSignalQuality("+CSQ: 20,99\r\nOK"));
        Assert.Null(ModemStatusParser.ParseSignalQuality("+CSQ: 99,99"));
        Assert.Null(ModemStatusParser.ParseSignalQuality("ERROR"));
    }

    [Theory]
    [InlineData("+CREG: 0,1", NetworkRegistrationState.RegisteredHome)]
    [InlineData("+CREG: 0,2", NetworkRegistrationState.Searching)]
    [InlineData("+CREG: 0,3", NetworkRegistrationState.Denied)]
    [InlineData("+CREG: 0,5", NetworkRegistrationState.RegisteredRoaming)]
    [InlineData("ERROR", NetworkRegistrationState.Unknown)]
    public void ParsesNetworkRegistration(string response, NetworkRegistrationState expected)
    {
        Assert.Equal(expected, ModemStatusParser.ParseRegistration(response));
    }

    [Fact]
    public void ParsesNetworkOperatorAndIdentity()
    {
        Assert.Equal("MTN", ModemStatusParser.ParseNetworkOperator("+COPS: 0,0,\"MTN\",7"));
        Assert.Contains("SIM800", ModemStatusParser.ParseModemIdentity("SIM800 R14.18\r\nOK"));
    }

    private static GasDevice ActiveDevice() => new()
    {
        Id = Guid.NewGuid(),
        DeviceCode = "GAS-001",
        IsActive = true
    };

    private static Incident NewIncident() => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = Guid.NewGuid(),
        TriggeringSmsMessageId = Guid.NewGuid(),
        DeviceCodeSnapshot = "GAS-001",
        DeviceNameSnapshot = "Test detector",
        LocationSnapshot = "Lab",
        Severity = Severity.Critical,
        OccurredAtUtc = DateTime.UtcNow,
        ReceivedAtUtc = DateTime.UtcNow,
        OpenedAtUtc = DateTime.UtcNow,
        DedupKey = Guid.NewGuid().ToString()
    };
}
