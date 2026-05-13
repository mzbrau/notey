using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.App.Diagnostics;
using Notey.Core.Configuration;
using Notey.Core.Platform;
using Notey.Pipelines.Catalog;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Registry;
using Notey.Pipelines.Validation;

namespace Notey.Tests;

public sealed class Phase11PackagingHardeningTests
{
    [Fact]
    public void Diagnostics_command_parses_optional_output_path()
    {
        var enabled = DiagnosticsCommand.TryParse(
            ["--export-diagnostics", "C:/temp/notey-diagnostics.md"],
            out var outputPath);

        Assert.True(enabled);
        Assert.Equal("C:/temp/notey-diagnostics.md", outputPath);
    }

    [Fact]
    public async Task Diagnostics_report_redacts_api_keys_and_includes_pipeline_validation()
    {
        var rootPath = CreateTempDirectory();
        var pipelinePath = Path.Combine(rootPath, "pipelines.json");
        await File.WriteAllTextAsync(pipelinePath, """
            {
              "pipelines": [
                {
                  "id": "invalid-pipeline",
                  "enabled": true,
                  "acceptedInputTypes": [ "TextData" ],
                  "steps": [],
                  "finalOutputType": "StructuredNoteData"
                }
              ]
            }
            """);
        var outputPath = Path.Combine(rootPath, "diagnostics.md");
        var options = new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = Path.Combine(rootPath, "vault"),
            },
            Ai = new AiOptions
            {
                DefaultProviderId = "default",
                BaseUrl = "https://example.test/v1",
                ApiKey = "super-secret-key",
                ModelName = "model-a",
            },
            Pipelines = new PipelineOptions
            {
                DefinitionFilePath = pipelinePath,
                DefaultScreenshotPipelineId = "screenshot-ocr-ai-structured",
            },
        };
        var writer = new DiagnosticsReportWriter(
            options,
            new PipelineCatalog(
                new FilePipelineDefinitionSource(pipelinePath),
                new PipelineValidator(new PipelineStepRegistry([]))),
            new FakePlatformRuntime(),
            TimeProvider.System,
            NullLogger<DiagnosticsReportWriter>.Instance);

        var writtenPath = await writer.WriteAsync(outputPath);
        var report = await File.ReadAllTextAsync(writtenPath);

        Assert.Equal(outputPath, writtenPath);
        Assert.Contains("# Notey diagnostics", report, StringComparison.Ordinal);
        Assert.Contains("invalid-pipeline", report, StringComparison.Ordinal);
        Assert.Contains("must declare at least one step", report, StringComparison.Ordinal);
        Assert.Contains("Default API key configured: yes", report, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-key", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_report_counts_environment_api_key_without_printing_value()
    {
        var variableName = $"NOTEY_TEST_API_KEY_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variableName, "env-secret-key");
        try
        {
            var rootPath = CreateTempDirectory();
            var outputPath = Path.Combine(rootPath, "diagnostics.md");
            var options = new NoteyOptions
            {
                Ai = new AiOptions
                {
                    ApiKeyEnvironmentVariable = variableName,
                    Providers =
                    [
                        new AiProviderOptions
                        {
                            Id = "env-provider",
                            ApiKeyEnvironmentVariable = variableName,
                        },
                    ],
                },
                Pipelines = new PipelineOptions
                {
                    DefinitionFilePath = Path.Combine(rootPath, "missing-pipelines.json"),
                },
            };
            var writer = new DiagnosticsReportWriter(
                options,
                new PipelineCatalog(
                    new FilePipelineDefinitionSource(options.Pipelines.DefinitionFilePath),
                    new PipelineValidator(new PipelineStepRegistry([]))),
                new FakePlatformRuntime(),
                TimeProvider.System,
                NullLogger<DiagnosticsReportWriter>.Instance);

            var writtenPath = await writer.WriteAsync(outputPath);
            var report = await File.ReadAllTextAsync(writtenPath);

            Assert.Contains("Default API key configured: yes", report, StringComparison.Ordinal);
            Assert.Contains("apiKeyConfigured=yes", report, StringComparison.Ordinal);
            Assert.Contains(variableName, report, StringComparison.Ordinal);
            Assert.DoesNotContain("env-secret-key", report, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public async Task Diagnostics_report_default_filename_uses_injected_time_provider()
    {
        var rootPath = CreateTempDirectory();
        var pipelinePath = Path.Combine(rootPath, "pipelines.json");
        await File.WriteAllTextAsync(pipelinePath, """{ "pipelines": [] }""");

        var generatedAt = new DateTimeOffset(2026, 01, 02, 03, 04, 05, TimeSpan.Zero);
        var writer = new DiagnosticsReportWriter(
            new NoteyOptions
            {
                Pipelines = new PipelineOptions
                {
                    DefinitionFilePath = pipelinePath,
                },
            },
            new PipelineCatalog(
                new FilePipelineDefinitionSource(pipelinePath),
                new PipelineValidator(new PipelineStepRegistry([]))),
            new FakePlatformRuntime(),
            new FixedTimeProvider(generatedAt),
            NullLogger<DiagnosticsReportWriter>.Instance);

        var writtenPath = await writer.WriteAsync();
        var report = await File.ReadAllTextAsync(writtenPath);

        Assert.Equal($"notey-diagnostics-{generatedAt:yyyyMMdd-HHmmss}.md", Path.GetFileName(writtenPath));
        Assert.Contains($"Generated: {generatedAt:O}", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_profile_uses_single_file_self_contained_windows_settings()
    {
        var profilePath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Notey.App",
            "Properties",
            "PublishProfiles",
            "win-x64-folder.pubxml");

        var xml = XDocument.Load(profilePath);
        var properties = xml.Descendants("PropertyGroup").Elements()
            .ToDictionary(static element => element.Name.LocalName, static element => element.Value);

        Assert.Equal("win-x64", properties["RuntimeIdentifier"]);
        Assert.Equal("true", properties["SelfContained"]);
        Assert.Equal("true", properties["PublishSingleFile"]);
        Assert.Equal("false", properties["PublishTrimmed"]);
    }

    [Fact]
    public void Docusaurus_docs_have_frontmatter_and_category_files_are_valid_json()
    {
        var docsRoot = Path.Combine(FindRepoRoot(), "docs");
        var markdownFiles = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories);
        var categoryFiles = Directory.GetFiles(docsRoot, "_category_.json", SearchOption.AllDirectories);

        Assert.NotEmpty(markdownFiles);
        Assert.NotEmpty(categoryFiles);
        foreach (var markdownFile in markdownFiles)
        {
            var content = File.ReadAllText(markdownFile);
            Assert.StartsWith("---\n", content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        foreach (var categoryFile in categoryFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(categoryFile));
            Assert.True(document.RootElement.TryGetProperty("label", out _));
            Assert.True(document.RootElement.TryGetProperty("position", out _));
        }
    }

    [Fact]
    public void App_uses_resource_backed_icons()
    {
        var root = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Notey.App.csproj"));
        var trayService = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Platform", "AvaloniaTrayService.cs"));
        var appMarkup = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "App.axaml"));
        var mainWindowMarkup = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Views", "MainWindow.axaml"));

        Assert.True(File.Exists(Path.Combine(root, "src", "Notey.App", "Assets", "notey.png")));
        Assert.True(File.Exists(Path.Combine(root, "src", "Notey.App", "Assets", "notey-tray.png")));
        Assert.Contains("Assets\\notey.png", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\notey-tray.png", project, StringComparison.Ordinal);
        Assert.Contains("avares://Notey/Assets/notey-tray.png", trayService, StringComparison.Ordinal);
        Assert.Contains("avares://Notey/Assets/notey.png", mainWindowMarkup, StringComparison.Ordinal);
        Assert.Contains("M6,4 L14,4 L18,8 L18,20 L6,20 Z", mainWindowMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("M12,5 L12,19 M5,12 L19,12", mainWindowMarkup, StringComparison.Ordinal);
        Assert.Contains("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml", appMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("avares://Notey.App/Assets", trayService, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_workflow_builds_tests_and_can_publish_windows_artifact()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "ci.yml"));
        var publishScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "publish-windows.ps1"));

        Assert.Contains("dotnet build Notey.slnx", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test Notey.slnx", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/publish-windows.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::IsPathRooted($OutputPath)", publishScript, StringComparison.Ordinal);
        Assert.Contains("-p:PublishProfile=\"$publishProfile\"", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PublishSingleFile=true", publishScript, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notey-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Notey.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class FakePlatformRuntime : IPlatformRuntime
    {
        public string OperatingSystem => "Test";

        public bool IsWindows => false;

        public bool SupportsGlobalHotkeys => false;

        public bool SupportsScreenSnips => false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
