using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Application.Time;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Configuration;
using GasGuard.Infrastructure.DependencyInjection;
using GasGuard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GasGuard.IntegrationTests;

internal sealed class TestClock : IClock
{
    public TestClock(DateTime? utcNow = null)
    {
        UtcNow = utcNow ?? new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    }

    public DateTime UtcNow { get; set; }
    public DateTime ToLagos(DateTime utc) => AfricaLagosTime.ToLocal(utc);
    public DateTime LagosToUtc(DateTime local) => AfricaLagosTime.LocalToUtc(local);
    public DateOnly ToLagosDate(DateTime utc) => AfricaLagosTime.ToLocalDate(utc);
    public string LagosTimeZoneName => AfricaLagosTime.TimeZoneName;
}

internal sealed class TestUserContext(string name = "test-operator") : IUserContext
{
    public string OperatorName => name;
}

internal sealed class IntegrationTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly bool _deleteOnDispose;
    private bool _disposed;

    private IntegrationTestHost(
        ServiceProvider provider,
        GasGuardPaths paths,
        TestClock clock,
        bool deleteOnDispose)
    {
        _provider = provider;
        Paths = paths;
        Clock = clock;
        _deleteOnDispose = deleteOnDispose;
    }

    public GasGuardPaths Paths { get; }
    public TestClock Clock { get; }
    public string RootDirectory => Paths.RootDirectory;

    public static async Task<IntegrationTestHost> CreateAsync(
        string? existingRoot = null,
        bool deleteOnDispose = true)
    {
        var root = existingRoot ?? Path.Combine(
            Path.GetTempPath(),
            "GasGuardTests",
            Guid.NewGuid().ToString("N"));
        var paths = new GasGuardPaths(root);
        var clock = new TestClock();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddGasGuardInfrastructure(paths);
        services.RemoveAll<IClock>();
        services.RemoveAll<IUserContext>();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IUserContext>(new TestUserContext());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var startup = provider.GetRequiredService<IStartupService>();
        var result = await startup.InitializeAsync();
        if (!result.Success)
        {
            await provider.DisposeAsync();
            throw new InvalidOperationException($"Test database startup failed: {result.Message}");
        }

        return new IntegrationTestHost(provider, paths, clock, deleteOnDispose);
    }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async Task<GasGuardDbContext> CreateDbContextAsync() =>
        await Get<IDbContextFactory<GasGuardDbContext>>().CreateDbContextAsync();

    public async Task<GasDevice> SeedDeviceAsync(
        string code = "GAS-001",
        string? sender = null,
        string name = "Boiler Room Detector",
        string location = "Main Building - Boiler Room",
        bool active = true,
        bool demo = false)
    {
        var deviceService = Get<IDeviceService>();
        sender ??= code == "GAS-002" ? "+2348031234568" : "+2348031234567";
        var result = await deviceService.CreateAsync(new DeviceInput(
            code,
            name,
            location,
            sender,
            Domain.DetectorType.CombustibleGas,
            "Test detector",
            new DateOnly(2026, 1, 15),
            active,
            null));
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(" ", result.ValidationErrors ?? []));
        }

        await using var context = await CreateDbContextAsync();
        var device = await context.GasDevices.SingleAsync(x => x.DeviceCode == code.ToUpperInvariant());
        if (demo)
        {
            device.IsDemo = true;
            await context.SaveChangesAsync();
        }

        return device;
    }

    public async Task<Contact> SeedContactAsync(
        string name = "Safety Officer",
        string phone = "+2348030000010",
        bool notifyCritical = true,
        bool notifyWarning = true,
        bool notifyCommunicationFailure = false)
    {
        var contactService = Get<IContactService>();
        var result = await contactService.CreateAsync(new ContactInput(
            name,
            phone,
            Domain.ContactRole.SafetyOfficer,
            true,
            notifyCritical,
            notifyWarning,
            notifyCommunicationFailure,
            "Integration test contact"));
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(" ", result.ValidationErrors ?? []));
        }

        await using var context = await CreateDbContextAsync();
        return await context.Contacts.SingleAsync(x => x.PhoneNumber == phone);
    }

    public async Task<int> CountAsync<TEntity>() where TEntity : class
    {
        await using var context = await CreateDbContextAsync();
        return await context.Set<TEntity>().CountAsync();
    }

    public async Task<List<TEntity>> ListAsync<TEntity>() where TEntity : class
    {
        await using var context = await CreateDbContextAsync();
        return await context.Set<TEntity>().AsNoTracking().ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (_deleteOnDispose)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(RootDirectory))
                    {
                        Directory.Delete(RootDirectory, true);
                    }

                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}

internal static class TestWait
{
    public static async Task UntilAsync(
        Func<Task<bool>> predicate,
        TimeSpan? timeout = null,
        string? because = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(because ?? "The expected condition was not observed in time.");
    }
}
