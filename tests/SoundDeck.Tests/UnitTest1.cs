using FluentAssertions;
using SoundDeck.Core;
using SoundDeck.Infrastructure;

namespace SoundDeck.Tests;

public sealed class SoundClipTests
{
    [Fact]
    public void EffectiveDuration_UsesNonDestructiveTrim()
    {
        var sound = ValidSound();
        sound.DurationSeconds = 12;
        sound.TrimStartSeconds = 2;
        sound.TrimEndSeconds = 9.5;

        sound.EffectiveDurationSeconds.Should().Be(7.5);
        sound.Validate();
    }

    [Fact]
    public void Validate_RejectsOverlappingFades()
    {
        var sound = ValidSound();
        sound.DurationSeconds = 3;
        sound.TrimEndSeconds = 3;
        sound.FadeInSeconds = 2;
        sound.FadeOutSeconds = 2;

        var action = sound.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*fundidos*");
    }

    [Theory]
    [InlineData(AudioRoute.Local)]
    [InlineData(AudioRoute.VirtualCable)]
    [InlineData(AudioRoute.Both)]
    public void Route_AllSupportedValuesAreValid(AudioRoute route)
    {
        var sound = ValidSound();
        sound.Route = route;

        sound.Validate();
    }

    private static SoundClip ValidSound() => new()
    {
        BoardId = Guid.NewGuid(),
        Name = "Prueba",
        FilePath = "sound.wav",
        DurationSeconds = 5,
        TrimEndSeconds = 5
    };
}

public sealed class SqliteSoundRepositoryTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SoundDeck.Tests-{Guid.NewGuid():N}");
    private SqliteSoundRepository _repository = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _repository = new SqliteSoundRepository(Path.Combine(_directory, "test.db"));
        await _repository.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Initialize_CreatesDefaultBoard()
    {
        var boards = await _repository.GetBoardsAsync();

        boards.Should().ContainSingle(board => board.Name == "Mi tablero");
    }

    [Fact]
    public async Task SaveSound_RoundTripsEditingAndMappings()
    {
        var board = (await _repository.GetBoardsAsync()).Single();
        var sound = new SoundClip
        {
            BoardId = board.Id,
            Name = "Alerta",
            FilePath = @"C:\audio\alerta.wav",
            DurationSeconds = 8,
            TrimStartSeconds = 1,
            TrimEndSeconds = 6,
            FadeInSeconds = .2,
            FadeOutSeconds = .4,
            GainDb = -2.5,
            Route = AudioRoute.Both,
            Hotkey = "Ctrl+Shift+1",
            MidiNote = 60
        };

        await _repository.SaveSoundAsync(sound);
        var restored = (await _repository.GetSoundsAsync(board.Id, "alert")).Single();

        restored.Should().BeEquivalentTo(sound);
    }

    [Fact]
    public async Task Settings_RoundTripDevicePreferences()
    {
        var settings = new AppSettings
        {
            MicrophoneDeviceId = "mic-1",
            LocalOutputDeviceId = "speakers-1",
            VirtualOutputDeviceId = "cable-1",
            MicrophoneVolume = .75f,
            StartWithWindows = true
        };

        await _repository.SaveSettingsAsync(settings);
        var restored = await _repository.GetSettingsAsync();

        restored.Should().BeEquivalentTo(settings);
    }
}

public sealed class LibraryServiceTests
{
    [Fact]
    public void SupportedExtensions_ContainsPlannedFormats()
    {
        var service = new LibraryService();

        service.SupportedExtensions.Should().Contain(
            [".wav", ".mp3", ".flac", ".ogg", ".m4a"]);
    }

    [Fact]
    public async Task Import_RejectsUnsupportedFormatBeforeCopying()
    {
        var path = Path.GetTempFileName();
        var unsupported = Path.ChangeExtension(path, ".txt");
        File.Move(path, unsupported);
        try
        {
            var action = async () => await new LibraryService().ImportAsync(unsupported);
            await action.Should().ThrowAsync<NotSupportedException>();
        }
        finally
        {
            File.Delete(unsupported);
        }
    }
}
