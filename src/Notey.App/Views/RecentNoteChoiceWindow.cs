using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed class RecentNoteChoiceWindow : Window
{
    private readonly Button _openSelectedButton = new();
    private readonly TextBox _filterBox = new();
    private readonly ListBox _recentNoteList = new();
    private readonly RecentNoteSummary[] _allRecentNotes;
    private RecentNoteChoice? _dialogResult;
    private bool _hasActivated;

    public RecentNoteChoiceWindow(IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        ArgumentNullException.ThrowIfNull(recentNotes);
        _allRecentNotes = recentNotes.ToArray();

        Title = "Open recent note";
        Width = 760;
        Height = recentNotes.Count == 0 ? 360 : 560;
        MinWidth = 680;
        MinHeight = recentNotes.Count == 0 ? 320 : 440;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        _openSelectedButton.Content = "Open note";
        _openSelectedButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _openSelectedButton.MinWidth = 96;
        _openSelectedButton.IsEnabled = false;
        _openSelectedButton.Click += (_, _) => CloseSelectedNote();

        _filterBox.PlaceholderText = "Filter recent notes";
        _filterBox.MinHeight = 36;
        _filterBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _filterBox.TextChanged += (_, _) => ApplyFilter(_filterBox.Text);

        Content = BuildContent(recentNotes);
        Activated += (_, _) => _hasActivated = true;
        Deactivated += OnWindowDeactivated;
        KeyDown += OnWindowKeyDown;
        Opened += (_, _) => FocusInitialControl();
    }

    public static async Task<RecentNoteChoice> ShowAsync(Window owner, IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        var dialog = new RecentNoteChoiceWindow(recentNotes);
        var completion = new TaskCompletionSource<RecentNoteChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => completion.TrySetResult(dialog._dialogResult);
        dialog.Show(owner);
        var result = await completion.Task;
        return RecentNoteChoice.FromDialogResult(result);
    }

    private Control BuildContent(IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        var startNewButton = new Button
        {
            Content = "Start new note",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 112
        };
        startNewButton.Click += (_, _) => CompleteChoice(RecentNoteChoice.NewNote);

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 88
        };
        cancelButton.Click += (_, _) => CompleteChoice(RecentNoteChoice.Cancel);

        Control noteSelectionContent = recentNotes.Count == 0
            ? CreateEmptyState()
            : CreateRecentNoteList(recentNotes);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                startNewButton
            }
        };

        if (recentNotes.Count > 0)
        {
            buttonBar.Children.Add(_openSelectedButton);
        }

        var contentGrid = new Grid
        {
            RowDefinitions = recentNotes.Count == 0
                ? new RowDefinitions("Auto,*,Auto")
                : new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 16
        };

        contentGrid.Children.Add(CreateHeader(recentNotes.Count));

        if (recentNotes.Count == 0)
        {
            contentGrid.Children.Add(noteSelectionContent.WithGridRow(1));
            contentGrid.Children.Add(buttonBar.WithGridRow(2));
        }
        else
        {
            contentGrid.Children.Add(_filterBox.WithGridRow(1));
            contentGrid.Children.Add(noteSelectionContent.WithGridRow(2));
            contentGrid.Children.Add(buttonBar.WithGridRow(3));
        }

        return new Border
        {
            Background = Brush.Parse("#10131A"),
            BorderBrush = Brush.Parse("#424754"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(28),
            Child = contentGrid
        };
    }

    private Control CreateHeader(int recentNoteCount)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16
        };

        grid.Children.Add(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "Open recent note",
                    Foreground = Brush.Parse("#E1E2EC"),
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Recent notes stay sorted from most recently updated to least recently updated.",
                    Foreground = Brush.Parse("#8C909F"),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });

        if (recentNoteCount > 0)
        {
            var countBadge = new Border
            {
                Background = Brush.Parse("#18253E"),
                BorderBrush = Brush.Parse("#2E4F8E"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                MinHeight = 28,
                Padding = new Thickness(12, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{recentNoteCount} recent note{(recentNoteCount == 1 ? string.Empty : "s")}",
                            Foreground = Brush.Parse("#ADC6FF"),
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center
                        }
                    }
                }
            };

            Grid.SetColumn(countBadge, 1);
            grid.Children.Add(countBadge);
        }

        return grid;
    }

    private Control CreateEmptyState()
    {
        return new Border
        {
            Background = Brush.Parse("#0B0E15"),
            BorderBrush = Brush.Parse("#424754"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Nothing recent to reopen",
                        Foreground = Brush.Parse("#E1E2EC"),
                        FontSize = 17,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Start a new note, or come back once a note has been updated in the last week.",
                        Foreground = Brush.Parse("#C2C6D6"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private Control CreateRecentNoteList(IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        _recentNoteList.Classes.Add("recentNoteList");
        _recentNoteList.SelectionMode = SelectionMode.Single;
        _recentNoteList.BorderThickness = new Thickness(0);
        _recentNoteList.Background = Brushes.Transparent;
        _recentNoteList.ItemTemplate = new FuncDataTemplate<RecentNoteChoiceItem?>((item, _) => CreateRecentNoteCardContent(item?.Note));
        _recentNoteList.KeyDown += OnRecentNoteListKeyDown;
        _recentNoteList.DoubleTapped += OnRecentNoteListDoubleTapped;
        _recentNoteList.SelectionChanged += OnRecentNoteSelectionChanged;
        _recentNoteList[ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto;
        _recentNoteList[ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled;
        ApplyFilter(null);

        return _recentNoteList;
    }

    internal static Control CreateRecentNoteCardContent(RecentNoteSummary? note)
    {
        return note is null
            ? new Border
            {
                IsVisible = false,
                Height = 0,
                Padding = new Thickness(0)
            }
            : CreateRecentNoteCard(RecentNoteChoiceItem.FromSummary(note));
    }

    private static Control CreateRecentNoteCard(RecentNoteChoiceItem item)
    {
        var pathText = new TextBlock
        {
            Text = item.FullPath,
            Classes = { "recentNotePath" },
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTip.SetTip(pathText, item.FullPath);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = item.Title,
                    Classes = { "recentNoteTitle" },
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = item.FileName,
                    Classes = { "recentNoteFileName" },
                    TextWrapping = TextWrapping.Wrap
                },
                pathText,
                new TextBlock
                {
                    Text = item.UpdatedLabel,
                    Classes = { "recentNoteMeta" }
                }
            }
        };
    }

    private void OnRecentNoteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateOpenButtonState();
    }

    private void OnRecentNoteListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CloseSelectedNote();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CompleteChoice(RecentNoteChoice.Cancel);
            e.Handled = true;
        }
    }

    private void OnRecentNoteListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!ShouldOpenSelectedNote(_recentNoteList.SelectedItem is RecentNoteChoiceItem))
        {
            return;
        }

        CloseSelectedNote();
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CompleteChoice(RecentNoteChoice.Cancel);
            e.Handled = true;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!ShouldCloseOnDeactivate(_hasActivated, _dialogResult is not null))
        {
            return;
        }

        CompleteChoice(RecentNoteChoice.Cancel);
    }

    private void CloseSelectedNote()
    {
        if (_recentNoteList.SelectedItem is RecentNoteChoiceItem item)
        {
            CompleteChoice(RecentNoteChoice.Open(item.Note));
        }
    }

    private void CompleteChoice(RecentNoteChoice choice)
    {
        if (_dialogResult is not null)
        {
            return;
        }

        _dialogResult = choice;
        Close();
    }

    private void UpdateOpenButtonState()
    {
        _openSelectedButton.IsEnabled = _recentNoteList.SelectedItem is RecentNoteChoiceItem;
    }

    private void ApplyFilter(string? filterText)
    {
        var selectedFilePath = (_recentNoteList.SelectedItem as RecentNoteChoiceItem)?.Note.FilePath;
        var filteredNotes = FilterRecentNotes(_allRecentNotes, filterText);
        var items = filteredNotes
            .Select(RecentNoteChoiceItem.FromSummary)
            .ToArray();

        _recentNoteList.ItemsSource = items;
        _recentNoteList.SelectedIndex = ResolveSelectedIndex(items, selectedFilePath);
        UpdateOpenButtonState();
    }

    private static int ResolveSelectedIndex(RecentNoteChoiceItem[] items, string? selectedFilePath)
    {
        if (selectedFilePath is not null)
        {
            var existingIndex = Array.FindIndex(items, item => string.Equals(item.Note.FilePath, selectedFilePath, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                return existingIndex;
            }
        }

        return GetPreferredSelectedIndex(items.Length);
    }

    private void FocusInitialControl()
    {
        if (_allRecentNotes.Length == 0)
        {
            return;
        }

        _filterBox.Focus();
    }

    internal static bool ShouldOpenSelectedNote(bool hasSelection)
    {
        return hasSelection;
    }

    internal static IReadOnlyList<RecentNoteSummary> FilterRecentNotes(
        IReadOnlyList<RecentNoteSummary> recentNotes,
        string? filterText)
    {
        ArgumentNullException.ThrowIfNull(recentNotes);

        if (string.IsNullOrWhiteSpace(filterText))
        {
            return recentNotes.ToArray();
        }

        var trimmedFilter = filterText.Trim();
        return recentNotes
            .Where(note => BuildSearchText(note).Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static int GetPreferredSelectedIndex(int filteredCount)
    {
        return filteredCount > 0 ? 0 : -1;
    }

    internal static bool ShouldCloseOnDeactivate(bool hasActivated, bool hasDialogResult)
    {
        return hasActivated && !hasDialogResult;
    }

    private static string BuildSearchText(RecentNoteSummary note)
    {
        return string.IsNullOrWhiteSpace(note.SearchText)
            ? string.Join(
                '\n',
                new[]
                {
                    note.Title,
                    Path.GetFileName(note.FilePath),
                    note.FilePath
                }.Where(static part => !string.IsNullOrWhiteSpace(part)))
            : note.SearchText;
    }

    private sealed record RecentNoteChoiceItem(RecentNoteSummary Note)
    {
        public string Title { get; } = Note.Title;

        public string FileName { get; } = Path.GetFileName(Note.FilePath);

        public string FullPath { get; } = Note.FilePath;

        public string UpdatedLabel { get; } = $"Updated {Note.CreatedAt:ddd, HH:mm}";

        public static RecentNoteChoiceItem FromSummary(RecentNoteSummary note) => new(note);
    }
}

file static class RecentNoteChoiceWindowControlExtensions
{
    public static T WithGridRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
