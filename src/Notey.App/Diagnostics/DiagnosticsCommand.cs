namespace Notey.App.Diagnostics;

public static class DiagnosticsCommand
{
    public const string ExportFlag = "--export-diagnostics";

    public static bool TryParse(IReadOnlyList<string> args, out string? outputPath)
    {
        ArgumentNullException.ThrowIfNull(args);

        outputPath = null;
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], ExportFlag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                outputPath = args[index + 1];
            }

            return true;
        }

        return false;
    }
}
