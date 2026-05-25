using System.Text.RegularExpressions;

namespace Notey.Core.Notes;

internal static class ClipboardHtmlUtilities
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    internal static string ExtractHtmlFragment(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (!html.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var startFragment = TryReadCfHtmlOffset(html, "StartFragment:");
        var endFragment = TryReadCfHtmlOffset(html, "EndFragment:");
        if (startFragment is not null
            && endFragment is not null
            && startFragment.Value >= 0
            && endFragment.Value > startFragment.Value
            && endFragment.Value <= html.Length)
        {
            return html[startFragment.Value..endFragment.Value];
        }

        var tableStart = html.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        return tableStart >= 0 ? html[tableStart..] : html;
    }

    internal static string NormalizeWhitespace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static int? TryReadCfHtmlOffset(string html, string headerName)
    {
        var headerStart = html.IndexOf(headerName, StringComparison.OrdinalIgnoreCase);
        if (headerStart < 0)
        {
            return null;
        }

        var valueStart = headerStart + headerName.Length;
        var valueEnd = html.IndexOfAny(['\r', '\n'], valueStart);
        var value = valueEnd < 0 ? html[valueStart..] : html[valueStart..valueEnd];
        return int.TryParse(value, out var offset) ? offset : null;
    }
}
