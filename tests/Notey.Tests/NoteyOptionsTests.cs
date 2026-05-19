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
        Assert.Equal("default", options.Ai.DefaultProviderId);
        Assert.Equal("NOTEY_AI_API_KEY", options.Ai.ApiKeyEnvironmentVariable);
        Assert.Equal(120, options.Ai.RequestTimeoutSeconds);
        Assert.False(options.Ai.StoreApiKeyInPlaintext);
        Assert.Equal("eng", options.Ocr.DefaultLanguage);
    }
}
