namespace GasGuard.Application.Services;

public static class SmsPrivacy
{
    public static string MaskPhoneNumber(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "[unknown]";
        }

        if (text.Length <= 4)
        {
            return "****";
        }

        return "****" + text[^4..];
    }
}
