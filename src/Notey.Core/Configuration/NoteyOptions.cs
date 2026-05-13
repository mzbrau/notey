namespace Notey.Core.Configuration;

public sealed class NoteyOptions
{
    public const string SectionName = "Notey";

    public UiOptions Ui { get; set; } = new();

    public HotkeyOptions Hotkeys { get; set; } = new();

    public VaultOptions Vault { get; set; } = new();

    public AiOptions Ai { get; set; } = new();

    public OcrOptions Ocr { get; set; } = new();
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
}

public sealed class AiOptions
{
    public string DefaultProviderId { get; set; } = "default";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = "NOTEY_AI_API_KEY";

    public string ModelName { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 60;

    public bool StoreApiKeyInPlaintext { get; set; }

    public List<AiProviderOptions> Providers { get; set; } = [];
}

public sealed class AiProviderOptions
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = "OpenAiCompatible";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public int? RequestTimeoutSeconds { get; set; }
}

public sealed class OcrOptions
{
    public string TesseractExecutablePath { get; set; } = "tesseract";

    public string TesseractDataPath { get; set; } = string.Empty;

    public string DefaultLanguage { get; set; } = "eng";
}
