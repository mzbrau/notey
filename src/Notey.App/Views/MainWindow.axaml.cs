using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.App.Assistant;
using Notey.App.Configuration;
using Notey.App.Editing;
using Notey.App.Imports;
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
using Notey.Vault.Tasks;

namespace Notey.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan IdleProcessingDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IndexRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RecentFinalNoteLookback = TimeSpan.FromDays(14);
    private static readonly TimeSpan TaskRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TasksPanelAnimationDuration = TimeSpan.FromMilliseconds(140);
    private const double TasksPanelMinWidth = 300;
    private const double TasksPanelMaxWidth = 620;

    private readonly NoteyOptions _options;
    private readonly INoteDraftStore _noteDraftStore;
    private readonly IVaultWorkspace _vaultWorkspace;
    private readonly IDocumentStoreIndex _documentStoreIndex;
    private readonly IVaultEntityStore _vaultEntityStore;
    private readonly IScreenSnipService _screenSnipService;
    private readonly ITesseractOcrEngine _ocrEngine;
    private readonly ObsidianLinkBuilder _linkBuilder;
    private readonly ITaskStore _taskStore;
    private readonly DraftProcessingService _draftProcessingService;
    private readonly NoteyAssistantService _assistantService;
    private readonly FileImportService _fileImportService;
    private readonly IRecentNoteChooser _recentNoteChooser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainWindow> _logger;
    private readonly NoteySettingsStore _settingsStore;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _idleProcessingTimer;
    private readonly DispatcherTimer _taskRefreshTimer;
    private readonly CancellationTokenSource _windowClosed = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly SemaphoreSlim _taskRefreshGate = new(1, 1);
    private readonly ImagePreviewMargin _imagePreviewMargin = new();
    private readonly TranslateTransform _tasksPanelTransform = new();
    private readonly NoteDirectiveParser _directiveParser = new();
    private readonly List<string> _directOcrSnippets = [];
    private NoteyTask? _currentEditTask;

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
    private bool _isRecentNoteDialogOpen;
    private bool _isTasksPanelAnimating;
    private bool _isResizingTasksPanel;
    private bool _isResizingAssistantPanel;
    private bool _assistantPanelVisible;
    private bool _isAssistantBusy;
    private double _tasksPanelResizeStartX;
    private double _tasksPanelResizeStartWidth;
    private double _assistantPanelResizeStartY;
    private double _assistantPanelResizeStartHeight;
    private long _revision;
    private long? _suppressedCompletionRevision;
    private HotkeyGesture? _openNoteGesture;
    private IReadOnlyList<VaultEntity> _peopleIndex = [];
    private IReadOnlyList<VaultFolderCommand> _folderCommands = [];
    private IReadOnlyList<CompletionSuggestion> _completionSuggestions = [];
    private IReadOnlyList<NoteyTask> _tasks = [];
    private NoteyAssistantResult? _pendingAssistantResult;
    private string? _pendingAssistantNoteIdentity;
    private readonly HashSet<TaskSectionKind> _collapsedTaskSections =
    [
        TaskSectionKind.NextWeek,
        TaskSectionKind.InTwoWeeks,
        TaskSectionKind.Future,
        TaskSectionKind.Undated,
        TaskSectionKind.Completed
    ];
    private CancellationTokenSource? _idleProcessingCancellation;
    private CancellationTokenSource? _assistantRequestCancellation;
    private DateTimeOffset _nextIndexRefreshAt = DateTimeOffset.MinValue;
    private bool _tasksPanelVisible = true;

    public bool HideInsteadOfClose { get; set; }

    public bool IsCaptureInProgress => _isCaptureInProgress;

    internal Control? OpenTaskEditPopupContent => TaskEditCard.IsVisible ? TaskEditCard : null;

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
            logger,
            taskStore: dependencies.TaskStore,
            assistantService: dependencies.AssistantService,
            fileImportService: dependencies.FileImportService)
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
        IRecentNoteChooser? recentNoteChooser = null,
        NoteySettingsStore? settingsStore = null,
        ITaskStore? taskStore = null,
        NoteyAssistantService? assistantService = null,
        FileImportService? fileImportService = null)
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
        _taskStore = taskStore ?? new FileSystemTaskStore(vaultWorkspace, linkBuilder, timeProvider);
        _draftProcessingService = draftProcessingService;
        _assistantService = assistantService ?? new NoteyAssistantService(
            options,
            new AiProviderRegistry([], string.IsNullOrWhiteSpace(options.Ai.DefaultProviderId) ? "default" : options.Ai.DefaultProviderId),
            NullLogger<NoteyAssistantService>.Instance);
        _fileImportService = fileImportService ?? new FileImportService(vaultWorkspace, linkBuilder, new MsgReaderMessageImportReader());
        _recentNoteChooser = recentNoteChooser ?? new RecentNoteDialogChooser();
        _timeProvider = timeProvider;
        _logger = logger;
        _settingsStore = settingsStore ?? CreateFallbackSettingsStore(options);
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _idleProcessingTimer = new DispatcherTimer { Interval = IdleProcessingDelay };
        _taskRefreshTimer = new DispatcherTimer { Interval = TaskRefreshInterval };
        _autosaveTimer.Tick += AutosaveTimerOnTick;
        _idleProcessingTimer.Tick += IdleProcessingTimerOnTick;
        _taskRefreshTimer.Tick += TaskRefreshTimerOnTick;
        _imagePreviewMargin.PreviewRequested += OnImagePreviewRequested;

        Width = options.Ui.DefaultWindowWidth;
        Height = options.Ui.DefaultWindowHeight;
        _openNoteGesture = TryParseOpenNoteGesture(options.Hotkeys.OpenNote);

        ConfigureEditor();
        ConfigureCommands();
        ConfigureTasksPanel();
        ConfigureAssistantPanel();
        UpdateEditorStatus();
        UpdateContextChip();
        UpdateCurrentNoteFooter();

        Opened += async (_, _) =>
        {
            await RefreshTasksAsync();
            await OpenInitialDraftAsync();
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _windowClosed.Cancel();
            CancelIdleProcessing();
            CancelAssistantRequest();
            DisposeIdleProcessingCancellation();
            DisposeAssistantRequestCancellation();
            _taskRefreshTimer.Stop();
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
        var ocrEngine = new TesseractNativeOcrEngine();
        var taskStore = new FileSystemTaskStore(workspace, linkBuilder, TimeProvider.System);
        var attachmentPromoter = new DraftAttachmentPromoter(workspace);
        var draftProcessingService = new DraftProcessingService(
            options,
            workspace,
            documentStoreIndex,
            aiRegistry,
            ocrEngine,
            TimeProvider.System,
            NullLogger<DraftProcessingService>.Instance,
            taskStore,
            attachmentPromoter);
        var assistantService = new NoteyAssistantService(options, aiRegistry, NullLogger<NoteyAssistantService>.Instance);
        var messageImportReader = new MsgReaderMessageImportReader();
        var fileImportService = new FileImportService(workspace, linkBuilder, messageImportReader);

        return new DefaultDependencies(
            options,
            new FileSystemNoteDraftStore(workspace, new NoteTemplateFactory(), new NoteFileNameGenerator()),
            workspace,
            documentStoreIndex,
            new FileSystemVaultEntityStore(workspace, linkBuilder, TimeProvider.System),
            new UnavailableScreenSnipService(),
            ocrEngine,
            linkBuilder,
            taskStore,
            draftProcessingService,
            assistantService,
            fileImportService);
    }

    private static NoteySettingsStore CreateFallbackSettingsStore(NoteyOptions options)
    {
        var providerRegistry = new AiProviderRegistry(
            OpenAiCompatibleAiProviderFactory.CreateProviders(options.Ai, static () => new HttpClient(), NullLoggerFactory.Instance),
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
        NoteEditor.TextArea.CommandBindings.Add(new RoutedCommandBinding(
            AvaloniaEdit.ApplicationCommands.Paste,
            OnEditorPasteExecuted,
            OnEditorPasteCanExecute));
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
        DragDrop.SetAllowDrop(NoteEditor, true);
        DragDrop.AddDragOverHandler(NoteEditor, OnEditorDragOver);
        DragDrop.AddDropHandler(NoteEditor, OnEditorDrop);
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
        TasksButton.Click += async (_, _) => await ToggleTasksPanelAsync();
        AddTaskButton.Click += (_, _) => ToggleAddTaskPanel();
        SaveNewTaskButton.Click += async (_, _) => await AddTaskFromPanelAsync();
        AssistantButton.Click += (_, _) => ToggleAssistantPanel();
        AssistantSendButton.Click += async (_, _) => await SendAssistantPromptAsync();
        AssistantCancelButton.Click += (_, _) => CancelAssistantRequest();
        AssistantApplyButton.Click += async (_, _) => await ApplyPendingAssistantChangesAsync();
        SettingsButton.Click += async (_, _) => await OpenSettingsAsync();
    }

    private void ConfigureTasksPanel()
    {
        TasksPanel.RenderTransform = _tasksPanelTransform;
        TasksPanelResizeHandle.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        TasksPanelResizeHandle.PointerPressed += OnTasksPanelResizeHandlePointerPressed;
        TasksPanelResizeHandle.PointerMoved += OnTasksPanelResizeHandlePointerMoved;
        TasksPanelResizeHandle.PointerReleased += OnTasksPanelResizeHandlePointerReleased;
        SetTasksPanelWidth(TasksPanel.Width);

        TaskEditSaveButton.Click += async (_, _) =>
        {
            if (_currentEditTask is null) return;
            if (await SaveTaskDetailsAsync(_currentEditTask, TaskEditTextBox.Text, FromPickerDate(TaskEditDueDatePicker.SelectedDate)))
            {
                HideTaskEditCard();
            }
            else
            {
                TaskEditErrorText.IsVisible = true;
            }
        };
        TaskEditCancelButton.Click += (_, _) => HideTaskEditCard();
        TaskEditClearDueDateButton.Click += (_, _) => TaskEditDueDatePicker.SelectedDate = null;
        TaskEditDeleteButton.Click += async (_, _) =>
        {
            if (_currentEditTask is null) return;
            if (await DeleteTaskAsync(_currentEditTask))
            {
                HideTaskEditCard();
            }
            else
            {
                TaskEditErrorText.IsVisible = true;
            }
        };
        TaskEditCard.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HideTaskEditCard();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                if (_currentEditTask is null) return;
                if (await SaveTaskDetailsAsync(_currentEditTask, TaskEditTextBox.Text, FromPickerDate(TaskEditDueDatePicker.SelectedDate)))
                {
                    HideTaskEditCard();
                }
                else
                {
                    TaskEditErrorText.IsVisible = true;
                }

                e.Handled = true;
            }
        };
    }

    private void ConfigureAssistantPanel()
    {
        AssistantPanelResizeHandle.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        AssistantPanelResizeHandle.PointerPressed += OnAssistantPanelResizeHandlePointerPressed;
        AssistantPanelResizeHandle.PointerMoved += OnAssistantPanelResizeHandlePointerMoved;
        AssistantPanelResizeHandle.PointerReleased += OnAssistantPanelResizeHandlePointerReleased;
        SetAssistantPanelHeight(AssistantPanel.Height);
        UpdateAssistantButtons();
        AssistantPromptTextBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && IsCommandModifier(e.KeyModifiers))
            {
                await SendAssistantPromptAsync();
                e.Handled = true;
            }
        };
    }

    private void OnAssistantPanelResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        _isResizingAssistantPanel = true;
        _assistantPanelResizeStartY = e.GetPosition(this).Y;
        _assistantPanelResizeStartHeight = GetAssistantPanelHeight();
        e.Pointer.Capture(AssistantPanelResizeHandle);
        e.Handled = true;
    }

    private void OnAssistantPanelResizeHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingAssistantPanel)
        {
            return;
        }

        var currentY = e.GetPosition(this).Y;
        SetAssistantPanelHeight(_assistantPanelResizeStartHeight + _assistantPanelResizeStartY - currentY);
        e.Handled = true;
    }

    private void OnAssistantPanelResizeHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingAssistantPanel)
        {
            return;
        }

        _isResizingAssistantPanel = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ToggleAssistantPanel()
    {
        _assistantPanelVisible = !_assistantPanelVisible;
        AssistantPanel.IsVisible = _assistantPanelVisible;
        AssistantPanelResizeHandle.IsVisible = _assistantPanelVisible;
        if (_assistantPanelVisible)
        {
            AssistantPromptTextBox.Focus();
        }
    }

    private void SetAssistantPanelHeight(double height)
    {
        AssistantPanel.Height = ClampAssistantPanelHeight(height);
    }

    private double GetAssistantPanelHeight()
    {
        return double.IsNaN(AssistantPanel.Height)
            ? ClampAssistantPanelHeight(AssistantPanel.Bounds.Height)
            : ClampAssistantPanelHeight(AssistantPanel.Height);
    }

    private double ClampAssistantPanelHeight(double height)
    {
        return Math.Clamp(height, AssistantPanel.MinHeight, AssistantPanel.MaxHeight);
    }

    private async Task SendAssistantPromptAsync()
    {
        if (_isAssistantBusy)
        {
            return;
        }

        var prompt = AssistantPromptTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowAssistantError("Enter a prompt for Notey Assistant.");
            return;
        }

        _isAssistantBusy = true;
        _pendingAssistantResult = null;
        AssistantErrorText.IsVisible = false;
        AssistantStatusText.Text = "Thinking";
        AssistantResponseText.Text = "Notey Assistant is thinking...";
        UpdateAssistantButtons();

        _assistantRequestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowClosed.Token);
        var requestCancellation = _assistantRequestCancellation;
        try
        {
            await RefreshTasksAsync();
            var result = await _assistantService.CompleteAsync(CreateAssistantRequest(prompt), _assistantRequestCancellation.Token);
            _pendingAssistantResult = result.Warnings.Count == 0 && result.HasChanges ? result : null;
            _pendingAssistantNoteIdentity = _pendingAssistantResult is null ? null : GetCurrentAssistantNoteIdentity();
            AssistantResponseText.Text = FormatAssistantResult(result);
            AssistantStatusText.Text = result.HasChanges
                ? result.Warnings.Count == 0 ? "Changes proposed" : "Review failed"
                : "Answered";
            AssistantPromptTextBox.Text = string.Empty;
            if (result.Warnings.Count > 0)
            {
                ShowAssistantError("Assistant proposed changes that failed validation. Nothing was applied.");
            }
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested || requestCancellation.IsCancellationRequested)
        {
            AssistantStatusText.Text = "Cancelled";
            AssistantResponseText.Text = "Notey Assistant request cancelled.";
        }
        catch (AiProviderException ex)
        {
            AssistantStatusText.Text = "AI error";
            ShowAssistantError(IsAiConfigurationError(ex) ? "AI provider is not configured." : "AI provider returned an error.");
            _logger.LogError(ex, "Notey Assistant AI request failed.");
        }
        catch (HttpRequestException ex)
        {
            AssistantStatusText.Text = "AI error";
            ShowAssistantError("AI request failed.");
            _logger.LogError(ex, "Notey Assistant HTTP request failed.");
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            AssistantStatusText.Text = "Invalid response";
            ShowAssistantError("AI response could not be safely understood.");
            _logger.LogError(ex, "Notey Assistant returned an invalid response.");
        }
        finally
        {
            _isAssistantBusy = false;
            DisposeAssistantRequestCancellation();
            UpdateAssistantButtons();
        }
    }

    private NoteyAssistantRequest CreateAssistantRequest(string prompt)
    {
        return new NoteyAssistantRequest(
            prompt,
            NoteEditor.Document.Text,
            NoteEditor.CaretOffset,
            NoteEditor.SelectionStart,
            NoteEditor.SelectionLength,
            _currentFinalNotePath,
            _currentDraft is not null,
            _tasks);
    }

    private string GetCurrentAssistantNoteIdentity()
    {
        if (_currentFinalNotePath is not null)
        {
            return $"final:{Path.GetFullPath(_currentFinalNotePath)}";
        }

        if (_currentDraft is not null)
        {
            return $"draft:{Path.GetFullPath(_currentDraft.FilePath)}";
        }

        return "none";
    }

    private void ClearPendingAssistantResult()
    {
        _pendingAssistantResult = null;
        _pendingAssistantNoteIdentity = null;
        UpdateAssistantButtons();
    }

    private async Task ApplyPendingAssistantChangesAsync()
    {
        if (_pendingAssistantResult is null || _isAssistantBusy)
        {
            return;
        }

        _isAssistantBusy = true;
        UpdateAssistantButtons();
        var pendingResult = _pendingAssistantResult;
        var pendingNoteIdentity = _pendingAssistantNoteIdentity;
        _pendingAssistantResult = null;
        _pendingAssistantNoteIdentity = null;
        try
        {
            if (!string.Equals(pendingNoteIdentity, GetCurrentAssistantNoteIdentity(), StringComparison.Ordinal))
            {
                AssistantStatusText.Text = "Review failed";
                ShowAssistantError("The current note changed since the assistant response. Ask again before applying.");
                return;
            }

            await RefreshTasksAsync();
            var validation = AssistantOperationValidator.Validate(
                new NoteyAssistantResponse(
                    pendingResult.Message,
                    pendingResult.NoteOperations,
                    pendingResult.TaskOperations),
                NoteEditor.Document.Text,
                _tasks);
            if (validation.Warnings.Count > 0)
            {
                AssistantResponseText.Text = FormatAssistantResult(validation);
                AssistantStatusText.Text = "Review failed";
                ShowAssistantError("Current note or tasks changed since the assistant response. Ask again before applying.");
                return;
            }

            await ApplyAssistantTaskOperationsAsync(validation.TaskOperations);
            ApplyAssistantNoteOperations(validation.NoteOperations);
            AssistantStatusText.Text = "Applied";
            AssistantErrorText.IsVisible = false;
            AutosaveStatusText.Text = "ASSISTANT APPLIED";
            UpdateEditorStatus();
            UpdateAssistantButtons();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AssistantStatusText.Text = "Apply failed";
            ShowAssistantError("Notey could not apply the assistant changes.");
            _logger.LogError(ex, "Failed to apply Notey Assistant changes.");
        }
        finally
        {
            _isAssistantBusy = false;
            UpdateAssistantButtons();
        }
    }

    private void ApplyAssistantNoteOperations(IReadOnlyList<AssistantNoteOperation> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        if (NoteEditor.IsReadOnly)
        {
            throw new InvalidOperationException("The current note is read-only.");
        }

        using (NoteEditor.Document.RunUpdate())
        {
            if (operations.OfType<ReplaceAllNoteTextOperation>().SingleOrDefault() is { } replaceAll)
            {
                NoteEditor.Document.Text = replaceAll.Text;
                NoteEditor.CaretOffset = Math.Min(NoteEditor.CaretOffset, NoteEditor.Document.TextLength);
                return;
            }

            foreach (var edit in operations
                         .Select(ToDocumentEdit)
                         .OrderByDescending(static edit => edit.Start)
                         .ThenByDescending(static edit => edit.Order))
            {
                NoteEditor.Document.Replace(edit.Start, edit.Length, edit.Text);
            }
        }
    }

    private async Task ApplyAssistantTaskOperationsAsync(IReadOnlyList<AssistantTaskOperation> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case AssistantTaskOperationKind.Add:
                    await _taskStore.AddAsync([new NewNoteyTask(operation.Text ?? string.Empty, operation.DueDate, _currentFinalNotePath)], today, _windowClosed.Token);
                    break;
                case AssistantTaskOperationKind.Update:
                    var task = _tasks.First(task => string.Equals(task.Id, operation.TaskId, StringComparison.OrdinalIgnoreCase));
                    if (await _taskStore.SetDetailsAsync(operation.TaskId!, operation.Text ?? task.Text, operation.DueDate ?? task.DueDate, _windowClosed.Token) is null)
                    {
                        throw new InvalidOperationException($"Task '{operation.TaskId}' no longer exists.");
                    }

                    break;
                case AssistantTaskOperationKind.Remove:
                    await _taskStore.RemoveAsync([operation.TaskId!], _windowClosed.Token);
                    break;
                case AssistantTaskOperationKind.Complete:
                    if (await _taskStore.SetCompletedAsync(operation.TaskId!, today, _windowClosed.Token) is null)
                    {
                        throw new InvalidOperationException($"Task '{operation.TaskId}' no longer exists.");
                    }

                    break;
                case AssistantTaskOperationKind.Reopen:
                    if (await _taskStore.SetCompletedAsync(operation.TaskId!, null, _windowClosed.Token) is null)
                    {
                        throw new InvalidOperationException($"Task '{operation.TaskId}' no longer exists.");
                    }

                    break;
                case AssistantTaskOperationKind.SetDueDate:
                    if (await _taskStore.SetDueDateAsync(operation.TaskId!, operation.DueDate, _windowClosed.Token) is null)
                    {
                        throw new InvalidOperationException($"Task '{operation.TaskId}' no longer exists.");
                    }

                    break;
            }
        }

        await RefreshTasksAsync();
    }

    private static (int Start, int Length, string Text, int Order) ToDocumentEdit(AssistantNoteOperation operation, int order)
    {
        return operation switch
        {
            InsertNoteTextOperation insert => (insert.Offset, 0, insert.Text, order),
            ReplaceNoteRangeOperation replace => (replace.Start, replace.Length, replace.Text, order),
            DeleteNoteRangeOperation delete => (delete.Start, delete.Length, string.Empty, order),
            _ => throw new InvalidOperationException("Unsupported assistant note operation.")
        };
    }

    private string FormatAssistantResult(NoteyAssistantResult result)
    {
        var builder = new StringBuilder(result.Message.Trim());
        if (result.NoteOperations.Count > 0 || result.TaskOperations.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Proposed changes:");
            if (result.NoteOperations.Count > 0)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- Note edits: {result.NoteOperations.Count}");
                foreach (var operation in result.NoteOperations)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"  - {DescribeNoteOperation(operation)}");
                }
            }

            if (result.TaskOperations.Count > 0)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- Task changes: {result.TaskOperations.Count}");
                foreach (var operation in result.TaskOperations)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"  - {DescribeTaskOperation(operation)}");
                }
            }
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Validation warnings:");
            foreach (var warning in result.Warnings)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {warning}");
            }
        }

        return builder.ToString();
    }

    private static string DescribeNoteOperation(AssistantNoteOperation operation)
    {
        return operation switch
        {
            InsertNoteTextOperation insert => $"Insert at {insert.Offset}: \"{PreviewText(insert.Text)}\"",
            ReplaceNoteRangeOperation replace => $"Replace {replace.Length} character(s) at {replace.Start}: \"{PreviewText(replace.ExpectedText ?? string.Empty)}\" -> \"{PreviewText(replace.Text)}\"",
            DeleteNoteRangeOperation delete => $"Delete {delete.Length} character(s) at {delete.Start}: \"{PreviewText(delete.ExpectedText ?? string.Empty)}\"",
            ReplaceAllNoteTextOperation replaceAll => $"Replace entire note with {replaceAll.Text.Length} character(s).",
            _ => "Unknown note edit"
        };
    }

    private string DescribeTaskOperation(AssistantTaskOperation operation)
    {
        var task = operation.TaskId is null
            ? null
            : _tasks.FirstOrDefault(task => string.Equals(task.Id, operation.TaskId, StringComparison.OrdinalIgnoreCase));
        return operation.Kind switch
        {
            AssistantTaskOperationKind.Add => $"Add task: \"{operation.Text}\"{FormatDueDatePreview(operation.DueDate)}",
            AssistantTaskOperationKind.Update => $"Update task \"{task?.Text ?? operation.TaskId}\": \"{operation.Text}\"{FormatDueDatePreview(operation.DueDate)}",
            AssistantTaskOperationKind.Remove => $"Remove task: \"{task?.Text ?? operation.TaskId}\"",
            AssistantTaskOperationKind.Complete => $"Complete task: \"{task?.Text ?? operation.TaskId}\"",
            AssistantTaskOperationKind.Reopen => $"Reopen task: \"{task?.Text ?? operation.TaskId}\"",
            AssistantTaskOperationKind.SetDueDate => $"Set due date for \"{task?.Text ?? operation.TaskId}\"{FormatDueDatePreview(operation.DueDate)}",
            _ => "Unknown task change"
        };
    }

    private static string FormatDueDatePreview(DateOnly? dueDate)
    {
        return dueDate is { } value
            ? $" due {value:yyyy-MM-dd}"
            : string.Empty;
    }

    private static string PreviewText(string text)
    {
        var normalized = text
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
        return normalized.Length <= 80 ? normalized : $"{normalized[..80]}...";
    }

    private void ShowAssistantError(string message)
    {
        AssistantErrorText.Text = message;
        AssistantErrorText.IsVisible = true;
    }

    private void UpdateAssistantButtons()
    {
        AssistantSendButton.IsEnabled = !_isAssistantBusy;
        AssistantCancelButton.IsEnabled = _isAssistantBusy;
        AssistantApplyButton.IsEnabled = !_isAssistantBusy
            && _pendingAssistantResult is { HasChanges: true, Warnings.Count: 0 };
    }

    private void OnTasksPanelResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        _isResizingTasksPanel = true;
        _tasksPanelResizeStartX = e.GetPosition(this).X;
        _tasksPanelResizeStartWidth = GetTasksPanelWidth();
        e.Pointer.Capture(TasksPanelResizeHandle);
        e.Handled = true;
    }

    private void OnTasksPanelResizeHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingTasksPanel)
        {
            return;
        }

        var currentX = e.GetPosition(this).X;
        SetTasksPanelWidth(_tasksPanelResizeStartWidth + _tasksPanelResizeStartX - currentX);
        e.Handled = true;
    }

    private void OnTasksPanelResizeHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingTasksPanel)
        {
            return;
        }

        _isResizingTasksPanel = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void SetTasksPanelWidth(double width)
    {
        TasksPanel.Width = Math.Clamp(width, TasksPanelMinWidth, TasksPanelMaxWidth);
    }

    private double GetTasksPanelWidth()
    {
        return double.IsNaN(TasksPanel.Width)
            ? Math.Clamp(TasksPanel.Bounds.Width, TasksPanelMinWidth, TasksPanelMaxWidth)
            : Math.Clamp(TasksPanel.Width, TasksPanelMinWidth, TasksPanelMaxWidth);
    }

    private async Task ToggleTasksPanelAsync()
    {
        if (_tasksPanelVisible)
        {
            await HideTasksPanelAsync();
        }
        else
        {
            await ShowTasksPanelAsync();
        }
    }

    private async Task ShowTasksPanelAsync()
    {
        if (_isTasksPanelAnimating)
        {
            return;
        }

        _isTasksPanelAnimating = true;
        try
        {
            var panelWidth = GetTasksPanelWidth();
            _tasksPanelVisible = true;
            _tasksPanelTransform.X = panelWidth;
            TasksPanel.Opacity = 0;
            TasksPanel.IsVisible = true;
            TasksPanelResizeHandle.IsVisible = true;
            _taskRefreshTimer.Start();
            await RefreshTasksAsync();
            await AnimateTasksPanelAsync(panelWidth, 0);
        }
        finally
        {
            _tasksPanelTransform.X = 0;
            TasksPanel.Opacity = 1;
            _isTasksPanelAnimating = false;
        }
    }

    private async Task HideTasksPanelAsync()
    {
        if (_isTasksPanelAnimating)
        {
            return;
        }

        _isTasksPanelAnimating = true;
        try
        {
            _tasksPanelVisible = false;
            _taskRefreshTimer.Stop();
            await AnimateTasksPanelAsync(0, GetTasksPanelWidth());
            TasksPanel.IsVisible = false;
            TasksPanelResizeHandle.IsVisible = false;
        }
        finally
        {
            _tasksPanelTransform.X = 0;
            TasksPanel.Opacity = 1;
            _isTasksPanelAnimating = false;
        }
    }

    private Task AnimateTasksPanelAsync(double from, double to)
    {
        var completion = new TaskCompletionSource();
        var stopwatch = Stopwatch.StartNew();
        var opening = to < from;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var progress = Math.Min(1, stopwatch.Elapsed.TotalMilliseconds / TasksPanelAnimationDuration.TotalMilliseconds);
            var eased = EaseOutCubic(progress);
            _tasksPanelTransform.X = from + ((to - from) * eased);
            TasksPanel.Opacity = opening ? eased : 1 - eased;
            if (progress < 1)
            {
                return;
            }

            timer.Stop();
            _tasksPanelTransform.X = to;
            TasksPanel.Opacity = opening ? 1 : 0;
            completion.TrySetResult();
        };

        timer.Start();
        return completion.Task;
    }

    private static double EaseOutCubic(double progress)
    {
        var inverse = 1 - progress;
        return 1 - (inverse * inverse * inverse);
    }

    private void ToggleAddTaskPanel()
    {
        AddTaskPanel.IsVisible = !AddTaskPanel.IsVisible;
        if (AddTaskPanel.IsVisible)
        {
            NewTaskDueDatePicker.SelectedDate = ToPickerDate(DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime));
            NewTaskTextBox.Focus();
        }
    }

    private async Task AddTaskFromPanelAsync()
    {
        var text = NewTaskTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            AutosaveStatusText.Text = "TASK TEXT REQUIRED";
            return;
        }

        var dueDate = FromPickerDate(NewTaskDueDatePicker.SelectedDate);

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        try
        {
            await _taskStore.AddAsync([new NewNoteyTask(text, dueDate, SourceFilePath: null)], today, _windowClosed.Token);
            NewTaskTextBox.Text = string.Empty;
            AddTaskPanel.IsVisible = false;
            AutosaveStatusText.Text = "TASK ADDED";
            await RefreshTasksAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "TASK SAVE ERROR";
            _logger.LogError(ex, "Failed to add task from the task panel.");
        }
    }

    private async void TaskRefreshTimerOnTick(object? sender, EventArgs e)
    {
        if (_tasksPanelVisible && IsVisible && _taskRefreshGate.CurrentCount > 0)
        {
            await RefreshTasksAsync();
        }
    }

    private async Task RefreshTasksAsync()
    {
        var refreshLockTaken = false;
        try
        {
            await _taskRefreshGate.WaitAsync(_windowClosed.Token);
            refreshLockTaken = true;
            _tasks = await _taskStore.LoadAsync(_windowClosed.Token);
            RenderTasks();
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Task refresh was cancelled because the window closed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            AutosaveStatusText.Text = "TASKS ERROR";
            _logger.LogError(ex, "Failed to refresh tasks from {TasksPath}.", _taskStore.GetTasksFilePath());
        }
        finally
        {
            if (refreshLockTaken)
            {
                _taskRefreshGate.Release();
            }
        }
    }

    private void RenderTasks()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var badgeCount = TaskGrouper.CountBadgeTasks(_tasks, today);
        TasksBadge.IsVisible = badgeCount > 0;
        TasksBadgeText.Text = badgeCount.ToString(CultureInfo.InvariantCulture);

        if (_currentEditTask is not null && !_tasks.Any(t => t.Id == _currentEditTask.Id))
        {
            HideTaskEditCard();
        }

        TaskSectionsPanel.Children.Clear();
        foreach (var section in TaskGrouper.Group(_tasks, today))
        {
            TaskSectionsPanel.Children.Add(CreateTaskSection(section, today));
        }

        if (_tasksPanelVisible && !_taskRefreshTimer.IsEnabled)
        {
            _taskRefreshTimer.Start();
        }
    }

    private Control CreateTaskSection(TaskSection section, DateOnly today)
    {
        var container = new StackPanel { Spacing = 6 };
        var header = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        var isCollapsed = _collapsedTaskSections.Contains(section.Kind);
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        var triangle = new PathIcon
        {
            Data = Geometry.Parse("M7,4 L17,12 L7,20 Z"),
            Width = 10,
            Height = 10,
            Foreground = Brush.Parse("#8C909F"),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new RotateTransform(isCollapsed ? 0 : 90),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(triangle, 0);
        headerGrid.Children.Add(triangle);

        var title = new TextBlock
        {
            Text = section.Title,
            Classes = { "label" },
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        headerGrid.Children.Add(title);

        var count = new Border
        {
            Background = section.Kind == TaskSectionKind.Completed ? Brush.Parse("#1F5F35") : Brush.Parse("#384255"),
            CornerRadius = new CornerRadius(9),
            MinWidth = 20,
            Height = 20,
            Child = new TextBlock
            {
                Text = section.Count.ToString(CultureInfo.InvariantCulture),
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        Grid.SetColumn(count, 2);
        headerGrid.Children.Add(count);
        header.Content = headerGrid;
        header.Click += (_, _) =>
        {
            if (!_collapsedTaskSections.Add(section.Kind))
            {
                _collapsedTaskSections.Remove(section.Kind);
            }

            RenderTasks();
        };
        container.Children.Add(header);

        if (!isCollapsed)
        {
            foreach (var task in section.Tasks)
            {
                container.Children.Add(CreateTaskRow(task, section.Kind, today));
            }
        }

        return container;
    }

    private Control CreateTaskRow(NoteyTask task, TaskSectionKind sectionKind, DateOnly today)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var checkbox = new CheckBox
        {
            IsChecked = task.IsCompleted,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(checkbox, 0);
        checkbox.Click += async (_, _) => await SetTaskCompletedAsync(task, checkbox.IsChecked == true);
        row.Children.Add(checkbox);

        var taskText = new TextBlock
        {
            Text = task.Text,
            Foreground = task.IsCompleted ? Brush.Parse("#8C909F") : Brush.Parse("#E1E2EC"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(taskText, 1);
        row.Children.Add(taskText);

        if (sectionKind == TaskSectionKind.Incomplete && !task.IsCompleted)
        {
            var moveButton = new Button
            {
                Name = "SetDueTodayButton",
                Width = 24,
                Height = 24,
                MinWidth = 24,
                Padding = new Thickness(0),
                Content = new PathIcon
                {
                    Data = Geometry.Parse("M5,4 H7 V2 H9 V4 H15 V2 H17 V4 H19 C20.1,4 21,4.9 21,6 V8 H3 V6 C3,4.9 3.9,4 5,4 Z M5,10 H19 V20 H5 Z"),
                    Width = 13,
                    Height = 13,
                    Foreground = Brush.Parse("#C2C6D6")
                }
            };
            ToolTip.SetTip(moveButton, "Set due today");
            Grid.SetColumn(moveButton, 2);
            moveButton.Click += async (_, _) => await MoveTaskToThisWeekAsync(task, today);
            row.Children.Add(moveButton);
        }

        if (task.SourceFilePath is not null)
        {
            var sourceButton = new Button
            {
                Content = "Open",
                Padding = new Thickness(8, 3),
                FontSize = 11
            };
            Grid.SetColumn(sourceButton, 3);
            ToolTip.SetTip(sourceButton, "Open source note in Obsidian");
            sourceButton.Click += (_, _) => OpenTaskSourceInObsidian(task.SourceFilePath);
            row.Children.Add(sourceButton);
        }

        var dateButton = new Button
        {
            Content = FormatTaskDate(task.DueDate),
            Padding = new Thickness(8, 3),
            FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        var dateShiftButtons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var previousDayButton = CreateTaskDateShiftButton("M12,17 L6,9 L18,9 Z", "Move task back one day");
        previousDayButton.IsEnabled = task.DueDate is not null;
        previousDayButton.Click += async (_, _) => await ShiftTaskDueDateAsync(task, -1);
        var nextDayButton = CreateTaskDateShiftButton("M12,7 L18,15 L6,15 Z", "Move task forward one day");
        nextDayButton.IsEnabled = task.DueDate is not null;
        nextDayButton.Click += async (_, _) => await ShiftTaskDueDateAsync(task, 1);
        dateShiftButtons.Children.Add(previousDayButton);
        dateShiftButtons.Children.Add(nextDayButton);
        Grid.SetColumn(dateShiftButtons, 4);
        row.Children.Add(dateShiftButtons);

        Grid.SetColumn(dateButton, 5);
        dateButton.Click += (_, _) => ShowTaskEditPopup(task, dateButton);
        row.Children.Add(dateButton);

        return row;
    }

    private static Button CreateTaskDateShiftButton(string iconData, string tooltip)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            MinWidth = 22,
            Padding = new Thickness(0),
            Content = new PathIcon
            {
                Data = Geometry.Parse(iconData),
                Width = 10,
                Height = 10,
                Foreground = Brush.Parse("#C2C6D6")
            }
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private void ShowTaskEditPopup(NoteyTask task, Button _)
    {
        _currentEditTask = task;
        TaskEditTextBox.Text = task.Text;
        TaskEditDueDatePicker.SelectedDate = ToPickerDate(task.DueDate);
        TaskEditErrorText.IsVisible = false;
        TaskEditBackdrop.IsVisible = true;
        TaskEditCard.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            TaskEditTextBox.Focus();
            TaskEditTextBox.CaretIndex = TaskEditTextBox.Text?.Length ?? 0;
        });
    }

    private void HideTaskEditCard()
    {
        _currentEditTask = null;
        TaskEditBackdrop.IsVisible = false;
        TaskEditCard.IsVisible = false;
    }

    private async Task ShiftTaskDueDateAsync(NoteyTask task, int days)
    {
        if (task.DueDate is not { } dueDate)
        {
            return;
        }

        HideTaskEditCard();
        await SetTaskDueDateAsync(task, dueDate.AddDays(days));
    }

    private async Task SetTaskCompletedAsync(NoteyTask task, bool completed)
    {
        try
        {
            var completedDate = completed
                ? DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime)
                : (DateOnly?)null;
            await _taskStore.SetCompletedAsync(task.Id, completedDate, _windowClosed.Token);
            AutosaveStatusText.Text = completed ? "TASK COMPLETED" : "TASK REOPENED";
            await RefreshTasksAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "TASK SAVE ERROR";
            _logger.LogError(ex, "Failed to update task {TaskId}.", task.Id);
        }
    }

    private async Task SetTaskDueDateAsync(NoteyTask task, DateOnly? dueDate)
    {
        try
        {
            HideTaskEditCard();
            await _taskStore.SetDueDateAsync(task.Id, dueDate, _windowClosed.Token);
            AutosaveStatusText.Text = "TASK UPDATED";
            await RefreshTasksAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "TASK SAVE ERROR";
            _logger.LogError(ex, "Failed to change due date for task {TaskId}.", task.Id);
        }
    }

    private async Task MoveTaskToThisWeekAsync(NoteyTask task, DateOnly today)
    {
        try
        {
            HideTaskEditCard();
            await _taskStore.MoveToThisWeekAsync(task.Id, today, _windowClosed.Token);
            AutosaveStatusText.Text = "TASK MOVED";
            await RefreshTasksAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "TASK SAVE ERROR";
            _logger.LogError(ex, "Failed to move overdue task {TaskId} to this week.", task.Id);
        }
    }

    private async Task<bool> SaveTaskDetailsAsync(NoteyTask task, string? text, DateOnly? dueDate)
    {
        try
        {
            var updated = await _taskStore.SetDetailsAsync(task.Id, text ?? string.Empty, dueDate, _windowClosed.Token);
            if (updated is null)
            {
                throw new InvalidOperationException("Task was not found.");
            }

            AutosaveStatusText.Text = "TASK UPDATED";
            await RefreshTasksAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "TASK SAVE ERROR";
            _logger.LogError(ex, "Failed to save task {TaskId}.", task.Id);
            return false;
        }
    }

    private async Task<bool> DeleteTaskAsync(NoteyTask task)
    {
        try
        {
            await _taskStore.RemoveAsync([task.Id], _windowClosed.Token);
            AutosaveStatusText.Text = "TASK DELETED";
            await RefreshTasksAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AutosaveStatusText.Text = "TASK DELETE ERROR";
            _logger.LogError(ex, "Failed to delete task {TaskId}.", task.Id);
            return false;
        }
    }

    private void OpenTaskSourceInObsidian(string sourceFilePath)
    {
        try
        {
            var uri = _linkBuilder.BuildOpenFileUri(sourceFilePath);
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
            _logger.LogError(ex, "Failed to open task source note {FilePath} in Obsidian.", sourceFilePath);
        }
    }

    private DateTimeOffset? ToPickerDate(DateOnly? dueDate)
    {
        return dueDate is { } value
            ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), _timeProvider.GetLocalNow().Offset)
            : null;
    }

    private static DateOnly? FromPickerDate(DateTimeOffset? selectedDate)
    {
        return selectedDate is { } value
            ? DateOnly.FromDateTime(value.DateTime)
            : null;
    }

    private static string FormatTaskDate(DateOnly? dueDate)
    {
        return dueDate is { } value
            ? value.ToString("ddd d/M", CultureInfo.InvariantCulture)
            : "No date";
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

        try
        {
            await _noteDraftStore.DeleteEmptyDraftsAsync(_windowClosed.Token);
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up empty drafts on startup; continuing.");
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
            ClearPendingAssistantResult();
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

    internal async Task OpenRecentFinalNoteAsync()
    {
        if (!TryBeginOpenRecentDialog(ref _isRecentNoteDialogOpen))
        {
            return;
        }

        OpenRecentNoteButton.IsEnabled = false;
        try
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
            RecentDialogOverlay.IsVisible = true;
            var choice = await _recentNoteChooser.ChooseAsync(this, recent);
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
        finally
        {
            _isRecentNoteDialogOpen = false;
            RecentDialogOverlay.IsVisible = false;
            OpenRecentNoteButton.IsEnabled = true;
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

        var recentCandidates = new List<(string FilePath, DateTimeOffset CreatedAt)>();
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

            recentCandidates.Add((filePath, created));
        }

        var recent = new List<RecentNoteSummary>();
        foreach (var candidate in recentCandidates
                     .OrderByDescending(static item => item.CreatedAt)
                     .Take(20))
        {
            recent.Add(await CreateRecentFinalNoteSummaryAsync(candidate.FilePath, candidate.CreatedAt, cancellationToken));
        }

        return recent;
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

            ClearPendingAssistantResult();
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
            DeleteDraftAssetsIfExists(_currentDraft.FilePath);
            ClearPendingAssistantResult();
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
                ClearPendingAssistantResult();
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

        if (IsPasteShortcut(e.Key, e.KeyModifiers))
        {
            if (NoteEditor.IsReadOnly)
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            _ = PasteFromClipboardAsync();
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

        if (IsFormatTablesShortcut(e.Key, e.KeyModifiers))
        {
            if (NoteEditor.IsReadOnly)
            {
                e.Handled = true;
                return;
            }

            var edit = MarkdownTableFormatter.TryFormatTables(NoteEditor.Document.Text, NoteEditor.CaretOffset);
            if (edit is not null)
            {
                ApplyEdit(edit);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            var direction = e.KeyModifiers == KeyModifiers.Shift
                ? MarkdownTableNavigationDirection.Backward
                : MarkdownTableNavigationDirection.Forward;
            var tableNavigation = MarkdownTableFormatter.TryNavigateTableCell(
                NoteEditor.Document.Text,
                NoteEditor.CaretOffset,
                NoteEditor.SelectionLength,
                direction);
            if (tableNavigation is not null)
            {
                ApplyEdit(tableNavigation);
                e.Handled = true;
                return;
            }
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

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsOpenRecentDialogShortcut(e.Key, e.KeyModifiers))
        {
            return;
        }

        _ = OpenRecentFinalNoteAsync();
        e.Handled = true;
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

    private void OnEditorPasteCanExecute(object? sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !NoteEditor.IsReadOnly;
        e.Handled = true;
    }

    private async void OnEditorPasteExecuted(object? sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        await PasteFromClipboardAsync();
    }

    private void OnEditorDragOver(object? sender, DragEventArgs e)
    {
        var canImportFiles = !NoteEditor.IsReadOnly
            && e.DataTransfer.Formats.Contains(DataFormat.File)
            && e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Any() == true;
        e.DragEffects = canImportFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnEditorDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (NoteEditor.IsReadOnly || !e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            return;
        }

        var storageFiles = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray() ?? [];
        if (storageFiles.Length == 0)
        {
            AutosaveStatusText.Text = "NO FILES TO IMPORT";
            return;
        }

        var dropOffset = GetDropInsertionOffset(e);
        await ImportFilesAsync(storageFiles.Select(CreateImportFile).ToArray(), dropOffset);
    }

    internal async Task ImportFilesForTestingAsync(IReadOnlyList<ImportFile> files, int? insertionOffset = null)
    {
        await ImportFilesAsync(files, insertionOffset);
    }

    private async Task ImportFilesAsync(IReadOnlyList<ImportFile> files, int? insertionOffset = null)
    {
        if (files.Count == 0)
        {
            return;
        }

        var context = await GetFileImportContextAsync();
        if (context is null)
        {
            return;
        }

        var placeholder = $"<!-- notey-import-{Guid.NewGuid():N} -->";
        var replacementStart = Math.Clamp(insertionOffset ?? NoteEditor.SelectionStart, 0, NoteEditor.Document.TextLength);
        var replacementLength = insertionOffset.HasValue ? 0 : NoteEditor.SelectionLength;
        NoteEditor.Document.Replace(replacementStart, replacementLength, placeholder);
        NoteEditor.CaretOffset = replacementStart + placeholder.Length;

        try
        {
            AutosaveStatusText.Text = "IMPORTING FILES";
            var result = await _fileImportService.ImportAsync(files, context, _windowClosed.Token);
            ReplaceImportPlaceholder(placeholder, result.Markdown.Trim());
            AutosaveStatusText.Text = result.WrittenPaths.Count == 1 ? "FILE IMPORTED" : "FILES IMPORTED";
            UpdateEditorStatus();
            UpdateContextChip();
            ScheduleAutosave();
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            RemoveImportPlaceholder(placeholder);
            _logger.LogDebug("File import was cancelled because the window closed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            RemoveImportPlaceholder(placeholder);
            AutosaveStatusText.Text = "IMPORT ERROR";
            _logger.LogError(ex, "Failed to import dropped files.");
        }
    }

    private int GetDropInsertionOffset(DragEventArgs e)
    {
        var textPosition = NoteEditor.GetPositionFromPoint(e.GetPosition(NoteEditor));
        if (textPosition is null)
        {
            return NoteEditor.SelectionStart;
        }

        return NoteEditor.Document.GetOffset(textPosition.Value.Line, textPosition.Value.Column);
    }

    private async Task<FileImportContext?> GetFileImportContextAsync()
    {
        if (_currentFinalNotePath is not null)
        {
            return FileImportContext.ForFinalNote(_currentFinalNotePath);
        }

        if (_currentDraft is null && !await TryCreateAndLoadDraftAsync("importing files"))
        {
            return null;
        }

        return _currentDraft is null ? null : FileImportContext.ForDraft(_currentDraft.FilePath);
    }

    private void ReplaceImportPlaceholder(string placeholder, string markdown)
    {
        var index = NoteEditor.Document.Text.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0)
        {
            AppendMarkdownBlock(markdown);
            return;
        }

        NoteEditor.Document.Replace(index, placeholder.Length, markdown);
        NoteEditor.CaretOffset = index + markdown.Length;
    }

    private void RemoveImportPlaceholder(string placeholder)
    {
        var index = NoteEditor.Document.Text.IndexOf(placeholder, StringComparison.Ordinal);
        if (index >= 0)
        {
            NoteEditor.Document.Remove(index, placeholder.Length);
            NoteEditor.CaretOffset = Math.Min(index, NoteEditor.Document.TextLength);
        }
    }

    private static ImportFile CreateImportFile(IStorageFile storageFile)
    {
        if (storageFile.Path.IsFile && File.Exists(storageFile.Path.LocalPath))
        {
            return new ImportFile(
                storageFile.Name,
                cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Stream stream = new FileStream(storageFile.Path.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
                    return ValueTask.FromResult(stream);
                });
        }

        return new ImportFile(
            storageFile.Name,
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await storageFile.OpenReadAsync();
            });
    }

    private async Task PasteFromClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            _logger.LogWarning("Paste command could not access the system clipboard.");
            return;
        }

        IAsyncDataTransfer? data;
        try
        {
            data = await clipboard.TryGetDataAsync();

            if (data is not null)
            {
                var html = await TryGetClipboardHtmlAsync(data);
                var htmlTableDetected = !string.IsNullOrWhiteSpace(html) && MarkdownTableFormatter.ContainsHtmlTable(html);
                if (htmlTableDetected && MarkdownTableFormatter.TryConvertHtmlTable(html!, out var htmlTable))
                {
                    ReplaceSelection(htmlTable);
                    return;
                }

                var rtf = await TryGetClipboardRtfAsync(data);
                if (!htmlTableDetected
                    && !string.IsNullOrWhiteSpace(rtf)
                    && MarkdownTableFormatter.TryConvertRtfTable(rtf, out var rtfTable))
                {
                    ReplaceSelection(rtfTable);
                    return;
                }

                var dataText = await data.TryGetTextAsync();
                if (!string.IsNullOrEmpty(dataText))
                {
                    if (!htmlTableDetected && MarkdownTableFormatter.TryConvertPlainTextTable(dataText, out var textTable))
                    {
                        ReplaceSelection(textTable);
                        return;
                    }

                    if (htmlTableDetected || !string.IsNullOrWhiteSpace(rtf))
                    {
                        var formatSummary = await GetClipboardFormatSummaryAsync(data);
                        _logger.LogDebug("Clipboard contained table-like rich data that could not be converted. Formats: {ClipboardFormats}", formatSummary);
                    }

                    ReplaceSelection(dataText);
                    return;
                }
            }

            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                ReplaceSelection(text);
            }
            else
            {
                _logger.LogDebug("Paste command found no text content on the system clipboard.");
            }
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "Paste command failed while reading the system clipboard.");
            return;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Paste command failed while reading the system clipboard.");
            return;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Paste command failed while reading the system clipboard.");
            return;
        }
    }

    private static async Task<string?> TryGetClipboardHtmlAsync(IAsyncDataTransfer data)
    {
        return await TryGetClipboardPayloadAsync(data, IsHtmlClipboardFormat);
    }

    private static async Task<string?> TryGetClipboardRtfAsync(IAsyncDataTransfer data)
    {
        return await TryGetClipboardPayloadAsync(data, IsRtfClipboardFormat);
    }

    private static async Task<string?> TryGetClipboardPayloadAsync(IAsyncDataTransfer data, Func<DataFormat, bool> formatPredicate)
    {
        foreach (var item in data.Items)
        {
            foreach (var format in item.Formats.Where(formatPredicate))
            {
                var value = await item.TryGetRawAsync(format);
                if (TryGetClipboardString(value) is { Length: > 0 } html)
                {
                    return html;
                }
            }
        }

        return null;
    }

    private static bool IsHtmlClipboardFormat(DataFormat format)
    {
        return format.Identifier.Contains("html", StringComparison.OrdinalIgnoreCase)
            || format.Identifier.Equals("HTML Format", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRtfClipboardFormat(DataFormat format)
    {
        return format.Identifier.Contains("rtf", StringComparison.OrdinalIgnoreCase)
            || format.Identifier.Contains("rich text", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetClipboardFormatSummaryAsync(IAsyncDataTransfer data)
    {
        var summaries = new List<string>();
        foreach (var item in data.Items)
        {
            foreach (var format in item.Formats)
            {
                var value = await item.TryGetRawAsync(format);
                summaries.Add($"{format.Identifier}:{value?.GetType().Name ?? "null"}");
            }
        }

        return string.Join(", ", summaries.Distinct(StringComparer.Ordinal));
    }

    private static string? TryGetClipboardString(object? value)
    {
        return value switch
        {
            string text => text,
            byte[] bytes => DecodeClipboardBytes(bytes),
            _ => null
        };
    }

    private static string DecodeClipboardBytes(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes);
            }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        var evenNulls = 0;
        var oddNulls = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0)
            {
                if (i % 2 == 0)
                {
                    evenNulls++;
                }
                else
                {
                    oddNulls++;
                }
            }
        }

        return oddNulls > bytes.Length / 4 && evenNulls == 0
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
    }

    private void ReplaceSelection(string replacementText)
    {
        var selectionStart = NoteEditor.SelectionStart;
        var caretOffset = selectionStart + replacementText.Length;
        ApplyEdit(new MarkdownTextEdit(selectionStart, NoteEditor.SelectionLength, replacementText, caretOffset, 0, caretOffset));
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
            || exception.Message.Contains("has no configured model name", StringComparison.Ordinal)
            || exception.Message.Contains("is not configured", StringComparison.Ordinal);
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

    private void CancelAssistantRequest()
    {
        try
        {
            _assistantRequestCancellation?.Cancel();
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

    private void DisposeAssistantRequestCancellation()
    {
        try
        {
            _assistantRequestCancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _assistantRequestCancellation = null;
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
        using (NoteEditor.Document.RunUpdate())
        {
            if (edit.ReplacementLength != 0 || edit.ReplacementText.Length != 0)
            {
                NoteEditor.Document.Replace(edit.ReplacementStart, edit.ReplacementLength, edit.ReplacementText);
            }

            var selectionStart = Math.Clamp(edit.SelectionStart, 0, NoteEditor.Document.TextLength);
            var selectionLength = Math.Clamp(edit.SelectionLength, 0, NoteEditor.Document.TextLength - selectionStart);
            var caretOffset = Math.Clamp(edit.CaretOffset, 0, NoteEditor.Document.TextLength);

            NoteEditor.Select(selectionStart, selectionLength);
            NoteEditor.CaretOffset = caretOffset;
        }

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
                ClearPendingAssistantResult();
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

    internal static bool IsOpenRecentDialogShortcut(Key key, KeyModifiers modifiers)
    {
        return key == Key.R && IsCommandModifier(modifiers);
    }

    internal static bool IsFormatTablesShortcut(Key key, KeyModifiers modifiers)
    {
        return key == Key.T && (modifiers == (KeyModifiers.Control | KeyModifiers.Alt)
            || modifiers == (KeyModifiers.Meta | KeyModifiers.Alt));
    }

    internal static bool IsPasteShortcut(Key key, KeyModifiers modifiers)
    {
        return key == Key.V && IsCommandModifier(modifiers);
    }

    internal static bool TryBeginOpenRecentDialog(ref bool isRecentNoteDialogOpen)
    {
        if (isRecentNoteDialogOpen)
        {
            return false;
        }

        isRecentNoteDialogOpen = true;
        return true;
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

    private async Task<RecentNoteSummary> CreateRecentFinalNoteSummaryAsync(
        string filePath,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var title = Path.GetFileNameWithoutExtension(filePath);
        var content = string.Empty;

        try
        {
            content = await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to read recent note contents for filtering from {FilePath}.", filePath);
        }

        return new RecentNoteSummary(filePath, createdAt, title)
        {
            SearchText = BuildRecentNoteSearchText(filePath, title, content)
        };
    }

    private static string BuildRecentNoteSearchText(string filePath, string title, string content)
    {
        var parts = new StringBuilder();
        AppendSearchPart(parts, title);
        AppendSearchPart(parts, Path.GetFileName(filePath));
        AppendSearchPart(parts, filePath);
        AppendSearchPart(parts, content);
        return parts.ToString();
    }

    private static void AppendSearchPart(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(value);
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private void DeleteDraftAssetsIfExists(string draftFilePath)
    {
        var draftAssetsDirectory = AttachmentImportPaths.GetDraftAssetsDirectory(draftFilePath);
        if (!Directory.Exists(draftAssetsDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(draftAssetsDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete empty draft attachment staging folder {DraftAssetsDirectory}.", draftAssetsDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Notey does not have permission to delete empty draft attachment staging folder {DraftAssetsDirectory}.", draftAssetsDirectory);
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
        ITaskStore TaskStore,
        DraftProcessingService DraftProcessingService,
        NoteyAssistantService AssistantService,
        FileImportService FileImportService);

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
