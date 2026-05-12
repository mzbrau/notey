using System.Globalization;

namespace Notey.Ocr;

public static class TesseractTsvParser
{
    private const int LevelColumn = 0;
    private const int PageColumn = 1;
    private const int BlockColumn = 2;
    private const int ParagraphColumn = 3;
    private const int LineColumn = 4;
    private const int ConfidenceColumn = 10;
    private const int TextColumn = 11;

    public static TesseractTsvParseResult Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new TesseractTsvParseResult(string.Empty, null);
        }

        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 1 || !lines[0].StartsWith("level\t", StringComparison.OrdinalIgnoreCase))
        {
            return new TesseractTsvParseResult(output.Trim(), null);
        }

        var textLines = new List<(string Key, List<string> Words)>();
        var confidences = new List<double>();

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length <= TextColumn || columns[LevelColumn] != "5")
            {
                continue;
            }

            var word = columns[TextColumn].Trim();
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            var key = string.Join(
                '.',
                columns[PageColumn],
                columns[BlockColumn],
                columns[ParagraphColumn],
                columns[LineColumn]);
            var currentLine = textLines.LastOrDefault();
            if (currentLine.Words is null || !string.Equals(currentLine.Key, key, StringComparison.Ordinal))
            {
                currentLine = (key, []);
                textLines.Add(currentLine);
            }

            currentLine.Words.Add(word);

            if (double.TryParse(columns[ConfidenceColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                && confidence >= 0)
            {
                confidences.Add(confidence / 100);
            }
        }

        var text = string.Join(
            '\n',
            textLines
                .Select(static line => string.Join(' ', line.Words))
                .Where(static line => !string.IsNullOrWhiteSpace(line)));
        double? aggregateConfidence = confidences.Count == 0 ? null : confidences.Average();

        return new TesseractTsvParseResult(text, aggregateConfidence);
    }
}

public sealed record TesseractTsvParseResult(string Text, double? Confidence);
