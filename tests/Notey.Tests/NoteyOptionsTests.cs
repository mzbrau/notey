using Notey.Core.Configuration;

namespace Notey.Tests;

public sealed class NoteyOptionsTests
{
    [Fact]
    public void Defaults_match_initial_product_decisions()
    {
        var options = new NoteyOptions();

        Assert.Equal("Dark", options.Ui.Theme);
        Assert.Equal("Ctrl+Alt+N", options.Hotkeys.OpenNote);
        Assert.Equal("", options.Vault.RootPath);
        Assert.Equal("Notes", options.Vault.NotesPath);
        Assert.Equal("People", options.Vault.PeoplePath);
        Assert.Equal("Topics", options.Vault.TopicsPath);
        Assert.Equal("Projects", options.Vault.ProjectsPath);
        Assert.Equal("Attachments/Snips", options.Vault.ScreenshotPath);
        Assert.False(options.Ai.StoreApiKeyInPlaintext);
        Assert.Equal("pipelines.json", options.Pipelines.DefinitionFilePath);
    }
}
