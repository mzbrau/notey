namespace Notey.AI.Providers;

public interface IAiProviderRegistry
{
    bool TryGet(string? providerId, out IAiProvider provider);

    void ReplaceProviders(IEnumerable<IAiProvider> providers, string defaultProviderId);
}

public sealed class AiProviderRegistry(
    IEnumerable<IAiProvider> providers,
    string defaultProviderId) : IAiProviderRegistry
{
    private readonly object _gate = new();
    private Dictionary<string, IAiProvider> _providers = providers.ToDictionary(
        static provider => provider.Id,
        StringComparer.OrdinalIgnoreCase);
    private string _defaultProviderId = defaultProviderId;

    public bool TryGet(string? providerId, out IAiProvider provider)
    {
        lock (_gate)
        {
            var id = string.IsNullOrWhiteSpace(providerId) ? _defaultProviderId : providerId;
            return _providers.TryGetValue(id, out provider!);
        }
    }

    public void ReplaceProviders(IEnumerable<IAiProvider> providers, string defaultProviderId)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var nextProviders = providers.ToDictionary(
            static provider => provider.Id,
            StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            _providers = nextProviders;
            _defaultProviderId = defaultProviderId;
        }
    }
}
