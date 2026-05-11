using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Notey.App.Editing;

public sealed partial class MarkdownColorizingTransformer : DocumentColorizingTransformer
{
    private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.Parse("#565B68"));
    private static readonly IBrush HeadingBrush = new SolidColorBrush(Color.Parse("#ADC6FF"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#ADC6FF"));
    private static readonly IBrush TopicBrush = new SolidColorBrush(Color.Parse("#FFB786"));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#B9C8DE"));

    protected override void ColorizeLine(DocumentLine line)
    {
        var lineText = CurrentContext.Document.GetText(line);
        var lineOffset = line.Offset;

        ColorizeHeading(lineText, lineOffset);
        ColorizeInlineMatches(WikiLinkRegex(), lineText, lineOffset, LinkBrush);
        ColorizeInlineMatches(TopicRegex(), lineText, lineOffset, TopicBrush);
        ColorizeInlineMatches(InlineCodeRegex(), lineText, lineOffset, CodeBrush);
        ColorizeInlineMatches(MarkerRegex(), lineText, lineOffset, MarkerBrush);
    }

    private void ColorizeHeading(string lineText, int lineOffset)
    {
        var match = HeadingRegex().Match(lineText);
        if (!match.Success)
        {
            return;
        }

        var marker = match.Groups["marker"];
        ChangeForeground(lineOffset + marker.Index, lineOffset + marker.Index + marker.Length, MarkerBrush);
        ChangeForeground(lineOffset + match.Index, lineOffset + match.Index + match.Length, HeadingBrush);
    }

    private void ColorizeInlineMatches(Regex regex, string lineText, int lineOffset, IBrush brush)
    {
        foreach (Match match in regex.Matches(lineText))
        {
            ChangeForeground(lineOffset + match.Index, lineOffset + match.Index + match.Length, brush);
        }
    }

    private void ChangeForeground(int startOffset, int endOffset, IBrush brush)
    {
        ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
    }

    [GeneratedRegex(@"^(?<marker>#{1,6})\s+.+$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\[\[[^\]]+\]\]")]
    private static partial Regex WikiLinkRegex();

    [GeneratedRegex(@"(?<!\w)#[\p{L}\p{N}_/-]+")]
    private static partial Regex TopicRegex();

    [GeneratedRegex(@"`[^`]+`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^\s*(?:[-*+]\s+|\d+[.)]\s+|>\s*)")]
    private static partial Regex MarkerRegex();
}
