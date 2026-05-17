namespace Notey.Vault.Tasks;

public interface ITaskStore
{
    string GetTasksFilePath();

    Task<IReadOnlyList<NoteyTask>> LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NoteyTask>> AddAsync(
        IReadOnlyList<NewNoteyTask> tasks,
        DateOnly headingDate,
        CancellationToken cancellationToken = default);

    Task<NoteyTask?> SetCompletedAsync(
        string taskId,
        DateOnly? completedDate,
        CancellationToken cancellationToken = default);

    Task<NoteyTask?> SetDueDateAsync(
        string taskId,
        DateOnly? dueDate,
        CancellationToken cancellationToken = default);

    Task<NoteyTask?> SetDetailsAsync(
        string taskId,
        string text,
        DateOnly? dueDate,
        CancellationToken cancellationToken = default);

    Task<NoteyTask?> MoveToThisWeekAsync(
        string taskId,
        DateOnly today,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        IReadOnlyList<string> taskIds,
        CancellationToken cancellationToken = default);

    Task AddSourceBacklinksAsync(
        string sourceFilePath,
        IReadOnlyList<NoteyTask> tasks,
        CancellationToken cancellationToken = default);
}
