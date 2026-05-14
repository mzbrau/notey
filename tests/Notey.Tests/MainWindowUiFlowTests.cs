using Avalonia.Headless.XUnit;
using Notey.App.Views;

namespace Notey.Tests;

public sealed class MainWindowUiFlowTests
{
    [AvaloniaFact]
    public async Task Draft_can_be_processed_and_reopened_from_recent_notes()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var draft = """
            /meeting
            /customer Microsoft
            /topic Accounts

            Capture the implementation plan for the accounts launch.
            """;

        await harness.SetEditorTextAsync(draft);

        Assert.Equal("meeting | topic: Accounts | customer: Microsoft", harness.ContextText);

        await harness.Window.StartNewNoteAsync();
        await harness.DrainAsync();

        var expectedPath = harness.GetExpectedCustomerMeetingPath("Microsoft", "Accounts");
        Assert.True(File.Exists(expectedPath));
        var expectedContent = await File.ReadAllTextAsync(expectedPath);
        Assert.Contains("meeting: true", expectedContent);
        Assert.Contains("topic: \"Accounts\"", expectedContent);
        Assert.Contains("customer: \"Microsoft\"", expectedContent);
        Assert.Contains("Captured accounts launch context.", expectedContent);
        Assert.Contains("  - \"Jane Doe\"", expectedContent);
        Assert.Contains("  - \"#accounts\"", expectedContent);

        harness.RecentNoteChooser.Choose = notes =>
        {
            var selected = Assert.Single(notes, note => string.Equals(note.FilePath, expectedPath, StringComparison.Ordinal));
            return RecentNoteChoice.Open(selected);
        };

        await harness.Window.OpenRecentFinalNoteAsync();
        await harness.DrainAsync();

        Assert.Equal(expectedPath, harness.CurrentNotePathText);
        Assert.Equal("Opened recent note", harness.ContextText);
        Assert.Equal(expectedContent, harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Recent_note_can_be_opened_with_correct_path_and_content()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var content = """
            ---
            created: 2026-05-14T09:30+00:00
            processed: 2026-05-14T09:31+00:00
            meeting: false
            topic: "Roadmap"
            people: []
            tags: []
            links: []
            ---
            Arranged recent note body.
            """;
        var filePath = await harness.WriteFinalNoteAsync("roadmap.md", content);

        await harness.OpenRecentNoteAsync(filePath);

        Assert.Equal(filePath, harness.CurrentNotePathText);
        Assert.Equal("Opened recent note", harness.ContextText);
        Assert.Equal(content, harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Open_recent_note_edits_are_processed_in_place()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var originalContent = """
            ---
            created: 2026-05-14T09:30+00:00
            processed: 2026-05-14T09:31+00:00
            meeting: false
            topic: "Roadmap"
            people: []
            tags: []
            links: []
            ---
            Original recent note body.
            """;
        var filePath = await harness.WriteFinalNoteAsync("roadmap.md", originalContent);
        await harness.OpenRecentNoteAsync(filePath);

        await harness.SetEditorTextAsync("""
            ---
            created: 2026-05-14T09:30+00:00
            processed: 2026-05-14T09:31+00:00
            meeting: false
            topic: "Roadmap"
            people: []
            tags: []
            links: []
            ---
            Updated recent note body.
            """);
        harness.RecentNoteChooser.Choose = static _ => RecentNoteChoice.Cancel;

        await harness.Window.OpenRecentFinalNoteAsync();
        await harness.DrainAsync();

        var updatedContent = await File.ReadAllTextAsync(filePath);
        Assert.Equal(filePath, harness.CurrentNotePathText);
        Assert.Equal(updatedContent, harness.Editor.Document.Text);
        Assert.Contains("topic: \"Roadmap\"", updatedContent);
        Assert.Contains("  - \"#updated\"", updatedContent);
        Assert.Contains("Updated recent note body.", updatedContent);
        Assert.DoesNotContain("Original recent note body.", updatedContent);
    }
}
