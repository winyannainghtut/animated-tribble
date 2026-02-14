using System.Text.Json;
using CodexBar.Core.Models;

namespace CodexBar.Core.HostAPIs;

public sealed class StatusApi : IStatusApi
{
    private readonly IHttpApi _httpApi;
    private readonly Dictionary<ProviderId, Uri> _statusEndpoints;
    private readonly Dictionary<ProviderId, (DateTimeOffset CachedAtUtc, ProviderIncidentStatus Status)> _cache = new();
    private readonly TimeSpan _cacheWindow;
    private readonly object _sync = new();

    public StatusApi(IHttpApi httpApi, TimeSpan? cacheWindow = null)
    {
        _httpApi = httpApi;
        _cacheWindow = cacheWindow ?? TimeSpan.FromMinutes(2);
        _statusEndpoints = new Dictionary<ProviderId, Uri>
        {
            [ProviderId.Codex] = new Uri("https://status.openai.com/api/v2/incidents.json"),
            [ProviderId.Claude] = new Uri("https://status.anthropic.com/api/v2/incidents.json"),
            [ProviderId.Cursor] = new Uri("https://status.cursor.com/api/v2/incidents.json"),
            [ProviderId.Copilot] = new Uri("https://www.githubstatus.com/api/v2/incidents.json"),
            [ProviderId.Gemini] = new Uri("https://status.cloud.google.com/incidents.json")
        };
    }

    public async Task<ProviderIncidentStatus> GetStatusAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(providerId, out var cached) && DateTimeOffset.UtcNow - cached.CachedAtUtc < _cacheWindow)
            {
                return cached.Status;
            }
        }

        if (!_statusEndpoints.TryGetValue(providerId, out var endpoint))
        {
            return Cache(providerId, new ProviderIncidentStatus(providerId, false, IncidentSeverity.None, null, DateTimeOffset.UtcNow));
        }

        try
        {
            var response = await _httpApi.SendAsync(
                new HttpApiRequest(HttpMethod.Get, endpoint, null, null, TimeSpan.FromSeconds(15)),
                cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode >= 400)
            {
                return Cache(providerId, new ProviderIncidentStatus(providerId, true, IncidentSeverity.Minor, $"Status API HTTP {(int)response.StatusCode}", DateTimeOffset.UtcNow));
            }

            var status = ParseIncidentPayload(providerId, response.Body);
            return Cache(providerId, status);
        }
        catch (Exception ex)
        {
            return Cache(providerId, new ProviderIncidentStatus(providerId, true, IncidentSeverity.Minor, ex.Message, DateTimeOffset.UtcNow));
        }
    }

    private ProviderIncidentStatus Cache(ProviderId providerId, ProviderIncidentStatus status)
    {
        lock (_sync)
        {
            _cache[providerId] = (DateTimeOffset.UtcNow, status);
        }

        return status;
    }

    private static ProviderIncidentStatus ParseIncidentPayload(ProviderId providerId, string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("incidents", out var incidents) || incidents.ValueKind != JsonValueKind.Array)
            {
                return new ProviderIncidentStatus(providerId, false, IncidentSeverity.None, null, DateTimeOffset.UtcNow);
            }

            var active = incidents
                .EnumerateArray()
                .Where(static incident =>
                {
                    if (!incident.TryGetProperty("status", out var status))
                    {
                        return false;
                    }

                    var value = status.GetString();
                    return !string.Equals(value, "resolved", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(value, "completed", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();

            if (active.Length == 0)
            {
                return new ProviderIncidentStatus(providerId, false, IncidentSeverity.None, null, DateTimeOffset.UtcNow);
            }

            var incidentName = active[0].TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : "Service incident";

            var severity = active.Any(static incident =>
                incident.TryGetProperty("impact", out var impact) &&
                impact.GetString()?.Equals("critical", StringComparison.OrdinalIgnoreCase) == true)
                ? IncidentSeverity.Critical
                : IncidentSeverity.Major;

            return new ProviderIncidentStatus(providerId, true, severity, incidentName, DateTimeOffset.UtcNow);
        }
        catch
        {
            return new ProviderIncidentStatus(providerId, false, IncidentSeverity.None, null, DateTimeOffset.UtcNow);
        }
    }
}
