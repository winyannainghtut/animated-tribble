using System.Text.Json;
using CodexBar.Core.Models;

namespace CodexBar.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore(string? settingsPath = null)
    {
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexBar",
            "settings.json");

        _settingsPath = settingsPath ?? defaultPath;
    }

    public async Task<AppSettings> LoadAsync(IEnumerable<Models.ProviderId>? knownProviders = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                var defaults = BuildDefaultSettings(knownProviders);
                await SaveInternalAsync(defaults, cancellationToken).ConfigureAwait(false);
                return defaults;
            }

            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? BuildDefaultSettings(knownProviders);

            return MergeWithDefaults(settings, knownProviders);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveInternalAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static AppSettings BuildDefaultSettings(IEnumerable<Models.ProviderId>? knownProviders)
    {
        var enabled = new Dictionary<Models.ProviderId, bool>();
        if (knownProviders is not null)
        {
            foreach (var provider in knownProviders)
            {
                enabled[provider] = provider is Models.ProviderId.Codex or Models.ProviderId.Claude or Models.ProviderId.Cursor;
            }
        }

        return new AppSettings
        {
            ProviderEnabled = enabled,
            RefreshIntervalMinutes = 5,
            MergeIcons = false,
            ShowUsageAsUsed = true,
            ActiveProviderInMergeMode = Models.ProviderId.Codex,
            PollIncidents = true
        };
    }

    private static AppSettings MergeWithDefaults(AppSettings loaded, IEnumerable<Models.ProviderId>? knownProviders)
    {
        var merged = new Dictionary<Models.ProviderId, bool>(loaded.ProviderEnabled);
        if (knownProviders is not null)
        {
            foreach (var provider in knownProviders)
            {
                if (!merged.ContainsKey(provider))
                {
                    merged[provider] = provider is Models.ProviderId.Codex or Models.ProviderId.Claude or Models.ProviderId.Cursor;
                }
            }
        }

        return new AppSettings
        {
            ProviderEnabled = merged,
            RefreshIntervalMinutes = loaded.RefreshIntervalMinutes <= 0 ? 5 : loaded.RefreshIntervalMinutes,
            MergeIcons = loaded.MergeIcons,
            ShowUsageAsUsed = loaded.ShowUsageAsUsed,
            ActiveProviderInMergeMode = loaded.ActiveProviderInMergeMode,
            PollIncidents = loaded.PollIncidents
        };
    }
}
