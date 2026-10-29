namespace GasGuard.Contracts.Modem;

public static class ModemStatusParser
{
    public static SimReadiness ParseSimReadiness(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return SimReadiness.Unknown;
        }

        var upper = response.ToUpperInvariant();
        if (upper.Contains("+CPIN: READY"))
        {
            return SimReadiness.Ready;
        }

        if (upper.Contains("SIM PIN"))
        {
            return SimReadiness.PinRequired;
        }

        if (upper.Contains("SIM PUK"))
        {
            return SimReadiness.PukRequired;
        }

        if (upper.Contains("NOT INSERTED") || upper.Contains("NOT READY"))
        {
            return upper.Contains("NOT INSERTED") ? SimReadiness.NotInserted : SimReadiness.Busy;
        }

        if (upper.Contains("ERROR"))
        {
            return SimReadiness.Error;
        }

        return SimReadiness.Unknown;
    }

    public static string SimReadinessText(SimReadiness readiness) => readiness switch
    {
        SimReadiness.Ready => "SIM ready",
        SimReadiness.PinRequired => "SIM PIN required",
        SimReadiness.PukRequired => "SIM PUK required",
        SimReadiness.NotInserted => "SIM not inserted",
        SimReadiness.Busy => "SIM busy",
        SimReadiness.Error => "SIM error",
        _ => "SIM status unknown"
    };

    public static int? ParseSignalQuality(string response)
    {
        var value = ExtractPrefixedValue(response, "+CSQ:");
        if (value is null)
        {
            return null;
        }

        var segments = value.Split(',', StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || !int.TryParse(segments[0], out var quality))
        {
            return null;
        }

        return quality is >= 0 and <= 31 ? quality : null;
    }

    public static string SignalDescription(int? quality) => quality switch
    {
        null => "Signal quality unknown",
        <= 9 => "Weak signal",
        <= 14 => "Fair signal",
        <= 19 => "Good signal",
        _ => "Excellent signal"
    };

    public static NetworkRegistrationState ParseRegistration(string response)
    {
        var value = ExtractPrefixedValue(response, "+CREG:");
        if (string.IsNullOrWhiteSpace(value))
        {
            return NetworkRegistrationState.Unknown;
        }

        var segments = value.Split(',', StringSplitOptions.TrimEntries);
        var statusText = segments.Length >= 2 ? segments[1] : segments[0];
        if (!int.TryParse(statusText, out var status))
        {
            return NetworkRegistrationState.Unknown;
        }

        return status switch
        {
            0 => NetworkRegistrationState.NotRegistered,
            1 => NetworkRegistrationState.RegisteredHome,
            2 => NetworkRegistrationState.Searching,
            3 => NetworkRegistrationState.Denied,
            4 => NetworkRegistrationState.Unknown,
            5 => NetworkRegistrationState.RegisteredRoaming,
            _ => NetworkRegistrationState.Unknown
        };
    }

    public static string RegistrationText(NetworkRegistrationState state) => state switch
    {
        NetworkRegistrationState.RegisteredHome => "Registered on home network",
        NetworkRegistrationState.RegisteredRoaming => "Registered while roaming",
        NetworkRegistrationState.Searching => "Searching for network",
        NetworkRegistrationState.Denied => "Network registration denied",
        NetworkRegistrationState.NotRegistered => "Not registered on network",
        _ => "Registration unknown"
    };

    public static string? ParseNetworkOperator(string response)
    {
        var value = ExtractPrefixedValue(response, "+COPS:");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        return parts[2].Trim('"');
    }

    public static string? ParseModemIdentity(string atiResponse)
    {
        if (string.IsNullOrWhiteSpace(atiResponse))
        {
            return null;
        }

        var lines = atiResponse
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.Equals("ATI", StringComparison.OrdinalIgnoreCase) &&
                           !line.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                           !line.StartsWith("+", StringComparison.Ordinal))
            .ToArray();

        return lines.Length == 0 ? null : string.Join(" | ", lines);
    }

    private static string? ExtractPrefixedValue(string? response, string prefix)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var index = response.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var lineEnd = response.IndexOfAny(['\r', '\n'], index);
        var end = lineEnd < 0 ? response.Length : lineEnd;
        return response[(index + prefix.Length)..end].Trim();
    }
}
