using System.Runtime.InteropServices;
using AudioFlow.Models;

namespace AudioFlow.Services;

/// <summary>
/// App-owned, looping 48 kHz stereo stream. EQ snapshots are published on the UI
/// thread and consumed by a worker without reading mutable profile objects there.
/// This is not a system-wide audio hook.
/// </summary>
public sealed class LiveEqPreviewStream : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int BlockFrames = 960; // 20 ms
    private readonly object _gate = new();
    private readonly object _controlGate = new();
    private DspSettings? _settings;
    private CancellationTokenSource? _stop;
    private Task? _playback;
    private bool _disposed;

    public event Action<string>? PlaybackFailed;
    public bool IsPlaying { get { lock (_gate) return _playback is { IsCompleted: false }; } }

    public static DspSettings Snapshot(DspSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new DspSettings
        {
            Enabled = source.Enabled,
            GraphicEnabled = source.GraphicEnabled,
            GraphicBands = new System.Collections.ObjectModel.ObservableCollection<PeqBand>(
                source.GraphicBands.Select(b => new PeqBand
                {
                    Enabled = b.Enabled, Type = b.Type, Frequency = b.Frequency,
                    Q = b.Q, GainDb = b.GainDb
                }))
        };
    }

    public void UpdateSettings(DspSettings source) => Volatile.Write(ref _settings, Snapshot(source));

    public void Play(DspSettings source, bool processed)
    {
        lock (_controlGate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LiveEqPreviewStream));
            StopCore();
            UpdateSettings(source);
            var stop = new CancellationTokenSource();
            lock (_gate)
            {
                _stop = stop;
                _playback = Task.Run(() => Run(processed, stop.Token));
            }
        }
    }

    private void Run(bool processed, CancellationToken cancellation)
    {
        try
        {
            using var output = new WaveOutPcmSink(SampleRate, Channels, BlockFrames);
            var dry = EqPreviewAudio.CreateSamples();
            var block = new float[BlockFrames * Channels];
            var pipeline = new RealtimeEqPipeline(SampleRate, Channels, processed);
            var position = 0;
            while (!cancellation.IsCancellationRequested)
            {
                for (var i = 0; i < block.Length; i++)
                {
                    block[i] = dry[position++];
                    if (position == dry.Length) position = 0;
                }
                pipeline.Process(block, Volatile.Read(ref _settings)!);
                output.Write(block, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { PlaybackFailed?.Invoke(ex.Message); } catch { /* UI may be shutting down. */ }
        }
    }

    public void Stop()
    {
        lock (_controlGate) StopCore();
    }

    private void StopCore()
    {
        CancellationTokenSource? stop;
        Task? playback;
        lock (_gate) { stop = _stop; playback = _playback; }
        if (stop is null) return;
        stop.Cancel();
        if (playback is not null && !playback.Wait(TimeSpan.FromSeconds(2)))
            throw new TimeoutException("Audio preview did not stop within two seconds.");
        lock (_gate) { _stop = null; _playback = null; }
        stop.Dispose();
    }

    public void Dispose()
    {
        lock (_controlGate)
        {
            if (_disposed) return;
            StopCore();
            _disposed = true;
        }
    }
}

/// <summary>Stateful block processor; crossfades between snapshots to avoid parameter-change clicks.</summary>
public sealed class RealtimeEqPipeline
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly bool _processed;
    private DspSettings? _current;
    private PeqProcessor? _filter;

    public RealtimeEqPipeline(int sampleRate, int channels, bool processed)
    {
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        _sampleRate = sampleRate;
        _channels = channels;
        _processed = processed;
    }

    public void Process(Span<float> samples, DspSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (samples.Length % _channels != 0) throw new ArgumentException("Incomplete audio frame.", nameof(samples));
        if (!_processed) return;
        if (ReferenceEquals(snapshot, _current)) { _filter?.Process(samples); return; }

        var next = snapshot.Enabled && snapshot.GraphicEnabled
            ? new PeqProcessor(snapshot, _sampleRate, _channels) : null;
        if (next is { BandCount: 0 }) next = null;
        if (next is null && _filter is null) { _current = snapshot; return; }
        var before = samples.ToArray();
        _filter?.Process(before);
        next?.Process(samples);
        var fadeFrames = Math.Min(256, samples.Length / _channels);
        for (var frame = 0; frame < fadeFrames; frame++)
        {
            var blend = (frame + 1f) / fadeFrames;
            for (var channel = 0; channel < _channels; channel++)
            {
                var index = frame * _channels + channel;
                samples[index] = before[index] * (1 - blend) + samples[index] * blend;
            }
        }
        _current = snapshot;
        _filter = next;
    }
}

/// <summary>
/// Replaceable Windows output adapter. waveOut auto-converts the app's PCM stream
/// for the default endpoint; a future WASAPI adapter can replace it independently.
/// </summary>
internal sealed class WaveOutPcmSink : IDisposable
{
    private const uint WaveMapper = uint.MaxValue;
    private const uint HeaderDone = 1;
    private readonly IntPtr _handle;
    private readonly Slot[] _slots;
    private readonly int _samplesPerBlock;
    private bool _disposed;

    public WaveOutPcmSink(int rate, int channels, int framesPerBlock)
    {
        _samplesPerBlock = framesPerBlock * channels;
        var format = new WaveFormat
        {
            FormatTag = 1, Channels = (ushort)channels, SamplesPerSecond = (uint)rate,
            AverageBytesPerSecond = (uint)(rate * channels * sizeof(short)),
            BlockAlign = (ushort)(channels * sizeof(short)), BitsPerSample = 16
        };
        Check(waveOutOpen(out _handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0), "open output");
        _slots = new Slot[4];
        try
        {
            for (var i = 0; i < _slots.Length; i++) _slots[i] = new Slot(_handle, _samplesPerBlock * sizeof(short));
        }
        catch { Dispose(); throw; }
    }

    public void Write(ReadOnlySpan<float> samples, CancellationToken cancellation)
    {
        if (samples.Length != _samplesPerBlock) throw new ArgumentException("Wrong PCM block size.", nameof(samples));
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var slot in _slots)
            {
                if (slot is null || !slot.IsReady) continue;
                slot.Write(samples);
                return;
            }
            Thread.Sleep(2);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        waveOutReset(_handle);
        foreach (var slot in _slots ?? []) slot?.Dispose();
        waveOutClose(_handle);
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"Could not {operation} (Windows audio error {result}).");
    }

    private sealed class Slot : IDisposable
    {
        private readonly IntPtr _handle;
        private readonly IntPtr _data;
        private readonly IntPtr _header;
        private readonly short[] _pcm;
        private bool _prepared;
        private bool _queued;

        public Slot(IntPtr handle, int bytes)
        {
            _handle = handle;
            _pcm = new short[bytes / sizeof(short)];
            _data = Marshal.AllocHGlobal(bytes);
            try
            {
                _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
                Marshal.StructureToPtr(new WaveHeader { Data = _data, BufferLength = (uint)bytes }, _header, false);
                Check(waveOutPrepareHeader(_handle, _header, (uint)Marshal.SizeOf<WaveHeader>()), "prepare audio buffer");
                _prepared = true;
            }
            catch { Dispose(); throw; }
        }

        public bool IsReady => !_queued || (Marshal.PtrToStructure<WaveHeader>(_header).Flags & HeaderDone) != 0;

        public void Write(ReadOnlySpan<float> samples)
        {
            for (var i = 0; i < _pcm.Length; i++)
                _pcm[i] = float.IsFinite(samples[i])
                    ? (short)Math.Round(Math.Clamp(samples[i], -.3f, .3f) * short.MaxValue) : (short)0;
            Marshal.Copy(_pcm, 0, _data, _pcm.Length);
            Check(waveOutWrite(_handle, _header, (uint)Marshal.SizeOf<WaveHeader>()), "submit audio buffer");
            _queued = true;
        }

        public void Dispose()
        {
            // Never free memory while a driver still owns the prepared header.
            if (_prepared && waveOutUnprepareHeader(_handle, _header, (uint)Marshal.SizeOf<WaveHeader>()) != 0) return;
            Marshal.FreeHGlobal(_header);
            Marshal.FreeHGlobal(_data);
            _prepared = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSecond, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }

    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutOpen(
        out IntPtr handle, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutClose(IntPtr handle);
}
