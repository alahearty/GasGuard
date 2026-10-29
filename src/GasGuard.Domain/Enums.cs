namespace GasGuard.Domain;

public enum Severity
{
    Unknown = 0,
    Normal = 1,
    Warning = 2,
    Critical = 3
}

public enum DeviceReportedState
{
    Unknown = 0,
    Normal = 1,
    Warning = 2,
    Critical = 3
}

public enum DeviceOperationalState
{
    Unknown,
    Normal,
    Warning,
    Critical,
    Offline,
    CommunicationError
}

public enum GasEventCode
{
    Unknown = 0,
    Status = 1,
    GasWarning = 2,
    GasCritical = 3
}

public enum IncidentStatus
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3
}

public enum IncidentTimeSource
{
    DeviceReported = 1,
    DesktopReceipt = 2,
    DesktopReceiptFallback = 3
}

public enum SmsProcessingStatus
{
    Pending = 0,
    Processed = 1,
    Duplicate = 2,
    Invalid = 3,
    UnknownDevice = 4,
    UnauthorizedSender = 5,
    OutOfOrder = 6,
    Failed = 7,
    SimulationRejected = 8
}

public enum ContactRole
{
    SafetyOfficer = 1,
    Supervisor = 2,
    Operator = 3,
    Maintenance = 4,
    Other = 5
}

public enum CommunicationDirection
{
    Inbound = 1,
    Outbound = 2,
    System = 3
}

public enum CommunicationKind
{
    Connection = 1,
    AtCommand = 2,
    Polling = 3,
    SmsReceived = 4,
    SmsSend = 5,
    Processing = 6,
    Backup = 7,
    Reconnect = 8
}

public enum CommunicationOutcome
{
    Success = 1,
    Failure = 2,
    Timeout = 3,
    Rejected = 4,
    Information = 5,
    Skipped = 6
}

public enum AuditOutcome
{
    Success = 1,
    Failure = 2,
    Denied = 3
}

public enum SerialParity
{
    None = 0,
    Odd = 1,
    Even = 2,
    Mark = 3,
    Space = 4
}

public enum SerialStopBits
{
    One = 0,
    OnePointFive = 1,
    Two = 2
}

public enum SerialHandshake
{
    None = 0,
    XOnXOff = 1,
    RequestToSend = 2,
    RequestToSendXOnXOff = 3
}

public enum DetectorType
{
    CombustibleGas = 1,
    CarbonMonoxide = 2,
    LiquefiedPetroleumGas = 3,
    Methane = 4,
    MultiGas = 5,
    Other = 6
}
