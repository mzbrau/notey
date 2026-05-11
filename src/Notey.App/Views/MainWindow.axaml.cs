using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.Core.Configuration;

namespace Notey.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
        : this(new NoteyOptions(), NullLogger<MainWindow>.Instance)
    {
    }

    public MainWindow(NoteyOptions options, ILogger<MainWindow> logger)
    {
        InitializeComponent();

        Width = options.Ui.DefaultWindowWidth;
        Height = options.Ui.DefaultWindowHeight;

        logger.LogInformation("Notey shell initialized with {Theme} theme.", options.Ui.Theme);
    }
}
