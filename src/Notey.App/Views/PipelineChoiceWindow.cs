using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Notey.Pipelines.Definitions;

namespace Notey.App.Views;

public sealed class PipelineChoiceWindow : Window
{
    private readonly ListBox _pipelineList = new();

    public PipelineChoiceWindow(IReadOnlyList<PipelineDefinition> pipelines)
    {
        ArgumentNullException.ThrowIfNull(pipelines);

        Title = "Choose pipeline";
        Width = 520;
        Height = 360;
        MinWidth = 520;
        MinHeight = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        var runButton = new Button
        {
            Content = "Run pipeline",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        runButton.Click += (_, _) => CloseSelectedPipeline();

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => Close(null);

        _pipelineList.ItemsSource = pipelines.Select(static pipeline => new PipelineChoiceItem(pipeline)).ToArray();
        _pipelineList.SelectedIndex = 0;
        _pipelineList.DoubleTapped += (_, _) => CloseSelectedPipeline();

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
                        Text = "Choose screenshot pipeline",
                        Foreground = new SolidColorBrush(Color.Parse("#E1E2EC")),
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Multiple enabled pipelines can process image captures. Pick one to run for this snip.",
                        Foreground = new SolidColorBrush(Color.Parse("#C2C6D6")),
                        TextWrapping = TextWrapping.Wrap
                    },
                    _pipelineList,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            cancelButton,
                            runButton
                        }
                    }
                }
            }
        };
    }

    public static async Task<PipelineDefinition?> ShowAsync(Window owner, IReadOnlyList<PipelineDefinition> pipelines)
    {
        var dialog = new PipelineChoiceWindow(pipelines);
        return await dialog.ShowDialog<PipelineDefinition?>(owner);
    }

    private void CloseSelectedPipeline()
    {
        if (_pipelineList.SelectedItem is PipelineChoiceItem item)
        {
            Close(item.Pipeline);
        }
    }

    private sealed record PipelineChoiceItem(PipelineDefinition Pipeline)
    {
        public override string ToString()
        {
            var name = string.IsNullOrWhiteSpace(Pipeline.DisplayName) ? Pipeline.Id : Pipeline.DisplayName;
            return string.IsNullOrWhiteSpace(Pipeline.Description)
                ? name
                : $"{name} - {Pipeline.Description}";
        }
    }
}
