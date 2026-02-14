using CodexBar.Core.Models;

namespace CodexBar.Core.Services;

public sealed class UsageStore
{
    private readonly Dictionary<ProviderId, ProviderUsageSnapshot> _snapshots = new();
    private readonly object _sync = new();

    public event EventHandler<ProviderUsageSnapshot>? UsageUpdated;

    public void Upsert(ProviderUsageSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshots[snapshot.ProviderId] = snapshot;
        }

        UsageUpdated?.Invoke(this, snapshot);
    }

    public ProviderUsageSnapshot? TryGet(ProviderId providerId)
    {
        lock (_sync)
        {
            return _snapshots.TryGetValue(providerId, out var snapshot) ? snapshot : null;
        }
    }

    public IReadOnlyList<ProviderUsageSnapshot> GetAll()
    {
        lock (_sync)
        {
            return _snapshots.Values.OrderBy(snapshot => snapshot.ProviderId).ToArray();
        }
    }
}
