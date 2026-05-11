using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class PlatformRuntime : IPlatformRuntime
{
    public string OperatingSystem => OperatingSystemDescription();

    public bool IsWindows => System.OperatingSystem.IsWindows();

    public bool SupportsGlobalHotkeys => false;

    public bool SupportsScreenSnips => false;

    private static string OperatingSystemDescription()
    {
        if (System.OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (System.OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (System.OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        return "Unknown";
    }
}
