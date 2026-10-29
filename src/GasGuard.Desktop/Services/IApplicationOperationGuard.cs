namespace GasGuard.Desktop.Services;

public interface IApplicationOperationGuard
{
    event Action? StateChanged;
    bool IsOperationActive { get; }
    string? ActiveDescription { get; }
    IDisposable Begin(string description);
}
