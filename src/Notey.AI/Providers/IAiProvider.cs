namespace Notey.AI.Providers;

public interface IAiProvider
{
    string Id { get; }

    ValueTask<AiTextResponse> CompleteTextAsync(AiTextRequest request, CancellationToken cancellationToken = default);
}

public sealed record AiTextRequest(
    string Prompt,
    string? SystemPrompt = null,
    string? ModelName = null,
    bool JsonOutput = false,
    double? Temperature = null,
    int? MaxTokens = null);

public sealed record AiTextResponse(string Text, string ProviderId, string ModelName);
