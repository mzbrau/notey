using Notey.Core.Configuration;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;
using Notey.Vault.Tasks;

namespace Notey.Tests;

public sealed class TaskStoreTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task Store_parses_legacy_tasks_and_persists_stable_id_when_updated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var tasksPath = Path.Combine(rootPath, "Notes", "tasks.md");
        Directory.CreateDirectory(Path.GetDirectoryName(tasksPath)!);
        await File.WriteAllTextAsync(tasksPath, """
            # Tasks

            ## 2026-05-13
            - [ ] Send recap (due: 2026-05-20)
            """, cancellationToken);
        var store = CreateStore(rootPath);

        var task = Assert.Single(await store.LoadAsync(cancellationToken));
        Assert.StartsWith("legacy-", task.Id, StringComparison.Ordinal);
        Assert.Equal("Send recap", task.Text);
        Assert.Equal(new DateOnly(2026, 5, 20), task.DueDate);

        var updated = await store.SetCompletedAsync(task.Id, new DateOnly(2026, 5, 21), cancellationToken);

        Assert.NotNull(updated);
        Assert.StartsWith("notey-task-", updated.Id, StringComparison.Ordinal);
        var content = await File.ReadAllTextAsync(tasksPath, cancellationToken);
        Assert.Contains("- [x] Send recap (due: 2026-05-20) (completed: 2026-05-21)", content);
        Assert.Contains($"^{updated.Id}", content);
    }

    [Fact]
    public async Task Store_adds_source_link_and_source_note_backlink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var sourcePath = Path.Combine(rootPath, "Notes", "roadmap.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "Roadmap note.\n", cancellationToken);
        var store = CreateStore(rootPath);

        var created = await store.AddAsync(
            [new NewNoteyTask("Review launch", new DateOnly(2026, 5, 20), sourcePath)],
            new DateOnly(2026, 5, 13),
            cancellationToken);
        await store.AddSourceBacklinksAsync(sourcePath, created, cancellationToken);

        var task = Assert.Single(created);
        var tasks = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "tasks.md"), cancellationToken);
        Assert.Contains("(source: [[Notes/roadmap|roadmap]])", tasks);
        Assert.Contains($"^{task.Id}", tasks);
        var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        Assert.Contains("## Tasks", source);
        Assert.Contains($"[[Notes/tasks#^{task.Id}|Task: Review launch]]", source);
    }

    [Fact]
    public async Task Store_only_treats_trailing_due_metadata_as_due_date()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var tasksPath = Path.Combine(rootPath, "Notes", "tasks.md");
        Directory.CreateDirectory(Path.GetDirectoryName(tasksPath)!);
        await File.WriteAllTextAsync(tasksPath, """
            # Tasks

            ## 2026-05-13
            - [ ] Ask why invoice says (due: 2026-05-20) (due: 2026-05-21) ^notey-task-invoice
            """, cancellationToken);
        var store = CreateStore(rootPath);

        var task = Assert.Single(await store.LoadAsync(cancellationToken));

        Assert.Equal("Ask why invoice says (due: 2026-05-20)", task.Text);
        Assert.Equal(new DateOnly(2026, 5, 21), task.DueDate);
    }

    [Fact]
    public void Grouper_orders_tasks_by_due_date_sections()
    {
        var today = new DateOnly(2026, 5, 13);
        var tasks = new[]
        {
            new NoteyTask("overdue", "Overdue", new DateOnly(2026, 5, 12), null, null),
            new NoteyTask("this-week", "This week", new DateOnly(2026, 5, 15), null, null),
            new NoteyTask("next-week", "Next week", new DateOnly(2026, 5, 20), null, null),
            new NoteyTask("two-weeks", "Two weeks", new DateOnly(2026, 5, 27), null, null),
            new NoteyTask("future", "Future", new DateOnly(2026, 6, 3), null, null),
            new NoteyTask("undated", "Undated", null, null, null),
            new NoteyTask("recent-complete", "Recent complete", new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 12), null),
            new NoteyTask("recent-overdue-complete", "Recent overdue complete", new DateOnly(2026, 5, 12), new DateOnly(2026, 5, 12), null),
            new NoteyTask("old-complete", "Old complete", new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 10), null)
        };

        var sections = TaskGrouper.Group(tasks, today);

        Assert.Equal(["Overdue", "Recent overdue complete"], GetTexts(sections, TaskSectionKind.Incomplete));
        Assert.Equal(["Recent complete", "This week"], GetTexts(sections, TaskSectionKind.ThisWeek));
        Assert.Equal(["Next week"], GetTexts(sections, TaskSectionKind.NextWeek));
        Assert.Equal(["Two weeks"], GetTexts(sections, TaskSectionKind.InTwoWeeks));
        Assert.Equal(["Future"], GetTexts(sections, TaskSectionKind.Future));
        Assert.Equal(["Undated"], GetTexts(sections, TaskSectionKind.Undated));
        Assert.Equal(["Old complete"], GetTexts(sections, TaskSectionKind.Completed));
        Assert.Equal(2, TaskGrouper.CountBadgeTasks(tasks, today));
    }

    private static IReadOnlyList<string> GetTexts(IReadOnlyList<TaskSection> sections, TaskSectionKind kind)
    {
        return sections.Single(section => section.Kind == kind).Tasks.Select(static task => task.Text).ToArray();
    }

    private FileSystemTaskStore CreateStore(string rootPath)
    {
        var options = new NoteyOptions { Vault = new VaultOptions { RootPath = rootPath } };
        var workspace = new FileSystemVaultWorkspace(options);
        return new FileSystemTaskStore(
            workspace,
            new ObsidianLinkBuilder(workspace),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero)));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-task-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset localNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return localNow.ToUniversalTime();
        }
    }
}
