using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Notey.App.Views;

#pragma warning disable xUnit1051 // AvaloniaFact does not provide xUnit test-context cancellation token support.

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
    public async Task Tasks_panel_adds_task_and_updates_badge()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();

        harness.Find<Button>("AddTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();
        harness.Find<TextBox>("NewTaskTextBox").Text = "Team sync";
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        harness.Find<DatePicker>("NewTaskDueDatePicker").SelectedDate = ToPickerDate(dueDate, harness.LocalNow.Offset);
        harness.Find<Button>("SaveNewTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var tasksPath = Path.Combine(harness.RootPath, "Notes", "tasks.md");
        await harness.WaitForFileContainsAsync(tasksPath, $"- [ ] Team sync (due: {dueDate:yyyy-MM-dd})", TimeSpan.FromSeconds(2));

        Assert.Equal("1", harness.Find<TextBlock>("TasksBadgeText").Text);
    }

    [AvaloniaFact]
    public async Task Task_due_date_arrow_buttons_shift_dated_tasks()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var tasksPath = Path.Combine(harness.RootPath, "Notes", "tasks.md");

        harness.Find<Button>("AddTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();
        harness.Find<TextBox>("NewTaskTextBox").Text = "Moveable task";
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        harness.Find<DatePicker>("NewTaskDueDatePicker").SelectedDate = ToPickerDate(dueDate, harness.LocalNow.Offset);
        harness.Find<Button>("SaveNewTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.WaitForFileContainsAsync(tasksPath, $"- [ ] Moveable task (due: {dueDate:yyyy-MM-dd})", TimeSpan.FromSeconds(2));

        FindButtonByToolTip(harness.Window, "Move task back one day").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.WaitForFileContainsAsync(tasksPath, $"- [ ] Moveable task (due: {dueDate.AddDays(-1):yyyy-MM-dd})", TimeSpan.FromSeconds(2));

        FindButtonByToolTip(harness.Window, "Move task forward one day").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.WaitForFileContainsAsync(tasksPath, $"- [ ] Moveable task (due: {dueDate:yyyy-MM-dd})", TimeSpan.FromSeconds(2));
    }

    [AvaloniaFact]
    public async Task Task_due_date_arrow_buttons_are_disabled_for_undated_tasks()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var tasksPath = Path.Combine(harness.RootPath, "Notes", "tasks.md");

        harness.Find<Button>("AddTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();
        harness.Find<TextBox>("NewTaskTextBox").Text = "Undated task";
        harness.Find<DatePicker>("NewTaskDueDatePicker").SelectedDate = null;
        harness.Find<Button>("SaveNewTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.WaitForFileContainsAsync(tasksPath, "- [ ] Undated task", TimeSpan.FromSeconds(2));
        FindSectionButton(harness.Window, "UNDATED").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();

        Assert.False(FindButtonByToolTip(harness.Window, "Move task forward one day").IsEnabled);
        Assert.False(FindButtonByToolTip(harness.Window, "Move task back one day").IsEnabled);
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

    [AvaloniaFact]
    public async Task Bold_shortcut_unwraps_selected_text_without_crashing()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        await harness.SetEditorTextAsync("**hello**");
        harness.Editor.Select(0, harness.Editor.Document.TextLength);

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.B,
            KeyModifiers = KeyModifiers.Control,
            Source = harness.Editor.TextArea
        };

        harness.Editor.TextArea.RaiseEvent(args);
        await harness.DrainAsync();

        Assert.True(args.Handled);
        Assert.Equal("hello", harness.Editor.Document.Text);
        Assert.Equal(0, harness.Editor.SelectionStart);
        Assert.Equal(5, harness.Editor.SelectionLength);
    }

    [AvaloniaFact]
    public async Task Markdown_editor_uses_monospace_font_for_table_alignment()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();

        var fontFamily = harness.Editor.FontFamily.ToString();

        Assert.Contains("Cascadia Mono", fontFamily, StringComparison.Ordinal);
        Assert.DoesNotContain("Inter", fontFamily, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Paste_shortcut_converts_google_docs_newline_cell_table()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var clipboard = TopLevel.GetTopLevel(harness.Window)?.Clipboard
            ?? throw new InvalidOperationException("Clipboard was not available.");
        await clipboard.SetTextAsync("""
            Name
            Description
            Thing
            Smell
            Taste
            John
            Big
            Stuff
            bad
            bad
            """.ReplaceLineEndings("\n"));

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.V,
            KeyModifiers = KeyModifiers.Control,
            Source = harness.Editor.TextArea
        };
        var expected = """
            | Name | Description | Thing | Smell | Taste |
            | ---- | ----------- | ----- | ----- | ----- |
            | John | Big         | Stuff | bad   | bad   |
            """.ReplaceLineEndings("\n");

        harness.Editor.TextArea.RaiseEvent(args);
        await harness.WaitForEditorTextAsync(expected, TimeSpan.FromSeconds(2));

        Assert.True(args.Handled);
        Assert.Equal(expected, harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Paste_shortcut_does_not_mutate_read_only_editor()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        await harness.SetEditorTextAsync("original");
        harness.Editor.IsReadOnly = true;
        var clipboard = TopLevel.GetTopLevel(harness.Window)?.Clipboard
            ?? throw new InvalidOperationException("Clipboard was not available.");
        await clipboard.SetTextAsync("replacement");

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.V,
            KeyModifiers = KeyModifiers.Control,
            Source = harness.Editor.TextArea
        };

        harness.Editor.TextArea.RaiseEvent(args);
        await harness.DrainAsync();

        Assert.Equal("original", harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Format_tables_shortcut_does_not_mutate_read_only_editor()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var original = """
            | Name | Value |
            | --- | --- |
            | One | Two |
            """.ReplaceLineEndings("\n");
        await harness.SetEditorTextAsync(original);
        harness.Editor.IsReadOnly = true;

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.T,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            Source = harness.Editor.TextArea
        };

        harness.Editor.TextArea.RaiseEvent(args);
        await harness.DrainAsync();

        Assert.Equal(original, harness.Editor.Document.Text);
    }

    private static Button FindButtonByToolTip(Control root, string tooltip)
    {
        return root.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(ToolTip.GetTip(button)?.ToString(), tooltip, StringComparison.Ordinal));
    }

    private static Button FindSectionButton(Control root, string title)
    {
        return root.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.GetVisualDescendants()
                .OfType<TextBlock>()
                .Any(textBlock => string.Equals(textBlock.Text, title, StringComparison.Ordinal)));
    }

    private static DateTimeOffset ToPickerDate(DateOnly date, TimeSpan offset)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
    }
}

#pragma warning restore xUnit1051
