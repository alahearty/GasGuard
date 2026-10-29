using GasGuard.Application.Abstractions;

namespace GasGuard.Infrastructure.Configuration;

public sealed class AppPathProvider(GasGuardPaths paths) : IAppPathProvider
{
    public string RootDirectory => paths.RootDirectory;
    public string DatabasePath => paths.DatabasePath;
    public string BackupsDirectory => paths.BackupsDirectory;
    public string LogsDirectory => paths.LogsDirectory;
}
