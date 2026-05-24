using Microsoft.Extensions.Logging;
using Notey.Core.Configuration;

namespace Notey.AI.Providers;

public static class OpenAiCompatibleAiProviderFactory
{
    public static IReadOnlyList<IAiProvider> CreateProviders(AiOptions options, Func<HttpClient> httpClientFactory, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var providers = new List<IAiProvider>();
        var defaultConfiguration = CreateDefaultConfiguration(options);
        providers.Add(CreateProvider(defaultConfiguration, options.RequestTimeoutSeconds, httpClientFactory, loggerFactory));

        foreach (var provider in options.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id)
                || !string.Equals(provider.Type, "OpenAiCompatible", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var configuration = CreateProviderConfiguration(provider, options);
            providers.Add(CreateProvider(
                configuration,
                provider.RequestTimeoutSeconds ?? options.RequestTimeoutSeconds,
                httpClientFactory,
                loggerFactory));
        }

        return providers
            .GroupBy(static provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static IAiProvider CreateProvider(
        OpenAiCompatibleAiProviderConfiguration configuration,
        int requestTimeoutSeconds,
        Func<HttpClient> httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        var httpClient = httpClientFactory();
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, requestTimeoutSeconds));
        return new OpenAiCompatibleAiProvider(configuration, httpClient, loggerFactory.CreateLogger<OpenAiCompatibleAiProvider>());
    }

    private static OpenAiCompatibleAiProviderConfiguration CreateDefaultConfiguration(AiOptions options)
    {
        var environmentVariable = string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable)
            ? "NOTEY_AI_API_KEY"
            : options.ApiKeyEnvironmentVariable;
        var apiKey = ResolveApiKey(options.ApiKey, environmentVariable);

        return new OpenAiCompatibleAiProviderConfiguration(
            string.IsNullOrWhiteSpace(options.DefaultProviderId) ? "default" : options.DefaultProviderId,
            options.BaseUrl,
            apiKey,
            environmentVariable,
            options.ModelName,
            options.ReasoningModel);
    }

    private static OpenAiCompatibleAiProviderConfiguration CreateProviderConfiguration(
        AiProviderOptions provider,
        AiOptions options)
    {
        var environmentVariable = string.IsNullOrWhiteSpace(provider.ApiKeyEnvironmentVariable)
            ? options.ApiKeyEnvironmentVariable
            : provider.ApiKeyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(environmentVariable))
        {
            environmentVariable = "NOTEY_AI_API_KEY";
        }

        var configuredApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? options.ApiKey : provider.ApiKey;

        return new OpenAiCompatibleAiProviderConfiguration(
            provider.Id,
            string.IsNullOrWhiteSpace(provider.BaseUrl) ? options.BaseUrl : provider.BaseUrl,
            ResolveApiKey(configuredApiKey, environmentVariable),
            environmentVariable,
            string.IsNullOrWhiteSpace(provider.ModelName) ? options.ModelName : provider.ModelName,
            provider.ReasoningModel);
    }

    private static string ResolveApiKey(string configuredApiKey, string environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return configuredApiKey;
        }

        return Environment.GetEnvironmentVariable(environmentVariable) ?? string.Empty;
    }
}
