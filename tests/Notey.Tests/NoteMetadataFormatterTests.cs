using Notey.Vault.Notes;

namespace Notey.Tests;

public sealed class NoteMetadataFormatterTests
{
    [Fact]
    public void Apply_updates_frontmatter_and_adds_context_block()
    {
        var formatter = new NoteMetadataFormatter();
        var metadata = new NoteMetadata(
            ["[[People/Jane Doe|Jane Doe]]"],
            ["[[Topics/Product Strategy|Product Strategy]]"],
            ["[[Projects/Notey|Notey]]"],
            ["Teams meeting title: Roadmap"]);

        var result = formatter.Apply("""
            ---
            created: 2026-05-11T22:45:30.0000000+02:00
            people: []
            topics: []
            projects: []
            screenshots: []
            ---

            # Untitled note
            """, metadata);

        Assert.Contains("""
            people:
              - "[[People/Jane Doe|Jane Doe]]"
            """, result);
        Assert.Contains("- People: [[People/Jane Doe|Jane Doe]]", result);
        Assert.Contains("- Screenshot context: Teams meeting title: Roadmap", result);
        Assert.Contains("# Untitled note", result);
    }

    [Fact]
    public void Apply_replaces_existing_generated_context_block()
    {
        var formatter = new NoteMetadataFormatter();

        var result = formatter.Apply("""
            ---
            created: 2026-05-11T22:45:30.0000000+02:00
            people:
              - "[[People/Old|Old]]"
            ---
            <!-- notey-context:start -->
            ## Context
            - People: [[People/Old|Old]]
            <!-- notey-context:end -->

            # Notes
            """, new NoteMetadata(["[[People/New|New]]"], [], [], []));

        Assert.DoesNotContain("[[People/Old|Old]]", result);
        Assert.Contains("[[People/New|New]]", result);
        Assert.Contains("# Notes", result);
    }

    [Fact]
    public void Apply_removes_orphaned_context_start_marker()
    {
        var formatter = new NoteMetadataFormatter();

        var result = formatter.Apply("""
            ---
            created: 2026-05-11T22:45:30.0000000+02:00
            ---
            <!-- notey-context:start -->
            ## Context
            - People: [[People/Old|Old]]

            # Notes
            """, new NoteMetadata(["[[People/New|New]]"], [], [], []));

        Assert.DoesNotContain("[[People/Old|Old]]", result);
        Assert.Single(FindAll(result, "<!-- notey-context:start -->"));
        Assert.Contains("# Notes", result);
    }

    [Fact]
    public void Apply_preserves_note_body_when_orphaned_context_has_no_blank_separator()
    {
        var formatter = new NoteMetadataFormatter();

        var result = formatter.Apply("""
            ---
            created: 2026-05-11T22:45:30.0000000+02:00
            ---
            <!-- notey-context:start -->
            ## Context
            - People: [[People/Old|Old]]
            # Notes
            Important body text
            """, new NoteMetadata(["[[People/New|New]]"], [], [], []));

        Assert.DoesNotContain("[[People/Old|Old]]", result);
        Assert.Contains("# Notes", result);
        Assert.Contains("Important body text", result);
    }

    private static IEnumerable<int> FindAll(string text, string value)
    {
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            yield return index;
            index += value.Length;
        }
    }
}
