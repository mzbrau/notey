using System.Globalization;
using System.Text.Json.Nodes;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Steps;

namespace Notey.PipelineSteps;

public sealed class TeamsMeetingNormalizerStep : PipelineStep<StructuredNoteData, StructuredNoteData>
{
    public const string StepTypeId = "teams-meeting-normalizer";

    private const double DefaultParticipantConfidenceThreshold = 0.75;
    private const string DefaultSuggestedParticipantSectionHeading = "Suggested participants to review";
    private static readonly string[] TeamsNoiseTokens =
    [
        "Microsoft Teams",
        "Teams Meeting",
        "Meeting chat",
        "Participants",
        "Organizer",
        "Presenter",
        "Muted",
        "Unmuted",
        "Camera off",
        "Camera on",
        "Screen sharing",
        "Recording",
        "(External)",
        "(Guest)"
    ];

    public TeamsMeetingNormalizerStep()
        : base(PipelineDataType.StructuredNoteData)
    {
    }

    public override string Id => StepTypeId;

    public override string DisplayName => "Teams meeting normalizer";

    public override IReadOnlyList<string> ValidateConfiguration(PipelineStepDefinition definition)
    {
        var errors = new List<string>();
        if (!TryGetParticipantConfidenceThreshold(definition, out var threshold))
        {
            errors.Add("participantConfidenceThreshold must be a finite number between 0 and 1.");
        }
        else if (threshold is < 0 or > 1)
        {
            errors.Add("participantConfidenceThreshold must be a finite number between 0 and 1.");
        }

        return errors;
    }

    protected override ValueTask<PipelineStepResult<StructuredNoteData>> ExecuteTypedAsync(
        StructuredNoteData input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var threshold = GetParticipantConfidenceThreshold(context.Step);
        var (confidentParticipants, suggestedParticipants) = NormalizeParticipants(input.People, threshold);
        var sections = BuildSections(input.Sections, suggestedParticipants, GetSuggestedParticipantSectionHeading(context.Step));
        var normalized = new StructuredNoteData(
            Summary: NormalizeOptionalWhitespace(input.Summary),
            MeetingTitle: NormalizeMeetingTitle(input.MeetingTitle),
            People: confidentParticipants,
            Topics: MergeEntities(input.Topics, GetConfiguredEntity(context.Step, "topicName", "Teams meeting", "topic")),
            Projects: DeduplicateEntities(input.Projects),
            Tags: MergeTags(input.Tags, GetConfiguredString(context.Step, "tags", "teams,meeting")),
            Sections: sections);

        context.Context.SetValue(PipelineStepContextKeys.TeamsParticipantConfidenceThreshold, threshold);
        if (suggestedParticipants.Count > 0)
        {
            context.Context.SetValue(
                PipelineStepContextKeys.TeamsSuggestedParticipants,
                suggestedParticipants.Select(static participant => participant.Name).ToArray());
            context.Context.AddWarning(
                $"{suggestedParticipants.Count} Teams participant suggestion(s) need review before person documents are created.",
                context.Step.Id);
        }

        return ValueTask.FromResult(new PipelineStepResult<StructuredNoteData>(
            normalized,
            suggestedParticipants.Count == 0
                ? "Teams meeting context normalized."
                : "Teams meeting context normalized with participant suggestions."));
    }

    private static double GetParticipantConfidenceThreshold(PipelineStepDefinition definition)
    {
        return TryGetParticipantConfidenceThreshold(definition, out var threshold)
            ? threshold
            : DefaultParticipantConfidenceThreshold;
    }

    private static bool TryGetParticipantConfidenceThreshold(PipelineStepDefinition definition, out double threshold)
    {
        if (definition.Configuration is null
            || !definition.Configuration.TryGetPropertyValue("participantConfidenceThreshold", out var value))
        {
            threshold = DefaultParticipantConfidenceThreshold;
            return true;
        }

        if (value is JsonValue jsonValue
            && (jsonValue.TryGetValue<double>(out threshold)
                || (jsonValue.TryGetValue<string>(out var text)
                    && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold)))
            && double.IsFinite(threshold))
        {
            return true;
        }

        threshold = DefaultParticipantConfidenceThreshold;
        return false;
    }

    private static string GetSuggestedParticipantSectionHeading(PipelineStepDefinition definition)
    {
        var heading = StepConfigurationReader.GetString(definition.Configuration, "suggestedParticipantSectionHeading");
        return string.IsNullOrWhiteSpace(heading) ? DefaultSuggestedParticipantSectionHeading : heading.Trim();
    }

    private static string? GetConfiguredString(PipelineStepDefinition definition, string key, string? defaultValue)
    {
        return StepConfigurationReader.GetString(definition.Configuration, key) ?? defaultValue;
    }

    private static EntitySuggestion? GetConfiguredEntity(
        PipelineStepDefinition definition,
        string key,
        string? defaultName,
        string kind)
    {
        var name = GetConfiguredString(definition, key, defaultName);
        return string.IsNullOrWhiteSpace(name) ? null : new EntitySuggestion(name.Trim(), kind, 1, "configuration");
    }

    private static (IReadOnlyList<EntitySuggestion> Confident, IReadOnlyList<EntitySuggestion> Suggested) NormalizeParticipants(
        IReadOnlyList<EntitySuggestion>? participants,
        double threshold)
    {
        var confident = new List<EntitySuggestion>();
        var suggested = new List<EntitySuggestion>();
        var seenConfident = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSuggested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var participant in participants ?? [])
        {
            var name = NormalizeParticipantName(participant.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var normalized = participant with { Name = name };
            var key = NormalizeEntityKey(name);
            if (participant.Confidence is double confidence
                && confidence is >= 0 and <= 1
                && confidence >= threshold)
            {
                if (seenConfident.Add(key))
                {
                    confident.Add(normalized);
                }

                seenSuggested.Remove(key);
                suggested.RemoveAll(candidate => string.Equals(NormalizeEntityKey(candidate.Name), key, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (!seenConfident.Contains(key) && seenSuggested.Add(key))
            {
                suggested.Add(normalized);
            }
        }

        return (confident, suggested);
    }

    private static string NormalizeParticipantName(string name)
    {
        var normalized = NormalizeWhitespace(name);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        foreach (var token in TeamsNoiseTokens)
        {
            normalized = normalized.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        normalized = normalized.Trim(' ', '-', ':', '|', ',', ';');
        if (normalized.Equals("you", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("me", StringComparison.OrdinalIgnoreCase)
            || normalized.Length < 2)
        {
            return string.Empty;
        }

        return NormalizeWhitespace(normalized);
    }

    private static IReadOnlyDictionary<string, string> BuildSections(
        IReadOnlyDictionary<string, string>? existingSections,
        IReadOnlyList<EntitySuggestion> suggestedParticipants,
        string suggestedParticipantSectionHeading)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (heading, body) in existingSections ?? new Dictionary<string, string>())
        {
            var normalizedHeading = NormalizeWhitespace(heading);
            var normalizedBody = NormalizeSectionBody(body);
            if (!string.IsNullOrWhiteSpace(normalizedHeading) && !string.IsNullOrWhiteSpace(normalizedBody))
            {
                sections[normalizedHeading] = normalizedBody;
            }
        }

        if (suggestedParticipants.Count > 0)
        {
            sections[suggestedParticipantSectionHeading.Trim()] = string.Join(
                '\n',
                suggestedParticipants.Select(FormatSuggestedParticipant));
        }

        return sections;
    }

    private static string FormatSuggestedParticipant(EntitySuggestion participant)
    {
        var details = new List<string>();
        if (participant.Confidence is >= 0)
        {
            var confidence = participant.Confidence.Value;
            details.Add(confidence <= 1
                ? $"confidence {confidence.ToString("P0", CultureInfo.InvariantCulture)}"
                : $"confidence {confidence.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(participant.Source))
        {
            details.Add($"source: {participant.Source.Trim()}");
        }

        return details.Count == 0
            ? $"- {participant.Name}"
            : $"- {participant.Name} ({string.Join("; ", details)})";
    }

    private static IReadOnlyList<EntitySuggestion> MergeEntities(
        IReadOnlyList<EntitySuggestion>? existing,
        EntitySuggestion? addition)
    {
        var entities = DeduplicateEntities(existing).ToList();
        if (addition is not null && entities.All(entity => !EntityNameEquals(entity.Name, addition.Name)))
        {
            entities.Add(addition);
        }

        return entities;
    }

    private static IReadOnlyList<EntitySuggestion> DeduplicateEntities(IReadOnlyList<EntitySuggestion>? entities)
    {
        var output = new List<EntitySuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities ?? [])
        {
            var name = NormalizeWhitespace(entity.Name);
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(NormalizeEntityKey(name)))
            {
                output.Add(entity with { Name = name });
            }
        }

        return output;
    }

    private static IReadOnlyList<string> MergeTags(IReadOnlyList<string>? existing, string? configuredTags)
    {
        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTags(existing ?? [], tags, seen);
        AddTags(
            string.IsNullOrWhiteSpace(configuredTags)
                ? []
                : configuredTags.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            tags,
            seen);
        return tags;
    }

    private static void AddTags(IEnumerable<string> candidates, ICollection<string> output, ISet<string> seen)
    {
        foreach (var candidate in candidates)
        {
            var tag = NormalizeWhitespace(candidate).TrimStart('#');
            if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
            {
                output.Add(tag);
            }
        }
    }

    private static bool EntityNameEquals(string left, string right)
    {
        return string.Equals(NormalizeEntityKey(left), NormalizeEntityKey(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEntityKey(string value)
    {
        return NormalizeWhitespace(value).ToUpperInvariant();
    }

    private static string NormalizeWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeSectionBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(NormalizeWhitespace)
            .ToArray();
        var firstContentLine = Array.FindIndex(lines, static line => !string.IsNullOrWhiteSpace(line));
        var lastContentLine = Array.FindLastIndex(lines, static line => !string.IsNullOrWhiteSpace(line));

        return firstContentLine < 0 || lastContentLine < 0
            ? string.Empty
            : string.Join('\n', lines[firstContentLine..(lastContentLine + 1)]);
    }

    private static string? NormalizeOptionalWhitespace(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeMeetingTitle(string? meetingTitle)
    {
        var normalized = NormalizeWhitespace(meetingTitle);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Equals("Microsoft Teams Meeting", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Teams Meeting", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalized;
    }
}
