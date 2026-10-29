using System.Globalization;
using System.Text.RegularExpressions;
using GasGuard.Application.Abstractions;
using GasGuard.Contracts.Protocol;
using GasGuard.Domain;

namespace GasGuard.Application.Services;

public sealed partial class SmsProtocolParser : ISmsProtocolParser
{
    private static readonly HashSet<string> KnownFields =
        new(StringComparer.OrdinalIgnoreCase) { "v", "device", "event", "level", "reading", "unit", "seq", "ts" };

    public SmsParseResult Parse(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.Malformed,
                ProtocolErrorCodes.EmptyMessage,
                "The SMS body is empty.");
        }

        var body = rawBody.Trim();
        if (body.Length > SmsProtocolConstants.MaximumBodyLength)
        {
            return SmsParseResult.Failure(
                SmsParseStatus.Malformed,
                ProtocolErrorCodes.MessageTooLong,
                $"The SMS body exceeds the {SmsProtocolConstants.MaximumBodyLength}-character protocol limit.");
        }

        if (body.Any(character => character is < ' ' or '\u007F'))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.Malformed,
                ProtocolErrorCodes.ControlCharacter,
                "The protocol does not permit control characters or embedded line breaks.");
        }

        if (body.Any(character => character > '\u007F'))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.NonAsciiContent,
                "The v1 SMS protocol is ASCII-only. Unicode or UCS-2 content is not supported; enable a documented encoding protocol before sending it.");
        }

        var segments = body.Split('|');
        if (segments.Length < 2 || !segments[0].Trim().Equals(SmsProtocolConstants.Marker, StringComparison.OrdinalIgnoreCase))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.Malformed,
                ProtocolErrorCodes.MissingMarker,
                $"The SMS must begin with {SmsProtocolConstants.Marker}| followed by key=value fields.");
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<ProtocolIssue>();

        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i].Trim();
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return SmsParseResult.Failure(
                    SmsParseStatus.Malformed,
                    ProtocolErrorCodes.MalformedField,
                    $"Field {i} is not in key=value format.");
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();
            if (key.Length == 0)
            {
                return SmsParseResult.Failure(
                    SmsParseStatus.Malformed,
                    ProtocolErrorCodes.MalformedField,
                    $"Field {i} has an empty key.");
            }

            if (!KnownFields.Contains(key))
            {
                warnings.Add(ProtocolIssue.Warning(
                    ProtocolErrorCodes.UnknownField,
                    $"Unknown field '{key}' was ignored."));
                continue;
            }

            if (!fields.TryAdd(key.ToLowerInvariant(), value))
            {
                return SmsParseResult.Failure(
                    SmsParseStatus.Malformed,
                    ProtocolErrorCodes.DuplicateField,
                    $"The field '{key}' appears more than once.");
            }
        }

        if (!fields.TryGetValue("v", out var version) || string.IsNullOrWhiteSpace(version))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.MissingVersion,
                "The protocol version field 'v' is required.");
        }

        if (!string.Equals(version, SmsProtocolConstants.CurrentVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.UnsupportedVersion,
                ProtocolErrorCodes.UnsupportedVersion,
                $"Protocol version '{version}' is not supported. Supported version: {SmsProtocolConstants.CurrentVersion}.");
        }

        if (!fields.TryGetValue("device", out var deviceValue) || string.IsNullOrWhiteSpace(deviceValue))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.MissingDevice,
                "The device field 'device' is required.");
        }

        var deviceCode = deviceValue.Trim().ToUpperInvariant();
        if (!DeviceCodePattern().IsMatch(deviceCode))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.InvalidDeviceCode,
                "The device code must be 3-32 characters using letters, digits, hyphens or underscores and must start with a letter or digit.");
        }

        if (!fields.TryGetValue("event", out var eventValue) || string.IsNullOrWhiteSpace(eventValue))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.MissingEvent,
                "The event field 'event' is required.");
        }

        if (!GasProtocolMap.TryMapEvent(eventValue, out var eventCode, out var eventText))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.UnsupportedEvent,
                $"Event '{eventValue}' is not supported. Supported events: {string.Join(", ", SmsProtocolConstants.SupportedEvents)}.");
        }

        if (!fields.TryGetValue("level", out var levelValue) || string.IsNullOrWhiteSpace(levelValue))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.MissingLevel,
                "The severity field 'level' is required.");
        }

        var levelText = levelValue.Trim().ToUpperInvariant();
        if (!SmsProtocolConstants.SupportedLevels.Contains(levelText, StringComparer.Ordinal))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.UnsupportedLevel,
                $"Level '{levelValue}' is not supported. Supported levels: {string.Join(", ", SmsProtocolConstants.SupportedLevels)}.");
        }

        var expectedLevel = GasProtocolMap.ExpectedLevel(eventCode);
        if (!string.Equals(levelText, expectedLevel, StringComparison.Ordinal))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.EventLevelMismatch,
                $"Event {eventText} must use level {expectedLevel}; received {levelText}.");
        }

        if (!fields.TryGetValue("reading", out var readingValue) || string.IsNullOrWhiteSpace(readingValue))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.MissingReading,
                "The reading field 'reading' is required.");
        }

        if (!int.TryParse(readingValue, NumberStyles.None, CultureInfo.InvariantCulture, out var reading))
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.InvalidReading,
                "The reading must be a non-negative whole number.");
        }

        if (reading > SmsProtocolConstants.MaximumReading)
        {
            return SmsParseResult.Failure(
                SmsParseStatus.ValidationFailed,
                ProtocolErrorCodes.ReadingOutOfRange,
                $"The reading must be between 0 and {SmsProtocolConstants.MaximumReading}.");
        }

        var unit = SmsProtocolConstants.DefaultUnit;
        if (fields.TryGetValue("unit", out var unitValue) && !string.IsNullOrWhiteSpace(unitValue))
        {
            unit = unitValue.Trim();
            if (unit.Length > SmsProtocolConstants.MaximumUnitLength || !UnitPattern().IsMatch(unit))
            {
                return SmsParseResult.Failure(
                    SmsParseStatus.ValidationFailed,
                    ProtocolErrorCodes.InvalidUnit,
                    "The unit must be 1-16 characters using letters, digits, %, ., _, / or -.");
            }
        }

        long? sequenceNumber = null;
        if (fields.TryGetValue("seq", out var sequenceValue) && !string.IsNullOrWhiteSpace(sequenceValue))
        {
            if (!long.TryParse(sequenceValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSequence))
            {
                return SmsParseResult.Failure(
                    SmsParseStatus.ValidationFailed,
                    ProtocolErrorCodes.InvalidSequence,
                    "The sequence number 'seq' must be a non-negative whole number.");
            }

            sequenceNumber = parsedSequence;
        }

        DateTimeOffset? deviceTimestamp = null;
        var hasExplicitOffset = false;
        if (fields.TryGetValue("ts", out var timestampValue) && !string.IsNullOrWhiteSpace(timestampValue))
        {
            if (!TimestampPattern().IsMatch(timestampValue))
            {
                warnings.Add(ProtocolIssue.Warning(
                    ProtocolErrorCodes.TimestampWithoutOffset,
                    "The device timestamp must be ISO-8601 with an explicit UTC offset (for example 2026-10-28T14:05:00+01:00). The timestamp was ignored."));
            }
            else if (!DateTimeOffset.TryParse(timestampValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTimestamp))
            {
                warnings.Add(ProtocolIssue.Warning(
                    ProtocolErrorCodes.InvalidTimestamp,
                    "The device timestamp could not be parsed and was ignored."));
            }
            else
            {
                deviceTimestamp = parsedTimestamp;
                hasExplicitOffset = true;
            }
        }

        var parsed = new ParsedGasMessage(
            version,
            deviceCode,
            eventCode,
            eventText,
            GasProtocolMap.ToSeverity(eventCode),
            levelText,
            reading,
            unit,
            sequenceNumber,
            deviceTimestamp,
            hasExplicitOffset,
            warnings);

        return SmsParseResult.Valid(parsed);
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9_-]{2,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceCodePattern();

    [GeneratedRegex(@"^[A-Za-z%][A-Za-z0-9%._/-]{0,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex UnitPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TimestampPattern();
}
