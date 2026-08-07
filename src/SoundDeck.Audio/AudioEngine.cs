using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundDeck.Core;
using PlaybackStatus = SoundDeck.Core.PlaybackState;

namespace SoundDeck.Audio;

public sealed class AudioEngine : IAudioEngine
{
    private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MMDeviceEnumerator _devices = new();
    private MixingSampleProvider? _virtualMixer;
    private WasapiOut? _virtualOutput;
    private WasapiCapture? _microphoneCapture;
    private BufferedWaveProvider? _microphoneBuffer;
    private VolumeSampleProvider? _microphoneVolume;
    private MeteringSampleProvider? _microphoneMeter;
    private WasapiOut? _localOutput;
    private IDisposable? _localReader;
    private AppSettings _settings = new();
    private Guid? _playingId;
    private int _generation;

    public event EventHandler<PlaybackStatus>? PlaybackChanged;
    public event EventHandler<AudioLevel>? MicrophoneLevelChanged;

    public PlaybackStatus State { get; private set; } = new(null, false);

    public async Task ConfigureAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            StopPlaybackCore();
            DisposePipeline();
            _settings = settings;

            var virtualDevice = ResolveDevice(settings.VirtualOutputDeviceId, DataFlow.Render)
                ?? FindCableDevice();
            if (virtualDevice is null)
                throw new InvalidOperationException("No se encontró CABLE Input. Instala o habilita VB-CABLE.");

            _virtualMixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
            _virtualMixer.MixerInputEnded += OnVirtualInputEnded;
            _virtualOutput = new WasapiOut(virtualDevice, AudioClientShareMode.Shared, true, 50);
            _virtualOutput.Init(_virtualMixer);
            _virtualOutput.Play();

            var microphone = ResolveDevice(settings.MicrophoneDeviceId, DataFlow.Capture)
                ?? TryGetDefault(DataFlow.Capture);
            if (microphone is not null)
                StartMicrophone(microphone);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PlayAsync(SoundClip sound, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sound);
        sound.Validate();
        if (!File.Exists(sound.FilePath))
            throw new FileNotFoundException("No se encontró el archivo del sonido.", sound.FilePath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State.IsPlaying && _playingId == sound.Id)
            {
                StopPlaybackCore();
                SetState(new PlaybackStatus(null, false));
                return;
            }

            StopPlaybackCore();
            var generation = ++_generation;
            _playingId = sound.Id;

            if (sound.Route is AudioRoute.VirtualCable or AudioRoute.Both)
            {
                if (_virtualMixer is null)
                    throw new InvalidOperationException("La salida virtual todavía no está configurada.");

                var virtualSource = CreateSource(sound);
                _virtualMixer.AddMixerInput(virtualSource.Provider);
            }

            if (sound.Route is AudioRoute.Local or AudioRoute.Both)
            {
                var localDevice = ResolveDevice(_settings.LocalOutputDeviceId, DataFlow.Render)
                    ?? TryGetDefault(DataFlow.Render)
                    ?? throw new InvalidOperationException("No hay una salida local activa.");
                var localSource = CreateSource(sound);
                _localReader = localSource.Owner;
                _localOutput = new WasapiOut(localDevice, AudioClientShareMode.Shared, true, 50);
                _localOutput.PlaybackStopped += (_, args) =>
                {
                    if (args.Exception is not null)
                        SetState(new PlaybackStatus(sound.Id, false, args.Exception.Message));
                    else if (generation == _generation)
                        CompletePlayback(sound.Id);
                };
                _localOutput.Init(localSource.Provider);
                _localOutput.Play();
            }

            SetState(new PlaybackStatus(sound.Id, true));
        }
        catch (Exception exception)
        {
            StopPlaybackCore();
            SetState(new PlaybackStatus(sound.Id, false, exception.Message));
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            StopPlaybackCore();
            SetState(new PlaybackStatus(null, false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetMicrophoneVolume(float volume)
    {
        _settings.MicrophoneVolume = Math.Clamp(volume, 0, 2);
        if (_microphoneVolume is not null)
            _microphoneVolume.Volume = _settings.MicrophoneMuted ? 0 : _settings.MicrophoneVolume;
    }

    public void SetMicrophoneMuted(bool muted)
    {
        _settings.MicrophoneMuted = muted;
        if (_microphoneVolume is not null)
            _microphoneVolume.Volume = muted ? 0 : _settings.MicrophoneVolume;
    }

    private void StartMicrophone(MMDevice microphone)
    {
        _microphoneCapture = new WasapiCapture(microphone, true, 50);
        _microphoneBuffer = new BufferedWaveProvider(_microphoneCapture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(300),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        _microphoneCapture.DataAvailable += (_, args) =>
            _microphoneBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
        _microphoneCapture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
                SetState(new PlaybackStatus(_playingId, State.IsPlaying, args.Exception.Message));
        };

        ISampleProvider provider = _microphoneBuffer.ToSampleProvider();
        provider = ToStereo(provider);
        if (provider.WaveFormat.SampleRate != MixFormat.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, MixFormat.SampleRate);
        _microphoneVolume = new VolumeSampleProvider(provider)
        {
            Volume = _settings.MicrophoneMuted ? 0 : _settings.MicrophoneVolume
        };
        _microphoneMeter = new MeteringSampleProvider(_microphoneVolume, 100);
        _microphoneMeter.StreamVolume += (_, args) =>
        {
            var left = args.MaxSampleValues.ElementAtOrDefault(0);
            var right = args.MaxSampleValues.ElementAtOrDefault(1);
            MicrophoneLevelChanged?.Invoke(this, new AudioLevel(left, right));
        };
        _virtualMixer!.AddMixerInput(_microphoneMeter);
        _microphoneCapture.StartRecording();
    }

    private static Source CreateSource(SoundClip sound)
    {
        WaveStream reader = OpenReader(sound.FilePath);
        ISampleProvider provider = reader.ToSampleProvider();
        provider = ToStereo(provider);
        if (provider.WaveFormat.SampleRate != MixFormat.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, MixFormat.SampleRate);

        var offset = new OffsetSampleProvider(provider)
        {
            SkipOver = TimeSpan.FromSeconds(sound.TrimStartSeconds),
            Take = TimeSpan.FromSeconds(sound.EffectiveDurationSeconds)
        };
        var envelope = new EnvelopeSampleProvider(
            offset,
            sound.EffectiveDurationSeconds,
            sound.FadeInSeconds,
            sound.FadeOutSeconds,
            DecibelsToLinear(sound.GainDb),
            reader);
        return new Source(envelope, envelope);
    }

    private static WaveStream OpenReader(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => new VorbisWaveReader(path),
            ".m4a" or ".aac" or ".flac" => new MediaFoundationReader(path),
            _ => new AudioFileReader(path)
        };

    private static ISampleProvider ToStereo(ISampleProvider provider) =>
        provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => throw new NotSupportedException("SoundDeck admite archivos mono o estéreo.")
        };

    private static float DecibelsToLinear(double decibels) =>
        (float)Math.Pow(10, decibels / 20);

    private MMDevice? ResolveDevice(string? id, DataFlow flow)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            var device = _devices.GetDevice(id);
            return device.DataFlow == flow && device.State.HasFlag(DeviceState.Active) ? device : null;
        }
        catch
        {
            return null;
        }
    }

    private MMDevice? FindCableDevice() =>
        _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(device =>
                device.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
                device.FriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase));

    private MMDevice? TryGetDefault(DataFlow flow)
    {
        try
        {
            return _devices.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }

    private void OnVirtualInputEnded(object? sender, SampleProviderEventArgs args)
    {
        if (_playingId is not null && !ReferenceEquals(args.SampleProvider, _microphoneMeter))
        {
            if (args.SampleProvider is IDisposable disposable)
                disposable.Dispose();
            if (_localOutput is null)
                CompletePlayback(_playingId.Value);
        }
    }

    private void CompletePlayback(Guid soundId)
    {
        if (_playingId != soundId)
            return;
        _playingId = null;
        SetState(new PlaybackStatus(null, false));
    }

    private void StopPlaybackCore()
    {
        _generation++;
        _playingId = null;
        _localOutput?.Stop();
        _localOutput?.Dispose();
        _localOutput = null;
        _localReader?.Dispose();
        _localReader = null;

        if (_virtualMixer is not null)
        {
            foreach (var input in _virtualMixer.MixerInputs
                         .Where(input => !ReferenceEquals(input, _microphoneMeter))
                         .ToArray())
            {
                _virtualMixer.RemoveMixerInput(input);
                if (input is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private void DisposePipeline()
    {
        if (_virtualMixer is not null)
            _virtualMixer.MixerInputEnded -= OnVirtualInputEnded;
        _microphoneCapture?.StopRecording();
        _microphoneCapture?.Dispose();
        _microphoneCapture = null;
        _microphoneBuffer = null;
        _microphoneVolume = null;
        _microphoneMeter = null;
        _virtualOutput?.Stop();
        _virtualOutput?.Dispose();
        _virtualOutput = null;
        _virtualMixer = null;
    }

    private void SetState(PlaybackStatus state)
    {
        State = state;
        PlaybackChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            StopPlaybackCore();
            DisposePipeline();
            _devices.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private sealed record Source(ISampleProvider Provider, IDisposable Owner);
}

internal sealed class EnvelopeSampleProvider(
    ISampleProvider source,
    double durationSeconds,
    double fadeInSeconds,
    double fadeOutSeconds,
    float gain,
    IDisposable owner) : ISampleProvider, IDisposable
{
    private long _samplePosition;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;
        var totalFrames = Math.Max(1, (long)(durationSeconds * WaveFormat.SampleRate));
        var fadeInFrames = (long)(fadeInSeconds * WaveFormat.SampleRate);
        var fadeOutFrames = (long)(fadeOutSeconds * WaveFormat.SampleRate);

        for (var sample = 0; sample < read; sample += channels)
        {
            var frame = _samplePosition / channels;
            var multiplier = gain;
            if (fadeInFrames > 0 && frame < fadeInFrames)
                multiplier *= (float)frame / fadeInFrames;
            if (fadeOutFrames > 0 && frame > totalFrames - fadeOutFrames)
                multiplier *= (float)Math.Max(0, totalFrames - frame) / fadeOutFrames;

            for (var channel = 0; channel < channels && sample + channel < read; channel++)
                buffer[offset + sample + channel] = Math.Clamp(
                    buffer[offset + sample + channel] * multiplier, -1, 1);
            _samplePosition += channels;
        }

        return read;
    }

    public void Dispose()
    {
        owner.Dispose();
    }
}
