using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Notey.Core.Notes;

public static class MarkdownClipboardFormatter
{
    private static readonly Regex ClosingPunctuationSpacingRegex = new(@"\s+([,.;:!?)\]])", RegexOptions.Compiled);
    private static readonly Regex OpeningPunctuationSpacingRegex = new(@"([(\[])\s+", RegexOptions.Compiled);
    private static readonly Regex MarkdownTaskMarkerRegex = new(@"^(?<marker>[-*+])\s+\[(?<state>[ xX])\]\s*(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListMarkerRegex = new(@"^(?<marker>[-*+•◦▪■●○‣])\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListMarkerRegex = new(@"^(?<marker>(?<number>\d+|[A-Za-z]+)[.)])\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex CheckboxTextRegex = new(@"^\[(?<state>[ xX])\]\s*(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletGlyphCellRegex = new(@"^[-*+•◦▪■●○‣o]$", RegexOptions.Compiled);
    private static readonly Regex OrderedNumberCellRegex = new(@"^(\d+|[A-Za-z]+)[.)]$", RegexOptions.Compiled);

    public static string? TryConvertToMarkdown(string? html, string? rtf, string? text, out bool structuredHtmlDetected)
    {
        structuredHtmlDetected = false;

        if (!string.IsNullOrWhiteSpace(html))
        {
            if (MarkdownTableFormatter.ContainsHtmlTable(html))
            {
                structuredHtmlDetected = true;
                if (TryConvertHtmlListTable(html, out var htmlListTable))
                {
                    return htmlListTable;
                }

                return MarkdownTableFormatter.TryConvertHtmlTable(html, out var htmlTable) ? htmlTable : null;
            }

            if (ContainsHtmlList(html))
            {
                structuredHtmlDetected = true;
                return TryConvertHtmlList(html, out var htmlList) ? htmlList : null;
            }
        }

        if (!string.IsNullOrWhiteSpace(rtf)
            && MarkdownTableFormatter.TryConvertRtfTable(rtf, out var rtfTable))
        {
            return rtfTable;
        }

        if (!string.IsNullOrEmpty(text))
        {
            if (TryConvertPlainTextList(text, out var textList))
            {
                return textList;
            }

            if (MarkdownTableFormatter.TryConvertPlainTextTable(text, out var textTable))
            {
                return textTable;
            }
        }

        return null;
    }

    public static bool ContainsHtmlList(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var document = ParseClipboardHtmlDocument(html);
        return TryGetTopLevelListRoots(document, out _);
    }

    public static bool TryConvertHtmlList(string html, out string markdownList)
    {
        ArgumentNullException.ThrowIfNull(html);

        markdownList = string.Empty;
        var document = ParseClipboardHtmlDocument(html);
        if (!TryGetTopLevelListRoots(document, out var listRoots))
        {
            return false;
        }

        var lines = new List<string>();
        foreach (var listRoot in listRoots)
        {
            AppendHtmlList(listRoot, depth: 0, lines);
        }

        if (lines.Count == 0)
        {
            return false;
        }

        markdownList = string.Join("\n", lines) + "\n";
        return true;
    }

    private static bool TryConvertHtmlListTable(string html, out string markdownList)
    {
        markdownList = string.Empty;
        var fragment = ClipboardHtmlUtilities.ExtractHtmlFragment(html);
        var normalizedFragment = Regex.Replace(fragment, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        var document = new HtmlParser().ParseDocument(normalizedFragment);
        var table = document.QuerySelector("table");
        if (table is null)
        {
            return false;
        }

        var rows = table.QuerySelectorAll("tr")
            .Select(static row => row.Children
                .Where(static c => c.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase))
                .ToList())
            .Where(static cells => cells.Count > 0)
            .ToList();

        if (rows.Count < 2 || !rows.All(static cells => cells.Count == 2))
        {
            return false;
        }

        var firstCells = rows
            .Select(static cells => ClipboardHtmlUtilities.NormalizeWhitespace(cells[0].TextContent))
            .ToList();

        var allBullets = firstCells.All(c => BulletGlyphCellRegex.IsMatch(c));
        var allOrdered = !allBullets && firstCells.All(c => OrderedNumberCellRegex.IsMatch(c));

        if (!allBullets && !allOrdered)
        {
            return false;
        }

        var lines = new List<string>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var itemText = RenderInlineMarkdown(rows[i][1].ChildNodes);
            if (string.IsNullOrWhiteSpace(itemText))
            {
                return false;
            }

            string line;
            if (allOrdered)
            {
                line = $"{firstCells[i]} {itemText}";
            }
            else
            {
                var depth = firstCells[i].Length == 1 ? GetWordBulletGlyphDepth(firstCells[i][0]) : 0;
                var indent = depth > 0 ? new string(' ', depth * 4) : string.Empty;
                line = $"{indent}- {itemText}";
            }

            lines.Add(line);
        }

        markdownList = string.Join("\n", lines) + "\n";
        return true;
    }

    public static bool TryConvertPlainTextList(string text, out string markdownList)
    {
        ArgumentNullException.ThrowIfNull(text);

        markdownList = string.Empty;
        var normalizedText = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedText.Split('\n', StringSplitOptions.None);
        var items = new List<PlainTextListItem>();
        PlainTextListItem? currentItem = null;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (TryParsePlainTextListItem(rawLine, out var item))
            {
                items.Add(item);
                currentItem = item;
                continue;
            }

            if (currentItem is null)
            {
                return false;
            }

            var continuationIndent = CountIndentation(rawLine);
            if (continuationIndent <= currentItem.Indent)
            {
                return false;
            }

            var continuationText = ClipboardHtmlUtilities.NormalizeWhitespace(rawLine);
            currentItem.Text = currentItem.Text.Length == 0
                ? continuationText
                : $"{currentItem.Text} {continuationText}";
        }

        if (items.Count < 2)
        {
            return false;
        }

        var levels = CalculateIndentLevels(items);
        var builder = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var indent = new string(' ', levels[i] * 4);
            builder.Append(indent);
            builder.Append(GetPlainTextMarker(item));
            builder.Append(item.Text);
            builder.Append('\n');
        }

        markdownList = builder.ToString();
        return markdownList.Length > 0;
    }

    private static IDocument ParseClipboardHtmlDocument(string html)
    {
        var fragment = ClipboardHtmlUtilities.ExtractHtmlFragment(html);
        return new HtmlParser().ParseDocument(fragment);
    }

    private static bool TryGetTopLevelListRoots(IDocument document, out List<IElement> listRoots)
    {
        ArgumentNullException.ThrowIfNull(document);

        listRoots = [];
        if (document.QuerySelector("table") is not null)
        {
            return false;
        }

        var root = document.Body ?? document.DocumentElement;
        return root is not null
            && CollectTopLevelListRoots(root.ChildNodes, listRoots)
            && listRoots.Count > 0;
    }

    private static bool CollectTopLevelListRoots(IEnumerable<INode> nodes, List<IElement> listRoots)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IComment:
                    continue;
                case IText textNode when string.IsNullOrWhiteSpace(textNode.Text):
                    continue;
                case IText:
                    return false;
                case IElement element when IsListElement(element) && !HasListOrTableAncestor(element):
                    listRoots.Add(element);
                    continue;
                case IElement element when IsIgnorableElement(element):
                    continue;
                case IElement element when IsWrapperElement(element):
                    if (!CollectTopLevelListRoots(element.ChildNodes, listRoots))
                    {
                        return false;
                    }

                    continue;
                default:
                    return false;
            }
        }

        return true;
    }

    private static void AppendHtmlList(IElement listElement, int depth, List<string> lines)
    {
        var isOrdered = listElement.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase);
        var number = isOrdered ? GetOrderedListStart(listElement) : 1;

        foreach (var listItem in listElement.Children.Where(static child => child.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var parsedItem = ParseHtmlListItem(listItem);
            var marker = parsedItem.IsChecked switch
            {
                true => "- [x] ",
                false => "- [ ] ",
                null when isOrdered => string.Create(CultureInfo.InvariantCulture, $"{number}. "),
                _ => "- "
            };

            lines.Add($"{new string(' ', depth * 4)}{marker}{parsedItem.Text}".TrimEnd());

            foreach (var nestedList in parsedItem.NestedLists)
            {
                AppendHtmlList(nestedList, depth + 1, lines);
            }

            if (isOrdered)
            {
                number++;
            }
        }
    }

    private static HtmlListItem ParseHtmlListItem(IElement listItem)
    {
        var nestedLists = new List<IElement>();
        var builder = new StringBuilder();

        foreach (var child in listItem.ChildNodes)
        {
            if (child is IElement element && IsListElement(element))
            {
                nestedLists.Add(element);
                continue;
            }

            AppendInlineMarkdown(child, builder);
        }

        var text = CleanupRenderedInlineText(builder.ToString());
        var isChecked = TryGetCheckboxState(listItem);
        if (!isChecked.HasValue && TryStripLeadingCheckboxText(text, out var checkboxState, out var checkboxText))
        {
            isChecked = checkboxState;
            text = checkboxText;
        }

        return new HtmlListItem(text, nestedLists, isChecked);
    }

    private static void AppendInlineMarkdown(INode node, StringBuilder builder)
    {
        switch (node)
        {
            case IComment:
                return;
            case IText textNode:
                AppendToken(builder, textNode.Text);
                return;
            case IElement element when IsListElement(element):
                return;
            case IElement element when IsCheckboxInputElement(element):
                return;
            case IElement element when element.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase):
                AppendToken(builder, " ");
                return;
            case IElement element:
                var inner = RenderInlineMarkdown(element.ChildNodes);
                if (inner.Length == 0)
                {
                    return;
                }

                var rendered = element.LocalName.ToLowerInvariant() switch
                {
                    "strong" or "b" => $"**{inner}**",
                    "em" or "i" => $"_{inner}_",
                    "a" when !string.IsNullOrWhiteSpace(element.GetAttribute("href")) => $"[{inner}]({EscapeMarkdownUrl(element.GetAttribute("href")!)})",
                    _ => inner
                };
                AppendToken(builder, rendered);
                return;
        }
    }

    private static string RenderInlineMarkdown(IEnumerable<INode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            AppendInlineMarkdown(node, builder);
        }

        return CleanupRenderedInlineText(builder.ToString());
    }

    private static void AppendToken(StringBuilder builder, string token)
    {
        if (token.Length == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }

            return;
        }

        var normalized = ClipboardHtmlUtilities.NormalizeWhitespace(token.ReplaceLineEndings(" "));
        if (normalized.Length == 0)
        {
            return;
        }

        if (builder.Length > 0
            && builder[^1] != ' '
            && !StartsWithClosingPunctuation(normalized)
            && !EndsWithOpeningPunctuation(builder))
        {
            builder.Append(' ');
        }

        builder.Append(normalized);
    }

    private static string CleanupRenderedInlineText(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var normalized = ClipboardHtmlUtilities.NormalizeWhitespace(text.ReplaceLineEndings(" "));
        normalized = ClosingPunctuationSpacingRegex.Replace(normalized, "$1");
        normalized = OpeningPunctuationSpacingRegex.Replace(normalized, "$1");
        return normalized;
    }

    private static string EscapeMarkdownUrl(string url)
    {
        return url
            .Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal)
            .Replace(" ", "%20", StringComparison.Ordinal);
    }

    private static bool TryParsePlainTextListItem(string line, out PlainTextListItem item)
    {
        var trimmedLine = line.TrimEnd();
        var indent = CountIndentation(trimmedLine);
        var content = trimmedLine.TrimStart(' ', '\t');

        if (TryParseMarkdownTaskListItem(content, indent, out item))
        {
            return true;
        }

        if (TryParseCheckboxGlyphListItem(content, indent, out item))
        {
            return true;
        }

        var unorderedMatch = UnorderedListMarkerRegex.Match(content);
        if (unorderedMatch.Success)
        {
            var text = unorderedMatch.Groups["text"].Value;
            if (TryStripLeadingCheckboxText(text, out var checkboxState, out var checkboxText))
            {
                item = PlainTextListItem.Task(indent, checkboxState, checkboxText);
                return true;
            }

            item = PlainTextListItem.Unordered(indent, text);
            return true;
        }

        var orderedMatch = OrderedListMarkerRegex.Match(content);
        if (orderedMatch.Success)
        {
            var text = orderedMatch.Groups["text"].Value;
            if (TryStripLeadingCheckboxText(text, out var checkboxState, out var checkboxText))
            {
                item = PlainTextListItem.Task(indent, checkboxState, checkboxText);
                return true;
            }

            var numberToken = int.TryParse(orderedMatch.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : "1";
            item = PlainTextListItem.Ordered(indent, numberToken, text);
            return true;
        }

        // Handle Word sub-bullet characters (e.g. 'o' for level 2) in tab-separated format.
        if (content.Length >= 3 && content[1] == '\t')
        {
            var glyphDepth = GetWordBulletGlyphDepth(content[0]);
            if (glyphDepth > 0)
            {
                var subText = content[2..].Trim();
                if (!string.IsNullOrWhiteSpace(subText))
                {
                    item = PlainTextListItem.Unordered(indent + glyphDepth, subText);
                    return true;
                }
            }
        }

        item = PlainTextListItem.Unordered(0, string.Empty);
        return false;
    }

    private static bool TryParseMarkdownTaskListItem(string content, int indent, out PlainTextListItem item)
    {
        var taskMatch = MarkdownTaskMarkerRegex.Match(content);
        if (taskMatch.Success)
        {
            item = PlainTextListItem.Task(
                indent,
                IsCheckedState(taskMatch.Groups["state"].ValueSpan),
                taskMatch.Groups["text"].Value);
            return true;
        }

        item = PlainTextListItem.Unordered(0, string.Empty);
        return false;
    }

    private static bool TryParseCheckboxGlyphListItem(string content, int indent, out PlainTextListItem item)
    {
        if (TryStripLeadingCheckboxText(content, out var checkboxState, out var checkboxText))
        {
            item = PlainTextListItem.Task(indent, checkboxState, checkboxText);
            return true;
        }

        item = PlainTextListItem.Unordered(0, string.Empty);
        return false;
    }

    private static IReadOnlyList<int> CalculateIndentLevels(IReadOnlyList<PlainTextListItem> items)
    {
        var levels = new List<int>(items.Count);
        var indentStack = new List<int>();

        foreach (var item in items)
        {
            if (indentStack.Count == 0)
            {
                indentStack.Add(item.Indent);
                levels.Add(0);
                continue;
            }

            if (item.Indent > indentStack[^1])
            {
                indentStack.Add(item.Indent);
                levels.Add(indentStack.Count - 1);
                continue;
            }

            while (indentStack.Count > 1 && item.Indent < indentStack[^1])
            {
                indentStack.RemoveAt(indentStack.Count - 1);
            }

            if (item.Indent > indentStack[^1])
            {
                indentStack.Add(item.Indent);
            }
            else if (item.Indent < indentStack[^1])
            {
                indentStack[0] = item.Indent;
            }

            levels.Add(indentStack.Count - 1);
        }

        return levels;
    }

    private static string GetPlainTextMarker(PlainTextListItem item)
    {
        return item.Kind switch
        {
            PlainTextListKind.Ordered => $"{item.OrderedNumber}. ",
            PlainTextListKind.Task => $"- [{(item.IsChecked ? "x" : " ")}] ",
            _ => "- "
        };
    }

    /// <summary>
    /// Returns the nesting depth for a Word bullet glyph character.
    /// Word default list levels: • (level 1 / depth 0), o (level 2 / depth 1), ▪/■ (level 3 / depth 2).
    /// Returns -1 for characters that are not recognised Word bullet glyphs.
    /// </summary>
    private static int GetWordBulletGlyphDepth(char glyph)
    {
        return glyph switch
        {
            '•' or '◦' or '-' or '*' or '+' or '–' or '▶' or '►' => 0,
            'o' => 1,
            '▪' or '■' => 2,
            _ => -1
        };
    }

    private static int CountIndentation(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character == ' ')
            {
                count++;
                continue;
            }

            if (character == '\t')
            {
                count += 4;
                continue;
            }

            break;
        }

        return count;
    }

    private static int GetOrderedListStart(IElement listElement)
    {
        var startValue = listElement.GetAttribute("start");
        return int.TryParse(startValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1;
    }

    private static bool? TryGetCheckboxState(IElement listItem)
    {
        if (TryParseCheckboxState(listItem.GetAttribute("aria-checked")) is { } directState)
        {
            return directState;
        }

        foreach (var child in listItem.ChildNodes)
        {
            if (child is not IElement element || IsListElement(element))
            {
                continue;
            }

            if (IsCheckboxInputElement(element))
            {
                return element.HasAttribute("checked");
            }

            if (TryGetCheckboxStateFromDescendants(element) is { } descendantState)
            {
                return descendantState;
            }
        }

        return null;
    }

    private static bool? TryGetCheckboxStateFromDescendants(IElement element)
    {
        if (TryParseCheckboxState(element.GetAttribute("aria-checked")) is { } state)
        {
            return state;
        }

        if (IsCheckboxInputElement(element))
        {
            return element.HasAttribute("checked");
        }

        foreach (var child in element.Children)
        {
            if (IsListElement(child))
            {
                continue;
            }

            if (TryGetCheckboxStateFromDescendants(child) is { } descendantState)
            {
                return descendantState;
            }
        }

        return null;
    }

    private static bool TryStripLeadingCheckboxText(string text, out bool isChecked, out string remainingText)
    {
        var markdownTaskMatch = CheckboxTextRegex.Match(text);
        if (markdownTaskMatch.Success)
        {
            isChecked = IsCheckedState(markdownTaskMatch.Groups["state"].ValueSpan);
            remainingText = ClipboardHtmlUtilities.NormalizeWhitespace(markdownTaskMatch.Groups["text"].Value);
            return true;
        }

        if (text.Length > 0 && TryParseCheckboxGlyph(text[0]) is { } glyphState)
        {
            isChecked = glyphState;
            remainingText = ClipboardHtmlUtilities.NormalizeWhitespace(text[1..]);
            return true;
        }

        isChecked = false;
        remainingText = text;
        return false;
    }

    private static bool? TryParseCheckboxState(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            "mixed" => false,
            _ => null
        };
    }

    private static bool? TryParseCheckboxGlyph(char character)
    {
        return character switch
        {
            '☐' => false,
            '☑' or '☒' or '✅' or '✔' or '✓' => true,
            _ => null
        };
    }

    private static bool IsCheckedState(ReadOnlySpan<char> state)
    {
        return state.Length > 0 && char.ToLowerInvariant(state[0]) == 'x';
    }

    private static bool IsListElement(IElement element)
    {
        return element.LocalName.Equals("ul", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWrapperElement(IElement element)
    {
        return element.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("div", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("section", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("article", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("main", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("span", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("font", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnorableElement(IElement element)
    {
        return element.LocalName.Equals("meta", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasListOrTableAncestor(IElement element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)
                || ancestor.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase)
                || ancestor.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCheckboxInputElement(IElement element)
    {
        return element.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithClosingPunctuation(string token)
    {
        return token.Length > 0 && token[0] is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']';
    }

    private static bool EndsWithOpeningPunctuation(StringBuilder builder)
    {
        return builder.Length > 0 && builder[^1] is '(' or '[';
    }

    private sealed record HtmlListItem(string Text, IReadOnlyList<IElement> NestedLists, bool? IsChecked);

    private sealed class PlainTextListItem
    {
        private PlainTextListItem(int indent, PlainTextListKind kind, bool isChecked, string orderedNumber, string text)
        {
            Indent = indent;
            Kind = kind;
            IsChecked = isChecked;
            OrderedNumber = orderedNumber;
            Text = ClipboardHtmlUtilities.NormalizeWhitespace(text);
        }

        public int Indent { get; }

        public PlainTextListKind Kind { get; }

        public bool IsChecked { get; }

        public string OrderedNumber { get; }

        public string Text { get; set; }

        public static PlainTextListItem Ordered(int indent, string orderedNumber, string text)
        {
            return new PlainTextListItem(indent, PlainTextListKind.Ordered, isChecked: false, orderedNumber, text);
        }

        public static PlainTextListItem Task(int indent, bool isChecked, string text)
        {
            return new PlainTextListItem(indent, PlainTextListKind.Task, isChecked, string.Empty, text);
        }

        public static PlainTextListItem Unordered(int indent, string text)
        {
            return new PlainTextListItem(indent, PlainTextListKind.Unordered, isChecked: false, string.Empty, text);
        }
    }

    private enum PlainTextListKind
    {
        Unordered,
        Ordered,
        Task
    }
}
