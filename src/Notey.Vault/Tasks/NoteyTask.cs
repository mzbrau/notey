namespace Notey.Vault.Tasks;

public sealed record NoteyTask(
    string Id,
    string Text,
    DateOnly? DueDate,
    DateOnly? CompletedDate,
    string? SourceFilePath)
{
    public bool IsCompleted => CompletedDate is not null;
}

public sealed record NewNoteyTask(
    string Text,
    DateOnly? DueDate,
    string? SourceFilePath);
