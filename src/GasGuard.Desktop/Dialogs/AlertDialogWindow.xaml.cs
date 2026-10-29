using System.Windows;
using GasGuard.Desktop.Services;

namespace GasGuard.Desktop.Dialogs;

public partial class AlertDialogWindow : Window
{
    public AlertDialogWindow(
        string heading,
        string body,
        string confirmText,
        string? cancelText,
        bool destructive)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        HeadingText.Text = heading;
        BodyText.Text = body;
        ConfirmButton.Content = confirmText;
        if (destructive)
        {
            ConfirmButton.Style = (Style)FindResource("DangerButton");
        }

        if (cancelText is not null)
        {
            CancelButton.Content = cancelText;
            CancelButton.Visibility = Visibility.Visible;
        }
    }

    private void ConfirmClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
