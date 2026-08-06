namespace SoundDeck.Core;

public enum AudioRoute
{
    Local,
    VirtualCable,
    Both
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault, bool IsVirtualCable);

public sealed class SoundClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BoardId { get; set; }
    public Guid? CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Color { get; set; } = "#7C3AED";
    public string Icon { get; set; } = "\uE8D6";
    public double DurationSeconds { get; set; }
    public double TrimStartSeconds { get; set; }
    public double TrimEndSeconds { get; set; }
    public double FadeInSeconds { get; set; }
    public double FadeOutSeconds { get; set; }
    public double GainDb { get; set; }
    public AudioRoute Route { get; set; } = AudioRoute.Both;
    public string? Hotkey { get; set; }
    public int? MidiNote { get; set; }
    public int SortOrder { get; set; }

    public double EffectiveEndSeconds =>
        TrimEndSeconds > TrimStartSeconds ? TrimEndSeconds : DurationSeconds;

    public double EffectiveDurationSeconds =>
        Math.Max(0, EffectiveEndSeconds - Math.Max(0, TrimStartSeconds));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidOperationException("El sonido necesita un nombre.");
        if (string.IsNullOrWhiteSpace(FilePath))
            throw new InvalidOperationException("El sonido necesita un archivo.");
        if (TrimStartSeconds < 0 || TrimEndSeconds < 0)
            throw new InvalidOperationException("Los recortes no pueden ser negativos.");
        if (TrimEndSeconds > 0 && TrimEndSeconds <= TrimStartSeconds)
            throw new InvalidOperationException("El final debe ser posterior al inicio.");
        if (FadeInSeconds < 0 || FadeOutSeconds < 0)
            throw new InvalidOperationException("Los fundidos no pueden ser negativos.");
        if (FadeInSeconds + FadeOutSeconds > EffectiveDurationSeconds)
            throw new InvalidOperationException("Los fundidos exceden la duración recortada.");
        if (GainDb is < -60 or > 18)
            throw new InvalidOperationException("La ganancia debe estar entre -60 y +18 dB.");
    }
}

public sealed class SoundBoard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Mi tablero";
    public int SortOrder { get; set; }
}

public sealed class SoundCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BoardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class AppSettings
{
    public string? MicrophoneDeviceId { get; set; }
    public string? LocalOutputDeviceId { get; set; }
    public string? VirtualOutputDeviceId { get; set; }
    public float MicrophoneVolume { get; set; } = 1;
    public bool MicrophoneMuted { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
    public Guid? SelectedBoardId { get; set; }
}

public sealed record AudioLevel(float Left, float Right);

public sealed record PlaybackState(Guid? SoundId, bool IsPlaying, string? Error = null);
