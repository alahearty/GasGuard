using Microsoft.Data.Sqlite;

namespace GasGuard.Infrastructure.Configuration;

public sealed class GasGuardPaths
{
    private const string ApplicationFolderName = "GasGuardDesktop";

    public GasGuardPaths(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationFolderName)
            : Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public string DatabasePath => Path.Combine(RootDirectory, "gasguard.db");
    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public string IncomingSmsDirectory => Path.Combine(RootDirectory, "inbox");

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true,
        Pooling = true,
        DefaultTimeout = 10
    }.ToString();

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(IncomingSmsDirectory);
    }
}
