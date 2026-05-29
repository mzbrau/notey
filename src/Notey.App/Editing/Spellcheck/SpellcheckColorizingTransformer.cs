using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Notey.App.Editing.Spellcheck;

public sealed class SpellcheckColorizingTransformer(ISpellcheckService spellcheckService) : DocumentColorizingTransformer
{
    private static readonly TextDecorationCollection MisspellingDecorations =
    [
        new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Stroke = new SolidColorBrush(Color.Parse("#FFB4AB")),
            StrokeThickness = 1.5
        }
    ];

    private string? _indexedText;
    private IReadOnlyList<SpellcheckSpan> _spans = [];
    private bool _lastIndexedEnabled;

    public bool IsEnabled { get; set; } = true;

    public IReadOnlyList<SpellcheckSpan> Spans => _spans;

    public SpellcheckSpan? FindSpanAtOffset(int offset)
    {
        return MarkdownSpellcheckIndex.FindSpanAtOffset(_spans, offset);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!IsEnabled)
        {
            return;
        }

        var documentText = CurrentContext.Document.Text;
        EnsureIndexed(documentText);
        if (_spans.Count == 0)
        {
            return;
        }

        var lineEndOffset = line.EndOffset;
        for (var index = FindFirstSpanEndingAfter(line.Offset); index < _spans.Count; index++)
        {
            var span = _spans[index];
            if (span.Offset >= lineEndOffset)
            {
                break;
            }

            if (span.EndOffset <= line.Offset)
            {
                continue;
            }

            ChangeLinePart(
                Math.Max(span.Offset, line.Offset),
                Math.Min(span.EndOffset, lineEndOffset),
                element => element.TextRunProperties.SetTextDecorations(MisspellingDecorations));
        }
    }

    public void Refresh(string text)
    {
        EnsureIndexed(text);
    }

    private void EnsureIndexed(string text)
    {
        if (string.Equals(text, _indexedText, StringComparison.Ordinal)
            && IsEnabled == _lastIndexedEnabled)
        {
            return;
        }

        _indexedText = text;
        _lastIndexedEnabled = IsEnabled;
        if (!_lastIndexedEnabled)
        {
            _spans = [];
            return;
        }

        _spans = MarkdownSpellcheckIndex.Build(text, spellcheckService);
    }

    private int FindFirstSpanEndingAfter(int offset)
    {
        var low = 0;
        var high = _spans.Count;
        while (low < high)
        {
            var midpoint = low + ((high - low) / 2);
            if (_spans[midpoint].EndOffset <= offset)
            {
                low = midpoint + 1;
            }
            else
            {
                high = midpoint;
            }
        }

        return low;
    }
}
