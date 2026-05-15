using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Notey.Tests.HeadlessAvaloniaTestApplication))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Notey.Tests;

public static class HeadlessAvaloniaTestApplication
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true
            });
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            AddNoteyResources();
            Styles.Add(new FluentTheme());
            Styles.Add(new StyleInclude(new Uri("avares://Notey.Tests"))
            {
                Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
            });
        }

        private void AddNoteyResources()
        {
            Resources["Notey.SurfaceBrush"] = Brush.Parse("#10131A");
            Resources["Notey.SurfaceLowestBrush"] = Brush.Parse("#0B0E15");
            Resources["Notey.SurfaceLowBrush"] = Brush.Parse("#191B23");
            Resources["Notey.SurfaceBrushRaised"] = Brush.Parse("#1D2027");
            Resources["Notey.SurfaceHighBrush"] = Brush.Parse("#272A31");
            Resources["Notey.SurfaceHighestBrush"] = Brush.Parse("#32353C");
            Resources["Notey.PrimaryTextBrush"] = Brush.Parse("#E1E2EC");
            Resources["Notey.SecondaryTextBrush"] = Brush.Parse("#C2C6D6");
            Resources["Notey.MutedTextBrush"] = Brush.Parse("#8C909F");
            Resources["Notey.SubtleTextBrush"] = Brush.Parse("#565B68");
            Resources["Notey.OutlineBrush"] = Brush.Parse("#8C909F");
            Resources["Notey.OutlineVariantBrush"] = Brush.Parse("#424754");
            Resources["Notey.PrimaryBrush"] = Brush.Parse("#ADC6FF");
            Resources["Notey.PrimaryContainerBrush"] = Brush.Parse("#18253E");
            Resources["Notey.PrimaryBorderBrush"] = Brush.Parse("#2E4F8E");
            Resources["Notey.TertiaryBrush"] = Brush.Parse("#FFB786");
            Resources["Notey.TertiaryContainerBrush"] = Brush.Parse("#2A1D17");
            Resources["Notey.TertiaryBorderBrush"] = Brush.Parse("#6D3D1B");
            Resources["Notey.ErrorBrush"] = Brush.Parse("#FFB4AB");
        }
    }
}
