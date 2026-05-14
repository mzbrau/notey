using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.App.Configuration;
using Notey.App.Editing;
using Notey.App.Processing;
using Notey.Capture.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Core.Platform;
using Notey.Ocr;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;
using Notey.Vault.Linking;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan IdleProcessingDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IndexRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RecentFinalNoteLookback = TimeSpan.FromDays(14);

    private readonly NoteyOptions _options;
    private readonly INoteDraftStore _noteDraftStore;
    private readonly IVaultWorkspace _vaultWorkspace;
    private readonly IDocumentStoreIndex _documentStoreIndex;
    private readonly IVaultEntityStore _vaultEntityStore;
    private readonly IScreenSnipService _screenSnipService;
    private readonly ITesseractOcrEngine _ocrEngine;
    private readonly ObsidianLinkBuilder _linkBuilder;
    private readonly DraftProcessingService _draftProcessingService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainWindow> _logger;
    private readonly NoteySettingsStore _settingsStore;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _idleProcessingTimer;
    private readonly CancellationTokenSource _windowClosed = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly ImagePreviewMargin _imagePreviewMargin = new();
    private readonly NoteDirectiveParser _directiveParser = new();
    private readonly List<string> _directOcrSnippets = [];

    private NoteDraft? _currentDraft;
    private string? _currentFinalNotePath;
    private DateTimeOffset? _currentRecentNoteCreatedAt;
    private string _lastSavedText = string.Empty;
    private bool _recentNoteNeedsProcessing;
    private bool _isInitializing;
    private bool _isSwitchingDraft;
    private bool _isCaptureInProgress;
    private bool _isProcessingDraft;
    private bool _isCloseConfirmed;
    private bool _isClosePending;
    private bool _isExitRequested;
    private long _revision;
    private long? _suppressedCompletionRevision;
    private HotkeyGesture? _openNoteGesture;
    private IReadOnlyList<VaultEntity> _peopleIndex = [];
    private IReadOnlyList<VaultFolderCommand> _folderCommands = [];
    private IReadOnlyList<CompletionSuggestion> _completionSuggestions = [];
    private CancellationTokenSource? _idleProcessingCancellation;
    private DateTimeOffset _nextIndexRefreshAt = DateTimeOffset.MinValue;

    public bool HideInsteadOfClose { get; set; }

    public bool IsCaptureInProgress => _isCaptureInProgress;

    public event EventHandler? SettingsSaved;

    public MainWindow()
        : this(CreateDefaultDependencies(), TimeProvider.System, NullLogger<MainWindow>.Instance)
    {
    }

    private MainWindow(DefaultDependencies dependencies, TimeProvider timeProvider, ILogger<MainWindow> logger)
        : this(
            dependencies.Options,
            dependencies.NoteDraftStore,
            dependencies.VaultWorkspace,
            dependencies.DocumentStoreIndex,
            dependencies.VaultEntityStore,
            dependencies.ScreenSnipService,
            dependencies.OcrEngine,
            dependencies.LinkBuilder,
            dependencies.DraftProcessingService,
            timeProvider,
            logger)
    {
    }

    public MainWindow(
        NoteyOptions options,
        INoteDraftStore noteDraftStore,
        IVaultWorkspace vaultWorkspace,
        IDocumentStoreIndex documentStoreIndex,
        IVaultEntityStore vaultEntityStore,
        IScreenSnipService screenSnipService,
        ITesseractOcrEngine ocrEngine,
        ObsidianLinkBuilder linkBuilder,
        DraftProcessingService draftProcessingService,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger,
        NoteySettingsStore? settingsStore = null)
    {
        InitializeComponent();

        _options = options;
        _noteDraftStore = noteDraftStore;
        _vaultWorkspace = vaultWorkspace;
        _documentStoreIndex = documentStoreIndex;
        _vaultEntityStore = vaultEntityStore;
        _screenSnipService = screenSnipService;
        _ocrEngine = ocrEngine;
        _linkBuilder = linkBuilder;
        _draftProcessingService = draftProcessingService;
        _timeProvider = timeProvider;
        _logger = logger;
        _settingsStore = settingsStore ?? CreateFallbackSettingsStore(options);
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _idleProcessingTimer = new DispatcherTimer { Interval = IdleProcessingDelay };
        _autosaveTimer.Tick += AutosaveTimerOnTick;
        _idleProcessingTimer.Tick += IdleProcessingTimerOnTick;
        _imagePreviewMargin.PreviewRequested += OnImagePreviewRequested;

        Width = options.Ui.DefaultWindowWidth;
        Height = options.Ui.DefaultWindowHeight;
        _openNoteGesture = TryParseOpenNoteGesture(options.Hotkeys.OpenNote);

        ConfigureEditor();
        ConfigureCommands();
        UpdateEditorStatus();
        UpdateContextChip();
        UpdateCurrentNoteFooter();

        Opened += async (_, _) => await OpenInitialDraftAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _windowClosed.Cancel();
            CancelIdleProcessing();
            DisposeIdleProcessingCancellation();
            _windowClosed.Dispose();
            _saveGate.Dispose();
        };

        logger.LogInformation("Notey shell initialized with {Theme} theme.", options.Ui.Theme);
    }

    private static DefaultDependencies CreateDefaultDependencies()
    {
        var options = new NoteyOptions();
        var workspace = new FileSystemVaultWorkspace(options);
        var documentStoreIndex = new FileSystemDocumentStoreIndex(workspace);
        var linkBuilder = new ObsidianLinkBuilder(workspace);
        var aiRegistry = new AiProviderRegistry([], "default");
        var ocrEngine = new TesseractCliOcrEngine();
        var draftProcessingService = new DraftProcessingService(
            options,
            workspace,
            documentStoreIndex,
            aiRegistry,
            ocrEngine,
            TimeProvider.System);

        return new DefaultDependencies(
            options,
            new FileSystemNoteDraftStore(workspace, new NoteTemplateFactory(), new NoteFileNameGenerator()),
            workspace,
            documentStoreIndex,
            new FileSystemVaultEntityStore(workspace, linkBuilder, TimeProvider.System),
            new UnavailableScreenSnipService(),
            ocrEngine,
            linkBuilder,
            draftProcessingService);
    }

    private static NoteySettingsStore CreateFallbackSettingsStore(NoteyOptions options)
    {
        var providerRegistry = new AiProviderRegistry(
            OpenAiCompatibleAiProviderFactory.CreateProviders(options.Ai, static () => new HttpClient()),
            string.IsNullOrWhiteSpace(options.Ai.DefaultProviderId) ? "default" : options.Ai.DefaultProviderId);

        return new NoteySettingsStore(
            options,
            providerRegistry,
            new FallbackHttpClientFactory(),
            NullLogger<NoteySettingsStore>.Instance);
    }

    private void ConfigureEditor()
    {
        NoteEditor.TextChanged += (_, _) =>
        {
            if (_isInitializing)
            {
                return;
            }

            _revision++;
            if (_currentFinalNotePath is not null)
            {
                _recentNoteNeedsProcessing = true;
            }
            CancelIdleProcessing();
            UpdateCompletion();
            UpdateContextChip();
            ScheduleAutosave();
            ScheduleIdleProcessing();
        };
        NoteEditor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        NoteEditor.KeyUp += (_, _) =>
        {
            UpdateEditorStatus();
            UpdateCompletion();
        };
        NoteEditor.PointerReleased += (_, _) =>
        {
            UpdateEditorStatus();
            UpdateCompletion();
        };
        CompletionList.PointerReleased += async (_, _) => await InsertSelectedCompletionAsync();
        NoteEditor.Options.EnableHyperlinks = true;
        NoteEditor.Options.EnableEmailHyperlinks = true;
        ApplyEditorTheme();
        NoteEditor.TextArea.TextView.LineTransformers.Add(new MarkdownColorizingTransformer());
        NoteEditor.TextArea.LeftMargins.Insert(0, _imagePreviewMargin);
    }

    private void ConfigureCommands()
    {
        NewNoteButton.Click += async (_, _) => await StartNewNoteAsync();
        OpenRecentNoteButton.Click += async (_, _) => await OpenRecentFinalNoteAsync();
        CaptureAnalyzeButton.Click += async (_, _) => await CaptureTemporaryOcrAsync();
        CaptureSaveButton.Click += async (_, _) => await CapturePersistentImageAsync();
        SaveNoteButton.Click += async (_, _) =>
        {
            if (await FlushCurrentDocumentAsync(processRecentNote: true))
            {
                AutosaveStatusText.Text = "SAVED";
            }
        };
        OpenInObsidianButton.Click += (_, _) => OpenCurrentNoteInObsidian();
        SettingsButton.Click += async (_, _) => await OpenSettingsAsync();
    }

    public async Task ActivateOrResumeAsync()
    {
        if (_currentDraft is null && _currentFinalNotePath is null)
        {
            await OpenInitialDraftAsync();
            return;
        }

        FocusEditor();
    }

    public async Task StartNewNoteAsync()
    {
        if (_isSwitchingDraft)
        {
            return;
        }

        _idleProcessingTimer.Stop();
        if (_currentDraft is not null)
        {
            var outcome = await ProcessCurrentDraftAsync(ProcessTrigger.NewNote);
            if (!outcome.Succeeded)
            {
                return;
            }

            return;
        }

        if (_currentFinalNotePath is not null)
        {
            if (!await ProcessCurrentRecentNoteAsync())
            {
                return;
            }

            await TryCreateAndLoadDraftAsync("starting a new note");
            return;
        }

        await TryCreateAndLoadDraftAsync("starting a new note");
    }

    public void ReportHotkeyRegistrationFailure()
    {
        AutosaveStatusText.Text = "HOTKEY UNAVAILABLE";
    }

    public void RequestExit()
    {
        _isExitRequested = true;
        Close();
    }

    private async Task OpenInitialDraftAsync()
    {
        if (_currentDraft is not null || _currentFinalNotePath is not null)
        {
            return;
        }

        await TryCreateAndLoadDraftAsync("opening the initial note");
    }

    private async Task<bool> TryCreateAndLoadDraftAsync(string operation)
    {
        try
        {
            var draft = await _noteDraftStore.CreateAsync(_timeProvider.GetLocalNow(), _windowClosed.Token);
            await LoadDraftAsync(draft);
            return true;
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Draft activation while {Operation} was cancelled because the window closed.", operation);
            return false;
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Failed to create a note draft while {Operation}.", operation);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey does not have permission to create a note draft while {Operation}.", operation);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration prevented draft activation while {Operation}.", operation);
            return false;
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration contained an invalid value while {Operation}.", operation);
            return false;
        }
    }

    private async Task LoadDraftAsync(NoteDraft draft)
    {
        _isSwitchingDraft = true;
        _isInitializing = true;
        _autosaveTimer.Stop();
        _idleProcessingTimer.Stop();
        HideCompletion();

        try
        {
            _currentDraft = draft;
            _currentFinalNotePath = null;
            _currentRecentNoteCreatedAt = null;
            _recentNoteNeedsProcessing = false;
            _directOcrSnippets.Clear();
            NoteEditor.Document.Text = draft.Content;
            NoteEditor.CaretOffset = NoteEditor.Document.TextLength;
            NoteEditor.IsReadOnly = false;
            _lastSavedText = draft.Content;
            DateChipText.Text = draft.CreatedAt.ToString("ddd, HH:mm");
            await RefreshIndexesAsync();
            AutosaveStatusText.Text = "SAVED";
            _revision++;
            UpdateEditorStatus();
            UpdateContextChip();
            UpdateCurrentNoteFooter();
            FocusEditor();
        }
        finally
        {
            _isInitializing = false;
            _isSwitchingDraft = false;
        }
    }

    private async Task OpenRecentFinalNoteAsync()
    {
        DraftProcessOutcome outcome = DraftProcessOutcome.NoChange;
        if (_currentDraft is not null)
        {
            outcome = await ProcessCurrentDraftAsync(ProcessTrigger.OpenRecent, applyImmediateFollowUp: false);
            if (!outcome.Succeeded)
            {
                return;
            }
        }
        else if (_currentFinalNotePath is not null && !await ProcessCurrentRecentNoteAsync())
        {
            return;
        }

        var recent = await ListRecentFinalNotesAsync(_timeProvider.GetLocalNow().Subtract(RecentFinalNoteLookback), _windowClosed.Token);
        var choice = await RecentNoteChoiceWindow.ShowAsync(this, recent);
        if (choice.Action == RecentNoteChoiceAction.OpenExisting && choice.SelectedNote is not null)
        {
            await LoadRecentNoteAsync(choice.SelectedNote.FilePath);
        }
        else if (choice.Action == RecentNoteChoiceAction.NewNote)
        {
            await TryCreateAndLoadDraftAsync("starting a new note");
        }
        else if (outcome.DraftChanged)
        {
            await ApplyProcessedDraftFollowUpAsync(ProcessTrigger.OpenRecent, outcome.PrimaryFinalNotePath);
        }
    }

    private async Task<IReadOnlyList<RecentNoteSummary>> ListRecentFinalNotesAsync(
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken)
    {
        var paths = _vaultWorkspace.GetPaths();
        if (!Directory.Exists(paths.NotesPath))
        {
            return [];
        }

        var recent = new List<RecentNoteSummary>();
        foreach (var filePath in Directory.EnumerateFiles(paths.NotesPath, "*.md", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnderPath(paths.DraftPath, filePath))
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(filePath), "tasks.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var created = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
            if (created < createdAfter)
            {
                continue;
            }

            recent.Add(new RecentNoteSummary(filePath, created, Path.GetFileNameWithoutExtension(filePath)));
        }

        return RecentNotes.OrderByMostRecent(recent, 20);
    }

    private async Task LoadRecentNoteAsync(string filePath)
    {
        _isInitializing = true;
        HideCompletion();
        try
        {
            string content;
            try
            {
                content = await File.ReadAllTextAsync(filePath, _windowClosed.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                AutosaveStatusText.Text = "OPEN ERROR";
                _logger.LogError(ex, "Failed to load final note {FilePath}.", filePath);
                return;
            }

            _currentDraft = null;
            _currentFinalNotePath = filePath;
            _currentRecentNoteCreatedAt = ResolveRecentNoteCreatedAt(filePath, content);
            _recentNoteNeedsProcessing = false;
            _directOcrSnippets.Clear();
            NoteEditor.Document.Text = content;
            NoteEditor.CaretOffset = 0;
            NoteEditor.IsReadOnly = false;
            _lastSavedText = content;
            DateChipText.Text = Path.GetFileNameWithoutExtension(filePath);
            AutosaveStatusText.Text = "SAVED";
            UpdateEditorStatus();
            UpdateContextChip();
            UpdateCurrentNoteFooter();
            FocusEditor();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private async Task<DraftProcessOutcome> ProcessCurrentDraftAsync(ProcessTrigger trigger, bool applyImmediateFollowUp = true)
    {
        if (_currentDraft is null)
        {
            return DraftProcessOutcome.NoChange;
        }

        if (_isProcessingDraft)
        {
            AutosaveStatusText.Text = "PROCESSING";
            return DraftProcessOutcome.Failure;
        }

        if (!await FlushAutosaveAsync())
        {
            return DraftProcessOutcome.Failure;
        }

        var content = NoteEditor.Document.Text;
        var draftPath = _currentDraft.FilePath;
        var parsed = _directiveParser.Parse(content, _folderCommands.Select(static command => command.CommandName));
        if (string.IsNullOrWhiteSpace(parsed.Body)
            && parsed.Tasks.Count == 0
            && !_directOcrSnippets.Any(static snippet => !string.IsNullOrWhiteSpace(snippet)))
        {
            DeleteIfExists(_currentDraft.FilePath);
            _currentDraft = null;
            _lastSavedText = string.Empty;
            _directOcrSnippets.Clear();
            if (applyImmediateFollowUp)
            {
                await ApplyProcessedDraftFollowUpAsync(trigger, primaryFinalNotePath: null);
            }

            return DraftProcessOutcome.Changed(primaryFinalNotePath: null);
        }

        _isProcessingDraft = true;
        var wasReadOnly = NoteEditor.IsReadOnly;
        NoteEditor.IsReadOnly = true;
        var cancellation = trigger == ProcessTrigger.Idle
            ? CreateIdleProcessingCancellation()
            : CancellationTokenSource.CreateLinkedTokenSource(_windowClosed.Token);

        try
        {
            AutosaveStatusText.Text = "PROCESSING";
            var result = await _draftProcessingService.ProcessAsync(
                _currentDraft,
                content,
                _directOcrSnippets,
                cancellation.Token);
            if (result.Processed)
            {
                AutosaveStatusText.Text = "PROCESSED";
                var primaryFinalNotePath = SelectPrimaryWrittenNotePath(result.WrittenPaths);
                _currentDraft = null;
                _lastSavedText = string.Empty;
                _directOcrSnippets.Clear();
                if (applyImmediateFollowUp)
                {
                    await ApplyProcessedDraftFollowUpAsync(trigger, primaryFinalNotePath);
                }

                return DraftProcessOutcome.Changed(primaryFinalNotePath);
            }

            AutosaveStatusText.Text = result.Message?.ToUpperInvariant() ?? "NOTHING TO PROCESS";
            return DraftProcessOutcome.NoChange;
        }
        catch (OperationCanceledException) when (trigger == ProcessTrigger.Idle)
        {
            AutosaveStatusText.Text = "PROCESSING CANCELLED";
            NoteEditor.IsReadOnly = false;
            return DraftProcessOutcome.NoChange;
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Draft processing was cancelled because the window closed.");
            return DraftProcessOutcome.NoChange;
        }
        catch (AiProviderException ex)
        {
            AutosaveStatusText.Text = IsAiConfigurationError(ex) ? "AI NOT CONFIGURED" : "AI ERROR";
            _logger.LogError(ex, "AI processing failed for draft {DraftPath}.", draftPath);
            NoteEditor.IsReadOnly = wasReadOnly;
            return DraftProcessOutcome.Failure;
        }
        catch (HttpRequestException ex)
        {
            AutosaveStatusText.Text = "AI ERROR";
            _logger.LogError(ex, "AI request failed for draft {DraftPath}.", draftPath);
            NoteEditor.IsReadOnly = wasReadOnly;
            return DraftProcessOutcome.Failure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            AutosaveStatusText.Text = "PROCESSING FAILED";
            _logger.LogError(ex, "Failed to process draft {DraftPath}.", draftPath);
            NoteEditor.IsReadOnly = wasReadOnly;
            return DraftProcessOutcome.Failure;
        }
        finally
        {
            if (ReferenceEquals(_idleProcessingCancellation, cancellation))
            {
                _idleProcessingCancellation = null;
            }

            cancellation.Dispose();
            _isProcessingDraft = false;
        }
    }

    private async Task ApplyProcessedDraftFollowUpAsync(ProcessTrigger trigger, string? primaryFinalNotePath)
    {
        if (trigger == ProcessTrigger.OpenRecent && primaryFinalNotePath is not null)
        {
            await LoadRecentNoteAsync(primaryFinalNotePath);
            return;
        }

        var operation = trigger switch
        {
            ProcessTrigger.NewNote => "starting a new note",
            ProcessTrigger.OpenRecent => "continuing after opening recent notes",
            _ => "continuing after processing"
        };

        await TryCreateAndLoadDraftAsync(operation);
    }

    internal static string? SelectPrimaryWrittenNotePath(IReadOnlyList<string> writtenPaths)
    {
        ArgumentNullException.ThrowIfNull(writtenPaths);

        return writtenPaths.FirstOrDefault(static path =>
            !string.Equals(Path.GetFileName(path), "tasks.md", StringComparison.OrdinalIgnoreCase));
    }

    private CancellationTokenSource CreateIdleProcessingCancellation()
    {
        _idleProcessingCancellation?.Cancel();
        _idleProcessingCancellation?.Dispose();
        _idleProcessingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowClosed.Token);
        return _idleProcessingCancellation;
    }

    private async Task<bool> FlushAutosaveAsync()
    {
        if (_currentDraft is null)
        {
            return true;
        }

        await _saveGate.WaitAsync(_windowClosed.Token);
        try
        {
            var text = NoteEditor.Document.Text;
            if (string.Equals(text, _lastSavedText, StringComparison.Ordinal))
            {
                AutosaveStatusText.Text = "SAVED";
                return true;
            }

            AutosaveStatusText.Text = "SAVING";
            await _noteDraftStore.SaveAsync(_currentDraft, text, _windowClosed.Token);
            _lastSavedText = text;
            AutosaveStatusText.Text = "SAVED";
            return true;
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Autosave was cancelled because the window closed.");
            return true;
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Failed to autosave note draft {DraftPath}.", _currentDraft.FilePath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey does not have permission to autosave note draft {DraftPath}.", _currentDraft.FilePath);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration prevented autosave for draft {DraftPath}.", _currentDraft.FilePath);
            return false;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async void AutosaveTimerOnTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        await FlushCurrentDocumentAsync();
    }

    private async void IdleProcessingTimerOnTick(object? sender, EventArgs e)
    {
        _idleProcessingTimer.Stop();
        if (!HasExplicitProcessingDirectives())
        {
            return;
        }

        await ProcessCurrentDraftAsync(ProcessTrigger.Idle);
    }

    private void ScheduleAutosave()
    {
        AutosaveStatusText.Text = "UNSAVED CHANGES";
        UpdateEditorStatus();
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void ScheduleIdleProcessing()
    {
        if (_currentDraft is null)
        {
            return;
        }

        _idleProcessingTimer.Stop();
        _idleProcessingTimer.Start();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isCloseConfirmed)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosePending)
        {
            return;
        }

        _isClosePending = true;
        _autosaveTimer.Stop();
        _idleProcessingTimer.Stop();
        if (_currentDraft is not null)
        {
            var processed = await ProcessCurrentDraftAsync(ProcessTrigger.Close);
            if (!processed.Succeeded)
            {
                _logger.LogWarning("Draft processing failed while closing. Continuing close without processing.");

                if (HideInsteadOfClose && !_isExitRequested)
                {
                    _isClosePending = false;
                    Hide();
                    return;
                }

                _isClosePending = false;
                _isCloseConfirmed = true;
                Close();
                return;
            }
        }
        else if (_currentFinalNotePath is not null && !await ProcessCurrentRecentNoteAsync())
        {
            _isClosePending = false;
            return;
        }

        if (HideInsteadOfClose && !_isExitRequested)
        {
            _isClosePending = false;
            Hide();
            return;
        }

        _isCloseConfirmed = true;
        Close();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsOpenNoteGesture(e))
        {
            _ = ActivateOrResumeAsync();
            e.Handled = true;
            return;
        }

        if (TryHandleCompletionKey(e))
        {
            return;
        }

        if (IsCommandModifier(e.KeyModifiers) && e.Key == Key.B)
        {
            ApplyEdit(MarkdownEditorCommands.ToggleBold(NoteEditor.Document.Text, NoteEditor.SelectionStart, NoteEditor.SelectionLength));
            e.Handled = true;
            return;
        }

        if (IsCommandModifier(e.KeyModifiers) && e.Key == Key.I)
        {
            ApplyEdit(MarkdownEditorCommands.ToggleItalic(NoteEditor.Document.Text, NoteEditor.SelectionStart, NoteEditor.SelectionLength));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            var listContinuation = MarkdownEditorCommands.TryCreateListContinuation(NoteEditor.Document.Text, NoteEditor.CaretOffset);
            if (listContinuation is not null)
            {
                ApplyEdit(listContinuation);
                e.Handled = true;
            }
        }
    }

    private bool TryHandleCompletionKey(KeyEventArgs e)
    {
        if (!CompletionPanel.IsVisible)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            HideCompletion();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Down)
        {
            MoveCompletionSelection(1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Up)
        {
            MoveCompletionSelection(-1);
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.Enter or Key.Tab)
        {
            _ = InsertSelectedCompletionAsync();
            e.Handled = true;
            return true;
        }

        return false;
    }

    private async void UpdateCompletion()
    {
        if (_suppressedCompletionRevision is { } suppressedRevision)
        {
            if (_revision <= suppressedRevision)
            {
                HideCompletion();
                return;
            }

            _suppressedCompletionRevision = null;
        }

        if (NoteEditor.IsReadOnly)
        {
            HideCompletion();
            return;
        }

        var text = NoteEditor.Document.Text;
        var caretOffset = NoteEditor.CaretOffset;
        if (SlashCommandCompletionQuery.TryCreate(text, caretOffset) is { } commandQuery)
        {
            await RefreshIndexesAsync();
            ShowCompletion(BuildCommandSuggestions(commandQuery));
            return;
        }

        if (SlashCommandParameterQuery.TryCreate(text, caretOffset) is { } parameterQuery)
        {
            await RefreshIndexesAsync();
            ShowCompletion(await BuildParameterSuggestionsAsync(parameterQuery));
            return;
        }

        if (PersonReferenceCompletionQuery.TryCreate(text, caretOffset) is { } personQuery)
        {
            await RefreshIndexesAsync();
            ShowCompletion(BuildPersonSuggestions(personQuery));
            return;
        }

        HideCompletion();
    }

    private IReadOnlyList<CompletionSuggestion> BuildCommandSuggestions(SlashCommandCompletionQuery query)
    {
        var commands = new[]
            {
                new SlashCommandDefinition("meeting", "Meeting note", "/meeting"),
                new SlashCommandDefinition("topic", "Topic metadata", "/topic "),
                new SlashCommandDefinition("task", "Task", "/task ")
            }
            .Concat(_folderCommands.Select(static command => new SlashCommandDefinition(command.CommandName, command.FolderName, $"/{command.CommandName} ")));

        return commands
            .Where(command => command.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase))
            .Select(command => new CompletionSuggestion(
                $"/{command.Name}  {command.Description}",
                command.InsertionText,
                query.ReplacementStart,
                query.ReplacementLength,
                CompletionKind.Text))
            .Take(10)
            .ToArray();
    }

    private async Task<IReadOnlyList<CompletionSuggestion>> BuildParameterSuggestionsAsync(SlashCommandParameterQuery query)
    {
        if (query.IsTaskDueDateQuery)
        {
            var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            return
            [
                new CompletionSuggestion($"Due {today:yyyy-MM-dd}", ReplaceDueDate(query.SearchText, today), query.ReplacementStart, query.ReplacementLength, CompletionKind.Text),
                new CompletionSuggestion($"Due {today.AddDays(1):yyyy-MM-dd}", ReplaceDueDate(query.SearchText, today.AddDays(1)), query.ReplacementStart, query.ReplacementLength, CompletionKind.Text)
            ];
        }

        if (string.Equals(query.CommandName, "topic", StringComparison.OrdinalIgnoreCase))
        {
            var topics = await _documentStoreIndex.GetTopicSuggestionsAsync(_windowClosed.Token);
            return topics
                .Where(topic => topic.Title.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .Select(topic => new CompletionSuggestion(topic.Title, topic.Title, query.ReplacementStart, query.ReplacementLength, CompletionKind.Text))
                .ToArray();
        }

        var values = await _documentStoreIndex.GetDynamicValueSuggestionsAsync(query.CommandName, _windowClosed.Token);
        return values
            .Where(value => value.Value.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .Select(value => new CompletionSuggestion(value.Value, value.Value, query.ReplacementStart, query.ReplacementLength, CompletionKind.Text))
            .ToArray();
    }

    private IReadOnlyList<CompletionSuggestion> BuildPersonSuggestions(PersonReferenceCompletionQuery query)
    {
        var normalizedSearch = query.SearchText.Trim();
        var suggestions = _peopleIndex
            .Where(entity => string.IsNullOrWhiteSpace(normalizedSearch)
                || entity.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || entity.Aliases.Any(alias => alias.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)))
            .Take(8)
            .Select(entity => new CompletionSuggestion(
                $"@{entity.Name}",
                entity.ToWikiLink(),
                query.ReplacementStart,
                query.ReplacementLength,
                CompletionKind.Person,
                entity.Name))
            .ToList();

        if (normalizedSearch.Length >= 2 && !suggestions.Any(suggestion => suggestion.DisplayText.Equals($"@{normalizedSearch}", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add(new CompletionSuggestion(
                $"Create @{normalizedSearch}",
                normalizedSearch,
                query.ReplacementStart,
                query.ReplacementLength,
                CompletionKind.CreatePerson,
                normalizedSearch));
        }

        return suggestions;
    }

    private async Task InsertSelectedCompletionAsync()
    {
        if (CompletionList.SelectedItem is not CompletionSuggestion suggestion)
        {
            return;
        }

        var insertionText = suggestion.InsertionText;
        if (suggestion.Kind == CompletionKind.CreatePerson)
        {
            try
            {
                var entity = await _vaultEntityStore.EnsureAsync(VaultEntityKind.Person, suggestion.Payload ?? suggestion.InsertionText, _windowClosed.Token);
                insertionText = entity.ToWikiLink();
                await RefreshIndexesAsync(force: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                AutosaveStatusText.Text = "LINK ERROR";
                _logger.LogError(ex, "Failed to create person document for {PersonName}.", suggestion.Payload);
                return;
            }
        }

        _suppressedCompletionRevision = _revision + 1;
        NoteEditor.Document.Replace(suggestion.ReplacementStart, suggestion.ReplacementLength, insertionText);
        NoteEditor.CaretOffset = suggestion.ReplacementStart + insertionText.Length;
        HideCompletion();
        NoteEditor.Focus();
    }

    private void ShowCompletion(IReadOnlyList<CompletionSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            HideCompletion();
            return;
        }

        var selectedSuggestion = CompletionList.SelectedItem as CompletionSuggestion;
        var selectedIndex = CompletionList.SelectedIndex;

        _completionSuggestions = suggestions;
        CompletionList.ItemsSource = suggestions;
        CompletionList.SelectedIndex = ResolveCompletionSelectionIndex(suggestions, selectedSuggestion, selectedIndex);
        CompletionPanel.IsVisible = true;
    }

    private void HideCompletion()
    {
        CompletionPanel.IsVisible = false;
        CompletionList.ItemsSource = null;
        _completionSuggestions = [];
    }

    private void MoveCompletionSelection(int delta)
    {
        if (_completionSuggestions.Count == 0)
        {
            return;
        }

        var selectedIndex = CompletionList.SelectedIndex < 0 ? 0 : CompletionList.SelectedIndex;
        CompletionList.SelectedIndex = Math.Clamp(selectedIndex + delta, 0, _completionSuggestions.Count - 1);
    }

    private static int ResolveCompletionSelectionIndex(
        IReadOnlyList<CompletionSuggestion> suggestions,
        CompletionSuggestion? currentSelection,
        int currentIndex)
    {
        if (currentSelection is not null)
        {
            for (var index = 0; index < suggestions.Count; index++)
            {
                if (suggestions[index] == currentSelection)
                {
                    return index;
                }
            }
        }

        return currentIndex >= 0
            ? Math.Clamp(currentIndex, 0, suggestions.Count - 1)
            : 0;
    }

    private static string ReplaceDueDate(string searchText, DateOnly dueDate)
    {
        var marker = searchText.IndexOf("//", StringComparison.Ordinal);
        return marker < 0
            ? $"{searchText} // {dueDate:yyyy-MM-dd}"
            : $"{searchText[..(marker + 2)].TrimEnd()} {dueDate:yyyy-MM-dd}";
    }

    private static bool IsAiConfigurationError(AiProviderException exception)
    {
        return exception.Message.Contains("has no configured base URL", StringComparison.Ordinal)
            || exception.Message.Contains("has no API key", StringComparison.Ordinal)
            || exception.Message.Contains("has no configured model name", StringComparison.Ordinal);
    }

    private async Task RefreshIndexesAsync(bool force = false)
    {
        if (!force && _timeProvider.GetUtcNow() < _nextIndexRefreshAt)
        {
            return;
        }

        try
        {
            _folderCommands = await _documentStoreIndex.GetFolderCommandsAsync(_windowClosed.Token);
            _peopleIndex = await _vaultEntityStore.GetAllAsync(VaultEntityKind.Person, _windowClosed.Token);
            _nextIndexRefreshAt = _timeProvider.GetUtcNow().Add(IndexRefreshInterval);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Index refresh was cancelled because the window closed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError(ex, "Failed to refresh vault indexes.");
        }
    }

    private void CancelIdleProcessing()
    {
        try
        {
            _idleProcessingCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeIdleProcessingCancellation()
    {
        try
        {
            _idleProcessingCancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _idleProcessingCancellation = null;
        }
    }

    private bool HasExplicitProcessingDirectives()
    {
        if (_currentDraft is null)
        {
            return false;
        }

        if (_directOcrSnippets.Any(static snippet => !string.IsNullOrWhiteSpace(snippet)))
        {
            return true;
        }

        var parsed = _directiveParser.Parse(NoteEditor.Document.Text, _folderCommands.Select(static command => command.CommandName));
        return parsed.IsMeeting
            || !string.IsNullOrWhiteSpace(parsed.Topic)
            || parsed.DynamicDirectives.Count > 0
            || parsed.Tasks.Count > 0;
    }

    private async Task CapturePersistentImageAsync()
    {
        if (!await EnsureDraftReadyForCaptureAsync())
        {
            return;
        }

        var snip = await CaptureScreenshotAsync(ScreenSnipMode.SaveOnly);
        if (snip is null)
        {
            return;
        }

        AppendMarkdownBlock($"![[{Path.GetRelativePath(_vaultWorkspace.GetPaths().RootPath, snip.FilePath).Replace(Path.DirectorySeparatorChar, '/')}]]");
        AutosaveStatusText.Text = "IMAGE SAVED";
    }

    private async Task CaptureTemporaryOcrAsync()
    {
        if (!await EnsureDraftReadyForCaptureAsync())
        {
            return;
        }

        var snip = await CaptureScreenshotAsync(ScreenSnipMode.AnalyzeWithAi);
        if (snip is null)
        {
            return;
        }

        try
        {
            var result = await _ocrEngine.RecognizeAsync(
                new TesseractOcrRequest(
                    snip.FilePath,
                    _options.Ocr.TesseractExecutablePath,
                    _options.Ocr.DefaultLanguage,
                    string.IsNullOrWhiteSpace(_options.Ocr.TesseractDataPath) ? null : _options.Ocr.TesseractDataPath),
                _windowClosed.Token);
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                _directOcrSnippets.Add(result.Text.Trim());
                AutosaveStatusText.Text = "OCR ADDED";
                UpdateContextChip();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Win32Exception)
        {
            AutosaveStatusText.Text = "OCR ERROR";
            _logger.LogError(ex, "Failed to OCR temporary screen snip.");
        }
        finally
        {
            DeleteIfExists(snip.FilePath);
        }
    }

    private async Task<bool> EnsureDraftReadyForCaptureAsync()
    {
        if (_currentDraft is not null)
        {
            return true;
        }

        if (_currentFinalNotePath is not null && !await ProcessCurrentRecentNoteAsync())
        {
            return false;
        }

        return await TryCreateAndLoadDraftAsync("capturing content");
    }

    private async Task<ScreenSnipResult?> CaptureScreenshotAsync(ScreenSnipMode mode)
    {
        if (_isCaptureInProgress)
        {
            AutosaveStatusText.Text = "SNIP ACTIVE";
            return null;
        }

        _isCaptureInProgress = true;
        CaptureAnalyzeButton.IsEnabled = false;
        CaptureSaveButton.IsEnabled = false;
        var shouldRestoreWindow = IsVisible;

        try
        {
            AutosaveStatusText.Text = "SNIP SELECT";
            if (shouldRestoreWindow)
            {
                Hide();
                await Task.Delay(TimeSpan.FromMilliseconds(150), _windowClosed.Token);
            }

            return await _screenSnipService.CaptureAsync(mode, _windowClosed.Token);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Screen snip was cancelled because the window closed.");
            return null;
        }
        catch (OperationCanceledException)
        {
            AutosaveStatusText.Text = "SNIP CANCELLED";
            return null;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or Win32Exception or ExternalException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Screen snip failed.");
            return null;
        }
        finally
        {
            _isCaptureInProgress = false;
            CaptureAnalyzeButton.IsEnabled = true;
            CaptureSaveButton.IsEnabled = true;
            if (shouldRestoreWindow && !_windowClosed.IsCancellationRequested)
            {
                Show();
                Activate();
                FocusEditor();
            }
        }
    }

    private async Task OpenSettingsAsync()
    {
        NoteyOptions? updatedOptions;
        try
        {
            updatedOptions = await SettingsWindow.ShowAsync(this, _options);
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SETTINGS ERROR";
            _logger.LogError(ex, "Unable to open settings.");
            return;
        }

        if (updatedOptions is null)
        {
            return;
        }

        try
        {
            var result = await _settingsStore.SaveAsync(updatedOptions, _windowClosed.Token);
            Width = _options.Ui.DefaultWindowWidth;
            Height = _options.Ui.DefaultWindowHeight;
            _openNoteGesture = TryParseOpenNoteGesture(_options.Hotkeys.OpenNote);
            AutosaveStatusText.Text = result.RestartRequired ? "SETTINGS SAVED - RESTART NEEDED" : "SETTINGS SAVED";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "SETTINGS ERROR";
            _logger.LogError(ex, "Failed to save settings.");
        }
    }

    private void AppendMarkdownBlock(string markdown)
    {
        var trimmed = markdown.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        var currentText = NoteEditor.Document.Text;
        var separator = currentText.Length == 0
            ? string.Empty
            : currentText.EndsWith("\n\n", StringComparison.Ordinal)
                ? string.Empty
                : currentText.EndsWith('\n')
                    ? "\n"
                    : "\n\n";
        NoteEditor.Document.Insert(NoteEditor.Document.TextLength, $"{separator}{trimmed}\n");
        NoteEditor.CaretOffset = NoteEditor.Document.TextLength;
        UpdateEditorStatus();
    }

    private void UpdateContextChip()
    {
        if (_currentFinalNotePath is not null)
        {
            ContextChipText.Text = "Opened recent note";
            return;
        }

        var parsed = _directiveParser.Parse(NoteEditor.Document.Text, _folderCommands.Select(static command => command.CommandName));
        var parts = new List<string>();
        if (parsed.IsMeeting)
        {
            parts.Add("meeting");
        }

        if (!string.IsNullOrWhiteSpace(parsed.Topic))
        {
            parts.Add($"topic: {parsed.Topic}");
        }

        parts.AddRange(parsed.DynamicDirectives.Select(static directive => $"{directive.CommandName}: {directive.Value}"));
        if (parsed.Tasks.Count > 0)
        {
            parts.Add($"{parsed.Tasks.Count} task(s)");
        }

        if (_directOcrSnippets.Count > 0)
        {
            parts.Add($"{_directOcrSnippets.Count} OCR snippet(s)");
        }

        ContextChipText.Text = parts.Count == 0 ? "/ for commands, @ for people" : string.Join(" | ", parts);
    }

    private void ApplyEdit(MarkdownTextEdit edit)
    {
        NoteEditor.Document.Replace(edit.ReplacementStart, edit.ReplacementLength, edit.ReplacementText);
        NoteEditor.SelectionStart = edit.SelectionStart;
        NoteEditor.SelectionLength = edit.SelectionLength;
        NoteEditor.CaretOffset = edit.CaretOffset;
        UpdateEditorStatus();
    }

    private void UpdateEditorStatus()
    {
        var status = NoteEditorStatus.FromText(NoteEditor.Document.Text, NoteEditor.CaretOffset);
        WordCountText.Text = $"WORDS {status.WordCount}";
        CursorPositionText.Text = $"LINE {status.Line}, COL {status.Column}";
    }

    private void UpdateCurrentNoteFooter()
    {
        var isRecentNoteOpen = _currentFinalNotePath is not null;
        CurrentNotePathText.IsVisible = isRecentNoteOpen;
        OpenInObsidianButton.IsVisible = isRecentNoteOpen;
        OpenInObsidianButton.IsEnabled = isRecentNoteOpen;
        CurrentNotePathText.Text = isRecentNoteOpen ? _currentFinalNotePath : string.Empty;
    }

    private async Task<bool> FlushCurrentDocumentAsync(bool processRecentNote = false)
    {
        if (_currentDraft is not null)
        {
            return await FlushAutosaveAsync();
        }

        if (_currentFinalNotePath is not null)
        {
            return processRecentNote
                ? await ProcessCurrentRecentNoteAsync()
                : await SaveCurrentRecentNoteAsync();
        }

        return true;
    }

    private async Task<bool> SaveCurrentRecentNoteAsync()
    {
        if (_currentFinalNotePath is null)
        {
            return true;
        }

        await _saveGate.WaitAsync(_windowClosed.Token);
        try
        {
            var text = NoteEditor.Document.Text;
            if (string.Equals(text, _lastSavedText, StringComparison.Ordinal))
            {
                AutosaveStatusText.Text = "SAVED";
                return true;
            }

            AutosaveStatusText.Text = "SAVING";
            await _draftProcessingService.SaveExistingNoteAsync(_currentFinalNotePath, text, _windowClosed.Token);
            _lastSavedText = text;
            AutosaveStatusText.Text = "SAVED";
            return true;
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Recent note save was cancelled because the window closed.");
            return true;
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Failed to save recent note {FilePath}.", _currentFinalNotePath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey does not have permission to save recent note {FilePath}.", _currentFinalNotePath);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey could not save recent note {FilePath}.", _currentFinalNotePath);
            return false;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task<bool> ProcessCurrentRecentNoteAsync()
    {
        if (_currentFinalNotePath is null || !_recentNoteNeedsProcessing)
        {
            return true;
        }

        await _saveGate.WaitAsync(_windowClosed.Token);
        var wasReadOnly = NoteEditor.IsReadOnly;
        NoteEditor.IsReadOnly = true;
        try
        {
            AutosaveStatusText.Text = "PROCESSING";
            var caretOffset = NoteEditor.CaretOffset;
            var updatedContent = await _draftProcessingService.ProcessExistingNoteAsync(
                _currentFinalNotePath,
                NoteEditor.Document.Text,
                _currentRecentNoteCreatedAt ?? _timeProvider.GetLocalNow(),
                _windowClosed.Token);

            _isInitializing = true;
            try
            {
                NoteEditor.Document.Text = updatedContent;
                NoteEditor.CaretOffset = Math.Min(caretOffset, NoteEditor.Document.TextLength);
                _lastSavedText = updatedContent;
                _recentNoteNeedsProcessing = false;
                UpdateEditorStatus();
                UpdateContextChip();
                UpdateCurrentNoteFooter();
            }
            finally
            {
                _isInitializing = false;
            }

            AutosaveStatusText.Text = "SAVED";
            return true;
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Recent note processing was cancelled because the window closed.");
            return true;
        }
        catch (AiProviderException ex)
        {
            AutosaveStatusText.Text = IsAiConfigurationError(ex) ? "AI NOT CONFIGURED" : "AI ERROR";
            _logger.LogError(ex, "AI processing failed for recent note {FilePath}.", _currentFinalNotePath);
            return false;
        }
        catch (HttpRequestException ex)
        {
            AutosaveStatusText.Text = "AI ERROR";
            _logger.LogError(ex, "AI request failed for recent note {FilePath}.", _currentFinalNotePath);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            AutosaveStatusText.Text = "PROCESSING FAILED";
            _logger.LogError(ex, "Failed to process recent note {FilePath}.", _currentFinalNotePath);
            return false;
        }
        finally
        {
            NoteEditor.IsReadOnly = wasReadOnly;
            _saveGate.Release();
        }
    }

    private void OpenCurrentNoteInObsidian()
    {
        if (_currentFinalNotePath is null)
        {
            return;
        }

        try
        {
            var uri = _linkBuilder.BuildOpenFileUri(_currentFinalNotePath);
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            AutosaveStatusText.Text = "OPENED IN OBSIDIAN";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Win32Exception or PlatformNotSupportedException)
        {
            AutosaveStatusText.Text = "OBSIDIAN ERROR";
            _logger.LogError(ex, "Failed to open recent note {FilePath} in Obsidian.", _currentFinalNotePath);
        }
    }

    private static DateTimeOffset ResolveRecentNoteCreatedAt(string filePath, string content)
    {
        return TryReadCreatedAtFromFrontmatter(content)
            ?? new DateTimeOffset(File.GetCreationTimeUtc(filePath), TimeSpan.Zero);
    }

    private static DateTimeOffset? TryReadCreatedAtFromFrontmatter(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        var endIndex = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return null;
        }

        foreach (var line in normalized[4..endIndex].Split('\n'))
        {
            if (!line.StartsWith("created:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return DateTimeOffset.TryParse(line["created:".Length..].Trim(), out var createdAt)
                ? createdAt
                : null;
        }

        return null;
    }

    private void FocusEditor()
    {
        NoteEditor.Focus();
    }

    private async void OnImagePreviewRequested(object? sender, ImagePreviewRequestedEventArgs e)
    {
        var imagePath = Path.Combine(
            _vaultWorkspace.GetPaths().RootPath,
            e.Embed.VaultRelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            await ImagePreviewWindow.ShowAsync(this, imagePath, e.Embed.VaultRelativePath, _windowClosed.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "IMAGE PREVIEW ERROR";
            _logger.LogError(ex, "Failed to preview image {ImagePath}.", imagePath);
        }
    }

    private void ApplyEditorTheme()
    {
        var surfaceBrush = Brush.Parse("#10131A");
        var primaryTextBrush = Brush.Parse("#E1E2EC");
        var subtleTextBrush = Brush.Parse("#565B68");
        var primaryBrush = Brush.Parse("#ADC6FF");
        var selectionBrush = Brush.Parse("#2E4F8E");

        NoteEditor.Background = surfaceBrush;
        NoteEditor.Foreground = primaryTextBrush;
        NoteEditor.LineNumbersForeground = subtleTextBrush;
        NoteEditor.TextArea.Background = surfaceBrush;
        NoteEditor.TextArea.Foreground = primaryTextBrush;
        NoteEditor.TextArea.CaretBrush = primaryBrush;
        NoteEditor.TextArea.SelectionBrush = selectionBrush;
        NoteEditor.TextArea.SelectionForeground = primaryTextBrush;
        NoteEditor.TextArea.TextView.LinkTextForegroundBrush = primaryBrush;
        NoteEditor.TextArea.TextView.LinkTextBackgroundBrush = Brushes.Transparent;
        NoteEditor.TextArea.TextView.NonPrintableCharacterBrush = subtleTextBrush;
        NoteEditor.TextArea.TextView.CurrentLineBackground = Brush.Parse("#191B23");
    }

    private HotkeyGesture? TryParseOpenNoteGesture(string gesture)
    {
        try
        {
            return HotkeyGesture.Parse(gesture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            _logger.LogError(ex, "Configured open-note hotkey {Gesture} is invalid.", gesture);
            return null;
        }
    }

    private bool IsOpenNoteGesture(KeyEventArgs e)
    {
        if (_openNoteGesture is null)
        {
            return false;
        }

        return ToHotkeyModifiers(e.KeyModifiers) == _openNoteGesture.Modifiers
            && string.Equals(NormalizeAvaloniaKey(e.Key), _openNoteGesture.Key, StringComparison.Ordinal);
    }

    private static HotkeyModifiers ToHotkeyModifiers(KeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;
        if ((modifiers & KeyModifiers.Alt) == KeyModifiers.Alt)
        {
            result |= HotkeyModifiers.Alt;
        }

        if ((modifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            result |= HotkeyModifiers.Control;
        }

        if ((modifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            result |= HotkeyModifiers.Shift;
        }

        if ((modifiers & KeyModifiers.Meta) == KeyModifiers.Meta)
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }

    private static string NormalizeAvaloniaKey(Key key)
    {
        var text = key.ToString().ToUpperInvariant();
        return text.Length == 2 && text[0] == 'D' && char.IsDigit(text[1])
            ? text[1].ToString()
            : text;
    }

    private static bool IsCommandModifier(KeyModifiers modifiers)
    {
        return modifiers == KeyModifiers.Control || modifiers == KeyModifiers.Meta;
    }

    private static bool IsUnderPath(string directory, string filePath)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(filePath));
        return relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relativePath);
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private sealed record DefaultDependencies(
        NoteyOptions Options,
        INoteDraftStore NoteDraftStore,
        IVaultWorkspace VaultWorkspace,
        IDocumentStoreIndex DocumentStoreIndex,
        IVaultEntityStore VaultEntityStore,
        IScreenSnipService ScreenSnipService,
        ITesseractOcrEngine OcrEngine,
        ObsidianLinkBuilder LinkBuilder,
        DraftProcessingService DraftProcessingService);

    private sealed record SlashCommandDefinition(string Name, string Description, string InsertionText);

    private sealed record CompletionSuggestion(
        string DisplayText,
        string InsertionText,
        int ReplacementStart,
        int ReplacementLength,
        CompletionKind Kind,
        string? Payload = null)
    {
        public override string ToString()
        {
            return DisplayText;
        }
    }

    private sealed record DraftProcessOutcome(bool Succeeded, bool DraftChanged, string? PrimaryFinalNotePath)
    {
        public static DraftProcessOutcome Failure { get; } = new(false, false, null);

        public static DraftProcessOutcome NoChange { get; } = new(true, false, null);

        public static DraftProcessOutcome Changed(string? primaryFinalNotePath) => new(true, true, primaryFinalNotePath);
    }

    private enum CompletionKind
    {
        Text,
        Person,
        CreatePerson
    }

    private enum ProcessTrigger
    {
        NewNote,
        OpenRecent,
        Idle,
        Close
    }

    private sealed class FallbackHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
