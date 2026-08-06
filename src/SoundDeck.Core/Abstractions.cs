namespace SoundDeck.Core;

public interface ISoundRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SoundBoard>> GetBoardsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SoundCategory>> GetCategoriesAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SoundClip>> GetSoundsAsync(Guid boardId, string? search = null, CancellationToken cancellationToken = default);
    Task SaveBoardAsync(SoundBoard board, CancellationToken cancellationToken = default);
    Task SaveCategoryAsync(SoundCategory category, CancellationToken cancellationToken = default);
    Task SaveSoundAsync(SoundClip sound, CancellationToken cancellationToken = default);
    Task DeleteSoundAsync(Guid soundId, CancellationToken cancellationToken = default);
    Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ILibraryService
{
    IReadOnlySet<string> SupportedExtensions { get; }
    Task<string> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<string> CreateBackupAsync(string destinationZip, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string sourceZip, CancellationToken cancellationToken = default);
}

public interface IAudioDeviceService : IDisposable
{
    event EventHandler? DevicesChanged;
    IReadOnlyList<AudioDevice> GetCaptureDevices();
    IReadOnlyList<AudioDevice> GetRenderDevices();
    AudioDevice? FindVirtualCableInput();
}

public interface IAudioMetadataService
{
    double GetDurationSeconds(string path);
    IReadOnlyList<float> GetWaveform(string path, int points = 600);
    double CalculatePeakGainDb(string path, double targetPeakDb = -1);
}

public interface IAudioEngine : IAsyncDisposable
{
    event EventHandler<PlaybackState>? PlaybackChanged;
    event EventHandler<AudioLevel>? MicrophoneLevelChanged;
    PlaybackState State { get; }
    Task ConfigureAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task PlayAsync(SoundClip sound, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void SetMicrophoneVolume(float volume);
    void SetMicrophoneMuted(bool muted);
}

public interface IHotkeyService : IDisposable
{
    event EventHandler<Guid>? SoundRequested;
    event EventHandler? StopRequested;
    void Attach(nint windowHandle);
    bool RegisterSound(Guid soundId, string gesture);
    bool RegisterStop(string gesture);
    void Clear();
}

public interface IMidiInputService : IDisposable
{
    event EventHandler<int>? NoteReceived;
    IReadOnlyList<string> GetDeviceNames();
    void Connect(int deviceIndex);
    void Disconnect();
}

public interface IStartupService
{
    Task<bool> IsEnabledAsync();
    Task<bool> SetEnabledAsync(bool enabled);
}
