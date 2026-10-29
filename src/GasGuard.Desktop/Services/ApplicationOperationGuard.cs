namespace GasGuard.Desktop.Services;

public sealed class ApplicationOperationGuard : IApplicationOperationGuard
{
    private readonly object _sync = new();
    private int _activeCount;
    private string? _activeDescription;

    public event Action? StateChanged;

    public bool IsOperationActive
    {
        get
        {
            lock (_sync)
            {
                return _activeCount > 0;
            }
        }
    }

    public string? ActiveDescription
    {
        get
        {
            lock (_sync)
            {
                return _activeDescription;
            }
        }
    }

    public IDisposable Begin(string description)
    {
        lock (_sync)
        {
            _activeCount++;
            _activeDescription = description;
        }

        StateChanged?.Invoke();
        return new Scope(this);
    }

    private void End()
    {
        lock (_sync)
        {
            _activeCount = Math.Max(0, _activeCount - 1);
            if (_activeCount == 0)
            {
                _activeDescription = null;
            }
        }

        StateChanged?.Invoke();
    }

    private sealed class Scope(ApplicationOperationGuard owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.End();
            }
        }
    }
}
