using System.Globalization;
using System.Text;
using Notey.Vault.Abstractions;

namespace Notey.Vault.Linking;

public sealed class FileSystemVaultEntityStore(
    IVaultWorkspace workspace,
    ObsidianLinkBuilder linkBuilder,
    TimeProvider timeProvider) : IVaultEntityStore, IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly Dictionary<VaultEntityKind, IReadOnlyList<VaultEntity>> _entityCache = [];
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ensureGate.Dispose();
        _disposed = true;
    }

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
        await _ensureGate.WaitAsync(cancellationToken);
        try
        {
            return await EnsureCoreAsync(kind, displayName, cancellationToken);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    private async Task<VaultEntity> EnsureCoreAsync(VaultEntityKind kind, string displayName, CancellationToken cancellationToken)
    {
        if (!_entityCache.TryGetValue(kind, out var cachedEntities))
        {
            cachedEntities = await GetAllAsync(kind, cancellationToken);
            _entityCache[kind] = cachedEntities;
        }

        var existing = cachedEntities.FirstOrDefault(entity => EntityMatchesName(entity, displayName));
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
                    AddToCache(kind, collision);
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

                var created = await ReadEntityAsync(paths, kind, filePath, cancellationToken);
                AddToCache(kind, created);
                return created;
            }
            catch
            {
                DeleteIfExists(filePath);
                throw;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique vault entity filename.");
    }

    private void AddToCache(VaultEntityKind kind, VaultEntity entity)
    {
        if (_entityCache.TryGetValue(kind, out var existing))
        {
            _entityCache[kind] = [.. existing, entity];
        }
        else
        {
            _entityCache[kind] = [entity];
        }
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
        var timestamp = timeProvider.GetLocalNow().ToString("yyyy-MM-ddTHH:mmzzz", CultureInfo.InvariantCulture);
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

    private static IReadOnlySet<string> NormalizeLookupVariants(string value)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        var normalized = ObsidianLinkBuilder.NormalizeDisplayName(value);
        AddLookupVariant(variants, normalized);

        var commaIndex = normalized.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex > 0 && commaIndex + 1 < normalized.Length)
        {
            var last = normalized[..commaIndex].Trim();
            var first = normalized[(commaIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
            {
                AddLookupVariant(variants, $"{first} {last}");
            }
        }

        var tokens = normalized
            .Replace(",", " ", StringComparison.Ordinal)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            AddLookupVariant(variants, $"{tokens[^1]} {string.Join(' ', tokens[..^1])}");
        }

        return variants;
    }

    private static void AddLookupVariant(ISet<string> variants, string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            variants.Add(normalized.ToUpperInvariant());
        }
    }

    private static bool EntityMatchesName(VaultEntity entity, string name)
    {
        var nameVariants = NormalizeLookupVariants(name);

        return NormalizeLookupVariants(entity.Name).Overlaps(nameVariants)
            || entity.Aliases.Any(alias => NormalizeLookupVariants(alias).Overlaps(nameVariants));
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
