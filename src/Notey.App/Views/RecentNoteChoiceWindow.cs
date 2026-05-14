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
    private readonly ListBox _recentNoteList = new();

    public RecentNoteChoiceWindow(IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        ArgumentNullException.ThrowIfNull(recentNotes);

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

        Content = BuildContent(recentNotes);
        KeyDown += OnWindowKeyDown;
    }

    public static async Task<RecentNoteChoice> ShowAsync(Window owner, IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        var dialog = new RecentNoteChoiceWindow(recentNotes);
        var result = await dialog.ShowDialog<RecentNoteChoice?>(owner);
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
        startNewButton.Click += (_, _) => Close(RecentNoteChoice.NewNote);

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 88
        };
        cancelButton.Click += (_, _) => Close(RecentNoteChoice.Cancel);

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
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 16
        };

        contentGrid.Children.Add(CreateHeader(recentNotes.Count));
        contentGrid.Children.Add(new TextBlock
        {
            Text = recentNotes.Count == 0
                ? "No recent notes were updated in the last week."
                : "Browse notes updated in the last week. Each entry shows the note title, filename, and full vault path.",
            Foreground = Brush.Parse("#C2C6D6"),
            TextWrapping = TextWrapping.Wrap
        }.WithGridRow(1));
        contentGrid.Children.Add(noteSelectionContent.WithGridRow(2));
        contentGrid.Children.Add(buttonBar.WithGridRow(3));

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
                Padding = new Thickness(12, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = $"{recentNoteCount} recent note{(recentNoteCount == 1 ? string.Empty : "s")}",
                    Foreground = Brush.Parse("#ADC6FF"),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
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
        _recentNoteList.ItemsSource = recentNotes
            .Select(RecentNoteChoiceItem.FromSummary)
            .ToArray();
        _recentNoteList.KeyDown += OnRecentNoteListKeyDown;
        _recentNoteList.SelectionChanged += OnRecentNoteSelectionChanged;
        _recentNoteList[ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto;
        _recentNoteList[ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled;
        _recentNoteList.SelectedIndex = 0;
        UpdateOpenButtonState();

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
            Close(RecentNoteChoice.Cancel);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(RecentNoteChoice.Cancel);
            e.Handled = true;
        }
    }

    private void CloseSelectedNote()
    {
        if (_recentNoteList.SelectedItem is RecentNoteChoiceItem item)
        {
            Close(RecentNoteChoice.Open(item.Note));
        }
    }

    private void UpdateOpenButtonState()
    {
        _openSelectedButton.IsEnabled = _recentNoteList.SelectedItem is RecentNoteChoiceItem;
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
