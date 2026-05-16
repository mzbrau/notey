using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.App.Processing;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Ocr;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;
using Notey.Vault.Linking;
using Notey.Vault.Notes;

namespace Notey.Tests;

internal sealed class MainWindowTestHarness : IDisposable
{
    private readonly FixedTimeProvider _timeProvider;

    private MainWindowTestHarness()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "notey-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(RootPath, "Notes", "Customers"));

        _timeProvider = new FixedTimeProvider(DateTimeOffset.Now);
        var options = new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = RootPath },
            Ai = new AiOptions { DefaultProviderId = "default", ModelName = "test" }
        };

        var workspace = new FileSystemVaultWorkspace(options);
        var documentStoreIndex = new FileSystemDocumentStoreIndex(workspace);
        var linkBuilder = new ObsidianLinkBuilder(workspace);
        var aiProviderRegistry = new AiProviderRegistry([new RecordingAiProvider()], "default");
        var ocrEngine = new RecordingOcrEngine();
        var draftProcessingService = new DraftProcessingService(
            options,
            workspace,
            documentStoreIndex,
            aiProviderRegistry,
            ocrEngine,
            _timeProvider);

        RecentNoteChooser = new TestRecentNoteChooser();
        Window = new MainWindow(
            options,
            new FileSystemNoteDraftStore(workspace, new NoteTemplateFactory(), new NoteFileNameGenerator()),
            workspace,
            documentStoreIndex,
            new FileSystemVaultEntityStore(workspace, linkBuilder, _timeProvider),
            new UnavailableScreenSnipService(),
            ocrEngine,
            linkBuilder,
            draftProcessingService,
            _timeProvider,
            NullLogger<MainWindow>.Instance,
            RecentNoteChooser);
    }

    public string RootPath { get; }

    public MainWindow Window { get; }

    public TestRecentNoteChooser RecentNoteChooser { get; }

    public DateTimeOffset LocalNow => _timeProvider.GetLocalNow();

    public TextEditor Editor => FindRequired<TextEditor>("NoteEditor");

    public string ContextText => FindRequired<TextBlock>("ContextChipText").Text ?? string.Empty;

    public string CurrentNotePathText => FindRequired<TextBlock>("CurrentNotePathText").Text ?? string.Empty;

    public static async Task<MainWindowTestHarness> CreateAsync()
    {
        var harness = new MainWindowTestHarness();
        harness.Window.Show();
        await harness.DrainAsync();
        return harness;
    }

    public async Task SetEditorTextAsync(string text)
    {
        Editor.Document.Text = text;
        Editor.CaretOffset = Editor.Document.TextLength;
        await DrainAsync();
    }

    public async Task<string> WriteFinalNoteAsync(string relativePath, string content)
    {
        var filePath = Path.Combine(RootPath, "Notes", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, content);
        File.SetLastWriteTimeUtc(filePath, LocalNow.UtcDateTime);
        return filePath;
    }

    public async Task OpenRecentNoteAsync(string filePath)
    {
        RecentNoteChooser.Choose = notes =>
        {
            var selected = Assert.Single(notes, note => string.Equals(note.FilePath, filePath, StringComparison.Ordinal));
            return RecentNoteChoice.Open(selected);
        };

        await Window.OpenRecentFinalNoteAsync();
        await DrainAsync();
    }

    public Task DrainAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() => Dispatcher.UIThread.RunJobs()).GetTask();
    }

    public async Task WaitForEditorTextAsync(string expectedText, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await DrainAsync();
            if (string.Equals(Editor.Document.Text, expectedText, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(10);
        }

        await DrainAsync();
        Assert.Equal(expectedText, Editor.Document.Text);
    }

    public string GetExpectedCustomerMeetingPath(string customer, string topic)
    {
        return Path.Combine(
            RootPath,
            "Notes",
            "Customers",
            ObsidianLinkBuilder.GetSafeFileStem(customer),
            "Meetings",
            $"{LocalNow:yyyy-MM-dd} - {ObsidianLinkBuilder.GetSafeFileStem(topic).ToLowerInvariant()}.md");
    }

    public void Dispose()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Window.Close();
            WaitForWindowClosed(Window);
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Window.Close();
                WaitForWindowClosed(Window);
            }).GetTask().GetAwaiter().GetResult();
        }

        DeleteDirectoryWithRetries(RootPath);
    }

    private T FindRequired<T>(string name)
        where T : Control
    {
        return Window.FindControl<T>(name)
            ?? throw new InvalidOperationException($"Control '{name}' was not found.");
    }

    private static void DeleteDirectoryWithRetries(string path)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            if (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }

        if (Directory.Exists(path))
        {
            throw new IOException($"Failed to delete temporary test directory '{path}' after 5 attempts.", lastException);
        }
    }

    private static void WaitForWindowClosed(Window window)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (!window.IsVisible)
            {
                return;
            }

            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

    internal sealed class TestRecentNoteChooser : IRecentNoteChooser
    {
        public Func<IReadOnlyList<RecentNoteSummary>, RecentNoteChoice> Choose { get; set; } =
            static _ => RecentNoteChoice.Cancel;

        public IReadOnlyList<RecentNoteSummary> LastRecentNotes { get; private set; } = [];

        public Task<RecentNoteChoice> ChooseAsync(Window owner, IReadOnlyList<RecentNoteSummary> recentNotes)
        {
            LastRecentNotes = recentNotes;
            return Task.FromResult(Choose(recentNotes));
        }
    }

    private sealed class RecordingAiProvider : IAiProvider
    {
        public string Id => "default";

        public ValueTask<AiTextResponse> CompleteTextAsync(AiTextRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Prompt.Contains("Updated recent note body.", StringComparison.Ordinal))
            {
                const string updatedResponse = """
                    {
                      "body": "Updated recent note body.",
                      "tags": ["updated"]
                    }
                    """;
                return ValueTask.FromResult(new AiTextResponse(updatedResponse, Id, "test"));
            }

            const string response = """
                {
                  "body": "Captured accounts launch context.",
                  "people": ["Jane Doe"],
                  "tags": ["accounts"]
                }
                """;
            return ValueTask.FromResult(new AiTextResponse(response, Id, "test"));
        }
    }

    private sealed class RecordingOcrEngine : ITesseractOcrEngine
    {
        public ValueTask<OcrResult> RecognizeAsync(TesseractOcrRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new OcrResult("ocr text", "eng", 1.0, []));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset localNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return localNow.ToUniversalTime();
        }
    }
}
