using Avalonia.Headless.XUnit;
using Notey.App.Editing.Spellcheck;

namespace Notey.Tests;

#pragma warning disable xUnit1051 // AvaloniaFact does not provide xUnit test-context cancellation token support.

public sealed class SpellcheckEditorIntegrationTests
{
    [AvaloniaFact]
    public async Task Editor_attaches_spellcheck_transformer()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();

        var transformer = harness.Editor.TextArea.TextView.LineTransformers
            .OfType<SpellcheckColorizingTransformer>()
            .Single();
        await harness.SetEditorTextAsync("This speling should be marked.");
        transformer.Refresh(harness.Editor.Document.Text);

        Assert.True(transformer.IsEnabled);
        Assert.Contains(transformer.Spans, span => string.Equals(span.Word, "speling", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(harness.Editor.ContextMenu);
    }
}
