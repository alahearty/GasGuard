using System.Text.RegularExpressions;
using GasGuard.Application.Abstractions;

namespace GasGuard.Application.Services;

public sealed partial class NigerianPhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private const string CountryCode = "+234";
    private const int NationalSignificantDigits = 10;

    public bool TryNormalize(string? input, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "A phone number is required.";
            return false;
        }

        var cleaned = Separators().Replace(input.Trim(), string.Empty);
        if (cleaned.StartsWith("00", StringComparison.Ordinal))
        {
            cleaned = "+" + cleaned[2..];
        }

        string national;

        if (cleaned.StartsWith(CountryCode, StringComparison.Ordinal))
        {
            national = cleaned[CountryCode.Length..];
        }
        else if (cleaned.StartsWith("234", StringComparison.Ordinal) && cleaned.Length == 13)
        {
            national = cleaned[3..];
        }
        else if (cleaned.StartsWith('0') && cleaned.Length == 11)
        {
            national = cleaned[1..];
        }
        else if (cleaned.Length == NationalSignificantDigits)
        {
            national = cleaned;
        }
        else
        {
            error = "Use a Nigerian mobile number in the form 0803 123 4567 or +234 803 123 4567.";
            return false;
        }

        if (!NigerianMobile().IsMatch(national))
        {
            error = "The number is not a valid Nigerian mobile number. The subscriber number must contain 10 digits and start with 7, 8 or 9.";
            return false;
        }

        normalized = CountryCode + national;
        return true;
    }

    public string FormatForDisplay(string? input)
    {
        if (!TryNormalize(input, out var normalized, out _))
        {
            return string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();
        }

        var national = normalized[CountryCode.Length..];
        return $"{CountryCode} {national[..3]} {national[3..6]} {national[6..]}";
    }

    public bool NumbersMatch(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        if (TryNormalize(expected, out var left, out _) && TryNormalize(actual, out var right, out _))
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        static string Clean(string value) => Separators().Replace(value.Trim(), string.Empty);
        return string.Equals(Clean(expected), Clean(actual), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Clean(expected), Clean(actual).Replace("+", string.Empty), StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[\s\-().\u00A0]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[789]\d{9}$")]
    private static partial Regex NigerianMobile();
}
