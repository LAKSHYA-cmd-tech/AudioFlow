using NAudio.Wave;
using System.Diagnostics;

namespace AudioFlow.Audio;

/// <summary>Keeps a reserve between independently scheduled capture and render callbacks.</summary>
internal sealed class PlaybackBuffer : IWaveProvider
{
    private readonly object sync = new();
    private readonly BufferedWaveProvider buffer;
    internal const int InitialReserveMilliseconds = 160;
    private int reserveBytes;
    private int maxReserveMs = 300;
    private long lastCaptureTimestamp;
    private bool priming = true;
    private BufferLatencyMode latencyMode = BufferLatencyMode.HighStability;

    public PlaybackBuffer(WaveFormat format, BufferLatencyMode mode = BufferLatencyMode.HighStability)
    {
        buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(500))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = false
        };
        SetLatencyMode(mode);
    }

    public BufferLatencyMode LatencyMode => latencyMode;

    public void SetLatencyMode(BufferLatencyMode mode)
    {
        lock (sync)
        {
            latencyMode = mode;
            int ms = mode switch
            {
                BufferLatencyMode.UltraLow => 25,
                BufferLatencyMode.Balanced => 60,
                _ => InitialReserveMilliseconds
            };
            maxReserveMs = mode switch
            {
                BufferLatencyMode.UltraLow => 60,
                BufferLatencyMode.Balanced => 140,
                _ => 300
            };
            reserveBytes = WaveFormat.SampleRate * ms / 1000 * WaveFormat.BlockAlign;
        }
    }

    public WaveFormat WaveFormat => buffer.WaveFormat;
    public long Underruns { get; private set; }
    public long Overruns { get; private set; }

    public (double BufferedMilliseconds, bool Priming, long Underruns, long Overruns) GetStatus()
    {
        lock (sync)
            return (buffer.BufferedBytes * 1000.0 / WaveFormat.AverageBytesPerSecond,
                priming, Underruns, Overruns);
    }

    public void AddSamples(byte[] data, int offset, int count)
        => AddSamples(data, offset, count, Stopwatch.GetTimestamp());

    internal void AddSamples(byte[] data, int offset, int count, long captureTimestamp)
    {
        lock (sync)
        {
            long now = captureTimestamp;
            if (count > 0 && lastCaptureTimestamp != 0)
            {
                double gapMs = (now - lastCaptureTimestamp) * 1000.0 / Stopwatch.Frequency;
                // Measured VM capture delivery can pause for 130 ms. Grow a
                // bounded reserve for longer pauses, retaining 20 ms headroom.
                int reserveMs = (int)Math.Min(maxReserveMs, Math.Ceiling((gapMs + 20) / 10) * 10);
                reserveBytes = Math.Max(reserveBytes,
                    WaveFormat.SampleRate * reserveMs / 1000 * WaveFormat.BlockAlign);
            }
            if (count > 0) lastCaptureTimestamp = now;
            if (count > buffer.BufferLength - buffer.BufferedBytes)
            {
                // A stalled output should resume with recent audio, not an old backlog.
                buffer.ClearBuffer();
                priming = true;
                Overruns++;
            }
            int keep = Math.Min(count, buffer.BufferLength);
            keep -= keep % WaveFormat.BlockAlign;
            buffer.AddSamples(data, offset + count - keep, keep);
        }
    }

    public int Read(Span<byte> destination)
    {
        lock (sync)
        {
            if (destination.Length == 0) return 0;
            if (destination.Length % WaveFormat.BlockAlign != 0)
                throw new ArgumentException("Playback reads must contain complete frames.", nameof(destination));
            if (priming && buffer.BufferedBytes < Math.Max(reserveBytes, destination.Length))
            {
                destination.Clear();
                return destination.Length;
            }
            priming = false;
            int read = buffer.Read(destination);
            if (read < destination.Length)
            {
                destination[read..].Clear();
                priming = true;
                Underruns++;
            }
            return destination.Length;
        }
    }
}
