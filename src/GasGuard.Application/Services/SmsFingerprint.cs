using System.Security.Cryptography;
using System.Text;

namespace GasGuard.Application.Services;

public static class SmsFingerprint
{
    public static string Create(string sender, string body)
    {
        var normalizedSender = (sender ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedBody = (body ?? string.Empty).Trim();
        var bytes = Encoding.UTF8.GetBytes($"{normalizedSender}\n{normalizedBody}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
