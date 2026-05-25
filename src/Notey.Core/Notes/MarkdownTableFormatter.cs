using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Notey.Core.Notes;

public enum MarkdownTableNavigationDirection
{
    Forward,
    Backward
}

public static class MarkdownTableFormatter
{
    private const int MinimumInferredColumnCount = 2;
    private const int MaximumInferredColumnCount = 8;
    private const int MaximumColumnPaddingWidth = 40;

    private static readonly HashSet<string> CommonHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "first name",
        "last name",
        "full name",
        "description",
        "item",
        "date",
        "status",
        "owner",
        "type",
        "notes",
        "note",
        "title",
        "role",
        "priority",
        "id",
        "reference",
        "customer",
        "client",
        "account",
        "company",
        "department",
        "team",
        "project",
        "task",
        "assignee",
        "due date",
        "deadline",
        "amount",
        "cost",
        "price",
        "quantity",
        "service",
        "contact",
        "email",
        "phone",
        "address",
        "city",
        "state",
        "postcode",
        "birthday",
        "relationship",
        "incident",
        "call type",
        "caller",
        "unit",
        "station",
        "location",
        "response",
        "arrival",
        "cleared",
        "severity",
        "agency"
    };

    public static bool TryConvertHtmlTable(string html, out string markdownTable)
    {
        ArgumentNullException.ThrowIfNull(html);

        markdownTable = string.Empty;
        var fragment = ClipboardHtmlUtilities.ExtractHtmlFragment(html);
        var normalizedFragment = Regex.Replace(fragment, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        var document = new HtmlParser().ParseDocument(normalizedFragment);
        var table = document.QuerySelector("table");
        if (table is null)
        {
            return false;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.Children
                .Where(static child => IsTableCellElement(child))
                .ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            if (cells.Any(static cell => cell.HasAttribute("rowspan") || cell.HasAttribute("colspan")))
            {
                return false;
            }

            rows.Add(cells.Select(static cell => ClipboardHtmlUtilities.NormalizeWhitespace(cell.TextContent)).ToList());
        }

        return TryBuildMarkdownTable(rows, out markdownTable);
    }

    public static bool ContainsHtmlTable(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var fragment = ClipboardHtmlUtilities.ExtractHtmlFragment(html);
        var document = new HtmlParser().ParseDocument(fragment);
        return document.QuerySelector("table") is not null;
    }

    public static bool TryConvertPlainTextTable(string text, out string markdownTable)
    {
        ArgumentNullException.ThrowIfNull(text);

        markdownTable = string.Empty;
        var normalizedText = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Any(static line => line.Contains('\t')))
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var line in lines)
            {
                var cells = line.Split('\t').Select(ClipboardHtmlUtilities.NormalizeWhitespace).ToList();
                if (cells.Count < 2)
                {
                    return false;
                }

                rows.Add(cells);
            }

            return TryBuildMarkdownTable(rows, out markdownTable);
        }

        return TryConvertNewlineCellTable(normalizedText, out markdownTable);
    }

    public static bool TryConvertRtfTable(string rtf, out string markdownTable)
    {
        ArgumentNullException.ThrowIfNull(rtf);

        markdownTable = string.Empty;
        if (!rtf.Contains(@"\cell", StringComparison.OrdinalIgnoreCase)
            || !rtf.Contains(@"\row", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rows = new List<IReadOnlyList<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();

        for (var i = 0; i < rtf.Length; i++)
        {
            var character = rtf[i];
            if (character == '{' || character == '}')
            {
                continue;
            }

            if (character != '\\')
            {
                currentCell.Append(character);
                continue;
            }

            if (i + 1 >= rtf.Length)
            {
                continue;
            }

            var next = rtf[i + 1];
            if (next is '\\' or '{' or '}')
            {
                currentCell.Append(next);
                i++;
                continue;
            }

            if (next == '\'')
            {
                if (i + 3 < rtf.Length
                    && int.TryParse(rtf.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
                {
                    currentCell.Append((char)codePoint);
                    i += 3;
                }

                continue;
            }

            var controlStart = i + 1;
            var controlEnd = controlStart;
            while (controlEnd < rtf.Length && char.IsLetter(rtf[controlEnd]))
            {
                controlEnd++;
            }

            if (controlEnd == controlStart)
            {
                i = controlStart;
                continue;
            }

            var control = rtf[controlStart..controlEnd];
            while (controlEnd < rtf.Length && (char.IsDigit(rtf[controlEnd]) || rtf[controlEnd] == '-'))
            {
                controlEnd++;
            }

            if (controlEnd < rtf.Length && rtf[controlEnd] == ' ')
            {
                controlEnd++;
            }

            if (control.Equals("cell", StringComparison.OrdinalIgnoreCase))
            {
                currentRow.Add(ClipboardHtmlUtilities.NormalizeWhitespace(currentCell.ToString()));
                currentCell.Clear();
            }
            else if (control.Equals("row", StringComparison.OrdinalIgnoreCase))
            {
                if (currentRow.Count > 0)
                {
                    rows.Add(currentRow.ToList());
                    currentRow.Clear();
                }

                currentCell.Clear();
            }
            else if (control.Equals("line", StringComparison.OrdinalIgnoreCase)
                || control.Equals("par", StringComparison.OrdinalIgnoreCase))
            {
                currentCell.Append(' ');
            }

            i = controlEnd - 1;
        }

        return TryBuildMarkdownTable(rows, out markdownTable);
    }

    public static MarkdownTextEdit? TryFormatTables(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateCaretOffset(text, caretOffset);

        var formatted = FormatTables(text);
        if (string.Equals(text, formatted, StringComparison.Ordinal))
        {
            return null;
        }

        var newCaretOffset = Math.Min(caretOffset, formatted.Length);
        return new MarkdownTextEdit(0, text.Length, formatted, newCaretOffset, 0, newCaretOffset);
    }

    public static MarkdownTextEdit? TryCreateTableRowContinuation(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateCaretOffset(text, caretOffset);

        var edit = TryNavigateTableCell(text, caretOffset, selectionLength: 0, MarkdownTableNavigationDirection.Forward);
        return edit is not null && (edit.ReplacementLength > 0 || edit.ReplacementText.Length > 0) ? edit : null;
    }

    public static MarkdownTextEdit? TryNavigateTableCell(
        string text,
        int caretOffset,
        int selectionLength,
        MarkdownTableNavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateCaretOffset(text, caretOffset);
        if (selectionLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionLength), "Selection length must be non-negative.");
        }

        if (selectionLength > 0)
        {
            return null;
        }

        var lines = ParseLines(text);
        var lineIndex = FindLineIndex(lines, caretOffset);
        if (lineIndex < 0 || IsLineInFence(lines, lineIndex))
        {
            return null;
        }

        var blocks = FindMarkdownTableBlocks(lines);
        var block = blocks.FirstOrDefault(tableBlock => tableBlock.StartLine <= lineIndex && lineIndex <= tableBlock.EndLine);
        if (block is null || lineIndex == block.StartLine + 1)
        {
            return null;
        }

        var line = lines[lineIndex];
        var row = TryParseMarkdownRow(line.Content);
        if (row is null || row.Cells.Count < 2 || IsSeparatorRow(row))
        {
            return null;
        }

        var caretColumn = caretOffset - line.Start;
        var cellIndex = GetCellIndexAtColumnOrEdge(row, caretColumn);
        if (cellIndex < 0)
        {
            return null;
        }

        var editableLineIndices = GetEditableLineIndices(block);
        var editableRowIndex = editableLineIndices.IndexOf(lineIndex);
        if (editableRowIndex < 0)
        {
            return null;
        }

        return direction switch
        {
            MarkdownTableNavigationDirection.Forward => TryNavigateForward(
                text,
                caretOffset,
                lines,
                block,
                editableLineIndices,
                editableRowIndex,
                cellIndex),
            MarkdownTableNavigationDirection.Backward => TryNavigateBackward(
                caretOffset,
                lines,
                editableLineIndices,
                editableRowIndex,
                cellIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown table navigation direction.")
        };
    }

    private static string FormatTables(string text)
    {
        var lines = ParseLines(text);
        var blocks = FindMarkdownTableBlocks(lines);
        if (blocks.Count == 0)
        {
            return text;
        }

        var formattedLines = lines.Select(static line => line.Content).ToArray();
        foreach (var block in blocks)
        {
            var blockLines = lines
                .Skip(block.StartLine)
                .Take(block.EndLine - block.StartLine + 1)
                .Select(static line => line.Content)
                .ToList();

            var formattedBlock = FormatTableBlock(blockLines);
            for (var i = 0; i < formattedBlock.Count; i++)
            {
                formattedLines[block.StartLine + i] = formattedBlock[i];
            }
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < lines.Count; i++)
        {
            builder.Append(formattedLines[i]);
            builder.Append(lines[i].NewLine);
        }

        return builder.ToString();
    }

    private static MarkdownTextEdit? TryNavigateForward(
        string text,
        int caretOffset,
        IReadOnlyList<DocumentLine> lines,
        TableBlock block,
        IReadOnlyList<int> editableLineIndices,
        int editableRowIndex,
        int cellIndex)
    {
        var lineIndex = editableLineIndices[editableRowIndex];
        var row = TryParseMarkdownRow(lines[lineIndex].Content);
        if (row is null)
        {
            return null;
        }

        if (cellIndex < row.Cells.Count - 1)
        {
            return CreateNavigationEdit(caretOffset, lines[lineIndex], cellIndex + 1);
        }

        if (editableRowIndex < editableLineIndices.Count - 1)
        {
            return CreateNavigationEdit(caretOffset, lines[editableLineIndices[editableRowIndex + 1]], 0);
        }

        return CreateRowInsertionEdit(text, lines, block);
    }

    private static MarkdownTextEdit? TryNavigateBackward(
        int caretOffset,
        IReadOnlyList<DocumentLine> lines,
        IReadOnlyList<int> editableLineIndices,
        int editableRowIndex,
        int cellIndex)
    {
        var lineIndex = editableLineIndices[editableRowIndex];
        if (cellIndex > 0)
        {
            return CreateNavigationEdit(caretOffset, lines[lineIndex], cellIndex - 1);
        }

        if (editableRowIndex > 0)
        {
            var previousLine = lines[editableLineIndices[editableRowIndex - 1]];
            var previousRow = TryParseMarkdownRow(previousLine.Content);
            if (previousRow is null)
            {
                return null;
            }

            return CreateNavigationEdit(caretOffset, previousLine, previousRow.Cells.Count - 1);
        }

        return new MarkdownTextEdit(caretOffset, 0, string.Empty, caretOffset, 0, caretOffset);
    }

    private static MarkdownTextEdit? CreateNavigationEdit(int caretOffset, DocumentLine targetLine, int targetCellIndex)
    {
        var targetRow = TryParseMarkdownRow(targetLine.Content);
        if (targetRow is null || targetCellIndex < 0 || targetCellIndex >= targetRow.Cells.Count)
        {
            return null;
        }

        var targetOffset = GetCellCaretOffset(targetLine, targetRow, targetCellIndex);
        return new MarkdownTextEdit(caretOffset, 0, string.Empty, targetOffset, 0, targetOffset);
    }

    private static MarkdownTextEdit? CreateRowInsertionEdit(
        string text,
        IReadOnlyList<DocumentLine> lines,
        TableBlock block)
    {
        var blockLines = lines
            .Skip(block.StartLine)
            .Take(block.EndLine - block.StartLine + 1)
            .Select(static line => line.Content)
            .ToList();
        var columnCount = blockLines
            .Select(TryParseMarkdownRow)
            .Where(static row => row is not null)
            .Max(static row => row!.Cells.Count);
        if (columnCount < 2)
        {
            return null;
        }

        blockLines.Add(CreateEmptyRow(columnCount));
        var formattedBlock = FormatTableBlock(blockLines);
        var newline = GetPreferredNewLine(text, lines[block.EndLine]);
        var replacementText = string.Join(newline, formattedBlock);
        var replacementStart = lines[block.StartLine].Start;
        var replacementLength = lines[block.EndLine].End - replacementStart;
        var newRowStart = GetJoinedLineStart(formattedBlock, newline, formattedBlock.Count - 1);
        var newRow = TryParseMarkdownRow(formattedBlock[^1]);
        if (newRow is null)
        {
            return null;
        }

        var caretOffset = replacementStart + newRowStart + GetCellCaretColumn(formattedBlock[^1], newRow, 0);
        return new MarkdownTextEdit(replacementStart, replacementLength, replacementText, caretOffset, 0, caretOffset);
    }

    private static List<int> GetEditableLineIndices(TableBlock block)
    {
        var lineIndices = new List<int> { block.StartLine };
        for (var lineIndex = block.StartLine + 2; lineIndex <= block.EndLine; lineIndex++)
        {
            lineIndices.Add(lineIndex);
        }

        return lineIndices;
    }

    private static int GetJoinedLineStart(IReadOnlyList<string> lines, string newline, int lineIndex)
    {
        var offset = 0;
        for (var i = 0; i < lineIndex; i++)
        {
            offset += lines[i].Length + newline.Length;
        }

        return offset;
    }

    private static bool TryConvertNewlineCellTable(string text, out string markdownTable)
    {
        markdownTable = string.Empty;
        var cells = text
            .Split('\n', StringSplitOptions.None)
            .Select(ClipboardHtmlUtilities.NormalizeWhitespace)
            .ToList();
        TrimEmptyEdges(cells);

        if (cells.Count < MinimumInferredColumnCount * 2 || cells.Any(static cell => cell.Contains('\t')))
        {
            return false;
        }

        var candidates = new List<InferredTableCandidate>();
        var maxColumns = Math.Min(MaximumInferredColumnCount, cells.Count / 2);
        for (var columnCount = MinimumInferredColumnCount; columnCount <= maxColumns; columnCount++)
        {
            if (cells.Count % columnCount != 0)
            {
                continue;
            }

            var rowCount = cells.Count / columnCount;
            if (rowCount < 2)
            {
                continue;
            }

            var rows = ChunkCells(cells, columnCount);
            if (!rows[0].All(IsLikelyHeaderCell))
            {
                continue;
            }

            var dataRows = rows.Skip(1).ToList();
            if (dataRows.All(static row => row.All(string.IsNullOrWhiteSpace)))
            {
                continue;
            }

            var score = ScoreInferredTable(rows, columnCount);
            if (score >= GetMinimumInferenceScore(columnCount))
            {
                candidates.Add(new InferredTableCandidate(columnCount, score, rows));
            }
        }

        var best = candidates
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.ColumnCount)
            .FirstOrDefault();

        return best is not null && TryBuildMarkdownTable(best.Rows, out markdownTable);
    }

    private static void TrimEmptyEdges(List<string> cells)
    {
        while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[0]))
        {
            cells.RemoveAt(0);
        }

        while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[^1]))
        {
            cells.RemoveAt(cells.Count - 1);
        }
    }

    private static List<IReadOnlyList<string>> ChunkCells(IReadOnlyList<string> cells, int columnCount)
    {
        var rows = new List<IReadOnlyList<string>>();
        for (var rowStart = 0; rowStart < cells.Count; rowStart += columnCount)
        {
            rows.Add(cells.Skip(rowStart).Take(columnCount).ToList());
        }

        return rows;
    }

    private static int ScoreInferredTable(IReadOnlyList<IReadOnlyList<string>> rows, int columnCount)
    {
        var score = columnCount * 2;
        score += rows[0].Count(IsLikelyHeaderCell) * 2;
        score += rows[0].Count(static cell => CommonHeaderNames.Contains(cell)) * 2;

        if (rows.Count == 2)
        {
            score += 4;
        }

        var dataCells = rows.Skip(1).SelectMany(static row => row).Where(static cell => !string.IsNullOrWhiteSpace(cell)).ToList();
        if (dataCells.Any(static cell => cell.Any(char.IsLower)))
        {
            score += 2;
        }

        var likelyHeaderDataCells = dataCells.Count(IsLikelyHeaderCell);
        if (dataCells.Count > 0 && likelyHeaderDataCells * 2 > dataCells.Count)
        {
            score -= 4;
        }

        return score;
    }

    private static int GetMinimumInferenceScore(int columnCount)
    {
        return columnCount == 2 ? 10 : columnCount * 4;
    }

    private static bool IsLikelyHeaderCell(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell) || cell.Length > 40)
        {
            return false;
        }

        if (cell.StartsWith("- ", StringComparison.Ordinal)
            || cell.StartsWith("* ", StringComparison.Ordinal)
            || cell.StartsWith("+ ", StringComparison.Ordinal)
            || cell.Contains('.', StringComparison.Ordinal)
            || cell.Contains('?', StringComparison.Ordinal)
            || cell.Contains('!', StringComparison.Ordinal)
            || cell.Contains(';', StringComparison.Ordinal))
        {
            return false;
        }

        var words = cell.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is > 0 and <= 3 && words.All(IsLikelyHeaderWord);
    }

    private static bool IsLikelyHeaderWord(string word)
    {
        var letters = word.Where(char.IsLetter).ToList();
        if (letters.Count == 0)
        {
            return word.All(char.IsDigit);
        }

        return letters.All(char.IsUpper)
            || (char.IsUpper(letters[0]) && letters.Skip(1).All(char.IsLower));
    }

    private static IReadOnlyList<string> FormatTableBlock(IReadOnlyList<string> lines)
    {
        var rows = lines.Select(static line => TryParseMarkdownRow(line)!).ToList();
        var columnCount = rows.Max(static row => row.Cells.Count);
        var alignments = GetAlignments(rows[1], columnCount);
        var normalizedRows = rows
            .Select((row, index) => NormalizeRow(row, columnCount, isSeparator: index == 1))
            .ToList();

        var widths = new int[columnCount];
        for (var column = 0; column < columnCount; column++)
        {
            var width = GetMinimumSeparatorWidth(alignments[column]);
            foreach (var row in normalizedRows.Where(static (_, index) => index != 1))
            {
                width = Math.Max(width, row[column].Length);
            }

            widths[column] = Math.Min(width, MaximumColumnPaddingWidth);
        }

        var prefix = GetLeadingWhitespace(lines[0]);
        var formatted = new List<string>(normalizedRows.Count);
        for (var rowIndex = 0; rowIndex < normalizedRows.Count; rowIndex++)
        {
            formatted.Add(rowIndex == 1
                ? prefix + FormatSeparatorRow(alignments, widths)
                : prefix + FormatDataRow(normalizedRows[rowIndex], widths));
        }

        return formatted;
    }

    private static List<string> NormalizeRow(MarkdownRow row, int columnCount, bool isSeparator)
    {
        var values = new List<string>(columnCount);
        for (var column = 0; column < columnCount; column++)
        {
            var value = column < row.Cells.Count ? row.Cells[column].Text.Trim() : string.Empty;
            values.Add(isSeparator ? value.Replace(" ", string.Empty, StringComparison.Ordinal) : value);
        }

        return values;
    }

    private static string FormatDataRow(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        var builder = new StringBuilder();
        builder.Append('|');
        for (var i = 0; i < cells.Count; i++)
        {
            builder.Append(' ');
            builder.Append(cells[i].PadRight(widths[i]));
            builder.Append(" |");
        }

        return builder.ToString();
    }

    private static string FormatSeparatorRow(IReadOnlyList<TableAlignment> alignments, IReadOnlyList<int> widths)
    {
        var builder = new StringBuilder();
        builder.Append('|');
        for (var i = 0; i < widths.Count; i++)
        {
            builder.Append(' ');
            builder.Append(CreateSeparatorCell(alignments[i], widths[i]));
            builder.Append(" |");
        }

        return builder.ToString();
    }

    private static string CreateSeparatorCell(TableAlignment alignment, int width)
    {
        return alignment switch
        {
            TableAlignment.Left => ":" + new string('-', width - 1),
            TableAlignment.Center => ":" + new string('-', width - 2) + ":",
            TableAlignment.Right => new string('-', width - 1) + ":",
            _ => new string('-', width)
        };
    }

    private static int GetMinimumSeparatorWidth(TableAlignment alignment)
    {
        return alignment switch
        {
            TableAlignment.Left or TableAlignment.Right => 4,
            TableAlignment.Center => 5,
            _ => 3
        };
    }

    private static IReadOnlyList<TableAlignment> GetAlignments(MarkdownRow separatorRow, int columnCount)
    {
        var alignments = new List<TableAlignment>(columnCount);
        for (var i = 0; i < columnCount; i++)
        {
            var value = i < separatorRow.Cells.Count ? separatorRow.Cells[i].Text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) : string.Empty;
            alignments.Add(ParseAlignment(value));
        }

        return alignments;
    }

    private static TableAlignment ParseAlignment(string value)
    {
        if (!IsSeparatorCell(value))
        {
            return TableAlignment.None;
        }

        var left = value.StartsWith(':');
        var right = value.EndsWith(':');
        return (left, right) switch
        {
            (true, true) => TableAlignment.Center,
            (true, false) => TableAlignment.Left,
            (false, true) => TableAlignment.Right,
            _ => TableAlignment.None
        };
    }

    private static bool TryBuildMarkdownTable(IReadOnlyList<IReadOnlyList<string>> rows, out string markdownTable)
    {
        markdownTable = string.Empty;
        if (rows.Count == 0)
        {
            return false;
        }

        var columnCount = rows.Max(static row => row.Count);
        if (columnCount < 2 || rows.Any(static row => row.Count == 0))
        {
            return false;
        }

        var tableLines = new List<string>(rows.Count + 1)
        {
            FormatDataRow(PadRow(rows[0], columnCount), Enumerable.Repeat(3, columnCount).ToArray()),
            FormatSeparatorRow(Enumerable.Repeat(TableAlignment.None, columnCount).ToArray(), Enumerable.Repeat(3, columnCount).ToArray())
        };

        foreach (var row in rows.Skip(1))
        {
            tableLines.Add(FormatDataRow(PadRow(row, columnCount), Enumerable.Repeat(3, columnCount).ToArray()));
        }

        markdownTable = string.Join('\n', FormatTableBlock(tableLines));
        return true;
    }

    private static List<string> PadRow(IReadOnlyList<string> row, int columnCount)
    {
        var cells = new List<string>(columnCount);
        for (var i = 0; i < columnCount; i++)
        {
            cells.Add(i < row.Count ? row[i] : string.Empty);
        }

        return cells;
    }

    private static IReadOnlyList<TableBlock> FindMarkdownTableBlocks(IReadOnlyList<DocumentLine> lines)
    {
        var blocks = new List<TableBlock>();
        var inFence = false;
        for (var i = 0; i < lines.Count - 1; i++)
        {
            if (IsFenceLine(lines[i].Content))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            var header = TryParseMarkdownRow(lines[i].Content);
            var separator = TryParseMarkdownRow(lines[i + 1].Content);
            if (header is null || separator is null || !IsSeparatorRow(separator))
            {
                continue;
            }

            var endLine = i + 1;
            while (endLine + 1 < lines.Count
                && !IsFenceLine(lines[endLine + 1].Content)
                && TryParseMarkdownRow(lines[endLine + 1].Content) is { } row
                && !IsSeparatorRow(row))
            {
                endLine++;
            }

            blocks.Add(new TableBlock(i, endLine));
            i = endLine;
        }

        return blocks;
    }

    private static bool IsLineInFence(IReadOnlyList<DocumentLine> lines, int lineIndex)
    {
        var inFence = false;
        for (var i = 0; i <= lineIndex && i < lines.Count; i++)
        {
            if (IsFenceLine(lines[i].Content))
            {
                inFence = !inFence;
            }
        }

        return inFence;
    }

    private static bool IsFenceLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static bool IsSeparatorRow(MarkdownRow row)
    {
        return row.Cells.Count >= 2 && row.Cells.All(static cell => IsSeparatorCell(cell.Text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)));
    }

    private static bool IsSeparatorCell(string value)
    {
        var hyphens = value.Trim(':');
        return hyphens.Length >= 3
            && hyphens.All(static character => character == '-')
            && value.Count(static character => character == ':') <= 2
            && (!value.Contains(':', StringComparison.Ordinal)
                || value.StartsWith(":", StringComparison.Ordinal)
                || value.EndsWith(":", StringComparison.Ordinal));
    }

    private static MarkdownRow? TryParseMarkdownRow(string line)
    {
        var pipePositions = GetUnescapedPipePositions(line);
        if (pipePositions.Count == 0)
        {
            return null;
        }

        var firstNonWhitespace = FindFirstNonWhitespace(line);
        var lastNonWhitespace = FindLastNonWhitespace(line);
        if (firstNonWhitespace < 0 || lastNonWhitespace < 0)
        {
            return null;
        }

        var hasLeadingPipe = line[firstNonWhitespace] == '|';
        var hasTrailingPipe = line[lastNonWhitespace] == '|';
        var boundaries = new List<int>();
        boundaries.Add(hasLeadingPipe ? firstNonWhitespace : -1);
        boundaries.AddRange(pipePositions.Where(position =>
            (!hasLeadingPipe || position != firstNonWhitespace) && (!hasTrailingPipe || position != lastNonWhitespace)));
        boundaries.Add(hasTrailingPipe ? lastNonWhitespace : line.Length);

        if (boundaries.Count < 3)
        {
            return null;
        }

        var cells = new List<MarkdownCell>();
        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var start = boundaries[i] + 1;
            var end = boundaries[i + 1];
            cells.Add(new MarkdownCell(line[start..end].Trim(), start, end));
        }

        return cells.Count >= 2 ? new MarkdownRow(cells) : null;
    }

    private static List<int> GetUnescapedPipePositions(string line)
    {
        var positions = new List<int>();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '|' && !IsEscaped(line, i))
            {
                positions.Add(i);
            }
        }

        return positions;
    }

    private static bool IsEscaped(string text, int index)
    {
        var backslashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
        {
            backslashCount++;
        }

        return backslashCount % 2 == 1;
    }

    private static int GetCellIndexAtColumn(MarkdownRow row, int column)
    {
        for (var i = 0; i < row.Cells.Count; i++)
        {
            if (column >= row.Cells[i].StartColumn && column <= row.Cells[i].EndColumn)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetCellIndexAtColumnOrEdge(MarkdownRow row, int column)
    {
        var cellIndex = GetCellIndexAtColumn(row, column);
        if (cellIndex >= 0)
        {
            return cellIndex;
        }

        if (column <= row.Cells[0].StartColumn)
        {
            return 0;
        }

        if (column >= row.Cells[^1].EndColumn)
        {
            return row.Cells.Count - 1;
        }

        return -1;
    }

    private static int GetCellCaretOffset(DocumentLine line, MarkdownRow row, int cellIndex)
    {
        return line.Start + GetCellCaretColumn(line.Content, row, cellIndex);
    }

    private static int GetCellCaretColumn(string line, MarkdownRow row, int cellIndex)
    {
        var cell = row.Cells[cellIndex];
        var segment = line[cell.StartColumn..cell.EndColumn];
        var firstNonWhitespace = FindFirstNonWhitespace(segment);
        return firstNonWhitespace >= 0
            ? cell.StartColumn + firstNonWhitespace
            : Math.Min(cell.StartColumn + 1, cell.EndColumn);
    }

    private static string CreateEmptyRow(int columnCount)
    {
        return "| " + string.Join(" | ", Enumerable.Repeat(string.Empty, columnCount)) + " |";
    }

    private static List<DocumentLine> ParseLines(string text)
    {
        var lines = new List<DocumentLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var contentEnd = i;
            var newlineStart = i;
            if (contentEnd > start && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
                newlineStart--;
            }

            lines.Add(new DocumentLine(start, text[start..contentEnd], text[newlineStart..(i + 1)]));
            start = i + 1;
        }

        if (start <= text.Length)
        {
            lines.Add(new DocumentLine(start, text[start..], string.Empty));
        }

        return lines;
    }

    private static int FindLineIndex(IReadOnlyList<DocumentLine> lines, int offset)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (offset >= lines[i].Start && offset <= lines[i].End)
            {
                return i;
            }

            if (offset > lines[i].End && offset < lines[i].End + lines[i].NewLine.Length)
            {
                return i;
            }
        }

        return lines.Count == 0 ? -1 : lines.Count - 1;
    }

    private static string GetPreferredNewLine(string text, DocumentLine line)
    {
        if (line.NewLine.Length > 0)
        {
            return line.NewLine;
        }

        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static bool IsTableCellElement(IElement element)
    {
        return element.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLeadingWhitespace(string text)
    {
        var firstNonWhitespace = FindFirstNonWhitespace(text);
        return firstNonWhitespace <= 0 ? string.Empty : text[..firstNonWhitespace];
    }

    private static int FindFirstNonWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindLastNonWhitespace(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static void ValidateCaretOffset(string text, int caretOffset)
    {
        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset), "Caret offset must be within the markdown text.");
        }
    }

    private enum TableAlignment
    {
        None,
        Left,
        Center,
        Right
    }

    private sealed record DocumentLine(int Start, string Content, string NewLine)
    {
        public int End => Start + Content.Length;
    }

    private sealed record MarkdownRow(IReadOnlyList<MarkdownCell> Cells);

    private sealed record MarkdownCell(string Text, int StartColumn, int EndColumn);

    private sealed record TableBlock(int StartLine, int EndLine);

    private sealed record InferredTableCandidate(int ColumnCount, int Score, IReadOnlyList<IReadOnlyList<string>> Rows);
}
