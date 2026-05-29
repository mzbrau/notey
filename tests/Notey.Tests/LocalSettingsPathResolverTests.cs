using Notey.App.Configuration;

namespace Notey.Tests;

public sealed class LocalSettingsPathResolverTests
{
    [Fact]
    public void Resolve_returns_path_ending_with_appsettings_Local_json()
    {
        var path = LocalSettingsPathResolver.Resolve();

        Assert.EndsWith("appsettings.Local.json", path, StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(path));
    }

    [Fact]
    public void Resolve_returns_path_outside_app_base_directory()
    {
        var path = LocalSettingsPathResolver.Resolve();
        var appBase = Path.GetFullPath(AppContext.BaseDirectory);

        // The resolved path should NOT be inside AppContext.BaseDirectory
        // (unless the system has no writable profile folders, which is an edge case).
        var resolvedDir = Path.GetFullPath(Path.GetDirectoryName(path)!);
        if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 })
        {
            Assert.False(
                resolvedDir.StartsWith(appBase, StringComparison.OrdinalIgnoreCase),
                $"Resolved settings path '{resolvedDir}' should not be inside app base directory '{appBase}'.");
        }
    }

    [Fact]
    public void Resolve_directory_contains_Notey_folder_name()
    {
        var directory = LocalSettingsPathResolver.ResolveDirectory();

        Assert.Contains("Notey", directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrateIfNeeded_copies_legacy_file_when_target_does_not_exist()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"notey-migration-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var legacyPath = Path.Combine(tempRoot, "legacy", "appsettings.Local.json");
            var targetPath = Path.Combine(tempRoot, "target", "appsettings.Local.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, """{"Notey":{}}""");

            LocalSettingsPathResolver.MigrateIfNeeded(legacyPath, targetPath);

            Assert.True(File.Exists(targetPath), "Target settings file should have been copied from legacy path.");
            Assert.Equal("""{"Notey":{}}""", File.ReadAllText(targetPath));
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(targetPath);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void MigrateIfNeeded_is_no_op_when_target_already_exists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"notey-migration-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var legacyPath = Path.Combine(tempRoot, "legacy", "appsettings.Local.json");
            var targetPath = Path.Combine(tempRoot, "target", "appsettings.Local.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(legacyPath, """{"Notey":{"legacy":true}}""");
            File.WriteAllText(targetPath, """{"Notey":{"existing":true}}""");

            LocalSettingsPathResolver.MigrateIfNeeded(legacyPath, targetPath);

            Assert.Equal("""{"Notey":{"existing":true}}""", File.ReadAllText(targetPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HostBootstrapper_loads_settings_from_user_profile_path()
    {
        // Verify the HostBootstrapper source references LocalSettingsPathResolver.
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Configuration", "HostBootstrapper.cs"));

        Assert.Contains("LocalSettingsPathResolver.MigrateIfNeeded()", source, StringComparison.Ordinal);
        Assert.Contains("LocalSettingsPathResolver.Resolve()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoteySettingsStore_default_path_uses_resolver()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Configuration", "NoteySettingsStore.cs"));

        Assert.Contains("LocalSettingsPathResolver.Resolve()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory, \"appsettings.Local.json\")", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "Notey.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
