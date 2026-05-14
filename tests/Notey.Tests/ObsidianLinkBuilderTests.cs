using Notey.Core.Configuration;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.Tests;

public sealed class ObsidianLinkBuilderTests
{
    [Fact]
    public void BuildWikiLink_uses_configured_vault_relative_folder()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-link-builder");
        var builder = CreateBuilder(rootPath);

        var link = builder.BuildWikiLink(VaultEntityKind.Person, " Jane   Doe ");

        Assert.Equal("[[People/Jane Doe|Jane Doe]]", link);
    }

    [Fact]
    public void BuildWikiLink_sanitizes_file_name_without_changing_alias()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-link-builder");
        var builder = CreateBuilder(rootPath);

        var link = builder.BuildWikiLink(VaultEntityKind.Topic, "Roadmap: Q2");

        Assert.Equal("[[Notes/Topics/Roadmap- Q2|Roadmap: Q2]]", link);
    }

    [Fact]
    public void BuildImageEmbed_uses_vault_relative_path_and_keeps_extension()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-link-builder");
        var builder = CreateBuilder(rootPath);
        var imagePath = Path.Combine(rootPath, "Attachments", "Snips", "2026-05-12-201007-snip.png");

        var embed = builder.BuildImageEmbed(imagePath);

        Assert.Equal("![[Attachments/Snips/2026-05-12-201007-snip.png]]", embed);
    }

    [Fact]
    public void BuildImageEmbed_rejects_paths_outside_vault()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-link-builder");
        var builder = CreateBuilder(rootPath);

        Assert.Throws<InvalidOperationException>(() => builder.BuildImageEmbed(Path.Combine(Path.GetTempPath(), "outside.png")));
    }

    [Fact]
    public void BuildOpenFileUri_uses_vault_name_and_relative_file_path()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-link-builder");
        var builder = CreateBuilder(rootPath);
        var notePath = Path.Combine(rootPath, "Notes", "Projects", "Launch Plan.md");

        var uri = builder.BuildOpenFileUri(notePath);

        Assert.Equal("obsidian", uri.Scheme);
        Assert.Equal("open", uri.Host);
        Assert.Equal("?vault=notey-link-builder&file=Notes%2FProjects%2FLaunch%20Plan.md", uri.Query);
    }

    [Fact]
    public void BuildOpenFileUri_rejects_paths_outside_vault()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-link-builder");
        var builder = CreateBuilder(rootPath);

        Assert.Throws<InvalidOperationException>(() => builder.BuildOpenFileUri(Path.Combine(Path.GetTempPath(), "outside.md")));
    }

    private static ObsidianLinkBuilder CreateBuilder(string rootPath)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = rootPath
            }
        };

        return new ObsidianLinkBuilder(new FileSystemVaultWorkspace(options));
    }
}
