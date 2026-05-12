using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed class RecentNoteChoiceWindow : Window
{
    public RecentNoteChoiceWindow(NoteDraft recentDraft)
    {
        Title = "Resume note?";
        Width = 420;
        Height = 220;
        MinWidth = 420;
        MinHeight = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        var resumeButton = new Button
        {
            Content = "Resume previous note",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        resumeButton.Click += (_, _) => Close(RecentNoteChoice.Resume);

        var newButton = new Button
        {
            Content = "Start new note",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        newButton.Click += (_, _) => Close(RecentNoteChoice.NewNote);

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => Close(RecentNoteChoice.Cancel);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#10131A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#424754")),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(24),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Resume recent note?",
                        Foreground = new SolidColorBrush(Color.Parse("#E1E2EC")),
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = $"{Path.GetFileName(recentDraft.FilePath)}\nCreated {recentDraft.CreatedAt:ddd, HH:mm}",
                        Foreground = new SolidColorBrush(Color.Parse("#C2C6D6")),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            cancelButton,
                            newButton,
                            resumeButton
                        }
                    }
                }
            }
        };
    }

    public static async Task<RecentNoteChoice> ShowAsync(Window owner, NoteDraft recentDraft)
    {
        var dialog = new RecentNoteChoiceWindow(recentDraft);
        return await dialog.ShowDialog<RecentNoteChoice>(owner);
    }
}
