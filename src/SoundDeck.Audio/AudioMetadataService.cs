using NAudio.Vorbis;
using NAudio.Wave;
using SoundDeck.Core;

namespace SoundDeck.Audio;

public sealed class AudioMetadataService : IAudioMetadataService
{
    public double GetDurationSeconds(string path)
    {
        using var reader = Open(path);
        return reader.TotalTime.TotalSeconds;
    }

    public IReadOnlyList<float> GetWaveform(string path, int points = 600)
    {
        if (points <= 0)
            throw new ArgumentOutOfRangeException(nameof(points));

        using var reader = Open(path);
        var provider = reader.ToSampleProvider();
        var samplesPerPoint = Math.Max(
            provider.WaveFormat.Channels,
            (long)(reader.TotalTime.TotalSeconds * provider.WaveFormat.SampleRate *
                   provider.WaveFormat.Channels / points));
        var result = new float[points];
        var buffer = new float[8192];
        long sampleIndex = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++, sampleIndex++)
            {
                var bucket = (int)Math.Min(points - 1, sampleIndex / samplesPerPoint);
                result[bucket] = Math.Max(result[bucket], Math.Abs(buffer[index]));
            }
        }
        return result;
    }

    public double CalculatePeakGainDb(string path, double targetPeakDb = -1)
    {
        using var reader = Open(path);
        var provider = reader.ToSampleProvider();
        var buffer = new float[8192];
        float peak = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
                peak = Math.Max(peak, Math.Abs(buffer[index]));
        }
        if (peak <= float.Epsilon)
            return 0;
        var currentDb = 20 * Math.Log10(peak);
        return Math.Clamp(targetPeakDb - currentDb, -60, 18);
    }

    private static WaveStream Open(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => new VorbisWaveReader(path),
            ".m4a" or ".aac" or ".flac" => new MediaFoundationReader(path),
            _ => new AudioFileReader(path)
        };
}
