using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Notey.App.Editing;

public sealed class PeopleWikiLinkElementGenerator : VisualLineElementGenerator
{
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#ADC6FF"));

    private string? _indexedText;
    private IReadOnlyList<PeopleWikiLinkSpan> _spans = [];
    private int _caretOffset;
    private int _selectionStart;
    private int _selectionLength;

    public event EventHandler<PeopleWikiLinkClickedEventArgs>? LinkClicked;

    public IReadOnlyList<PeopleWikiLinkSpan> Spans => _spans;

    public void SetActiveRange(int caretOffset, int selectionStart, int selectionLength)
    {
        _caretOffset = Math.Max(0, caretOffset);
        _selectionStart = Math.Max(0, selectionStart);
        _selectionLength = Math.Max(0, selectionLength);
    }

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);

        var text = context.Document.Text;
        if (!string.Equals(text, _indexedText, StringComparison.Ordinal))
        {
            _indexedText = text;
            _spans = PeopleWikiLinkIndex.Build(text);
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        foreach (var span in _spans)
        {
            if (span.Offset >= startOffset && !IsActive(span))
            {
                return span.Offset;
            }
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var span = _spans.FirstOrDefault(candidate => candidate.Offset == offset && !IsActive(candidate));
        return span is null
            ? null
            : new PeopleWikiLinkElement(span, OnLinkClicked);
    }

    private bool IsActive(PeopleWikiLinkSpan span)
    {
        if (_selectionLength > 0)
        {
            var spanEnd = span.Offset + span.Length;
            var selectionEnd = _selectionStart + _selectionLength;
            return _selectionStart < spanEnd && selectionEnd > span.Offset;
        }

        return _caretOffset >= span.Offset && _caretOffset <= span.Offset + span.Length;
    }

    private void OnLinkClicked(PeopleWikiLinkSpan span)
    {
        LinkClicked?.Invoke(this, new PeopleWikiLinkClickedEventArgs(span));
    }

    private sealed class PeopleWikiLinkElement(PeopleWikiLinkSpan span, Action<PeopleWikiLinkSpan> linkClicked)
        : VisualLineElement(span.DisplayText.Length, span.Length)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            var properties = new VisualLineElementTextRunProperties(context.GlobalTextRunProperties);
            properties.SetForegroundBrush(LinkBrush);
            properties.SetTextDecorations(TextDecorations.Underline);

            var start = Math.Clamp(startVisualColumn - VisualColumn, 0, span.DisplayText.Length);
            return new TextCharacters(span.DisplayText.AsMemory(start), properties);
        }

        public override int GetNextCaretPosition(int visualColumn, AvaloniaEdit.Document.LogicalDirection direction, CaretPositioningMode mode)
        {
            return direction == AvaloniaEdit.Document.LogicalDirection.Forward
                ? VisualColumn + VisualLength
                : VisualColumn;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                linkClicked(span);
                e.Handled = true;
            }
        }
    }
}

public sealed class PeopleWikiLinkClickedEventArgs(PeopleWikiLinkSpan span) : EventArgs
{
    public PeopleWikiLinkSpan Span { get; } = span;
}
