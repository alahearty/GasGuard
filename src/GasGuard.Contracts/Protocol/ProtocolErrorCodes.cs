namespace GasGuard.Contracts.Protocol;

public static class ProtocolErrorCodes
{
    public const string EmptyMessage = "EMPTY_MESSAGE";
    public const string MessageTooLong = "MESSAGE_TOO_LONG";
    public const string MissingMarker = "MISSING_MARKER";
    public const string ControlCharacter = "CONTROL_CHARACTER";
    public const string NonAsciiContent = "NON_ASCII_CONTENT";
    public const string MalformedField = "MALFORMED_FIELD";
    public const string DuplicateField = "DUPLICATE_FIELD";
    public const string MissingVersion = "MISSING_VERSION";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string MissingDevice = "MISSING_DEVICE";
    public const string InvalidDeviceCode = "INVALID_DEVICE_CODE";
    public const string MissingEvent = "MISSING_EVENT";
    public const string UnsupportedEvent = "UNSUPPORTED_EVENT";
    public const string MissingLevel = "MISSING_LEVEL";
    public const string UnsupportedLevel = "UNSUPPORTED_LEVEL";
    public const string EventLevelMismatch = "EVENT_LEVEL_MISMATCH";
    public const string MissingReading = "MISSING_READING";
    public const string InvalidReading = "INVALID_READING";
    public const string ReadingOutOfRange = "READING_OUT_OF_RANGE";
    public const string InvalidUnit = "INVALID_UNIT";
    public const string InvalidSequence = "INVALID_SEQUENCE";
    public const string InvalidTimestamp = "INVALID_TIMESTAMP";
    public const string TimestampWithoutOffset = "TIMESTAMP_WITHOUT_OFFSET";
    public const string UnknownField = "UNKNOWN_FIELD";
}
