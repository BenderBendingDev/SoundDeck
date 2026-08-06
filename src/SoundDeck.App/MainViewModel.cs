using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SoundDeck.Core;

namespace SoundDeck_App;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISoundRepository _repository;
    private readonly ILibraryService _library;
    private readonly IAudioEngine _audio;
    private readonly IAudioDeviceService _devices;
    private readonly IAudioMetadataService _metadata;
    private readonly IHotkeyService _hotkeys;
    private readonly IMidiInputService _midi;
    private readonly IStartupService _startup;
    private readonly DispatcherQueue _dispatcher;
    private AppSettings _settings = new();
    private SoundClip? _midiLearningSound;

    [ObservableProperty] public partial SoundBoard? SelectedBoard { get; set; }
    [ObservableProperty] public partial SoundCategory? SelectedCategory { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Preparando SoundDeck…";
    [ObservableProperty] public partial bool CableAvailable { get; set; }
    [ObservableProperty] public partial bool MicrophoneMuted { get; set; }
    [ObservableProperty] public partial double MicrophoneVolume { get; set; } = 100;
    [ObservableProperty] public partial double MicrophoneLevel { get; set; }
    [ObservableProperty] public partial AudioDevice? SelectedMicrophone { get; set; }
    [ObservableProperty] public partial AudioDevice? SelectedLocalOutput { get; set; }
    [ObservableProperty] public partial AudioDevice? SelectedVirtualOutput { get; set; }
    [ObservableProperty] public partial string? SelectedMidiDevice { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public MainViewModel(
        ISoundRepository repository,
        ILibraryService library,
        IAudioEngine audio,
        IAudioDeviceService devices,
        IAudioMetadataService metadata,
        IHotkeyService hotkeys,
        IMidiInputService midi,
        IStartupService startup)
    {
        _repository = repository;
        _library = library;
        _audio = audio;
        _devices = devices;
        _metadata = metadata;
        _hotkeys = hotkeys;
        _midi = midi;
        _startup = startup;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _audio.PlaybackChanged += OnPlaybackChanged;
        _audio.MicrophoneLevelChanged += OnMicrophoneLevelChanged;
        _devices.DevicesChanged += OnDevicesChanged;
        _hotkeys.SoundRequested += OnHotkeySoundRequested;
        _hotkeys.StopRequested += async (_, _) => await StopAsync();
        _midi.NoteReceived += OnMidiNoteReceived;
    }

    public ObservableCollection<SoundBoard> Boards { get; } = [];
    public ObservableCollection<SoundCategory> Categories { get; } = [];
    public ObservableCollection<SoundClip> Sounds { get; } = [];
    public ObservableCollection<AudioDevice> Microphones { get; } = [];
    public ObservableCollection<AudioDevice> LocalOutputs { get; } = [];
    public ObservableCollection<AudioDevice> VirtualOutputs { get; } = [];
    public ObservableCollection<string> MidiDevices { get; } = [];

    public async Task InitializeAsync(nint windowHandle)
    {
        IsBusy = true;
        try
        {
            await _repository.InitializeAsync();
            _settings = await _repository.GetSettingsAsync();
            _hotkeys.Attach(windowHandle);
            RefreshDevices();
            RefreshMidiDevices();

            Boards.ReplaceWith(await _repository.GetBoardsAsync());
            SelectedBoard = Boards.FirstOrDefault(board => board.Id == _settings.SelectedBoardId)
                ?? Boards.FirstOrDefault();
            MicrophoneMuted = _settings.MicrophoneMuted;
            MicrophoneVolume = _settings.MicrophoneVolume * 100;
            StartWithWindows = await _startup.IsEnabledAsync();
            await ConfigureAudioAsync();
            await LoadBoardAsync();
            StatusMessage = CableAvailable
                ? "Listo. Usa CABLE Output como micrófono en Discord o en el juego."
                : "VB-CABLE no está disponible. Instálalo para activar la salida virtual.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportAsync(string sourcePath)
    {
        if (SelectedBoard is null)
            return;
        IsBusy = true;
        try
        {
            var importedPath = await _library.ImportAsync(sourcePath);
            var duration = _metadata.GetDurationSeconds(importedPath);
            var sound = new SoundClip
            {
                BoardId = SelectedBoard.Id,
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                FilePath = importedPath,
                DurationSeconds = duration,
                TrimEndSeconds = duration,
                SortOrder = Sounds.Count,
                Route = CableAvailable ? AudioRoute.Both : AudioRoute.Local
            };
            await _repository.SaveSoundAsync(sound);
            Sounds.Add(sound);
            RegisterHotkeys();
            StatusMessage = $"“{sound.Name}” añadido al tablero.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PlayAsync(SoundClip? sound)
    {
        if (sound is null)
            return;
        try
        {
            await _audio.PlayAsync(sound);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _audio.StopAsync();
        StatusMessage = "Reproducción detenida.";
    }

    [RelayCommand]
    private async Task AddBoardAsync()
    {
        var board = new SoundBoard
        {
            Name = $"Tablero {Boards.Count + 1}",
            SortOrder = Boards.Count
        };
        await _repository.SaveBoardAsync(board);
        Boards.Add(board);
        SelectedBoard = board;
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (SelectedBoard is null)
            return;
        var category = new SoundCategory
        {
            BoardId = SelectedBoard.Id,
            Name = $"Categoría {Categories.Count + 1}",
            SortOrder = Categories.Count
        };
        await _repository.SaveCategoryAsync(category);
        Categories.Add(category);
        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task DeleteSoundAsync(SoundClip? sound)
    {
        if (sound is null)
            return;
        if (_audio.State.SoundId == sound.Id)
            await _audio.StopAsync();
        await _repository.DeleteSoundAsync(sound.Id);
        Sounds.Remove(sound);
        RegisterHotkeys();
        StatusMessage = $"“{sound.Name}” eliminado.";
    }

    public async Task SaveSoundAsync(SoundClip sound)
    {
        sound.Validate();
        await _repository.SaveSoundAsync(sound);
        RegisterHotkeys();
        OnPropertyChanged(nameof(Sounds));
        StatusMessage = $"Cambios guardados en “{sound.Name}”.";
    }

    public async Task NormalizeAsync(SoundClip sound)
    {
        sound.GainDb = _metadata.CalculatePeakGainDb(sound.FilePath);
        await SaveSoundAsync(sound);
    }

    public IReadOnlyList<float> GetWaveform(SoundClip sound) => _metadata.GetWaveform(sound.FilePath);

    public async Task CreateBackupAsync(string path)
    {
        await _library.CreateBackupAsync(path);
        StatusMessage = "Copia de seguridad creada.";
    }

    public async Task RestoreBackupAsync(string path)
    {
        await _audio.StopAsync();
        await _library.RestoreBackupAsync(path);
        await _repository.InitializeAsync();
        Boards.ReplaceWith(await _repository.GetBoardsAsync());
        SelectedBoard = Boards.FirstOrDefault();
        StatusMessage = "Copia restaurada.";
    }

    public void BeginMidiLearn(SoundClip sound)
    {
        _midiLearningSound = sound;
        StatusMessage = $"Pulsa una nota MIDI para asignarla a “{sound.Name}”…";
    }

    public async Task SearchAsync()
    {
        await LoadBoardAsync();
    }

    public async Task SaveSoundOrderAsync()
    {
        for (var index = 0; index < Sounds.Count; index++)
        {
            Sounds[index].SortOrder = index;
            await _repository.SaveSoundAsync(Sounds[index]);
        }
    }

    partial void OnSelectedBoardChanged(SoundBoard? value)
    {
        if (value is null)
            return;
        _settings.SelectedBoardId = value.Id;
        _ = SaveSettingsAndLoadBoardAsync();
    }

    partial void OnMicrophoneMutedChanged(bool value)
    {
        _settings.MicrophoneMuted = value;
        _audio.SetMicrophoneMuted(value);
        _ = _repository.SaveSettingsAsync(_settings);
    }

    partial void OnMicrophoneVolumeChanged(double value)
    {
        _settings.MicrophoneVolume = (float)(value / 100);
        _audio.SetMicrophoneVolume(_settings.MicrophoneVolume);
    }

    partial void OnSelectedMicrophoneChanged(AudioDevice? value)
    {
        _settings.MicrophoneDeviceId = value?.Id;
        _ = SaveAndReconfigureAsync();
    }

    partial void OnSelectedLocalOutputChanged(AudioDevice? value)
    {
        _settings.LocalOutputDeviceId = value?.Id;
        _ = SaveAndReconfigureAsync();
    }

    partial void OnSelectedVirtualOutputChanged(AudioDevice? value)
    {
        _settings.VirtualOutputDeviceId = value?.Id;
        CableAvailable = value is not null;
        _ = SaveAndReconfigureAsync();
    }

    partial void OnSelectedMidiDeviceChanged(string? value)
    {
        _midi.Disconnect();
        if (value is not null)
            _midi.Connect(MidiDevices.IndexOf(value));
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settings.StartWithWindows = value;
        _ = _startup.SetEnabledAsync(value);
        _ = _repository.SaveSettingsAsync(_settings);
    }

    private async Task LoadBoardAsync()
    {
        if (SelectedBoard is null)
            return;
        Guid? selectedCategoryId =
            SelectedCategory?.BoardId == SelectedBoard.Id ? SelectedCategory.Id : null;
        Categories.ReplaceWith(await _repository.GetCategoriesAsync(SelectedBoard.Id));
        SelectedCategory = Categories.FirstOrDefault(category => category.Id == selectedCategoryId);
        var sounds = await _repository.GetSoundsAsync(SelectedBoard.Id, SearchText);
        if (SelectedCategory is not null)
            sounds = sounds.Where(sound => sound.CategoryId == SelectedCategory.Id).ToArray();
        Sounds.ReplaceWith(sounds);
        RegisterHotkeys();
    }

    private async Task SaveSettingsAndLoadBoardAsync()
    {
        await _repository.SaveSettingsAsync(_settings);
        await LoadBoardAsync();
    }

    private async Task SaveAndReconfigureAsync()
    {
        try
        {
            await _repository.SaveSettingsAsync(_settings);
            await ConfigureAudioAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task ConfigureAudioAsync()
    {
        if (SelectedVirtualOutput is null)
        {
            CableAvailable = false;
            return;
        }
        await _audio.ConfigureAsync(_settings);
        CableAvailable = true;
    }

    private void RefreshDevices()
    {
        var microphones = _devices.GetCaptureDevices();
        var outputs = _devices.GetRenderDevices().Where(device => !device.IsVirtualCable).ToArray();
        var virtualOutputs = _devices.GetRenderDevices().Where(device => device.IsVirtualCable).ToArray();
        Microphones.ReplaceWith(microphones);
        LocalOutputs.ReplaceWith(outputs);
        VirtualOutputs.ReplaceWith(virtualOutputs);
        SelectedMicrophone = microphones.FirstOrDefault(device => device.Id == _settings.MicrophoneDeviceId)
            ?? microphones.FirstOrDefault(device => device.IsDefault);
        SelectedLocalOutput = outputs.FirstOrDefault(device => device.Id == _settings.LocalOutputDeviceId)
            ?? outputs.FirstOrDefault(device => device.IsDefault);
        SelectedVirtualOutput = virtualOutputs.FirstOrDefault(device => device.Id == _settings.VirtualOutputDeviceId)
            ?? virtualOutputs.FirstOrDefault();
        CableAvailable = SelectedVirtualOutput is not null;
    }

    private void RefreshMidiDevices()
    {
        MidiDevices.ReplaceWith(_midi.GetDeviceNames());
    }

    private void RegisterHotkeys()
    {
        _hotkeys.Clear();
        _hotkeys.RegisterStop("Ctrl+Shift+Escape");
        foreach (var sound in Sounds.Where(sound => !string.IsNullOrWhiteSpace(sound.Hotkey)))
            _hotkeys.RegisterSound(sound.Id, sound.Hotkey!);
    }

    private void OnDevicesChanged(object? sender, EventArgs args) =>
        _dispatcher.TryEnqueue(RefreshDevices);

    private void OnPlaybackChanged(object? sender, PlaybackState state) =>
        _dispatcher.TryEnqueue(() =>
            StatusMessage = state.Error ??
                (state.IsPlaying ? "Reproduciendo…" : "Listo."));

    private void OnMicrophoneLevelChanged(object? sender, AudioLevel level) =>
        _dispatcher.TryEnqueue(() => MicrophoneLevel = Math.Max(level.Left, level.Right) * 100);

    private void OnHotkeySoundRequested(object? sender, Guid soundId)
    {
        var sound = Sounds.FirstOrDefault(item => item.Id == soundId);
        if (sound is not null)
            _dispatcher.TryEnqueue(async () => await PlayAsync(sound));
    }

    private void OnMidiNoteReceived(object? sender, int note)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            if (_midiLearningSound is not null)
            {
                _midiLearningSound.MidiNote = note;
                await SaveSoundAsync(_midiLearningSound);
                StatusMessage = $"Nota {note} asignada a “{_midiLearningSound.Name}”.";
                _midiLearningSound = null;
                return;
            }
            var sound = Sounds.FirstOrDefault(item => item.MidiNote == note);
            if (sound is not null)
                await PlayAsync(sound);
        });
    }

    public void Dispose()
    {
        _audio.PlaybackChanged -= OnPlaybackChanged;
        _audio.MicrophoneLevelChanged -= OnMicrophoneLevelChanged;
        _devices.DevicesChanged -= OnDevicesChanged;
        _hotkeys.SoundRequested -= OnHotkeySoundRequested;
        _midi.NoteReceived -= OnMidiNoteReceived;
    }
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
            collection.Add(value);
    }
}
