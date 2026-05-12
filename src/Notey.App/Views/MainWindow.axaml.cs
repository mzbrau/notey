using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Controls;
using Notey.App.Editing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Vault.Abstractions;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly INoteDraftStore _noteDraftStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly CancellationTokenSource _windowClosed = new();
    private readonly SemaphoreSlim _autosaveGate = new(1, 1);
    private NoteDraft? _currentDraft;
    private bool _isInitializing;
    private bool _isCloseConfirmed;
    private bool _isClosePending;
    private string _lastSavedText = string.Empty;

    public MainWindow()
        : this(CreateDefaultDependencies(), TimeProvider.System, NullLogger<MainWindow>.Instance)
    {
    }

    private MainWindow(
        (NoteyOptions Options, INoteDraftStore NoteDraftStore) dependencies,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger)
        : this(dependencies.Options, dependencies.NoteDraftStore, timeProvider, logger)
    {
    }

    public MainWindow(
        NoteyOptions options,
        INoteDraftStore noteDraftStore,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _noteDraftStore = noteDraftStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _autosaveTimer.Tick += AutosaveTimerOnTick;

        Width = options.Ui.DefaultWindowWidth;
        Height = options.Ui.DefaultWindowHeight;

        ConfigureEditor();
        UpdateEditorStatus();

        Opened += async (_, _) => await CreateInitialDraftAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _windowClosed.Cancel();
            _windowClosed.Dispose();
            _autosaveGate.Dispose();
        };

        logger.LogInformation("Notey shell initialized with {Theme} theme.", options.Ui.Theme);
    }

    private static (NoteyOptions Options, INoteDraftStore NoteDraftStore) CreateDefaultDependencies()
    {
        var options = new NoteyOptions();

        return (options, new FileSystemNoteDraftStore(
            new FileSystemVaultWorkspace(options),
            new NoteTemplateFactory(),
            new NoteFileNameGenerator()));
    }

    private void ConfigureEditor()
    {
        NoteEditor.TextChanged += (_, _) =>
        {
            if (_isInitializing)
            {
                return;
            }

            AutosaveStatusText.Text = "UNSAVED CHANGES";
            UpdateEditorStatus();
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        };

        NoteEditor.KeyDown += OnEditorKeyDown;
        NoteEditor.KeyUp += (_, _) => UpdateEditorStatus();
        NoteEditor.PointerReleased += (_, _) => UpdateEditorStatus();
        NoteEditor.Options.EnableHyperlinks = true;
        NoteEditor.Options.EnableEmailHyperlinks = true;
        NoteEditor.TextArea.TextView.LineTransformers.Add(new MarkdownColorizingTransformer());
    }

    private async Task CreateInitialDraftAsync()
    {
        _isInitializing = true;

        try
        {
            _currentDraft = await _noteDraftStore.CreateAsync(_timeProvider.GetLocalNow(), _windowClosed.Token);
            NoteEditor.Document.Text = _currentDraft.Content;
            NoteEditor.CaretOffset = NoteEditor.Document.TextLength;
            _lastSavedText = _currentDraft.Content;
            NoteEditor.IsReadOnly = false;
            AutosaveStatusText.Text = "SAVED";
            UpdateEditorStatus();
            NoteEditor.Focus();
        }
        catch (OperationCanceledException) when (_windowClosed.IsCancellationRequested)
        {
            _logger.LogDebug("Draft creation was cancelled because the window closed.");
        }
        catch (IOException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Failed to create the initial note draft.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey does not have permission to create the initial note draft.");
        }
        catch (InvalidOperationException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration prevented draft creation.");
        }
        finally
        {
            _isInitializing = false;
        }
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
            return string.IsNullOrEmpty(NoteEditor.Document.Text);
        }

        var acquired = false;
        var savedSnapshot = false;

        try
        {
            await _autosaveGate.WaitAsync(_windowClosed.Token);
            acquired = true;

            var text = NoteEditor.Document.Text;
            if (string.Equals(text, _lastSavedText, StringComparison.Ordinal))
            {
                AutosaveStatusText.Text = "SAVED";
                return true;
            }

            AutosaveStatusText.Text = "SAVING";
            await _noteDraftStore.SaveAsync(_currentDraft, text, _windowClosed.Token);
            _lastSavedText = text;
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
        finally
        {
            if (acquired)
            {
                _autosaveGate.Release();
            }

            if (savedSnapshot && _currentDraft is not null && !string.Equals(NoteEditor.Document.Text, _lastSavedText, StringComparison.Ordinal))
            {
                AutosaveStatusText.Text = "UNSAVED CHANGES";
                _autosaveTimer.Stop();
                _autosaveTimer.Start();
            }
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
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
}
