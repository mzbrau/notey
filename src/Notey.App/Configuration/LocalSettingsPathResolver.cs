using System.Runtime.InteropServices;

namespace Notey.App.Configuration;

/// <summary>
/// Resolves the path to <c>appsettings.Local.json</c> in a user-profile location
/// that persists across application upgrades (Velopack replaces the app directory).
/// </summary>
internal static class LocalSettingsPathResolver
{
    private const string SettingsFileName = "appsettings.Local.json";

    /// <summary>
    /// Returns the full path to the local settings file in the user-profile data folder.
    /// On Windows this is <c>%LOCALAPPDATA%\Notey\appsettings.Local.json</c>;
    /// on other platforms it is <c>~/.local/share/Notey/appsettings.Local.json</c>.
    /// </summary>
    public static string Resolve()
    {
        return Path.Combine(ResolveDirectory(), SettingsFileName);
    }

    /// <summary>
    /// Returns the directory that holds the local settings file.
    /// </summary>
    public static string ResolveDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData) && Path.IsPathFullyQualified(localAppData))
            {
                return Path.Combine(localAppData, "Notey");
            }
        }
        else
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Path.IsPathFullyQualified(userProfile))
            {
                return Path.Combine(userProfile, ".local", "share", "Notey");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData) && Path.IsPathFullyQualified(localAppData))
            {
                return Path.Combine(localAppData, "Notey");
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Notey");
    }

    /// <summary>
    /// If the legacy app-directory settings file exists but the new user-profile location does not,
    /// copies the legacy file to the new location so settings survive upgrades.
    /// </summary>
    public static void MigrateIfNeeded()
    {
        var targetPath = Resolve();
        if (File.Exists(targetPath))
        {
            return;
        }

        var legacyPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(legacyPath, targetPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort migration; the app can still function without the migrated file.
        }
    }
}
