using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Notey.Core.Configuration;

namespace Notey.App.Setup;

public enum SetupWizardMode
{
    InitialSetup,
    ImportOnly
}

public sealed class SetupWizardWindow : Window
{
    private readonly SetupWizardMode mode;
    private readonly TextBlock validationText = new();
    private readonly TextBox vaultRootInput = CreateTextBox();
    private readonly TextBox sourceFolderInput = CreateTextBox();
    private readonly TextBox customersInput = CreateMultiLineTextBox();
    private readonly TextBox projectsInput = CreateMultiLineTextBox();
    private readonly TextBox topicsInput = CreateMultiLineTextBox();

    private SetupWizardWindow(NoteyOptions options, SetupWizardMode mode)
    {
        this.mode = mode;
        Title = mode == SetupWizardMode.InitialSetup ? "Set up Notey" : "Import documents";
        Width = 720;
        Height = mode == SetupWizardMode.InitialSetup ? 760 : 620;
        MinWidth = 640;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        vaultRootInput.Text = options.Vault.RootPath;
        Content = BuildContent();
    }

    public static async Task<SetupWizardResult?> ShowAsync(Window owner, NoteyOptions options, SetupWizardMode mode)
    {
        var dialog = new SetupWizardWindow(options, mode);
        return await dialog.ShowDialog<SetupWizardResult?>(owner);
    }

    private Control BuildContent()
    {
        var primaryButton = new Button
        {
            Content = mode == SetupWizardMode.InitialSetup ? "Complete setup" : "Import documents",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 128
        };
        primaryButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 86
        };
        cancelButton.Click += (_, _) => Close(null);

        validationText.Foreground = Brush.Parse("#FFB4AB");
        validationText.TextWrapping = TextWrapping.Wrap;
        validationText.IsVisible = false;

        var form = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                CreateHeader(),
                validationText,
            }
        };

        if (mode == SetupWizardMode.InitialSetup)
        {
            form.Children.Add(CreateSection("Vault", [
                CreateField("Vault root", vaultRootInput),
                CreateWarning("Choose the folder Notey should manage. Notey will create Images, Notes, Notes/Draft, and People under this root.")
            ]));
        }

        form.Children.Add(CreateSection("Vault structure", [
            CreateField("Customers", customersInput),
            CreateField("Projects", projectsInput),
            CreateField("Topics", topicsInput),
            CreateWarning("Enter one value per line, or separate values with commas/semicolons. Notey creates fixed top-level folders under Notes.")
        ]));
        form.Children.Add(CreateSection(mode == SetupWizardMode.InitialSetup ? "Initial import" : "Import", [
            CreateField("Source folder", sourceFolderInput),
            CreateWarning("All files under this folder are copied into Notey. Markdown/text and .msg files become notes; unknown files become managed attachments.")
        ]));

        return new Border
        {
            Background = Brush.Parse("#10131A"),
            BorderBrush = Brush.Parse("#424754"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(24),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Avalonia.Thickness(0, 16, 0, 0),
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Children = { cancelButton, primaryButton }
                    },
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = form
                    }
                }
            }
        };
    }

    private Control CreateHeader()
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = mode == SetupWizardMode.InitialSetup ? "Welcome to Notey" : "Import existing documents",
                    Foreground = Brush.Parse("#E1E2EC"),
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = mode == SetupWizardMode.InitialSetup
                        ? "Choose a vault root, set up the default structure, and optionally import existing documents."
                        : "Copy a folder of existing documents into the managed Notey vault.",
                    Foreground = Brush.Parse("#C2C6D6"),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private static Control CreateSection(string title, IEnumerable<Control> controls)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush.Parse("#ADC6FF"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        });

        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    private static Control CreateField(string label, Control input)
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = Brush.Parse("#8C909F"),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.SemiBold
                },
                input.WithColumn(1)
            }
        };
    }

    private static Control CreateWarning(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush.Parse("#C2C6D6"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(190, 0, 0, 0)
        };
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            MinHeight = 34,
            Background = Brush.Parse("#1D2027"),
            Foreground = Brush.Parse("#E1E2EC"),
            BorderBrush = Brush.Parse("#424754"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(10, 6)
        };
    }

    private static TextBox CreateMultiLineTextBox()
    {
        var textBox = CreateTextBox();
        textBox.AcceptsReturn = true;
        textBox.MinHeight = 72;
        return textBox;
    }

    private void SaveAndClose()
    {
        var errors = new List<string>();
        var vaultRoot = Trim(vaultRootInput.Text);
        if (mode == SetupWizardMode.InitialSetup && string.IsNullOrWhiteSpace(vaultRoot))
        {
            errors.Add("Vault root is required for first-run setup.");
        }

        var sourceFolder = Trim(sourceFolderInput.Text);
        if (!string.IsNullOrWhiteSpace(sourceFolder) && !Directory.Exists(sourceFolder))
        {
            errors.Add("Source folder must exist.");
        }

        if (mode == SetupWizardMode.ImportOnly && string.IsNullOrWhiteSpace(sourceFolder))
        {
            errors.Add("Source folder is required for import.");
        }

        if (errors.Count > 0)
        {
            validationText.Text = string.Join(Environment.NewLine, errors);
            validationText.IsVisible = true;
            return;
        }

        Close(new SetupWizardResult(
            vaultRoot,
            sourceFolder,
            ParseValues(customersInput.Text),
            ParseValues(projectsInput.Text),
            ParseValues(topicsInput.Text)));
    }

    private static IReadOnlyList<string> ParseValues(string? value)
    {
        return (value ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Trim(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

public sealed record SetupWizardResult(
    string VaultRootPath,
    string SourceFolderPath,
    IReadOnlyList<string> Customers,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Topics)
{
    public VaultBootstrapRequest ToBootstrapRequest()
    {
        return new VaultBootstrapRequest(Customers, Projects, Topics);
    }
}

file static class SetupWizardControlExtensions
{
    public static T WithColumn<T>(this T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
