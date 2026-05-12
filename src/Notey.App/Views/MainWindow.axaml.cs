using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Controls;
using Notey.App.Editing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly INoteDraftStore _noteDraftStore;
    private readonly IVaultEntityStore _vaultEntityStore;
    private readonly NoteMetadataFormatter _metadataFormatter = new();
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly CancellationTokenSource _windowClosed = new();
    private readonly SemaphoreSlim _autosaveGate = new(1, 1);
    private NoteDraft? _currentDraft;
    private bool _isInitializing;
    private bool _isCloseConfirmed;
    private bool _isClosePending;
    private bool _metadataDirty;
    private string _lastSavedText = string.Empty;
    private IReadOnlyList<VaultEntity> _peopleIndex = [];
    private IReadOnlyList<PersonAutocompleteSuggestion> _personSuggestions = [];

    public MainWindow()
        : this(CreateDefaultDependencies(), TimeProvider.System, NullLogger<MainWindow>.Instance)
    {
    }

    private MainWindow(
        (NoteyOptions Options, INoteDraftStore NoteDraftStore, IVaultEntityStore VaultEntityStore) dependencies,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger)
        : this(dependencies.Options, dependencies.NoteDraftStore, dependencies.VaultEntityStore, timeProvider, logger)
    {
    }

    public MainWindow(
        NoteyOptions options,
        INoteDraftStore noteDraftStore,
        IVaultEntityStore vaultEntityStore,
        TimeProvider timeProvider,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _noteDraftStore = noteDraftStore;
        _vaultEntityStore = vaultEntityStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _autosaveTimer.Tick += AutosaveTimerOnTick;

        Width = options.Ui.DefaultWindowWidth;
        Height = options.Ui.DefaultWindowHeight;

        ConfigureEditor();
        ConfigureMetadataInputs();
        UpdateEditorStatus();
        UpdateMetadataChips();

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

    private static (NoteyOptions Options, INoteDraftStore NoteDraftStore, IVaultEntityStore VaultEntityStore) CreateDefaultDependencies()
    {
        var options = new NoteyOptions();
        var workspace = new FileSystemVaultWorkspace(options);
        var linkBuilder = new ObsidianLinkBuilder(workspace);

        return (options, new FileSystemNoteDraftStore(
            workspace,
            new NoteTemplateFactory(),
            new NoteFileNameGenerator()),
            new FileSystemVaultEntityStore(workspace, linkBuilder, TimeProvider.System));
    }

    private void ConfigureEditor()
    {
        NoteEditor.TextChanged += (_, _) =>
        {
            if (_isInitializing)
            {
                return;
            }

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
        NoteEditor.TextArea.TextView.LineTransformers.Add(new MarkdownColorizingTransformer());
    }

    private void ConfigureMetadataInputs()
    {
        PeopleInput.TextChanged += MetadataInputOnTextChanged;
        TopicsInput.TextChanged += MetadataInputOnTextChanged;
        ProjectsInput.TextChanged += MetadataInputOnTextChanged;
        ScreenshotContextInput.TextChanged += MetadataInputOnTextChanged;
        PersonAutocompleteList.PointerReleased += async (_, _) => await InsertSelectedPersonLinkAsync();
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
            DateChipText.Text = _currentDraft.CreatedAt.ToString("ddd, HH:mm");
            await RefreshPeopleIndexAsync();
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
        catch (ArgumentException ex)
        {
            AutosaveStatusText.Text = "SAVE ERROR";
            _logger.LogError(ex, "Notey vault configuration contained an invalid value.");
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
        UpdateMetadataChips();
        ScheduleAutosave();
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

        await RefreshPeopleIndexAsync();

        return new NoteMetadata(
            peopleLinks,
            topicLinks,
            projectLinks,
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

        PeopleChipText.Text = FormatChip(people, "@person", static person => $"@{person}");
        TopicChipText.Text = FormatChip(topics, "#topic", static topic => $"#{topic}");
        ProjectChipText.Text = FormatChip(projects, "[[Project]]", static project => $"[[{project}]]");
    }

    private MetadataInputSnapshot GetMetadataInputSnapshot()
    {
        return new MetadataInputSnapshot(
            PeopleInput.Text ?? string.Empty,
            TopicsInput.Text ?? string.Empty,
            ProjectsInput.Text ?? string.Empty,
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

    private sealed record PersonAutocompleteSuggestion(string Name, bool IsCreateAction, VaultEntity? Entity)
    {
        public override string ToString()
        {
            return IsCreateAction ? $"Create \"{Name}\"" : Name;
        }
    }

    private sealed record MetadataInputSnapshot(string People, string Topics, string Projects, string ScreenshotContext);
}
