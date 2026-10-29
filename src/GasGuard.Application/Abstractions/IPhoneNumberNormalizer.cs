namespace GasGuard.Application.Abstractions;

public interface IPhoneNumberNormalizer
{
    bool TryNormalize(string? input, out string normalized, out string? error);

    string FormatForDisplay(string? input);

    bool NumbersMatch(string? expected, string? actual);
}
