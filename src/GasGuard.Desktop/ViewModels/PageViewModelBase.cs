using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public abstract partial class PageViewModelBase : ObservableObject
{
    private readonly ILogger _logger;

    protected PageViewModelBase(ILogger logger)
    {
        _logger = logger;
    }

    public abstract string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public Task ActivateAsync() => ExecuteRefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync() => await ExecuteRefreshAsync();

    protected abstract Task LoadAsync();

    protected void SetStatus(string? message)
    {
        StatusMessage = message;
    }

    protected void SetError(string? message)
    {
        ErrorMessage = message;
    }

    protected async Task RunOperationAsync(string operation, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Page {Page} operation {Operation} failed.", Title, operation);
            ErrorMessage = $"{operation} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteRefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Page {Page} failed to refresh.", Title);
            ErrorMessage = $"This workspace could not be refreshed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
