namespace Notey.Core.Platform;

public interface IPlatformRuntime
{
    string OperatingSystem { get; }

    bool IsWindows { get; }

    bool SupportsGlobalHotkeys { get; }

    bool SupportsScreenSnips { get; }
}
