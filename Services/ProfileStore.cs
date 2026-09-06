using System.Text.Json;
using OpenMonitorManager.Models;

namespace OpenMonitorManager.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenMonitorManager",
        "profiles.json");

    public async Task<IReadOnlyList<MonitorProfile>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<List<MonitorProfile>>(
                       stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write("Could not load profiles.", exception);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IEnumerable<MonitorProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, profiles, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, FilePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
