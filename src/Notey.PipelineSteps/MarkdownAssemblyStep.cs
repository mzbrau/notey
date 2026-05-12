using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Steps;

namespace Notey.PipelineSteps;

public sealed class MarkdownAssemblyStep : PipelineStep<StructuredNoteData, MarkdownContent>
{
    public const string StepTypeId = "markdown-assembly";

    public MarkdownAssemblyStep()
        : base(PipelineDataType.StructuredNoteData)
    {
    }

    public override string Id => StepTypeId;

    public override string DisplayName => "Markdown assembly";

    protected override ValueTask<PipelineStepResult<MarkdownContent>> ExecuteTypedAsync(
        StructuredNoteData input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        var heading = StepConfigurationReader.GetString(context.Step.Configuration, "heading") ?? "AI context";
        var markdown = BuildMarkdown(input, heading);

        return ValueTask.FromResult(new PipelineStepResult<MarkdownContent>(
            new MarkdownContent(markdown, context.Pipeline.Id),
            "Markdown assembled."));
    }

    private static string BuildMarkdown(StructuredNoteData data, string heading)
    {
        var lines = new List<string> { $"## {heading.Trim()}" };

        if (!string.IsNullOrWhiteSpace(data.MeetingTitle))
        {
            lines.Add(string.Empty);
            lines.Add($"- Meeting title: {data.MeetingTitle.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(data.Summary))
        {
            lines.Add(string.Empty);
            lines.Add(data.Summary.Trim());
        }

        AppendEntities(lines, "People", data.People);
        AppendEntities(lines, "Topics", data.Topics);
        AppendEntities(lines, "Projects", data.Projects);

        if (data.Tags is { Count: > 0 })
        {
            lines.Add($"- Tags: {string.Join(", ", data.Tags.Select(static tag => $"#{tag.Trim().TrimStart('#')}"))}");
        }

        if (data.Sections is not null)
        {
            foreach (var (sectionHeading, body) in data.Sections)
            {
                if (string.IsNullOrWhiteSpace(sectionHeading) || string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                lines.Add(string.Empty);
                lines.Add($"### {sectionHeading.Trim()}");
                lines.Add(body.Trim());
            }
        }

        return string.Join('\n', lines).Trim();
    }

    private static void AppendEntities(
        ICollection<string> lines,
        string label,
        IReadOnlyList<EntitySuggestion>? entities)
    {
        if (entities is not { Count: > 0 })
        {
            return;
        }

        lines.Add($"- {label}: {string.Join(", ", entities.Select(static entity => entity.Name))}");
    }
}
