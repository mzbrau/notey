namespace Notey.Core.Configuration;

public sealed class NoteyOptions
{
    public const string SectionName = "Notey";

    public UiOptions Ui { get; set; } = new();

    public HotkeyOptions Hotkeys { get; set; } = new();

    public VaultOptions Vault { get; set; } = new();

    public AiOptions Ai { get; set; } = new();
}

public sealed class UiOptions
{
    public string Theme { get; set; } = "Dark";

    public int DefaultWindowWidth { get; set; } = 1180;

    public int DefaultWindowHeight { get; set; } = 760;
}

public sealed class HotkeyOptions
{
    public string OpenNote { get; set; } = "Ctrl+Alt+N";
}

public sealed class VaultOptions
{
    public string RootPath { get; set; } = string.Empty;

    public string NotesPath { get; set; } = "Notes";

    public string PeoplePath { get; set; } = "People";

    public string TopicsPath { get; set; } = "Topics";

    public string ProjectsPath { get; set; } = "Projects";

    public string ScreenshotPath { get; set; } = "Attachments/Snips";
}

public sealed class AiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public bool StoreApiKeyInPlaintext { get; set; }
}
