namespace GasGuard.Contracts.Modem;

public enum ModemConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4
}

public enum SimReadiness
{
    Unknown = 0,
    Ready = 1,
    PinRequired = 2,
    PukRequired = 3,
    NotInserted = 4,
    Busy = 5,
    Error = 6
}

public enum NetworkRegistrationState
{
    Unknown = 0,
    NotRegistered = 1,
    RegisteredHome = 2,
    Searching = 3,
    Denied = 4,
    RegisteredRoaming = 5
}

public enum SmsSendOutcome
{
    SubmittedToModem = 1,
    RejectedByModem = 2,
    Timeout = 3,
    NotConnected = 4,
    Cancelled = 5,
    SimulatedFailure = 6
}

public enum SimulatedSendMode
{
    Success = 0,
    ModemRejected = 1,
    Timeout = 2
}

public enum ModemOperationOutcome
{
    Success = 1,
    NotConnected = 2,
    Timeout = 3,
    Rejected = 4,
    Failure = 5,
    Cancelled = 6
}
