using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;

namespace Notey.Tests;

#pragma warning disable xUnit1051 // AvaloniaFact does not provide xUnit test-context cancellation token support.

public sealed class PeopleWikiLinkDisplayTests
{
    [AvaloniaFact]
    public async Task Inactive_people_link_collapses_visually_without_changing_document_text()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        const string rawLink = "[[People/james simpson|James Simpson]]";
        var text = $"Met {rawLink} today.";
        await harness.SetEditorTextAsync(text);
        harness.Editor.CaretOffset = 0;
        await harness.DrainAsync();

        var element = harness.Editor.TextArea.TextView
            .GetOrConstructVisualLine(harness.Editor.Document.GetLineByNumber(1))
            .Elements
            .SingleOrDefault(candidate => candidate.DocumentLength == rawLink.Length && candidate.VisualLength == "James Simpson".Length);

        Assert.NotNull(element);
        Assert.Equal(text, harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Caret_adjacent_people_link_expands_to_raw_document_text()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        const string rawLink = "[[People/james simpson|James Simpson]]";
        var text = $"Met {rawLink} today.";
        await harness.SetEditorTextAsync(text);
        harness.Editor.CaretOffset = text.IndexOf(rawLink, StringComparison.Ordinal);
        await harness.DrainAsync();

        var collapsedElement = harness.Editor.TextArea.TextView
            .GetOrConstructVisualLine(harness.Editor.Document.GetLineByNumber(1))
            .Elements
            .SingleOrDefault(candidate => candidate.DocumentLength == rawLink.Length && candidate.VisualLength == "James Simpson".Length);

        Assert.Null(collapsedElement);
        Assert.Equal(text, harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Collapsed_people_link_reports_absolute_caret_positions()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        const string rawLink = "[[People/james simpson|James Simpson]]";
        var text = $"Met {rawLink} today.";
        await harness.SetEditorTextAsync(text);
        harness.Editor.CaretOffset = 0;
        await harness.DrainAsync();

        var element = harness.Editor.TextArea.TextView
            .GetOrConstructVisualLine(harness.Editor.Document.GetLineByNumber(1))
            .Elements
            .Single(candidate => candidate.DocumentLength == rawLink.Length && candidate.VisualLength == "James Simpson".Length);

        Assert.Equal(element.VisualColumn + element.VisualLength, element.GetNextCaretPosition(
            element.VisualColumn,
            LogicalDirection.Forward,
            CaretPositioningMode.Normal));
        Assert.Equal(element.VisualColumn, element.GetNextCaretPosition(
            element.VisualColumn + element.VisualLength,
            LogicalDirection.Backward,
            CaretPositioningMode.Normal));
    }
}
