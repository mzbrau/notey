using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notey.Pipelines.Definitions;

public sealed class FilePipelineDefinitionSource(string filePath) : IPipelineDefinitionSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public async ValueTask<PipelineDefinitionLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                return new PipelineDefinitionLoadResult(
                    [],
                    [$"Pipeline definition file '{filePath}' was not found."]);
            }

            await using var stream = File.OpenRead(filePath);
            var file = await JsonSerializer.DeserializeAsync<PipelineDefinitionFile>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (file?.Pipelines is null)
            {
                return PipelineDefinitionLoadResult.Empty;
            }

            var definitions = new List<PipelineDefinition>();
            var warnings = new List<string>();

            for (var index = 0; index < file.Pipelines.Count; index++)
            {
                var definition = file.Pipelines[index];
                if (definition is null)
                {
                    warnings.Add($"Pipeline definition at index {index} is null and was ignored.");
                    continue;
                }

                definitions.Add(definition);
            }

            return new PipelineDefinitionLoadResult(definitions, warnings);
        }
        catch (JsonException exception)
        {
            return CreateLoadFailureResult(exception);
        }
        catch (IOException exception)
        {
            return CreateLoadFailureResult(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CreateLoadFailureResult(exception);
        }
    }

    private PipelineDefinitionLoadResult CreateLoadFailureResult(Exception exception)
    {
        return new PipelineDefinitionLoadResult(
            [],
            [$"Pipeline definition file '{filePath}' could not be loaded: {exception.Message}"]);
    }

    private sealed class PipelineDefinitionFile
    {
        public List<PipelineDefinition?>? Pipelines { get; init; } = [];
    }
}
