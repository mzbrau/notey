namespace Notey.Vault.Tasks;

public sealed record TaskSection(TaskSectionKind Kind, string Title, IReadOnlyList<NoteyTask> Tasks)
{
    public int Count => Tasks.Count;
}
