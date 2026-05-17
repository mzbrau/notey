namespace Notey.Vault.Tasks;

public static class TaskGrouper
{
    public static IReadOnlyList<TaskSection> Group(IReadOnlyList<NoteyTask> tasks, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var sections = new Dictionary<TaskSectionKind, List<NoteyTask>>
        {
            [TaskSectionKind.Incomplete] = [],
            [TaskSectionKind.ThisWeek] = [],
            [TaskSectionKind.NextWeek] = [],
            [TaskSectionKind.InTwoWeeks] = [],
            [TaskSectionKind.Future] = [],
            [TaskSectionKind.Undated] = [],
            [TaskSectionKind.Completed] = []
        };

        var thisWeekEnd = GetWeekEnd(today);
        var nextWeekEnd = thisWeekEnd.AddDays(7);
        var twoWeeksEnd = nextWeekEnd.AddDays(7);

        foreach (var task in tasks)
        {
            var section = GetSection(task, today, thisWeekEnd, nextWeekEnd, twoWeeksEnd);
            sections[section].Add(task);
        }

        return
        [
            Create(TaskSectionKind.Incomplete, "INCOMPLETE", sections),
            Create(TaskSectionKind.ThisWeek, "THIS WEEK", sections),
            Create(TaskSectionKind.NextWeek, "NEXT WEEK", sections),
            Create(TaskSectionKind.InTwoWeeks, "IN 2 WEEKS", sections),
            Create(TaskSectionKind.Future, "FUTURE", sections),
            Create(TaskSectionKind.Undated, "UNDATED", sections),
            Create(TaskSectionKind.Completed, "COMPLETED", sections)
        ];
    }

    public static int CountBadgeTasks(IReadOnlyList<NoteyTask> tasks, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var thisWeekEnd = GetWeekEnd(today);
        return tasks.Count(task =>
            !task.IsCompleted
            && task.DueDate is { } dueDate
            && dueDate <= thisWeekEnd);
    }

    private static TaskSectionKind GetSection(NoteyTask task, DateOnly today, DateOnly thisWeekEnd, DateOnly nextWeekEnd, DateOnly twoWeeksEnd)
    {
        if (task.CompletedDate is { } completedDate && completedDate < today.AddDays(-2))
        {
            return TaskSectionKind.Completed;
        }

        if (task.DueDate is not { } dueDate)
        {
            return task.IsCompleted ? TaskSectionKind.Completed : TaskSectionKind.Undated;
        }

        if (dueDate < today)
        {
            return TaskSectionKind.Incomplete;
        }

        if (dueDate <= thisWeekEnd)
        {
            return TaskSectionKind.ThisWeek;
        }

        if (dueDate <= nextWeekEnd)
        {
            return TaskSectionKind.NextWeek;
        }

        return dueDate <= twoWeeksEnd ? TaskSectionKind.InTwoWeeks : TaskSectionKind.Future;
    }

    private static DateOnly GetWeekEnd(DateOnly today)
    {
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysUntilSunday);
    }

    private static TaskSection Create(
        TaskSectionKind kind,
        string title,
        IReadOnlyDictionary<TaskSectionKind, List<NoteyTask>> sections)
    {
        return new TaskSection(
            kind,
            title,
            sections[kind]
                .OrderBy(task => task.DueDate ?? DateOnly.MaxValue)
                .ThenBy(task => task.Text, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
