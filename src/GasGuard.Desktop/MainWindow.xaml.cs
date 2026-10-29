using System.ComponentModel;
using System.Windows;
using GasGuard.Desktop.Services;
using GasGuard.Desktop.ViewModels;

namespace GasGuard.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IApplicationOperationGuard _operationGuard;

    public MainWindow(MainViewModel viewModel, IApplicationOperationGuard operationGuard)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        WindowTheme.FitToMonitorWorkArea(this);
        _viewModel = viewModel;
        _operationGuard = operationGuard;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_operationGuard.IsOperationActive)
        {
            return;
        }

        e.Cancel = true;
        MessageBox.Show(
            $"A maintenance operation is still running: {_operationGuard.ActiveDescription}.\n\nWait for it to finish before closing GasGuard Desktop.",
            "Maintenance in progress",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
