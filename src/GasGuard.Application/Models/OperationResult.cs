namespace GasGuard.Application.Models;

public sealed record OperationResult(bool Success, string Message, IReadOnlyList<string>? ValidationErrors = null)
{
    public static OperationResult Ok(string message) => new(true, message);

    public static OperationResult Fail(string message, IEnumerable<string>? validationErrors = null) =>
        new(false, message, validationErrors?.ToArray() ?? []);
}
