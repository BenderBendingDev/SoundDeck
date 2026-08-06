using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SoundDeck_App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private TrayIconService? _tray;
    private bool _allowClose;
    public static MainWindow Instance { get; private set; } = null!;
    public nint WindowHandle { get; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 780));
        AppWindow.Closing += OnClosing;
        ConfigureTray();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void ConfigureTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _tray = new TrayIconService(WindowHandle, iconPath);
        _tray.OpenRequested += (_, _) =>
        {
            AppWindow.Show();
            Activate();
        };
        _tray.ToggleMuteRequested += (_, _) =>
        {
            var viewModel = App.Services.GetRequiredService<MainViewModel>();
            viewModel.MicrophoneMuted = !viewModel.MicrophoneMuted;
        };
        _tray.StopRequested += async (_, _) =>
            await App.Services.GetRequiredService<MainViewModel>().StopCommand.ExecuteAsync(null);
        _tray.ExitRequested += (_, _) =>
        {
            _allowClose = true;
            _tray.Dispose();
            _tray = null;
            Close();
        };
    }

    private void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowClose)
            return;
        args.Cancel = true;
        sender.Hide();
    }
}
