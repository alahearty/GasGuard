using System.Windows;
using GasGuard.Desktop.Services;
using GasGuard.Desktop.ViewModels.Dialogs;

namespace GasGuard.Desktop.Dialogs;

public partial class ContactEditorWindow : Window
{
    public ContactEditorWindow(ContactEditorDialogViewModel viewModel, string title)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Title = title;
        DataContext = viewModel;
        viewModel.CloseRequested += accepted =>
        {
            DialogResult = accepted;
        };
    }
}
