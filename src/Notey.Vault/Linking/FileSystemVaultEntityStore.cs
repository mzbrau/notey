using System.Globalization;
using System.Text;
using Notey.Vault.Abstractions;

namespace Notey.Vault.Linking;

public sealed class FileSystemVaultEntityStore(
    IVaultWorkspace workspace,
    ObsidianLinkBuilder linkBuilder,
    TimeProvider timeProvider) : IVaultEntityStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<IReadOnlyList<VaultEntity>> GetAllAsync(VaultEntityKind kind, CancellationToken cancellationToken = default)
    {
        var paths = workspace.GetPaths();
        var folderPath = ObsidianLinkBuilder.GetFolderPath(paths, kind);
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        var entities = new List<VaultEntity>();
        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entities.Add(await ReadEntityAsync(paths, kind, filePath, cancellationToken));
        }

        return entities
            .OrderBy(static entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<VaultEntity> EnsureAsync(VaultEntityKind kind, string name, CancellationToken cancellationToken = default)
    {
        var displayName = ObsidianLinkBuilder.NormalizeDisplayName(name);
        var existing = await FindByNameAsync(kind, displayName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var baseFilePath = linkBuilder.GetEntityFilePath(kind, displayName);
        var directory = Path.GetDirectoryName(baseFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Vault entity file path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        var content = CreateEntityDocument(kind, displayName);

        for (var index = 1; index < int.MaxValue; index++)
        {
            var paths = workspace.GetPaths();
            var filePath = GetCandidateFilePath(baseFilePath, index);
            FileStream stream;

            try
            {
                stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            }
            catch (IOException) when (File.Exists(filePath))
            {
                var collision = await ReadEntityAsync(paths, kind, filePath, cancellationToken);
                if (EntityMatchesName(collision, displayName))
                {
                    return collision;
                }

                continue;
            }

            try
            {
                await using (stream)
                {
                    await using var writer = new StreamWriter(stream, Utf8NoBom);
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                return await ReadEntityAsync(paths, kind, filePath, cancellationToken);
            }
            catch
            {
                DeleteIfExists(filePath);
                throw;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique vault entity filename.");
    }

    private async Task<VaultEntity?> FindByNameAsync(VaultEntityKind kind, string name, CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeLookup(name);
        var entities = await GetAllAsync(kind, cancellationToken);

        return entities.FirstOrDefault(entity =>
            string.Equals(NormalizeLookup(entity.Name), normalizedName, StringComparison.Ordinal)
            || entity.Aliases.Any(alias => string.Equals(NormalizeLookup(alias), normalizedName, StringComparison.Ordinal)));
    }

    private async Task<VaultEntity> ReadEntityAsync(VaultPaths paths, VaultEntityKind kind, string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var frontmatter = FrontmatterReader.Read(content);
        var fallbackName = Path.GetFileNameWithoutExtension(filePath);
        var name = frontmatter.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)
            ? title
            : fallbackName;

        return new VaultEntity(
            kind,
            ObsidianLinkBuilder.NormalizeDisplayName(name),
            filePath,
            linkBuilder.GetLinkPath(paths, filePath),
            frontmatter.GetArray("aliases"));
    }

    private string CreateEntityDocument(VaultEntityKind kind, string name)
    {
        var timestamp = timeProvider.GetLocalNow().ToString("O", CultureInfo.InvariantCulture);
        var type = ObsidianLinkBuilder.GetKindLabel(kind);

        return $"""
            ---
            created: {timestamp}
            type: {type}
            title: "{EscapeYaml(name)}"
            aliases: []
            ---

            # {name}

            ## Related notes

            """;
    }

    private static string NormalizeLookup(string value)
    {
        return ObsidianLinkBuilder.NormalizeDisplayName(value).ToUpperInvariant();
    }

    private static bool EntityMatchesName(VaultEntity entity, string name)
    {
        var normalizedName = NormalizeLookup(name);

        return string.Equals(NormalizeLookup(entity.Name), normalizedName, StringComparison.Ordinal)
            || entity.Aliases.Any(alias => string.Equals(NormalizeLookup(alias), normalizedName, StringComparison.Ordinal));
    }

    private static string GetCandidateFilePath(string filePath, int index)
    {
        if (index == 1)
        {
            return filePath;
        }

        var directory = Path.GetDirectoryName(filePath);
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        return string.IsNullOrWhiteSpace(directory)
            ? $"{baseName}-{index}{extension}"
            : Path.Combine(directory, $"{baseName}-{index}{extension}");
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private sealed class FrontmatterReader : Dictionary<string, string>
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _arrays = new(StringComparer.OrdinalIgnoreCase);

        public FrontmatterReader()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public IReadOnlyList<string> GetArray(string key)
        {
            return _arrays.TryGetValue(key, out var values) ? values : [];
        }

        public static FrontmatterReader Read(string markdown)
        {
            var reader = new FrontmatterReader();
            var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length < 3 || lines[0] != "---")
            {
                return reader;
            }

            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line == "---")
                {
                    break;
                }

                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (value.StartsWith('[') && value.EndsWith(']'))
                {
                    reader._arrays[key] = ParseInlineArray(value);
                    continue;
                }

                if (string.IsNullOrEmpty(value))
                {
                    reader._arrays[key] = ParseBlockArray(lines, index + 1);
                    continue;
                }

                reader[key] = Unquote(value);
            }

            return reader;
        }

        private static IReadOnlyList<string> ParseInlineArray(string value)
        {
            var inner = value.Trim('[', ']');
            if (string.IsNullOrWhiteSpace(inner))
            {
                return [];
            }

            return inner
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        private static IReadOnlyList<string> ParseBlockArray(IReadOnlyList<string> lines, int startIndex)
        {
            var values = new List<string>();
            for (var index = startIndex; index < lines.Count; index++)
            {
                var line = lines[index];
                if (!line.StartsWith("  - ", StringComparison.Ordinal))
                {
                    break;
                }

                values.Add(Unquote(line[4..].Trim()));
            }

            return values;
        }

        private static string Unquote(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                return UnescapeDoubleQuotedScalar(trimmed[1..^1]);
            }

            if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            {
                return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            return trimmed;
        }

        private static string UnescapeDoubleQuotedScalar(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character != '\\' || index + 1 >= value.Length)
                {
                    builder.Append(character);
                    continue;
                }

                var escaped = value[++index];
                if (escaped is '"' or '\\')
                {
                    builder.Append(escaped);
                }
                else
                {
                    builder.Append('\\').Append(escaped);
                }
            }

            return builder.ToString();
        }
    }
}
