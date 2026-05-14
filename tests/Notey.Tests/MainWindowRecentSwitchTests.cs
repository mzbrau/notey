using Notey.App.Views;

namespace Notey.Tests;

public sealed class MainWindowRecentSwitchTests
{
    [Fact]
    public void SelectPrimaryWrittenNotePath_returns_first_non_tasks_file()
    {
        var path = MainWindow.SelectPrimaryWrittenNotePath(
        [
            "/vault/Notes/tasks.md",
            "/vault/Notes/2026-05-14-note.md"
        ]);

        Assert.Equal("/vault/Notes/2026-05-14-note.md", path);
    }

    [Fact]
    public void SelectPrimaryWrittenNotePath_returns_null_when_only_tasks_were_written()
    {
        var path = MainWindow.SelectPrimaryWrittenNotePath(
        [
            "/vault/Notes/tasks.md"
        ]);

        Assert.Null(path);
    }
}
