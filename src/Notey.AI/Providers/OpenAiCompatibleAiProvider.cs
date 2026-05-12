using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Notey.AI.Providers;

public sealed class OpenAiCompatibleAiProvider(
    OpenAiCompatibleAiProviderConfiguration configuration,
    HttpClient httpClient) : IAiProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Id => configuration.Id;

    public async ValueTask<AiTextResponse> CompleteTextAsync(
        AiTextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        var modelName = string.IsNullOrWhiteSpace(request.ModelName)
            ? configuration.ModelName
            : request.ModelName;
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new AiProviderException($"AI provider '{Id}' has no configured model name.");
        }

        using var message = CreateRequestMessage(request, modelName);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(
                $"AI provider '{Id}' returned {(int)response.StatusCode} {response.ReasonPhrase}: {TrimForError(responseBody)}");
        }

        var completion = DeserializeCompletion(responseBody);
        return new AiTextResponse(completion, Id, modelName);
    }

    private HttpRequestMessage CreateRequestMessage(AiTextRequest request, string modelName)
    {
        var endpoint = CreateChatCompletionsUri(configuration.BaseUrl);
        var body = new Dictionary<string, object?>
        {
            ["model"] = modelName,
            ["messages"] = BuildMessages(request),
        };

        if (request.Temperature is not null)
        {
            body["temperature"] = request.Temperature;
        }

        if (request.MaxTokens is not null)
        {
            body["max_tokens"] = request.MaxTokens;
        }

        if (request.JsonOutput)
        {
            body["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
        }

        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        return message;
    }

    private static Uri CreateChatCompletionsUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new AiProviderException("AI provider base URL must be an absolute URI.");
        }

        var builder = new UriBuilder(baseUri);
        var basePath = builder.Path.TrimEnd('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? "chat/completions"
            : $"{basePath}/chat/completions";
        return builder.Uri;
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildMessages(AiTextRequest request)
    {
        var messages = new List<Dictionary<string, string>>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new Dictionary<string, string>
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "user",
            ["content"] = request.Prompt
        });

        return messages;
    }

    private static string DeserializeCompletion(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var choices = document.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                throw new AiProviderException("AI response did not include any choices.");
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiProviderException("AI response content was empty.");
            }

            return content;
        }
        catch (JsonException ex)
        {
            throw new AiProviderException("AI response was not valid OpenAI-compatible JSON.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new AiProviderException("AI response did not match the OpenAI-compatible chat completion shape.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new AiProviderException("AI response did not match the OpenAI-compatible chat completion shape.", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(configuration.BaseUrl))
        {
            throw new AiProviderException($"AI provider '{Id}' has no configured base URL.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            throw new AiProviderException(
                $"AI provider '{Id}' has no API key. Configure Notey:Ai:ApiKey or set {configuration.ApiKeyEnvironmentVariable}.");
        }
    }

    private static string TrimForError(string value)
    {
        const int maxLength = 500;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..maxLength]}...";
    }
}

public sealed record OpenAiCompatibleAiProviderConfiguration(
    string Id,
    string BaseUrl,
    string ApiKey,
    string ApiKeyEnvironmentVariable,
    string ModelName);
