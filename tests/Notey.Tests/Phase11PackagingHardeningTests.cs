using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.App.Diagnostics;
using Notey.Core.Configuration;
using Notey.Core.Platform;

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
    public async Task Diagnostics_report_redacts_api_keys()
    {
        var rootPath = CreateTempDirectory();
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
        };
        var writer = new DiagnosticsReportWriter(
            options,
            new FakePlatformRuntime(),
            TimeProvider.System,
            NullLogger<DiagnosticsReportWriter>.Instance);

        var writtenPath = await writer.WriteAsync(outputPath);
        var report = await File.ReadAllTextAsync(writtenPath);

        Assert.Equal(outputPath, writtenPath);
        Assert.Contains("# Notey diagnostics", report, StringComparison.Ordinal);
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
            };
            var writer = new DiagnosticsReportWriter(
                options,
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
        var generatedAt = new DateTimeOffset(2026, 01, 02, 03, 04, 05, TimeSpan.Zero);
        var writer = new DiagnosticsReportWriter(
            new NoteyOptions(),
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
    public void Ocr_project_publishes_windows_native_tesseract_binaries()
    {
        var projectPath = Path.Combine(FindRepoRoot(), "src", "Notey.Ocr", "Notey.Ocr.csproj");
        var xml = XDocument.Load(projectPath);
        var packageReference = xml.Descendants("PackageReference")
            .Single(static element => string.Equals((string?)element.Attribute("Include"), "TesseractOCR", StringComparison.Ordinal));
        var content = xml.Descendants("Content")
            .Single(static element => ((string?)element.Attribute("Include"))?.Contains("$(TesseractOcrNativePlatform)", StringComparison.Ordinal) == true);

        Assert.Equal("true", (string?)packageReference.Attribute("GeneratePathProperty"));
        Assert.Contains("win-x64", xml.ToString(), StringComparison.Ordinal);
        Assert.Contains("win-x86", xml.ToString(), StringComparison.Ordinal);
        Assert.Contains("$(PkgTesseractOCR)", (string?)content.Attribute("Include"), StringComparison.Ordinal);
        Assert.Equal(@"$(TesseractOcrNativePlatform)\%(Filename)%(Extension)", (string?)content.Attribute("Link"));
        Assert.Equal("PreserveNewest", (string?)content.Attribute("CopyToPublishDirectory"));
    }

    [Fact]
    public void MinVer_versioning_is_configured_for_v_prefixed_release_tags()
    {
        var root = FindRepoRoot();
        var propsPath = Path.Combine(root, "Directory.Build.props");
        var appProjectPath = Path.Combine(root, "src", "Notey.App", "Notey.App.csproj");

        var props = XDocument.Load(propsPath);
        var properties = props.Descendants("PropertyGroup").Elements()
            .ToDictionary(static element => element.Name.LocalName, static element => element.Value);
        var minVerReference = props.Descendants("PackageReference")
            .Single(static element => string.Equals((string?)element.Attribute("Include"), "MinVer", StringComparison.Ordinal));
        var appProject = File.ReadAllText(appProjectPath);

        Assert.Equal("v", properties["MinVerTagPrefix"]);
        Assert.Equal("all", (string?)minVerReference.Attribute("PrivateAssets"));
        Assert.DoesNotContain("<Version>0.1.0</Version>", appProject, StringComparison.Ordinal);
    }

    [Fact]
    public void App_registers_Velopack_startup_hook_without_update_prompting()
    {
        var root = FindRepoRoot();
        var appProject = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Notey.App.csproj"));
        var program = File.ReadAllText(Path.Combine(root, "src", "Notey.App", "Program.cs"));

        Assert.Contains("<PackageReference Include=\"Velopack\"", appProject, StringComparison.Ordinal);
        Assert.Contains("VelopackApp.Build().Run();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateManager", program, StringComparison.Ordinal);
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

    [Fact]
    public void Release_workflow_builds_tests_packages_and_publishes_v_tag_releases()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("'v*'", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", workflow, StringComparison.Ordinal);
        Assert.Contains("fetch-tags: true", workflow, StringComparison.Ordinal);
        Assert.Contains("git fetch origin main:refs/remotes/origin/main", workflow, StringComparison.Ordinal);
        Assert.Contains("git merge-base --is-ancestor", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet restore Notey.slnx", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet msbuild src/Notey.App/Notey.App.csproj -target:MinVer -getProperty:MinVerVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("Tag version '$tagVersion' does not match MinVer version '$minVerVersion'.", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet build Notey.slnx --configuration Release --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test Notey.slnx --configuration Release --no-build", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/publish-windows.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/package-windows.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("\"upload\", \"github\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--merge", workflow, StringComparison.Ordinal);
        Assert.Contains("--pre", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_packaging_script_uses_Velopack_installer_assets()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "package-windows.ps1"));

        Assert.Contains("[string] $PackageId = \"Notey\"", script, StringComparison.Ordinal);
        Assert.Contains("[string] $MainExe = \"Notey.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("[switch] $Prerelease", script, StringComparison.Ordinal);
        Assert.Contains("Test-PreviousVelopackReleaseExists", script, StringComparison.Ordinal);
        Assert.Contains("RELEASES.$Channel.json", script, StringComparison.Ordinal);
        Assert.Contains("\"download\", \"github\"", script, StringComparison.Ordinal);
        Assert.Contains("No previous Velopack GitHub release assets were found.", script, StringComparison.Ordinal);
        Assert.Contains("Velopack packaging failed with exit code $LASTEXITCODE.", script, StringComparison.Ordinal);
        Assert.Contains("Velopack previous-release download failed with exit code $LASTEXITCODE.", script, StringComparison.Ordinal);
        Assert.Contains("vpk pack", script, StringComparison.Ordinal);
        Assert.Contains("--packId $PackageId", script, StringComparison.Ordinal);
        Assert.Contains("--packVersion $Version", script, StringComparison.Ordinal);
        Assert.Contains("--packDir $publishPathResolved", script, StringComparison.Ordinal);
        Assert.Contains("--mainExe $MainExe", script, StringComparison.Ordinal);
        Assert.Contains("--runtime $RuntimeIdentifier", script, StringComparison.Ordinal);
        Assert.Contains("--channel win", script, StringComparison.Ordinal);
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
