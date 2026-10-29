using GasGuard.Application.Models;

namespace GasGuard.Desktop.Services;

public interface IDialogService
{
    bool Confirm(string title, string message, string confirmText = "Continue", bool destructive = false);

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);

    DeviceInput? EditDevice(DeviceInput? existing, string title);

    ContactInput? EditContact(ContactInput? existing, string title);

    string? ChooseSaveFile(string title, string filter, string defaultFileName);

    string? ChooseOpenFile(string title, string filter);
}

public interface IAppShutdownService
{
    void Shutdown();
}

public interface IShellService
{
    void OpenFolder(string path);
}
