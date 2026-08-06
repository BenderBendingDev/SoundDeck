using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SoundDeck.Core;
using Windows.Storage.Pickers;

namespace SoundDeck_App;

public sealed partial class MainPage : Page
{
    private bool _initialized;
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_initialized)
            return;
        _initialized = true;
        await ViewModel.InitializeAsync(MainWindow.Instance.WindowHandle);
    }

    private async void ImportSound_Click(object sender, RoutedEventArgs args)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".ogg");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".aac");
        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
            await ViewModel.ImportAsync(file.Path);
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Enter)
            await ViewModel.SearchAsync();
    }

    private async void Category_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        await ViewModel.SearchAsync();

    private async void Sounds_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        await ViewModel.SaveSoundOrderAsync();

    private async void EditSound_Click(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not SoundClip sound)
            return;

        var name = new TextBox { Header = "Nombre", Text = sound.Name };
        var start = NumberBox("Inicio (segundos)", sound.TrimStartSeconds, 0, sound.DurationSeconds);
        var end = NumberBox("Final (segundos)", sound.EffectiveEndSeconds, 0, sound.DurationSeconds);
        var fadeIn = NumberBox("Fundido de entrada (segundos)", sound.FadeInSeconds, 0, 30);
        var fadeOut = NumberBox("Fundido de salida (segundos)", sound.FadeOutSeconds, 0, 30);
        var gain = NumberBox("Ganancia (dB)", sound.GainDb, -60, 18);
        var hotkey = new TextBox { Header = "Atajo global", Text = sound.Hotkey, PlaceholderText = "Ej.: Ctrl+Shift+1" };
        var route = new ComboBox
        {
            Header = "Salida",
            ItemsSource = Enum.GetValues<AudioRoute>(),
            SelectedItem = sound.Route
        };
        var category = new ComboBox
        {
            Header = "Categoría",
            DisplayMemberPath = "Name",
            ItemsSource = ViewModel.Categories,
            SelectedItem = ViewModel.Categories.FirstOrDefault(item => item.Id == sound.CategoryId),
            PlaceholderText = "Sin categoría"
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(CreateWaveform(sound));
        content.Children.Add(name);
        content.Children.Add(category);
        content.Children.Add(route);
        content.Children.Add(start);
        content.Children.Add(end);
        content.Children.Add(fadeIn);
        content.Children.Add(fadeOut);
        content.Children.Add(gain);
        content.Children.Add(hotkey);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Editar {sound.Name}",
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var original = (sound.Name, sound.CategoryId, sound.Route, sound.TrimStartSeconds, sound.TrimEndSeconds,
            sound.FadeInSeconds, sound.FadeOutSeconds, sound.GainDb, sound.Hotkey);
        try
        {
            sound.Name = name.Text.Trim();
            sound.CategoryId = (category.SelectedItem as SoundCategory)?.Id;
            sound.Route = (AudioRoute)(route.SelectedItem ?? AudioRoute.Both);
            sound.TrimStartSeconds = start.Value;
            sound.TrimEndSeconds = end.Value;
            sound.FadeInSeconds = fadeIn.Value;
            sound.FadeOutSeconds = fadeOut.Value;
            sound.GainDb = gain.Value;
            sound.Hotkey = string.IsNullOrWhiteSpace(hotkey.Text) ? null : hotkey.Text.Trim();
            await ViewModel.SaveSoundAsync(sound);
        }
        catch (Exception exception)
        {
            (sound.Name, sound.CategoryId, sound.Route, sound.TrimStartSeconds, sound.TrimEndSeconds,
                sound.FadeInSeconds, sound.FadeOutSeconds, sound.GainDb, sound.Hotkey) = original;
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void NormalizeSound_Click(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is SoundClip sound)
            await ViewModel.NormalizeAsync(sound);
    }

    private void LearnMidi_Click(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is SoundClip sound)
            ViewModel.BeginMidiLearn(sound);
    }

    private async void DeleteSound_Click(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not SoundClip sound)
            return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Eliminar sonido",
            Content = $"¿Quieres eliminar “{sound.Name}” del tablero? El archivo importado se conservará.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteSoundCommand.ExecuteAsync(sound);
    }

    private async void Backup_Click(object sender, RoutedEventArgs args)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"SoundDeck-{DateTime.Now:yyyyMMdd}" };
        picker.FileTypeChoices.Add("Copia de SoundDeck", [".zip"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            await ViewModel.CreateBackupAsync(file.Path);
    }

    private async void Restore_Click(object sender, RoutedEventArgs args)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await ViewModel.RestoreBackupAsync(file.Path);
    }

    private static NumberBox NumberBox(string header, double value, double minimum, double maximum) =>
        new()
        {
            Header = header,
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 0.05
        };

    private Border CreateWaveform(SoundClip sound)
    {
        const double width = 520;
        const double height = 80;
        var values = ViewModel.GetWaveform(sound);
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.MediumPurple),
            StrokeThickness = 1.5
        };
        for (var index = 0; index < values.Count; index++)
        {
            var x = index * width / Math.Max(1, values.Count - 1);
            var y = height / 2 - values[index] * (height / 2 - 4);
            polyline.Points.Add(new Windows.Foundation.Point(x, y));
        }
        return new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 18, 18, 31)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Child = polyline
        };
    }

    private async Task ShowErrorAsync(string message)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "No se pudieron guardar los cambios",
            Content = message,
            CloseButtonText = "Cerrar"
        }.ShowAsync();
    }
}
