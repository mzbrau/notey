using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Notey.App.Imports;
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
        Assert.Contains("  - \"accounts\"", expectedContent);

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
    public async Task File_import_into_open_final_note_copies_attachment_to_final_assets()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var content = """
            ---
            created: 2026-05-14T09:30+00:00
            processed: 2026-05-14T09:31+00:00
            topic: "Roadmap"
            people: []
            tags: []
            links: []
            ---
            Roadmap note.
            """;
        var filePath = await harness.WriteFinalNoteAsync("roadmap.md", content);
        await harness.OpenRecentNoteAsync(filePath);
        harness.Editor.CaretOffset = harness.Editor.Document.TextLength;

        await harness.Window.ImportFilesForTestingAsync([ImportFile.FromBytes("brief.pdf", [1, 2, 3])]);
        await harness.DrainAsync();

        var attachmentPath = Path.Combine(harness.RootPath, "Notes", "roadmap.assets", "brief.pdf");
        Assert.True(File.Exists(attachmentPath));
        Assert.Contains("[[Notes/roadmap.assets/brief.pdf|brief.pdf]]", harness.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task File_import_for_explicit_insertion_offset_inserts_at_requested_position()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var content = """
            ---
            created: 2026-05-14T09:30+00:00
            processed: 2026-05-14T09:31+00:00
            topic: "Roadmap"
            people: []
            tags: []
            links: []
            ---
            Roadmap note.
            """;
        var filePath = await harness.WriteFinalNoteAsync("roadmap.md", content);
        await harness.OpenRecentNoteAsync(filePath);
        harness.Editor.Select(harness.Editor.Document.TextLength - 5, 5);

        await harness.Window.ImportFilesForTestingAsync([ImportFile.FromBytes("brief.pdf", [1, 2, 3])], insertionOffset: 0);
        await harness.DrainAsync();

        Assert.StartsWith("[[Notes/roadmap.assets/brief.pdf|brief.pdf]]", harness.Editor.Document.Text, StringComparison.Ordinal);
        Assert.Contains("Roadmap note.", harness.Editor.Document.Text);
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
    public async Task Set_due_today_action_is_icon_only_and_moves_task_to_today()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime).AddDays(-1);
        var today = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        var tasksPath = await AddTaskThroughPanelAsync(harness, "Overdue task", dueDate);

        var setDueTodayButton = FindButtonByToolTip(harness.Window, "Set due today");

        Assert.IsType<PathIcon>(setDueTodayButton.Content);
        setDueTodayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.WaitForTextBlockTextAsync("AutosaveStatusText", "TASK MOVED", TimeSpan.FromSeconds(5));
        await harness.WaitForFileContainsAsync(tasksPath, $"- [ ] Overdue task (due: {today:yyyy-MM-dd})", TimeSpan.FromSeconds(5));
        await harness.WaitForFileDoesNotContainAsync(tasksPath, $"- [ ] Overdue task (due: {dueDate:yyyy-MM-dd})", TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public async Task Task_edit_popup_saves_text_and_due_date_changes()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        var updatedDate = dueDate.AddDays(2);
        var tasksPath = await AddTaskThroughPanelAsync(harness, "Editable task", dueDate);

        await OpenTaskEditPopupAsync(harness, dueDate);
        FindPopupControl<TextBox>(harness, "TaskEditTextBox").Text = "Updated task";
        FindPopupControl<DatePicker>(harness, "TaskEditDueDatePicker").SelectedDate = ToPickerDate(updatedDate, harness.LocalNow.Offset);
        FindPopupControl<Button>(harness, "TaskEditSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await harness.WaitForFileContainsAsync(tasksPath, $"- [ ] Updated task (due: {updatedDate:yyyy-MM-dd})", TimeSpan.FromSeconds(2));
        var content = await File.ReadAllTextAsync(tasksPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Editable task", content);
    }

    [AvaloniaFact]
    public async Task Task_edit_popup_cancel_discards_staged_changes()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        var tasksPath = await AddTaskThroughPanelAsync(harness, "Cancelable task", dueDate);

        await OpenTaskEditPopupAsync(harness, dueDate);
        FindPopupControl<TextBox>(harness, "TaskEditTextBox").Text = "Should not save";
        FindPopupControl<Button>(harness, "TaskEditClearDueDateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindPopupControl<Button>(harness, "TaskEditCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();

        var content = await File.ReadAllTextAsync(tasksPath, TestContext.Current.CancellationToken);
        Assert.Contains($"- [ ] Cancelable task (due: {dueDate:yyyy-MM-dd})", content);
        Assert.DoesNotContain("Should not save", content);
    }

    [AvaloniaFact]
    public async Task Task_edit_popup_clear_due_date_is_staged_until_save()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        var tasksPath = await AddTaskThroughPanelAsync(harness, "Clearable task", dueDate);

        await OpenTaskEditPopupAsync(harness, dueDate);
        FindPopupControl<Button>(harness, "TaskEditClearDueDateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var beforeSave = await File.ReadAllTextAsync(tasksPath, TestContext.Current.CancellationToken);
        Assert.Contains($"- [ ] Clearable task (due: {dueDate:yyyy-MM-dd})", beforeSave);

        FindPopupControl<Button>(harness, "TaskEditSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.WaitForFileDoesNotContainAsync(tasksPath, $"Clearable task (due: {dueDate:yyyy-MM-dd})", TimeSpan.FromSeconds(2));
        var afterSave = await File.ReadAllTextAsync(tasksPath, TestContext.Current.CancellationToken);
        Assert.Contains("- [ ] Clearable task", afterSave);
    }

    [AvaloniaFact]
    public async Task Task_edit_popup_deletes_task()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();
        var dueDate = DateOnly.FromDateTime(harness.LocalNow.DateTime);
        var tasksPath = await AddTaskThroughPanelAsync(harness, "Deletable task", dueDate);

        await OpenTaskEditPopupAsync(harness, dueDate);
        FindPopupControl<Button>(harness, "TaskEditDeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await harness.WaitForFileDoesNotContainAsync(tasksPath, "Deletable task", TimeSpan.FromSeconds(5));
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
        Assert.Contains("  - \"updated\"", updatedContent);
        Assert.Contains("Updated recent note body.", updatedContent);
        Assert.DoesNotContain("Original recent note body.", updatedContent);
    }

    [AvaloniaFact]
    public async Task Assistant_panel_toggles_and_accepts_multiline_prompt()
    {
        using var harness = await MainWindowTestHarness.CreateAsync();

        harness.Find<Button>("AssistantButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();
        var panel = harness.Find<Border>("AssistantPanel");
        var handle = harness.Find<Border>("AssistantPanelResizeHandle");
        var prompt = harness.Find<TextBox>("AssistantPromptTextBox");

        prompt.Text = "Add a greeting\nand create a follow-up task.";

        Assert.True(panel.IsVisible);
        Assert.True(handle.IsVisible);
        Assert.True(prompt.AcceptsReturn);
        Assert.Contains("follow-up", prompt.Text, StringComparison.Ordinal);
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

    private static Button FindButtonByContent(Control root, string content)
    {
        return root.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal));
    }

    private static T FindPopupControl<T>(MainWindowTestHarness harness, string name)
        where T : Control
    {
        var popupContent = harness.Window.OpenTaskEditPopupContent
            ?? throw new InvalidOperationException("Task edit popup is not open.");
        return popupContent.GetVisualDescendants()
            .OfType<T>()
            .Single(control => string.Equals(control.Name, name, StringComparison.Ordinal));
    }

    private static async Task<string> AddTaskThroughPanelAsync(MainWindowTestHarness harness, string text, DateOnly? dueDate)
    {
        harness.Find<Button>("AddTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();
        harness.Find<TextBox>("NewTaskTextBox").Text = text;
        harness.Find<DatePicker>("NewTaskDueDatePicker").SelectedDate = dueDate is { } value
            ? ToPickerDate(value, harness.LocalNow.Offset)
            : null;
        harness.Find<Button>("SaveNewTaskButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var tasksPath = Path.Combine(harness.RootPath, "Notes", "tasks.md");
        var expectedLine = dueDate is { } valueDate
            ? $"- [ ] {text} (due: {valueDate:yyyy-MM-dd})"
            : $"- [ ] {text}";
        await harness.WaitForFileContainsAsync(tasksPath, expectedLine, TimeSpan.FromSeconds(2));
        return tasksPath;
    }

    private static async Task OpenTaskEditPopupAsync(MainWindowTestHarness harness, DateOnly dueDate)
    {
        FindButtonByContent(harness.Window, FormatTaskDate(dueDate)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await harness.DrainAsync();
        Assert.NotNull(harness.Window.OpenTaskEditPopupContent);
    }

    private static string FormatTaskDate(DateOnly dueDate)
    {
        return dueDate.ToString("ddd d/M", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ToPickerDate(DateOnly date, TimeSpan offset)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
    }
}

#pragma warning restore xUnit1051
