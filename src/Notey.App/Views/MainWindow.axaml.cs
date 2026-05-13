using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls;
using Notey.AI.Providers;
using Notey.App.Configuration;
using Notey.App.Editing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.Capture.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Core.Platform;
using Notey.PipelineSteps;
using Notey.Pipelines.Catalog;
using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;
using Notey.Pipelines.Progress;
using Notey.Pipelines.Registry;
using Notey.Pipelines.Validation;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ResumeLookback = TimeSpan.FromDays(7);

    private readonly INoteDraftStore _noteDraftStore;
    private readonly IVaultWorkspace _vaultWorkspace;
    private readonly IVaultEntityStore _vaultEntityStore;
    private readonly IScreenSnipService _screenSnipService;
    private readonly ObsidianLinkBuilder _linkBuilder;
    private readonly PipelineCatalog _pipelineCatalog;
    private readonly PipelineExecutor _pipelineExecutor;
    private readonly NoteyOptions _options;
    private readonly NoteySettingsStore _settingsStore;
    private readonly NoteMetadataFormatter _metadataFormatter = new();
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly CancellationTokenSource _windowClosed = new();
    private readonly SemaphoreSlim _autosaveGate = new(1, 1);
    private readonly ImagePreviewMargin _imagePreviewMargin = new();
    private NoteDraft? _currentDraft;
    private bool _isInitializing;
    private bool _isOpeningInitialDraft;
    private bool _isCloseConfirmed;
    private bool _isClosePending;
    private bool _isExitRequested;
    private bool _isSwitchingDraft;
    private bool _isCaptureInProgress;
    private bool _isOrganizationInProgress;
    private bool _metadataDirty;
    private long _organizationRevision;
    private string _lastSavedText = string.Empty;
    private HotkeyGesture? _openNoteGesture;
    private IReadOnlyList<VaultEntity> _peopleIndex = [];
    private IReadOnlyList<PersonAutocompleteSuggestion> _personSuggestions = [];

    public bool HideInsteadOfClose { get; set; }

    public bool IsCaptureInProgress => _isCaptureInProgress;

    public event EventHandler? SettingsSaved;

    public MainWindow()
        : this(CreateDefaultDependencies(), TimeProvider.System, NullLogger<MainWindow>.Instance)
    {
    }

    private MainWindow(
        (
            NoteyOptions Options,
            INoteDraftStore NoteDraftStore,
            IVaultWorkspace VaultWorkspace,
            IVaultEntityStore VaultEntityStore,
            IScreenSnipService ScreenSnipService,
            ObsidianLinkBuilder LinkBuilder,
            PipelineCatalog PipelineCatalog,
            PipelineExecutor PipelineExecutor) dependencies,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger)
        : this(
            dependencies.Options,
            dependencies.NoteDraftStore,
            dependencies.VaultWorkspace,
            dependencies.VaultEntityStore,
            dependencies.ScreenSnipService,
            dependencies.LinkBuilder,
            dependencies.PipelineCatalog,
            dependencies.PipelineExecutor,
            timeProvider,
            logger)
    {
    }

    public MainWindow(
        NoteyOptions options,
        INoteDraftStore noteDraftStore,
        IVaultWorkspace vaultWorkspace,
        IVaultEntityStore vaultEntityStore,
        IScreenSnipService screenSnipService,
        ObsidianLinkBuilder linkBuilder,
        PipelineCatalog pipelineCatalog,
        PipelineExecutor pipelineExecutor,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger,
        NoteySettingsStore? settingsStore = null)
    {
        InitializeComponent();

        _options = options;
        _noteDraftStore = noteDraftStore;
        _vaultWorkspace = vaultWorkspace;
        _vaultEntityStore = vaultEntityStore;
        _screenSnipService = screenSnipService;
        _linkBuilder = linkBuilder;
        _pipelineCatalog = pipelineCatalog;
        _pipelineExecutor = pipelineExecutor;
        _timeProvider = timeProvider;
        _logger = logger;
        _settingsStore = settingsStore ?? CreateFallbackSettingsStore(options);
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _autosaveTimer.Tick += AutosaveTimerOnTick;
        _imagePreviewMargin.PreviewRequested += OnImagePreviewRequested;

        Width = options.Ui.DefaultWindowWidth;
        Height = options.Ui.DefaultWindowHeight;
        _openNoteGesture = TryParseOpenNoteGesture(options.Hotkeys.OpenNote);

        ConfigureEditor();
        ConfigureMetadataInputs();
        ConfigureShellCommands();
        UpdateEditorStatus();
        UpdateMetadataChips();

        Opened += async (_, _) => await OpenInitialDraftAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _windowClosed.Cancel();
            _windowClosed.Dispose();
            _autosaveGate.Dispose();
        };

        logger.LogInformation("Notey shell initialized with {Theme} theme.", options.Ui.Theme);
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

    private static (
        NoteyOptions Options,
        INoteDraftStore NoteDraftStore,
        IVaultWorkspace VaultWorkspace,
        IVaultEntityStore VaultEntityStore,
        IScreenSnipService ScreenSnipService,
        ObsidianLinkBuilder LinkBuilder,
        PipelineCatalog PipelineCatalog,
        PipelineExecutor PipelineExecutor) CreateDefaultDependencies()
    {
        var options = new NoteyOptions();
        var workspace = new FileSystemVaultWorkspace(options);
        var linkBuilder = new ObsidianLinkBuilder(workspace);
        var registry = new PipelineStepRegistry([]);
        var validator = new PipelineValidator(registry);
        var pipelineCatalog = new PipelineCatalog(
            new FilePipelineDefinitionSource(options.Pipelines.DefinitionFilePath),
            validator);

        return (options, new FileSystemNoteDraftStore(
            workspace,
            new NoteTemplateFactory(),
            new NoteFileNameGenerator()),
            workspace,
            new FileSystemVaultEntityStore(workspace, linkBuilder, TimeProvider.System),
            new UnavailableScreenSnipService(),
            linkBuilder,
            pipelineCatalog,
            new PipelineExecutor(registry, validator, TimeProvider.System));
    }

    private void ConfigureEditor()
    {
        NoteEditor.TextChanged += (_, _) =>
        {
            if (_isInitializing)
            {
                return;
            }

            _organizationRevision++;
            UpdatePersonAutocomplete();
            ScheduleAutosave();
        };

        NoteEditor.KeyDown += OnEditorKeyDown;
        NoteEditor.KeyUp += (_, _) =>
        {
            UpdateEditorStatus();
            UpdatePersonAutocomplete();
        };
        NoteEditor.PointerReleased += (_, _) =>
        {
            UpdateEditorStatus();
            UpdatePersonAutocomplete();
        };
        NoteEditor.Options.EnableHyperlinks = true;
        NoteEditor.Options.EnableEmailHyperlinks = true;
        ApplyEditorTheme();
        NoteEditor.TextArea.TextView.LineTransformers.Add(new MarkdownColorizingTransformer());
        NoteEditor.TextArea.LeftMargins.Insert(0, _imagePreviewMargin);
    }

    private void ApplyEditorTheme()
    {
        var primaryTextBrush = Brush.Parse("#E1E2EC");
        var subtleTextBrush = Brush.Parse("#565B68");
        var primaryBrush = Brush.Parse("#ADC6FF");
        var selectionBrush = Brush.Parse("#2E4F8E");
        var surfaceBrush = Brush.Parse("#10131A");

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

    private void ConfigureMetadataInputs()
    {
        PeopleInput.TextChanged += MetadataInputOnTextChanged;
        TopicsInput.TextChanged += MetadataInputOnTextChanged;
        ProjectsInput.TextChanged += MetadataInputOnTextChanged;
        TagsInput.TextChanged += MetadataInputOnTextChanged;
        ScreenshotContextInput.TextChanged += MetadataInputOnTextChanged;
        SuggestedPeopleInput.TextChanged += SuggestionInputOnTextChanged;
        SuggestedTopicsInput.TextChanged += SuggestionInputOnTextChanged;
        SuggestedProjectsInput.TextChanged += SuggestionInputOnTextChanged;
        SuggestedTagsInput.TextChanged += SuggestionInputOnTextChanged;
        PersonAutocompleteList.PointerReleased += async (_, _) => await InsertSelectedPersonLinkAsync();
        AcceptMetadataSuggestionsButton.Click += (_, _) => AcceptMetadataSuggestions();
    }

    private void ConfigureShellCommands()
    {
        NewNoteButton.Click += async (_, _) => await StartNewNoteAsync();
        OpenRecentNoteButton.Click += async (_, _) => await OpenRecentOrCreateAsync(forceChoice: true);
        CaptureAnalyzeButton.Click += async (_, _) => await CaptureScreenshotAsync(ScreenSnipMode.AnalyzeWithAi);
        CaptureSaveButton.Click += async (_, _) => await CaptureScreenshotAsync(ScreenSnipMode.SaveOnly);
        ImproveMarkdownButton.Click += async (_, _) => await ImproveMarkdownAsync();
        SaveNoteButton.Click += async (_, _) =>
        {
            if (await FlushAutosaveAsync())
            {
                AutosaveStatusText.Text = "SAVED";
            }
        };
        SettingsButton.Click += async (_, _) => await OpenSettingsAsync();
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
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Settings save was cancelled because the window closed.");
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SETTINGS ERROR";
            _logger.LogError(ex, "Failed to write local settings.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SETTINGS ERROR";
            _logger.LogError(ex, "Notey does not have permission to write local settings.");
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SETTINGS INVALID";
            _logger.LogError(ex, "Invalid settings prevented saving.");
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "SETTINGS INVALID";
            _logger.LogError(ex, "Invalid settings prevented saving.");
        }
    }

    public async Task ActivateOrResumeAsync()
    {
        if (_currentDraft is null)
        {
            await OpenRecentOrCreateAsync(forceChoice: false);
            return;
        }

        FocusEditor();
    }

    public async Task StartNewNoteAsync()
    {
        if (_isSwitchingDraft || !await FlushAutosaveAsync())
        {
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

    private async Task CaptureScreenshotAsync(ScreenSnipMode mode)
    {
        if (_isCaptureInProgress)
        {
            AutosaveStatusText.Text = "SNIP ACTIVE";
            return;
        }

        _isCaptureInProgress = true;
        CaptureAnalyzeButton.IsEnabled = false;
        CaptureSaveButton.IsEnabled = false;

        try
        {
            await CaptureScreenshotCoreAsync(mode);
        }
        finally
        {
            _isCaptureInProgress = false;
            CaptureAnalyzeButton.IsEnabled = true;
            CaptureSaveButton.IsEnabled = true;
        }
    }

    private async Task ImproveMarkdownAsync()
    {
        if (_isOrganizationInProgress)
        {
            AutosaveStatusText.Text = "AI IMPROVE ACTIVE";
            return;
        }

        _isOrganizationInProgress = true;
        ImproveMarkdownButton.IsEnabled = false;

        try
        {
            await ImproveMarkdownCoreAsync();
        }
        finally
        {
            _isOrganizationInProgress = false;
            ImproveMarkdownButton.IsEnabled = true;
        }
    }

    private async Task ImproveMarkdownCoreAsync()
    {
        if (_currentDraft is null)
        {
            if (!await TryCreateAndLoadDraftAsync("preparing markdown improvement"))
            {
                return;
            }
        }

        var targetDraft = _currentDraft;
        if (targetDraft is null || !await FlushAutosaveAsync())
        {
            return;
        }

        var metadataSnapshot = GetMetadataInputSnapshot();
        var organizationInput = NoteOrganizationMarkdown.BuildOrganizationInput(
            NoteEditor.Document.Text,
            GetMetadataNames(metadataSnapshot.People, TrimPersonToken),
            GetMetadataNames(metadataSnapshot.Topics, TrimTopicToken),
            GetMetadataNames(metadataSnapshot.Projects, TrimProjectToken),
            GetMetadataNames(metadataSnapshot.Tags, TrimTagToken),
            out var userAuthoredBody);
        if (string.IsNullOrWhiteSpace(userAuthoredBody))
        {
            AutosaveStatusText.Text = "NO NOTE TEXT";
            return;
        }

        var organizationRevision = _organizationRevision;
        PipelineDefinition? pipeline;
        try
        {
            var compatiblePipelines = await _pipelineCatalog.GetEnabledCompatibleAsync(PipelineDataType.TextData, _windowClosed.Token);
            if (compatiblePipelines.Count == 0)
            {
                AutosaveStatusText.Text = "NO TEXT PIPELINE";
                return;
            }

            pipeline = compatiblePipelines.Count == 1
                ? compatiblePipelines[0]
                : await PipelineChoiceWindow.ShowAsync(this, compatiblePipelines);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Organization pipeline discovery was cancelled because the window closed.");
            return;
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Failed to load organization pipeline definitions.");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Notey does not have permission to read organization pipeline definitions.");
            return;
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Pipeline configuration prevented note organization.");
            return;
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Invalid pipeline configuration prevented note organization.");
            return;
        }

        if (pipeline is null)
        {
            AutosaveStatusText.Text = "PIPELINE CANCELLED";
            return;
        }

        await RunOrganizationPipelineAsync(pipeline, targetDraft, organizationRevision, organizationInput);
    }

    private async Task RunOrganizationPipelineAsync(
        PipelineDefinition pipeline,
        NoteDraft targetDraft,
        long organizationRevision,
        string organizationInput)
    {
        if (!IsCurrentDraft(targetDraft) || organizationRevision != _organizationRevision)
        {
            AutosaveStatusText.Text = "PIPELINE SKIPPED";
            return;
        }

        var context = new PipelineContext(pipeline.Id, _timeProvider.GetUtcNow());
        context.SetValue("note.filePath", targetDraft.FilePath);
        context.SetValue("note.organization.mode", "manual");
        var progress = new Progress<PipelineProgressUpdate>(update => UpdatePipelineStatus(update, pipeline));

        try
        {
            AutosaveStatusText.Text = $"PIPELINE {FormatPipelineName(pipeline).ToUpperInvariant()}";
            var result = await _pipelineExecutor.ExecuteAsync(
                pipeline,
                new TextData(organizationInput, targetDraft.FilePath),
                context,
                progress,
                _windowClosed.Token);
            await ApplyOrganizationPipelineResultAsync(result, targetDraft, organizationRevision);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Organization pipeline {PipelineId} was cancelled because the window closed.", pipeline.Id);
        }
        catch (OperationCanceledException)
        {
            AutosaveStatusText.Text = "PIPELINE CANCELLED";
        }
        catch (PipelineValidationException ex)
        {
            AutosaveStatusText.Text = "PIPELINE INVALID";
            _logger.LogError(ex, "Organization pipeline {PipelineId} is invalid.", pipeline.Id);
        }
        catch (PipelineExecutionException ex)
        {
            AutosaveStatusText.Text = "PIPELINE FAILED";
            _logger.LogError(ex, "Organization pipeline {PipelineId} failed.", pipeline.Id);
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "PIPELINE FAILED";
            _logger.LogError(ex, "Organization pipeline {PipelineId} could not be executed.", pipeline.Id);
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "PIPELINE FAILED";
            _logger.LogError(ex, "Organization pipeline {PipelineId} received invalid configuration.", pipeline.Id);
        }
    }

    private async Task CaptureScreenshotCoreAsync(ScreenSnipMode mode)
    {
        if (_currentDraft is null)
        {
            if (!await TryCreateAndLoadDraftAsync("capturing a screenshot"))
            {
                return;
            }
        }

        var targetDraft = _currentDraft;
        if (targetDraft is null || !await FlushAutosaveAsync())
        {
            return;
        }

        ScreenSnipResult snip;
        var shouldRestoreWindow = IsVisible;

        try
        {
            AutosaveStatusText.Text = "SNIP SELECT";
            if (shouldRestoreWindow)
            {
                Hide();
                await Task.Delay(TimeSpan.FromMilliseconds(150), _windowClosed.Token);
            }

            snip = await _screenSnipService.CaptureAsync(mode, _windowClosed.Token);
            if (_windowClosed.IsCancellationRequested)
            {
                return;
            }
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Screen snip was cancelled because the window closed.");
            return;
        }
        catch (OperationCanceledException)
        {
            AutosaveStatusText.Text = "SNIP CANCELLED";
            return;
        }
        catch (PlatformNotSupportedException ex)
        {
            AutosaveStatusText.Text = "SNIP UNAVAILABLE";
            _logger.LogWarning(ex, "Screen snipping is unavailable on this platform.");
            return;
        }
        catch (Win32Exception ex)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Windows screen capture failed.");
            return;
        }
        catch (ExternalException ex)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Screen snip image encoding failed.");
            return;
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Failed to save screen snip.");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Notey does not have permission to save screen snips.");
            return;
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Vault configuration prevented screen snip capture.");
            return;
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "SNIP ERROR";
            _logger.LogError(ex, "Invalid vault path prevented screen snip capture.");
            return;
        }
        finally
        {
            if (shouldRestoreWindow && !_windowClosed.IsCancellationRequested)
            {
                Show();
                Activate();
                FocusEditor();
            }
        }

        if (!IsCurrentDraft(targetDraft))
        {
            AutosaveStatusText.Text = "SNIP SKIPPED";
            _logger.LogInformation(
                "Skipped applying screen snip {FilePath} because the active draft changed.",
                snip.FilePath);
            return;
        }

        var embed = InsertScreenshotReference(snip);
        AutosaveStatusText.Text = "SNIP SAVED";

        if (mode == ScreenSnipMode.AnalyzeWithAi)
        {
            await ChooseAndRunScreenshotPipelineAsync(snip, embed, targetDraft);
        }
    }

    private string InsertScreenshotReference(ScreenSnipResult snip)
    {
        var embed = _linkBuilder.BuildImageEmbed(snip.FilePath);
        AppendMarkdownBlock($"## Screenshot {snip.CapturedAt:HH:mm}\n\n{embed}");
        AddScreenshotContextLines(
        [
            $"Screenshot: {embed}",
            $"Captured: {snip.CapturedAt:O}",
            $"Dimensions: {snip.Width}x{snip.Height}",
            $"Mode: {FormatScreenSnipMode(snip.Mode)}"
        ]);

        return embed;
    }

    private async Task ChooseAndRunScreenshotPipelineAsync(ScreenSnipResult snip, string embed, NoteDraft targetDraft)
    {
        IReadOnlyList<PipelineDefinition> compatiblePipelines;
        try
        {
            compatiblePipelines = await _pipelineCatalog.GetEnabledCompatibleAsync(PipelineDataType.ImageData, _windowClosed.Token);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Pipeline discovery was cancelled because the window closed.");
            return;
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Failed to load screenshot pipeline definitions.");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Notey does not have permission to read screenshot pipeline definitions.");
            return;
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Pipeline configuration prevented screenshot analysis.");
            return;
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "PIPELINE ERROR";
            _logger.LogError(ex, "Invalid pipeline configuration prevented screenshot analysis.");
            return;
        }

        if (compatiblePipelines.Count == 0)
        {
            if (IsCurrentDraft(targetDraft))
            {
                AddScreenshotContextLines(["Pipeline: no enabled image pipeline configured"]);
            }

            AutosaveStatusText.Text = "NO IMAGE PIPELINE";
            return;
        }

        var pipeline = compatiblePipelines.Count == 1
            ? compatiblePipelines[0]
            : await PipelineChoiceWindow.ShowAsync(this, compatiblePipelines);
        if (pipeline is null)
        {
            AutosaveStatusText.Text = "PIPELINE CANCELLED";
            return;
        }

        await RunScreenshotPipelineAsync(snip, embed, pipeline, targetDraft);
    }

    private async Task RunScreenshotPipelineAsync(
        ScreenSnipResult snip,
        string embed,
        PipelineDefinition pipeline,
        NoteDraft targetDraft)
    {
        var context = new PipelineContext(pipeline.Id, _timeProvider.GetUtcNow());
        context.SetValue("screenshot.filePath", snip.FilePath);
        context.SetValue("screenshot.embed", embed);
        context.SetValue("screenshot.capturedAt", snip.CapturedAt);
        context.SetValue("screenshot.width", snip.Width);
        context.SetValue("screenshot.height", snip.Height);

        var progress = new Progress<PipelineProgressUpdate>(update => UpdatePipelineStatus(update, pipeline));
        var input = new ImageData(snip.FilePath, snip.CapturedAt, snip.Width, snip.Height);

        try
        {
            AutosaveStatusText.Text = $"PIPELINE {FormatPipelineName(pipeline).ToUpperInvariant()}";
            var result = await _pipelineExecutor.ExecuteAsync(pipeline, input, context, progress, _windowClosed.Token);
            await ApplyPipelineResultAsync(result, snip, embed, targetDraft);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Screenshot pipeline {PipelineId} was cancelled because the window closed.", pipeline.Id);
        }
        catch (OperationCanceledException)
        {
            AutosaveStatusText.Text = "PIPELINE CANCELLED";
        }
        catch (PipelineValidationException ex)
        {
            AutosaveStatusText.Text = "PIPELINE INVALID";
            _logger.LogError(ex, "Screenshot pipeline {PipelineId} is invalid.", pipeline.Id);
            AddPipelineErrorContextIfCurrent(targetDraft, ex.Message);
        }
        catch (PipelineExecutionException ex)
        {
            AutosaveStatusText.Text = "PIPELINE FAILED";
            _logger.LogError(ex, "Screenshot pipeline {PipelineId} failed.", pipeline.Id);
            AddPipelineErrorContextIfCurrent(targetDraft, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "PIPELINE FAILED";
            _logger.LogError(ex, "Screenshot pipeline {PipelineId} could not be executed.", pipeline.Id);
            AddPipelineErrorContextIfCurrent(targetDraft, ex.Message);
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "PIPELINE FAILED";
            _logger.LogError(ex, "Screenshot pipeline {PipelineId} received invalid configuration.", pipeline.Id);
            AddPipelineErrorContextIfCurrent(targetDraft, ex.Message);
        }
    }

    private async Task ApplyPipelineResultAsync(
        PipelineExecutionResult result,
        ScreenSnipResult snip,
        string embed,
        NoteDraft targetDraft)
    {
        await _autosaveGate.WaitAsync(_windowClosed.Token);
        try
        {
            if (!IsCurrentDraft(targetDraft))
            {
                AutosaveStatusText.Text = "PIPELINE SKIPPED";
                _logger.LogInformation(
                    "Skipped applying pipeline {PipelineId} for screen snip {FilePath} because the active draft changed.",
                    result.Pipeline.Id,
                    snip.FilePath);
                return;
            }

            AddScreenshotContextLines(BuildPipelineContextLines(result, embed));
            ApplyPipelineOutput(result.Output, result.Pipeline);
        }
        finally
        {
            _autosaveGate.Release();
        }

        ScheduleAutosave();
        AutosaveStatusText.Text = "PIPELINE DONE";
    }

    private void ApplyPipelineOutput(PipelineData output, PipelineDefinition pipeline)
    {
        switch (output)
        {
            case MarkdownContent markdownContent:
                AppendMarkdownBlock(markdownContent.Markdown);
                break;
            case StructuredNoteData structuredNoteData:
                ApplyStructuredNoteData(structuredNoteData, pipeline);
                break;
        }
    }

    private async Task ApplyOrganizationPipelineResultAsync(
        PipelineExecutionResult result,
        NoteDraft targetDraft,
        long organizationRevision)
    {
        await _autosaveGate.WaitAsync(_windowClosed.Token);
        try
        {
            if (!IsCurrentDraft(targetDraft) || organizationRevision != _organizationRevision)
            {
                AutosaveStatusText.Text = "PIPELINE SKIPPED";
                _logger.LogInformation(
                    "Skipped applying organization pipeline {PipelineId} because the active draft or note content changed.",
                    result.Pipeline.Id);
                return;
            }

            ApplyOrganizationPipelineOutput(result.Output, result.Pipeline);
        }
        finally
        {
            _autosaveGate.Release();
        }

        ScheduleAutosave();
        AutosaveStatusText.Text = "AI SUGGESTIONS READY";
    }

    private void ApplyOrganizationPipelineOutput(PipelineData output, PipelineDefinition pipeline)
    {
        switch (output)
        {
            case StructuredNoteData structuredNoteData:
                ApplyOrganizationStructuredNoteData(structuredNoteData);
                break;
            case MarkdownContent markdownContent:
                ApplyOrganizationMarkdownContent(markdownContent, pipeline);
                break;
        }
    }

    private void ApplyOrganizationStructuredNoteData(StructuredNoteData data)
    {
        AddStructuredNoteSuggestions(data);
        var cleanupBlock = NoteOrganizationMarkdown.RenderCleanupBlock(data, "AI cleaned summary");
        if (!string.IsNullOrWhiteSpace(cleanupBlock))
        {
            NoteEditor.Document.Text = NoteOrganizationMarkdown.ReplaceCleanupBlock(NoteEditor.Document.Text, cleanupBlock);
            NoteEditor.CaretOffset = NoteEditor.Document.TextLength;
            UpdateEditorStatus();
        }
    }

    private void ApplyOrganizationMarkdownContent(MarkdownContent markdownContent, PipelineDefinition pipeline)
    {
        if (string.IsNullOrWhiteSpace(markdownContent.Markdown))
        {
            return;
        }

        var trimmed = markdownContent.Markdown.Trim();
        var hasCompleteCleanupBlock =
            trimmed.Contains(NoteOrganizationMarkdown.CleanupStartMarker, StringComparison.Ordinal)
            && trimmed.Contains(NoteOrganizationMarkdown.CleanupEndMarker, StringComparison.Ordinal);
        var cleanupBlock = hasCompleteCleanupBlock
            ? trimmed
            : string.Join('\n',
                NoteOrganizationMarkdown.CleanupStartMarker,
                $"## {FormatPipelineName(pipeline)}",
                string.Empty,
                trimmed,
                NoteOrganizationMarkdown.CleanupEndMarker);

        NoteEditor.Document.Text = NoteOrganizationMarkdown.ReplaceCleanupBlock(NoteEditor.Document.Text, cleanupBlock);
        NoteEditor.CaretOffset = NoteEditor.Document.TextLength;
        UpdateEditorStatus();
    }

    private void ApplyStructuredNoteData(StructuredNoteData data, PipelineDefinition pipeline)
    {
        AddStructuredNoteSuggestions(data);

        var markdown = BuildStructuredNoteMarkdown(data, pipeline);
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            AppendMarkdownBlock(markdown);
        }
    }

    private void AddStructuredNoteSuggestions(StructuredNoteData data)
    {
        SuggestedPeopleInput.Text = MetadataInputMerger.Merge(
            SuggestedPeopleInput.Text ?? string.Empty,
            data.People?.Select(static entity => entity.Name) ?? [],
            TrimPersonToken);
        SuggestedTopicsInput.Text = MetadataInputMerger.Merge(
            SuggestedTopicsInput.Text ?? string.Empty,
            data.Topics?.Select(static entity => entity.Name) ?? [],
            TrimTopicToken);
        SuggestedProjectsInput.Text = MetadataInputMerger.Merge(
            SuggestedProjectsInput.Text ?? string.Empty,
            data.Projects?.Select(static entity => entity.Name) ?? [],
            TrimProjectToken);
        SuggestedTagsInput.Text = MetadataInputMerger.Merge(
            SuggestedTagsInput.Text ?? string.Empty,
            data.Tags?.Select(static tag => $"#{tag.Trim().TrimStart('#')}") ?? [],
            TrimTagToken);

        if (HasMetadataSuggestions())
        {
            MetadataToggle.IsChecked = true;
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

    private void AddScreenshotContextLines(IEnumerable<string> lines)
    {
        var merged = GetScreenshotContext(ScreenshotContextInput.Text)
            .Concat(lines)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ScreenshotContextInput.Text = string.Join('\n', merged);
    }

    private void AcceptMetadataSuggestions()
    {
        if (!HasMetadataSuggestions())
        {
            AutosaveStatusText.Text = "NO AI SUGGESTIONS";
            return;
        }

        PeopleInput.Text = MetadataInputMerger.Merge(
            PeopleInput.Text ?? string.Empty,
            GetMetadataNames(SuggestedPeopleInput.Text, TrimPersonToken),
            TrimPersonToken);
        TopicsInput.Text = MetadataInputMerger.Merge(
            TopicsInput.Text ?? string.Empty,
            GetMetadataNames(SuggestedTopicsInput.Text, TrimTopicToken),
            TrimTopicToken);
        ProjectsInput.Text = MetadataInputMerger.Merge(
            ProjectsInput.Text ?? string.Empty,
            GetMetadataNames(SuggestedProjectsInput.Text, TrimProjectToken),
            TrimProjectToken);
        TagsInput.Text = MetadataInputMerger.Merge(
            TagsInput.Text ?? string.Empty,
            GetMetadataNames(SuggestedTagsInput.Text, TrimTagToken),
            TrimTagToken);

        SuggestedPeopleInput.Text = string.Empty;
        SuggestedTopicsInput.Text = string.Empty;
        SuggestedProjectsInput.Text = string.Empty;
        SuggestedTagsInput.Text = string.Empty;
        _metadataDirty = true;
        _organizationRevision++;
        UpdateMetadataChips();
        ScheduleAutosave();
        AutosaveStatusText.Text = "AI SUGGESTIONS ACCEPTED";
    }

    private bool HasMetadataSuggestions()
    {
        return !string.IsNullOrWhiteSpace(SuggestedPeopleInput.Text)
            || !string.IsNullOrWhiteSpace(SuggestedTopicsInput.Text)
            || !string.IsNullOrWhiteSpace(SuggestedProjectsInput.Text)
            || !string.IsNullOrWhiteSpace(SuggestedTagsInput.Text);
    }

    private IReadOnlyList<string> BuildPipelineContextLines(PipelineExecutionResult result, string embed)
    {
        var lines = new List<string>
        {
            $"Pipeline: {FormatPipelineName(result.Pipeline)} ({result.Pipeline.Id})",
            $"Pipeline source: {embed}"
        };

        if (result.Output is StructuredNoteData structuredNoteData
            && !string.IsNullOrWhiteSpace(structuredNoteData.Summary))
        {
            lines.Add($"Pipeline summary: {structuredNoteData.Summary.Trim()}");
        }

        lines.AddRange(result.Context.Warnings.Select(static warning => $"Pipeline warning: {warning.Message}"));
        return lines;
    }

    private static string BuildStructuredNoteMarkdown(StructuredNoteData data, PipelineDefinition pipeline)
    {
        var lines = new List<string> { $"## Screenshot analysis - {FormatPipelineName(pipeline)}" };

        if (!string.IsNullOrWhiteSpace(data.MeetingTitle))
        {
            lines.Add(string.Empty);
            lines.Add($"- Meeting title: {data.MeetingTitle.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(data.Summary))
        {
            lines.Add(string.Empty);
            lines.Add(data.Summary.Trim());
        }

        if (data.Sections is not null)
        {
            foreach (var (heading, body) in data.Sections)
            {
                if (string.IsNullOrWhiteSpace(heading) || string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                lines.Add(string.Empty);
                lines.Add($"### {heading.Trim()}");
                lines.Add(body.Trim());
            }
        }

        return lines.Count == 1 ? string.Empty : string.Join('\n', lines);
    }

    private void UpdatePipelineStatus(PipelineProgressUpdate update, PipelineDefinition pipeline)
    {
        AutosaveStatusText.Text = update.Status switch
        {
            PipelineProgressStatus.Started => $"PIPELINE {FormatPipelineName(pipeline).ToUpperInvariant()}",
            PipelineProgressStatus.StepStarted => $"PIPELINE {update.StepId?.ToUpperInvariant() ?? "STEP"}",
            PipelineProgressStatus.StepCompleted => $"PIPELINE {update.CompletedSteps}/{update.TotalSteps}",
            PipelineProgressStatus.Completed => "PIPELINE DONE",
            PipelineProgressStatus.Cancelled => "PIPELINE CANCELLED",
            PipelineProgressStatus.Failed => "PIPELINE FAILED",
            _ => AutosaveStatusText.Text
        };
    }

    private bool IsCurrentDraft(NoteDraft draft)
    {
        return _currentDraft is not null
            && string.Equals(_currentDraft.FilePath, draft.FilePath, StringComparison.Ordinal);
    }

    private void AddPipelineErrorContextIfCurrent(NoteDraft targetDraft, string message)
    {
        if (IsCurrentDraft(targetDraft))
        {
            AddScreenshotContextLines([$"Pipeline error: {message}"]);
        }
    }

    private static string FormatPipelineName(PipelineDefinition pipeline)
    {
        return string.IsNullOrWhiteSpace(pipeline.DisplayName) ? pipeline.Id : pipeline.DisplayName;
    }

    private static string FormatScreenSnipMode(ScreenSnipMode mode)
    {
        return mode switch
        {
            ScreenSnipMode.SaveOnly => "saved-only",
            ScreenSnipMode.AnalyzeWithAi => "pipeline-processed",
            _ => mode.ToString()
        };
    }

    private async Task OpenInitialDraftAsync()
    {
        if (_currentDraft is not null || _isOpeningInitialDraft)
        {
            return;
        }

        _isOpeningInitialDraft = true;
        try
        {
            await OpenRecentOrCreateAsync(forceChoice: false);
        }
        finally
        {
            _isOpeningInitialDraft = false;
        }
    }

    private async Task OpenRecentOrCreateAsync(bool forceChoice)
    {
        if (_isSwitchingDraft || !await FlushAutosaveAsync())
        {
            return;
        }

        try
        {
            var recentDrafts = await _noteDraftStore.ListRecentAsync(
                _timeProvider.GetLocalNow().Subtract(ResumeLookback),
                _windowClosed.Token);
            if (forceChoice && _currentDraft is not null)
            {
                recentDrafts = recentDrafts
                    .Where(draft => !string.Equals(draft.FilePath, _currentDraft.FilePath, StringComparison.Ordinal))
                    .ToArray();
            }

            if (recentDrafts.Count == 0)
            {
                if (forceChoice)
                {
                    var emptyChoice = await RecentNoteChoiceWindow.ShowAsync(this, []);
                    if (emptyChoice.Action == RecentNoteChoiceAction.NewNote)
                    {
                        await TryCreateAndLoadDraftAsync("starting a new note");
                    }

                    return;
                }

                await TryCreateAndLoadDraftAsync("opening the initial note");
                return;
            }

            var choice = await RecentNoteChoiceWindow.ShowAsync(this, recentDrafts);
            switch (choice.Action)
            {
                case RecentNoteChoiceAction.OpenExisting when choice.SelectedNote is not null:
                    await LoadDraftAsync(await _noteDraftStore.OpenAsync(choice.SelectedNote.FilePath, _windowClosed.Token));
                    break;
                case RecentNoteChoiceAction.NewNote:
                    await TryCreateAndLoadDraftAsync("starting a new note");
                    break;
                case RecentNoteChoiceAction.Cancel:
                    if (_currentDraft is null)
                    {
                        await TryCreateAndLoadDraftAsync("opening the initial note");
                    }

                    break;
            }
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Recent note workflow was cancelled because the window closed.");
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Failed to open or create a note draft.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey does not have permission to open or create a note draft.");
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration prevented note activation.");
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration contained an invalid value.");
        }
    }

    private async Task CreateAndLoadDraftAsync()
    {
        var draft = await _noteDraftStore.CreateAsync(_timeProvider.GetLocalNow(), _windowClosed.Token);
        await LoadDraftAsync(draft);
    }

    private async Task<bool> TryCreateAndLoadDraftAsync(string operation)
    {
        try
        {
            await CreateAndLoadDraftAsync();
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
        HidePersonAutocomplete();

        try
        {
            _currentDraft = draft;
            NoteEditor.Document.Text = draft.Content;
            NoteEditor.CaretOffset = NoteEditor.Document.TextLength;
            _lastSavedText = draft.Content;
            _metadataDirty = false;
            DateChipText.Text = draft.CreatedAt.ToString("ddd, HH:mm");
            PopulateMetadataInputsFromDraft(draft.Content);
            await RefreshPeopleIndexAsync();
            _isInitializing = false;
            _isSwitchingDraft = false;
            NoteEditor.IsReadOnly = false;
            AutosaveStatusText.Text = "SAVED";
            UpdateEditorStatus();
            UpdateMetadataChips();
            FocusEditor();
        }
        finally
        {
            _isInitializing = false;
            _isSwitchingDraft = false;
        }
    }

    private void PopulateMetadataInputsFromDraft(string content)
    {
        var (people, topics, projects, tags, screenshotContext) = NoteMetadataFormatter.ReadFrontmatterInputs(content);
        PeopleInput.Text = string.Join(", ", people);
        TopicsInput.Text = string.Join(", ", topics);
        ProjectsInput.Text = string.Join(", ", projects);
        TagsInput.Text = string.Join(", ", tags);
        ScreenshotContextInput.Text = string.Join("\n", screenshotContext);
        SuggestedPeopleInput.Text = string.Empty;
        SuggestedTopicsInput.Text = string.Empty;
        SuggestedProjectsInput.Text = string.Empty;
        SuggestedTagsInput.Text = string.Empty;
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
        NoteEditor.IsReadOnly = true;

        var saved = await FlushAutosaveAsync();
        if (!saved)
        {
            _isClosePending = false;
            NoteEditor.IsReadOnly = _currentDraft is null;
            return;
        }

        if (HideInsteadOfClose && !_isExitRequested)
        {
            _isClosePending = false;
            NoteEditor.IsReadOnly = false;
            Hide();
            return;
        }

        _isCloseConfirmed = true;
        Close();
    }

    private async void AutosaveTimerOnTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        await FlushAutosaveAsync();
    }

    private async Task<bool> FlushAutosaveAsync()
    {
        if (_currentDraft is null)
        {
            return string.IsNullOrEmpty(NoteEditor.Document.Text) && !_metadataDirty;
        }

        var acquired = false;
        var savedSnapshot = false;

        try
        {
            await _autosaveGate.WaitAsync(_windowClosed.Token);
            acquired = true;

            var text = NoteEditor.Document.Text;
            if (string.Equals(text, _lastSavedText, StringComparison.Ordinal) && !_metadataDirty)
            {
                AutosaveStatusText.Text = "SAVED";
                return true;
            }

            AutosaveStatusText.Text = "SAVING";
            var metadataSnapshot = GetMetadataInputSnapshot();
            var metadata = await BuildPersistedMetadataAsync(metadataSnapshot, _windowClosed.Token);
            var persistedText = _metadataFormatter.Apply(text, metadata);
            await _noteDraftStore.SaveAsync(_currentDraft, persistedText, _windowClosed.Token);
            _lastSavedText = text;
            _metadataDirty = !metadataSnapshot.Equals(GetMetadataInputSnapshot());
            savedSnapshot = true;
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
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Invalid note metadata prevented autosave for draft {DraftPath}.", _currentDraft.FilePath);
            return false;
        }
        finally
        {
            if (acquired)
            {
                _autosaveGate.Release();
            }

            if (savedSnapshot
                && _currentDraft is not null
                && (_metadataDirty || !string.Equals(NoteEditor.Document.Text, _lastSavedText, StringComparison.Ordinal)))
            {
                AutosaveStatusText.Text = "UNSAVED CHANGES";
                _autosaveTimer.Stop();
                _autosaveTimer.Start();
            }
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsOpenNoteGesture(e))
        {
            _ = ActivateOrResumeAsync();
            e.Handled = true;
            return;
        }

        if (TryHandlePersonAutocompleteKey(e))
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

    private static bool IsCommandModifier(KeyModifiers modifiers)
    {
        return modifiers == KeyModifiers.Control || modifiers == KeyModifiers.Meta;
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

    private HotkeyGesture? TryParseOpenNoteGesture(string gesture)
    {
        try
        {
            return HotkeyGesture.Parse(gesture);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Configured open-note hotkey {Gesture} is invalid.", gesture);
            return null;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Configured open-note hotkey {Gesture} is invalid.", gesture);
            return null;
        }
    }

    private void ApplyEdit(MarkdownTextEdit edit)
    {
        NoteEditor.Document.Replace(edit.ReplacementStart, edit.ReplacementLength, edit.ReplacementText);
        NoteEditor.SelectionStart = edit.SelectionStart;
        NoteEditor.SelectionLength = edit.SelectionLength;
        NoteEditor.CaretOffset = edit.CaretOffset;
        UpdateEditorStatus();
    }

    private void MetadataInputOnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _metadataDirty = true;
        _organizationRevision++;
        UpdateMetadataChips();
        ScheduleAutosave();
    }

    private void SuggestionInputOnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _organizationRevision++;
    }

    private void ScheduleAutosave()
    {
        AutosaveStatusText.Text = "UNSAVED CHANGES";
        UpdateEditorStatus();
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private async Task RefreshPeopleIndexAsync()
    {
        try
        {
            _peopleIndex = await _vaultEntityStore.GetAllAsync(VaultEntityKind.Person, _windowClosed.Token);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("People index refresh was cancelled because the window closed.");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read people index from the configured vault.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Notey does not have permission to read the configured people folder.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Notey vault configuration prevented people autocomplete indexing.");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid vault path prevented people autocomplete indexing.");
        }
    }

    private bool TryHandlePersonAutocompleteKey(KeyEventArgs e)
    {
        if (!PersonAutocompletePanel.IsVisible)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            HidePersonAutocomplete();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Down)
        {
            MovePersonSelection(1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Up)
        {
            MovePersonSelection(-1);
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.Enter or Key.Tab)
        {
            _ = InsertSelectedPersonLinkAsync();
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void MovePersonSelection(int delta)
    {
        if (_personSuggestions.Count == 0)
        {
            return;
        }

        var selectedIndex = PersonAutocompleteList.SelectedIndex < 0 ? 0 : PersonAutocompleteList.SelectedIndex;
        PersonAutocompleteList.SelectedIndex = Math.Clamp(selectedIndex + delta, 0, _personSuggestions.Count - 1);
    }

    private void UpdatePersonAutocomplete()
    {
        if (NoteEditor.IsReadOnly)
        {
            HidePersonAutocomplete();
            return;
        }

        var query = PersonReferenceCompletionQuery.TryCreate(NoteEditor.Document.Text, NoteEditor.CaretOffset);
        if (query is null)
        {
            HidePersonAutocomplete();
            return;
        }

        _personSuggestions = BuildPersonSuggestions(query.SearchText);
        if (_personSuggestions.Count == 0)
        {
            HidePersonAutocomplete();
            return;
        }

        PersonAutocompleteList.ItemsSource = _personSuggestions;
        PersonAutocompleteList.SelectedIndex = 0;
        PersonAutocompletePanel.IsVisible = true;
    }

    private IReadOnlyList<PersonAutocompleteSuggestion> BuildPersonSuggestions(string searchText)
    {
        var normalizedSearch = searchText.Trim();
        var suggestions = _peopleIndex
            .Where(entity => PersonMatches(entity, normalizedSearch))
            .Take(8)
            .Select(static entity => new PersonAutocompleteSuggestion(entity.Name, false, entity))
            .ToList();

        if (normalizedSearch.Length >= 2 && !suggestions.Any(suggestion => PersonNameEquals(suggestion.Name, normalizedSearch)))
        {
            suggestions.Add(new PersonAutocompleteSuggestion(normalizedSearch, true, null));
        }

        return suggestions;
    }

    private static bool PersonMatches(VaultEntity entity, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return entity.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || entity.Aliases.Any(alias => alias.Contains(searchText, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PersonNameEquals(string left, string right)
    {
        return string.Equals(
            ObsidianLinkBuilder.NormalizeDisplayName(left),
            ObsidianLinkBuilder.NormalizeDisplayName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task InsertSelectedPersonLinkAsync()
    {
        if (PersonAutocompleteList.SelectedItem is not PersonAutocompleteSuggestion suggestion)
        {
            return;
        }

        var query = PersonReferenceCompletionQuery.TryCreate(NoteEditor.Document.Text, NoteEditor.CaretOffset);
        if (query is null)
        {
            HidePersonAutocomplete();
            return;
        }

        var expectedReferenceText = NoteEditor.Document.Text.Substring(query.ReplacementStart, query.ReplacementLength);

        try
        {
            var entity = suggestion.Entity ?? await _vaultEntityStore.EnsureAsync(VaultEntityKind.Person, suggestion.Name, _windowClosed.Token);
            query = PersonReferenceCompletionQuery.TryCreate(NoteEditor.Document.Text, NoteEditor.CaretOffset);
            if (query is null
                || query.ReplacementStart + query.ReplacementLength > NoteEditor.Document.TextLength
                || !string.Equals(NoteEditor.Document.Text.Substring(query.ReplacementStart, query.ReplacementLength), expectedReferenceText, StringComparison.Ordinal))
            {
                HidePersonAutocomplete();
                return;
            }

            var link = entity.ToWikiLink();

            NoteEditor.Document.Replace(query.ReplacementStart, query.ReplacementLength, link);
            NoteEditor.CaretOffset = query.ReplacementStart + link.Length;
            HidePersonAutocomplete();
            NoteEditor.Focus();

            if (suggestion.IsCreateAction)
            {
                await RefreshPeopleIndexAsync();
            }
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Person link insertion was cancelled because the window closed.");
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "LINK ERROR";
            _logger.LogError(ex, "Failed to create person document for {PersonName}.", suggestion.Name);
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "LINK ERROR";
            _logger.LogError(ex, "Notey does not have permission to create person document for {PersonName}.", suggestion.Name);
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "LINK ERROR";
            _logger.LogError(ex, "Vault configuration prevented person link insertion for {PersonName}.", suggestion.Name);
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "LINK ERROR";
            _logger.LogError(ex, "Invalid person reference {PersonName}.", suggestion.Name);
        }
    }

    private void HidePersonAutocomplete()
    {
        PersonAutocompletePanel.IsVisible = false;
        PersonAutocompleteList.ItemsSource = null;
        _personSuggestions = [];
    }

    private async Task<NoteMetadata> BuildPersistedMetadataAsync(MetadataInputSnapshot snapshot, CancellationToken cancellationToken)
    {
        var peopleLinks = await ResolveMetadataLinksAsync(snapshot.People, VaultEntityKind.Person, TrimPersonToken, cancellationToken);
        var topicLinks = await ResolveMetadataLinksAsync(snapshot.Topics, VaultEntityKind.Topic, TrimTopicToken, cancellationToken);
        var projectLinks = await ResolveMetadataLinksAsync(snapshot.Projects, VaultEntityKind.Project, TrimProjectToken, cancellationToken);
        var tags = GetMetadataNames(snapshot.Tags, TrimTagToken)
            .Select(static tag => $"#{tag.Trim().TrimStart('#')}")
            .ToArray();

        await RefreshPeopleIndexAsync();

        return new NoteMetadata(
            peopleLinks,
            topicLinks,
            projectLinks,
            tags,
            GetScreenshotContext(snapshot.ScreenshotContext));
    }

    private async Task<IReadOnlyList<string>> ResolveMetadataLinksAsync(
        string input,
        VaultEntityKind kind,
        Func<string, string> normalizer,
        CancellationToken cancellationToken)
    {
        var links = new List<string>();
        foreach (var name in GetMetadataNames(input, normalizer))
        {
            var entity = await _vaultEntityStore.EnsureAsync(kind, name, cancellationToken);
            links.Add(entity.ToWikiLink());
        }

        return links;
    }

    private void UpdateMetadataChips()
    {
        var metadataSnapshot = GetMetadataInputSnapshot();
        var people = GetMetadataNames(metadataSnapshot.People, TrimPersonToken);
        var topics = GetMetadataNames(metadataSnapshot.Topics, TrimTopicToken);
        var projects = GetMetadataNames(metadataSnapshot.Projects, TrimProjectToken);
        var tags = GetMetadataNames(metadataSnapshot.Tags, TrimTagToken);

        PeopleChipText.Text = FormatChip(people, "@person", static person => $"@{person}");
        TopicChipText.Text = FormatChip(topics, "#topic", static topic => $"#{topic}");
        TagsChipText.Text = FormatChip(tags, "#tag", static tag => $"#{tag.Trim().TrimStart('#')}");
        ProjectChipText.Text = FormatChip(projects, "[[Project]]", static project => $"[[{project}]]");
    }

    private MetadataInputSnapshot GetMetadataInputSnapshot()
    {
        return new MetadataInputSnapshot(
            PeopleInput.Text ?? string.Empty,
            TopicsInput.Text ?? string.Empty,
            ProjectsInput.Text ?? string.Empty,
            TagsInput.Text ?? string.Empty,
            ScreenshotContextInput.Text ?? string.Empty);
    }

    private static string FormatChip(IReadOnlyList<string> values, string placeholder, Func<string, string> formatter)
    {
        return values.Count switch
        {
            0 => placeholder,
            1 => formatter(values[0]),
            _ => $"{formatter(values[0])} +{values.Count - 1}"
        };
    }

    private static IReadOnlyList<string> GetMetadataNames(string? input, Func<string, string> normalizer)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(normalizer)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetScreenshotContext(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string TrimPersonToken(string value)
    {
        return TrimObsidianAlias(value).TrimStart('@').Trim();
    }

    private static string TrimTopicToken(string value)
    {
        return TrimObsidianAlias(value).TrimStart('#').Trim();
    }

    private static string TrimProjectToken(string value)
    {
        return TrimObsidianAlias(value).Trim();
    }

    private static string TrimTagToken(string value)
    {
        return TrimObsidianAlias(value).TrimStart('#').Trim();
    }

    private static string TrimObsidianAlias(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("[[", StringComparison.Ordinal) || !trimmed.EndsWith("]]", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var inner = trimmed[2..^2];
        var aliasSeparator = inner.LastIndexOf('|');
        if (aliasSeparator >= 0 && aliasSeparator + 1 < inner.Length)
        {
            return inner[(aliasSeparator + 1)..].Trim();
        }

        var pathSeparator = inner.LastIndexOf('/');
        return pathSeparator >= 0 && pathSeparator + 1 < inner.Length
            ? inner[(pathSeparator + 1)..].Trim()
            : inner.Trim();
    }

    private void UpdateEditorStatus()
    {
        var status = NoteEditorStatus.FromText(NoteEditor.Document.Text, NoteEditor.CaretOffset);
        WordCountText.Text = $"WORDS {status.WordCount}";
        CursorPositionText.Text = $"LINE {status.Line}, COL {status.Column}";
    }

    private void FocusEditor()
    {
        NoteEditor.Focus();
    }

    private async void OnImagePreviewRequested(object? sender, ImagePreviewRequestedEventArgs e)
    {
        try
        {
            var paths = _vaultWorkspace.GetPaths();
            var filePath = ResolveVaultFilePath(paths.RootPath, e.Embed.VaultRelativePath);
            await ImagePreviewWindow.ShowAsync(this, filePath, e.Embed.VaultRelativePath, _windowClosed.Token);
        }
        catch (OperationCanceledException)
        {
            // Window is closing; silently abandon the preview.
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "PREVIEW UNAVAILABLE";
            _logger.LogWarning(ex, "Image preview path was invalid for embed {EmbedPath}.", e.Embed.VaultRelativePath);
        }
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "PREVIEW UNAVAILABLE";
            _logger.LogWarning(ex, "Image preview path was invalid for embed {EmbedPath}.", e.Embed.VaultRelativePath);
        }
    }

    private static string ResolveVaultFilePath(string rootPath, string vaultRelativePath)
    {
        var normalizedRelativePath = vaultRelativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(normalizedRelativePath, rootPath);
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Image preview path must stay within the configured vault root.");
        }

        return fullPath;
    }

    private sealed record PersonAutocompleteSuggestion(string Name, bool IsCreateAction, VaultEntity? Entity)
    {
        public override string ToString()
        {
            return IsCreateAction ? $"Create \"{Name}\"" : Name;
        }
    }

    private sealed class FallbackHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    private sealed record MetadataInputSnapshot(string People, string Topics, string Projects, string Tags, string ScreenshotContext);
}
