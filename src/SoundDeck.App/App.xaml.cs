using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SoundDeck.Audio;
using SoundDeck.Core;
using SoundDeck.Infrastructure;

namespace SoundDeck_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public static IServiceProvider Services { get; private set; } = null!;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISoundRepository, SqliteSoundRepository>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IAudioMetadataService, AudioMetadataService>();
        services.AddSingleton<IAudioEngine, AudioEngine>();
        services.AddSingleton<IHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<IMidiInputService, MidiInputService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
