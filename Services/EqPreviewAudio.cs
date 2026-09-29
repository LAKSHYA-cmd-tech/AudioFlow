using AudioFlow.Models;
using System.IO;

namespace AudioFlow.Services;

/// <summary>
/// Plays a quiet, looping live-EQ sample without touching the system-wide
/// audio processing graph or any third-party configuration.
/// </summary>
public sealed class EqPreviewAudio : IDisposable
{
    private readonly LiveEqPreviewStream _stream = new();

    public event Action<string>? PlaybackFailed
    {
        add => _stream.PlaybackFailed += value;
        remove => _stream.PlaybackFailed -= value;
    }

    public bool IsPlaying => _stream.IsPlaying;

    public void UpdateSettings(DspSettings settings) => _stream.UpdateSettings(settings);

    public void Play(DspSettings settings, bool processed)
    {
        _stream.Play(settings, processed);
    }

    public void Stop() => _stream.Stop();

    public void Dispose() => _stream.Dispose();

    /// <summary>Returns a deterministic 3-second stereo PCM WAV for testing or playback.</summary>
    public static byte[] Render(DspSettings settings, bool processed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        const int rate = 48000;
        var samples = CreateSamples();
        var frames = samples.Length / 2;
        if (processed)
        {
            // The simplified EQ page previews only its visible graphic sliders.
            var graphicOnly = LiveEqPreviewStream.Snapshot(settings);
            new PeqProcessor(graphicOnly, rate, 2).Process(samples);
        }

        const int headerBytes = 44;
        var dataBytes = frames * 2 * sizeof(short);
        using var stream = new MemoryStream(headerBytes + dataBytes);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); // PCM format chunk
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(rate);
            writer.Write(rate * 2 * sizeof(short));
            writer.Write((short)(2 * sizeof(short)));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            for (var i = 0; i < frames; i++)
            {
                var fade = Math.Min(1d, Math.Min(i, frames - 1 - i) / (rate * .025));
                for (var channel = 0; channel < 2; channel++)
                {
                    var sample = (short)Math.Round(Math.Clamp(samples[2 * i + channel] * fade, -.3, .3) * short.MaxValue);
                    writer.Write(sample);
                }
            }
        }
        return stream.ToArray();
    }

    internal static float[] CreateSamples()
    {
        const int rate = 48000;
        const int frames = rate * 3;
        var samples = new float[frames * 2];
        double[] bass = [130.81, 110.00, 98.00]; // C3, A2, G2
        double[][] notes = [[261.63, 329.63, 392.00], [220.00, 261.63, 329.63], [196.00, 246.94, 392.00]];
        for (var i = 0; i < frames; i++)
        {
            // A deterministic three-chord musical phrase, not test noise.
            // The harmonics make bass, midrange and treble adjustments audible.
            var chord = i / rate;
            var time = i / (double)rate;
            var withinChord = (i % rate) / (double)rate;
            var envelope = Math.Min(1, withinChord * 25) * Math.Min(1, (1 - withinChord) * 12);
            var center = .45 * Math.Sin(2 * Math.PI * bass[chord] * time)
                + .12 * Math.Sin(2 * Math.PI * notes[chord][1] * time);
            static double Voice(double frequency, double time) =>
                .28 * Math.Sin(2 * Math.PI * frequency * time)
                + .12 * Math.Sin(4 * Math.PI * frequency * time)
                + .05 * Math.Sin(8 * Math.PI * frequency * time);
            samples[2 * i] = (float)((center + Voice(notes[chord][0], time)) * envelope * .2);
            samples[2 * i + 1] = (float)((center + Voice(notes[chord][2], time)) * envelope * .2);
        }
        return samples;
    }
}
