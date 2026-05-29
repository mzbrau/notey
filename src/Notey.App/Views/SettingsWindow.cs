using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Notey.App.Configuration;
using Notey.Core.Configuration;

namespace Notey.App.Views;

public sealed class SettingsWindow : Window
{
    private readonly NoteyOptions _sourceOptions;
    private readonly TextBlock _validationText = new();
    private readonly TextBox _windowWidthInput = CreateTextBox();
    private readonly TextBox _windowHeightInput = CreateTextBox();
    private readonly TextBox _hotkeyInput = CreateTextBox();
    private readonly TextBox _vaultRootInput = CreateTextBox();
    private readonly TextBox _aiProviderInput = CreateTextBox();
    private readonly TextBox _aiBaseUrlInput = CreateTextBox();
    private readonly TextBox _aiModelInput = CreateTextBox();
    private readonly TextBox _aiEnvironmentVariableInput = CreateTextBox();
    private readonly TextBox _aiApiKeyInput = CreateTextBox();
    private readonly CheckBox _storeApiKeyInput = new() { Content = "Store API key in plaintext local settings" };
    private readonly TextBox _aiTimeoutInput = CreateTextBox();
    private readonly TextBox _tesseractDataInput = CreateTextBox();
    private readonly TextBox _ocrLanguageInput = CreateTextBox();
    private readonly CheckBox _spellcheckEnabledInput = new() { Content = "Enable editor spellcheck" };
    private readonly TextBox _spellcheckLanguageInput = CreateTextBox();

    private SettingsWindow(NoteyOptions options)
    {
        _sourceOptions = NoteySettingsStore.Clone(options);

        Title = "Notey settings";
        Width = 680;
        Height = 720;
        MinWidth = 620;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        _aiApiKeyInput.PasswordChar = '*';

        PopulateInputs(_sourceOptions);
        Content = BuildContent();
    }

    public static async Task<NoteyOptions?> ShowAsync(Window owner, NoteyOptions options)
    {
        var dialog = new SettingsWindow(options);
        return await dialog.ShowDialog<NoteyOptions?>(owner);
    }

    private Control BuildContent()
    {
        var saveButton = new Button
        {
            Content = "Save settings",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 112
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 86
        };
        cancelButton.Click += (_, _) => Close(null);

        _validationText.Foreground = Brush.Parse("#FFB4AB");
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.IsVisible = false;

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
                        Children =
                        {
                            cancelButton,
                            saveButton
                        }
                    },
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new StackPanel
                        {
                            Spacing = 18,
                            Children =
                            {
                                CreateHeader(),
                                _validationText,
                                CreateSection("Window", [
                                    CreateField("Default width", _windowWidthInput),
                                    CreateField("Default height", _windowHeightInput),
                                    CreateField("Open-note hotkey", _hotkeyInput),
                                ]),
                                 CreateSection("Vault", [
                                     CreateField("Vault root", _vaultRootInput),
                                     CreateWarning("Notey owns Images, Notes, Notes/Draft, and People under this root."),
                                 ]),
                                CreateSection("AI", [
                                    CreateField("Default provider id", _aiProviderInput),
                                    CreateField("Base URL", _aiBaseUrlInput),
                                    CreateField("Model name", _aiModelInput),
                                    CreateField("API-key environment variable", _aiEnvironmentVariableInput),
                                    CreateField("API key", _aiApiKeyInput),
                                    _storeApiKeyInput,
                                    CreateWarning("Plaintext API keys are written to appsettings.Local.json. Prefer environment variables when possible."),
                                    CreateField("Request timeout seconds", _aiTimeoutInput),
                                ]),
                                CreateSection("OCR", [
                                    CreateField("Tesseract data path", _tesseractDataInput),
                                    CreateField("OCR language", _ocrLanguageInput),
                                    CreateWarning("OCR path changes are saved immediately but may require restart before running existing services."),
                                ]),
                                CreateSection("Editor", [
                                    _spellcheckEnabledInput,
                                    CreateField("Spellcheck language", _spellcheckLanguageInput),
                                    CreateWarning("Spellcheck currently supports the bundled en-US dictionary."),
                                ]),
                                CreateSection("Shortcuts", [
                                    CreateWarning("On macOS, Ctrl = ⌘. The open-note hotkey is configurable in the Window section above."),
                                    CreateShortcutRow("Ctrl+T", "New task"),
                                    CreateShortcutRow("Ctrl+R", "Open recent note"),
                                    CreateShortcutRow("Ctrl+B", "Bold"),
                                    CreateShortcutRow("Ctrl+I", "Italic"),
                                    CreateShortcutRow("Ctrl+Alt+T", "Format tables"),
                                    CreateShortcutRow("Tab / Shift+Tab", "Indent / Unindent list item"),
                                    CreateShortcutRow("Ctrl+V", "Paste (with format conversion)"),
                                    CreateShortcutRow("Enter", "Smart new list item in editor"),
                                ]),
                            }
                        }
                    }
                }
            }
        };
    }

    private static Control CreateHeader()
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "Settings",
                    Foreground = Brush.Parse("#E1E2EC"),
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Local machine settings are saved to appsettings.Local.json.",
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

    private static Control CreateShortcutRow(string keys, string description)
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = keys,
                    Foreground = Brush.Parse("#8C909F"),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = description,
                    Foreground = Brush.Parse("#C2C6D6"),
                    VerticalAlignment = VerticalAlignment.Center
                }.WithColumn(1)
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

    private void PopulateInputs(NoteyOptions options)
    {
        _windowWidthInput.Text = options.Ui.DefaultWindowWidth.ToString();
        _windowHeightInput.Text = options.Ui.DefaultWindowHeight.ToString();
        _hotkeyInput.Text = options.Hotkeys.OpenNote;
        _vaultRootInput.Text = options.Vault.RootPath;
        _aiProviderInput.Text = options.Ai.DefaultProviderId;
        _aiBaseUrlInput.Text = options.Ai.BaseUrl;
        _aiModelInput.Text = options.Ai.ModelName;
        _aiEnvironmentVariableInput.Text = options.Ai.ApiKeyEnvironmentVariable;
        _aiApiKeyInput.Text = options.Ai.StoreApiKeyInPlaintext ? options.Ai.ApiKey : string.Empty;
        _storeApiKeyInput.IsChecked = options.Ai.StoreApiKeyInPlaintext;
        _aiTimeoutInput.Text = options.Ai.RequestTimeoutSeconds.ToString();
        _tesseractDataInput.Text = options.Ocr.TesseractDataPath;
        _ocrLanguageInput.Text = options.Ocr.DefaultLanguage;
        _spellcheckEnabledInput.IsChecked = options.Spellcheck.Enabled;
        _spellcheckLanguageInput.Text = options.Spellcheck.Language;
    }

    private void SaveAndClose()
    {
        var options = NoteySettingsStore.Clone(_sourceOptions);
        var errors = ApplyInputs(options);
        errors.AddRange(NoteySettingsStore.Validate(options));
        if (errors.Count > 0)
        {
            _validationText.Text = string.Join(Environment.NewLine, errors);
            _validationText.IsVisible = true;
            return;
        }

        Close(options);
    }

    private List<string> ApplyInputs(NoteyOptions options)
    {
        var errors = new List<string>();
        options.Ui.Theme = "Dark";
        if (TryParseInt(_windowWidthInput.Text, "Default width", errors, out var width))
        {
            options.Ui.DefaultWindowWidth = width;
        }

        if (TryParseInt(_windowHeightInput.Text, "Default height", errors, out var height))
        {
            options.Ui.DefaultWindowHeight = height;
        }

        options.Hotkeys.OpenNote = Trim(_hotkeyInput.Text);
        options.Vault.RootPath = Trim(_vaultRootInput.Text);
        options.Ai.DefaultProviderId = Trim(_aiProviderInput.Text);
        options.Ai.BaseUrl = Trim(_aiBaseUrlInput.Text);
        options.Ai.ModelName = Trim(_aiModelInput.Text);
        options.Ai.ApiKeyEnvironmentVariable = Trim(_aiEnvironmentVariableInput.Text);
        options.Ai.StoreApiKeyInPlaintext = _storeApiKeyInput.IsChecked == true;
        options.Ai.ApiKey = options.Ai.StoreApiKeyInPlaintext ? Trim(_aiApiKeyInput.Text) : string.Empty;
        if (TryParseInt(_aiTimeoutInput.Text, "AI request timeout", errors, out var timeout))
        {
            options.Ai.RequestTimeoutSeconds = timeout;
        }

        options.Ocr.TesseractDataPath = Trim(_tesseractDataInput.Text);
        options.Ocr.DefaultLanguage = Trim(_ocrLanguageInput.Text);
        options.Spellcheck.Enabled = _spellcheckEnabledInput.IsChecked == true;
        options.Spellcheck.Language = Trim(_spellcheckLanguageInput.Text);

        return errors;
    }

    private static bool TryParseInt(string? value, string fieldName, ICollection<string> errors, out int result)
    {
        if (int.TryParse(value, out result))
        {
            return true;
        }

        errors.Add($"{fieldName} must be a whole number.");
        return false;
    }

    private static string Trim(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

file static class SettingsWindowControlExtensions
{
    public static T WithColumn<T>(this T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
