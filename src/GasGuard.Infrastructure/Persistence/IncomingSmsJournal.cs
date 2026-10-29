using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GasGuard.Application.Abstractions;
using GasGuard.Contracts.Modem;
using GasGuard.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Persistence;

public sealed class IncomingSmsJournal(
    GasGuardPaths paths,
    ILogger<IncomingSmsJournal> logger) : IIncomingSmsJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SmsJournalEntry> PersistAsync(
        IncomingSms message,
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var journalId = CreateJournalId(message.SourceReference);
            var finalPath = Path.Combine(paths.IncomingSmsDirectory, $"sms-{journalId}.json");
            if (File.Exists(finalPath))
            {
                try
                {
                    await using var existingStream = new FileStream(
                        finalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);
                    var existing = await JsonSerializer.DeserializeAsync<SmsJournalEntry>(
                        existingStream,
                        SerializerOptions,
                        cancellationToken);
                    if (existing is not null &&
                        string.Equals(existing.Message.SourceReference, message.SourceReference, StringComparison.Ordinal) &&
                        string.Equals(existing.Message.From, message.From, StringComparison.Ordinal) &&
                        string.Equals(existing.Message.Body, message.Body, StringComparison.Ordinal))
                    {
                        return existing;
                    }

                    logger.LogWarning(
                        "SMS journal identity collision at {Path}. The existing entry will be preserved and the new message will use a unique journal id.",
                        finalPath);
                    journalId = Guid.NewGuid().ToString("N");
                    finalPath = Path.Combine(paths.IncomingSmsDirectory, $"sms-{journalId}.json");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Existing SMS journal entry {Path} was unreadable and will be quarantined.", finalPath);
                    Quarantine(finalPath, ex.Message);
                    journalId = Guid.NewGuid().ToString("N");
                    finalPath = Path.Combine(paths.IncomingSmsDirectory, $"sms-{journalId}.json");
                }
            }

            var entry = new SmsJournalEntry(journalId, message);
            var temporaryPath = finalPath + ".tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, entry, SerializerOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, finalPath, true);
                return entry;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CreateJournalId(string? sourceReference)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return Guid.NewGuid().ToString("N");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceReference));
        return "ref" + Convert.ToHexString(hash)[..40];
    }
    public async Task<IReadOnlyList<SmsJournalEntry>> LoadPendingAsync(
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var entries = new List<SmsJournalEntry>();
        foreach (var path in Directory.EnumerateFiles(paths.IncomingSmsDirectory, "sms-*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var entry = await JsonSerializer.DeserializeAsync<SmsJournalEntry>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
                if (entry is null || string.IsNullOrWhiteSpace(entry.JournalId))
                {
                    Quarantine(path, "Journal entry did not contain a valid payload.");
                    continue;
                }

                entries.Add(entry);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Incoming SMS journal entry {Path} could not be read and was quarantined.", path);
                Quarantine(path, ex.Message);
            }
        }

        return entries;
    }

    public Task RemoveAsync(string journalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(journalId))
        {
            return Task.CompletedTask;
        }

        var safeId = new string(journalId.Where(char.IsAsciiLetterOrDigit).ToArray());
        var path = Path.Combine(paths.IncomingSmsDirectory, $"sms-{safeId}.json");
        TryDelete(path);
        return Task.CompletedTask;
    }

    private void Quarantine(string path, string reason)
    {
        try
        {
            var quarantineDirectory = Path.Combine(paths.IncomingSmsDirectory, "quarantine");
            Directory.CreateDirectory(quarantineDirectory);
            var destination = Path.Combine(
                quarantineDirectory,
                $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.UtcNow:yyyyMMddHHmmss}.bad");
            File.Move(path, destination, true);
            logger.LogWarning("Quarantined SMS journal entry {Path}: {Reason}", destination, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not quarantine malformed SMS journal entry {Path}.", path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
