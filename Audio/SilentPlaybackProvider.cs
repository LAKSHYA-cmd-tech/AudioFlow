using NAudio.Wave;

namespace AudioFlow.Audio;

/// <summary>Keeps the virtual render endpoint active through source playback gaps.</summary>
internal sealed class SilentPlaybackProvider(int sampleRate, int channels) : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = new(sampleRate, 16, channels);

    public int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public int Read(Span<byte> buffer)
    {
        buffer.Clear();
        return buffer.Length;
    }
}
