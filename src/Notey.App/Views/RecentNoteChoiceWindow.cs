using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed class RecentNoteChoiceWindow : Window
{
    private readonly ListBox _recentNoteList = new();

    public RecentNoteChoiceWindow(IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        ArgumentNullException.ThrowIfNull(recentNotes);

        Title = "Open previous note";
        Width = 560;
        Height = recentNotes.Count == 0 ? 280 : 420;
        MinWidth = 560;
        MinHeight = 280;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        var startNewButton = new Button
        {
            Content = "Start new note",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        startNewButton.Click += (_, _) => Close(RecentNoteChoice.NewNote);

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => Close(RecentNoteChoice.Cancel);

        var contentChildren = new List<Control>
        {
            new TextBlock
            {
                Text = "Open previous note",
                Foreground = new SolidColorBrush(Color.Parse("#E1E2EC")),
                FontSize = 20,
                FontWeight = FontWeight.SemiBold
            },
            new TextBlock
            {
                Text = recentNotes.Count == 0
                    ? "No previous notes were created in the last week."
                    : "Select a note from the last week to open it.",
                Foreground = new SolidColorBrush(Color.Parse("#C2C6D6")),
                TextWrapping = TextWrapping.Wrap
            }
        };

        if (recentNotes.Count > 0)
        {
            _recentNoteList.ItemsSource = recentNotes.Select(static note => new RecentNoteChoiceItem(note)).ToArray();
            _recentNoteList.PointerReleased += OnRecentNotePointerReleased;
            _recentNoteList.KeyDown += OnRecentNoteListKeyDown;
            contentChildren.Add(_recentNoteList);
        }

        contentChildren.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                startNewButton
            }
        });

        var stack = new StackPanel
        {
            Spacing = 14
        };

        foreach (var child in contentChildren)
        {
            stack.Children.Add(child);
        }

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#10131A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#424754")),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(24),
            Child = stack
        };
    }

    public static async Task<RecentNoteChoice> ShowAsync(Window owner, IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        var dialog = new RecentNoteChoiceWindow(recentNotes);
        return await dialog.ShowDialog<RecentNoteChoice>(owner);
    }

    private void OnRecentNotePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            CloseSelectedNote();
        }
    }

    private void OnRecentNoteListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CloseSelectedNote();
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

    private sealed record RecentNoteChoiceItem(RecentNoteSummary Note)
    {
        public override string ToString()
        {
            var fileName = Path.GetFileName(Note.FilePath);
            return $"{Note.Title}\n{fileName} - Created {Note.CreatedAt:ddd, HH:mm}";
        }
    }
}
