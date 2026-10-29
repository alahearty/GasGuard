using System.Diagnostics;
using System.IO;
using System.Windows;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Desktop.Dialogs;
using GasGuard.Desktop.ViewModels.Dialogs;
using Microsoft.Win32;

namespace GasGuard.Desktop.Services;

public sealed class DialogService(IPhoneNumberNormalizer phoneNumbers) : IDialogService
{
    public bool Confirm(string title, string message, string confirmText = "Continue", bool destructive = false)
    {
        var dialog = new AlertDialogWindow(title, message, confirmText, "Cancel", destructive)
        {
            Owner = ActiveOwner()
        };
        return dialog.ShowDialog() == true;
    }

    public void ShowInfo(string title, string message)
    {
        var dialog = new AlertDialogWindow(title, message, "Close", null, false)
        {
            Owner = ActiveOwner()
        };
        dialog.ShowDialog();
    }

    public void ShowError(string title, string message)
    {
        var dialog = new AlertDialogWindow(title, message, "Close", null, true)
        {
            Owner = ActiveOwner()
        };
        dialog.ShowDialog();
    }

    public DeviceInput? EditDevice(DeviceInput? existing, string title)
    {
        var viewModel = new DeviceEditorDialogViewModel(phoneNumbers, existing);
        var dialog = new DeviceEditorWindow(viewModel, title)
        {
            Owner = ActiveOwner()
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    public ContactInput? EditContact(ContactInput? existing, string title)
    {
        var viewModel = new ContactEditorDialogViewModel(phoneNumbers, existing);
        var dialog = new ContactEditorWindow(viewModel, title)
        {
            Owner = ActiveOwner()
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    public string? ChooseSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    }

    public string? ChooseOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    }

    private static Window? ActiveOwner() =>
        System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(x => x.IsActive) ?? System.Windows.Application.Current?.MainWindow;
}

public sealed class AppShutdownService : IAppShutdownService
{
    public void Shutdown() => System.Windows.Application.Current?.Shutdown();
}

public sealed class ShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}

