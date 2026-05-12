namespace Notey.AI.Providers;

public interface IAiProviderRegistry
{
    bool TryGet(string? providerId, out IAiProvider provider);
}

public sealed class AiProviderRegistry(
    IEnumerable<IAiProvider> providers,
    string defaultProviderId) : IAiProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> _providers = providers.ToDictionary(
        static provider => provider.Id,
        StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string? providerId, out IAiProvider provider)
    {
        var id = string.IsNullOrWhiteSpace(providerId) ? defaultProviderId : providerId;
        return _providers.TryGetValue(id, out provider!);
    }
}
