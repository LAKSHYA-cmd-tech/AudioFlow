using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SystemEqualizer;

#pragma warning disable CS0618 // WasapiLoopbackCapture and WasapiOut are required by this implementation.
/// <summary>
/// Captures the default render endpoint (the virtual cable), equalizes it, and
/// plays it through a separate hardware render endpoint.
/// </summary>
public sealed class EqualizerEngine : IDisposable
{
    public static readonly int[] BandFrequencies =
        [32, 64, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];

    private readonly object gate = new();
    private readonly float[] gainsDb = new float[BandFrequencies.Length];
    private WasapiLoopbackCapture? capture;
    private WasapiOut? output;
    private MMDevice? captureDevice;
    private MMDevice? outputDevice;
    private BufferedWaveProvider? buffer;
    private EqualizerSampleProvider? equalizer;
    private bool disposed;

    /// <summary>
    /// Starts routing. Set Windows' default playback endpoint to the virtual cable.
    /// The Console-role default is used for hardware output if it differs from
    /// the Multimedia-role cable. Otherwise supply the hardware endpoint ID.
    /// </summary>
    public void Start(string? hardwareOutputDeviceId = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (capture is not null)
                throw new InvalidOperationException("The equalizer is already running.");

            using var devices = new MMDeviceEnumerator();
            var cable = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var hardware = hardwareOutputDeviceId is null
                ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
                : devices.GetDevice(hardwareOutputDeviceId);

            if (cable.ID == hardware.ID || hardware.State != DeviceState.Active)
            {
                cable.Dispose();
                hardware.Dispose();
                throw new InvalidOperationException(
                    "Select an active hardware output device ID distinct from the virtual cable.");
            }

            WasapiLoopbackCapture? newCapture = null;
            WasapiOut? newOutput = null;
            try
            {
                newCapture = new WasapiLoopbackCapture(cable);
                // The buffer bridges the capture and render callback threads. Keeping it
                // short prevents old audio from building up after an output stall.
                var newBuffer = new BufferedWaveProvider(newCapture.WaveFormat, TimeSpan.FromMilliseconds(100))
                {
                    ReadFully = true,
                    DiscardOnBufferOverflow = false
                };
                var newEqualizer = new EqualizerSampleProvider(newBuffer.ToSampleProvider(), gainsDb);
                newOutput = new WasapiOut(hardware, AudioClientShareMode.Shared, true, 10);
                newOutput.Init(newEqualizer);

                newCapture.DataAvailable += OnDataAvailable;
                newCapture.RecordingStopped += OnRecordingStopped;
                buffer = newBuffer;
                equalizer = newEqualizer;
                output = newOutput;
                capture = newCapture;
                captureDevice = cable;
                outputDevice = hardware;

                // Start the render clock first so captured samples have a consumer.
                newOutput.Play();
                newCapture.StartRecording();
            }
            catch
            {
                if (newCapture is not null)
                {
                    newCapture.DataAvailable -= OnDataAvailable;
                    newCapture.RecordingStopped -= OnRecordingStopped;
                }
                capture = null;
                output = null;
                buffer = null;
                equalizer = null;
                newOutput?.Dispose();
                newCapture?.Dispose();
                cable.Dispose();
                hardware.Dispose();
                throw;
            }
        }
    }

    /// <summary>Updates one band while playback is running. Index 0 to 9 follows BandFrequencies.</summary>
    public void SetBandGain(int bandIndex, float decibels)
    {
        if ((uint)bandIndex >= BandFrequencies.Length)
            throw new ArgumentOutOfRangeException(nameof(bandIndex));
        if (!float.IsFinite(decibels) || decibels is < -24 or > 24)
            throw new ArgumentOutOfRangeException(nameof(decibels), "Gain must be between -24 and +24 dB.");

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            gainsDb[bandIndex] = decibels;
            equalizer?.SetBandGain(bandIndex, decibels);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (capture is null)
                return;

            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
            output?.Stop();
            output?.Dispose();
            capture.Dispose();
            captureDevice?.Dispose();
            outputDevice?.Dispose();
            capture = null;
            output = null;
            captureDevice = null;
            outputDevice = null;
            buffer = null;
            equalizer = null;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (gate)
            disposed = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var target = buffer;
        if (target is null || e.BytesRecorded == 0)
            return;

        // Keep the newest audio if the output device stalled instead of adding
        // an ever-growing delay. Both operations are synchronized by the provider.
        if (target.BufferedBytes + e.BytesRecorded > target.BufferLength)
            target.ClearBuffer();
        if (e.BytesRecorded <= target.BufferLength)
            target.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // A device disconnect or capture error must not leave the render clock
        // playing silence indefinitely. The UI can call Stop and then Start.
        output?.Stop();
    }

    private sealed class EqualizerSampleProvider : ISampleProvider
    {
        private const float Q = 1.41421356f; // Approximately one octave per band.
        private readonly object filterGate = new();
        private readonly ISampleProvider source;
        private readonly BiQuadFilter[,] filters;
        private readonly float[] gains;

        public EqualizerSampleProvider(ISampleProvider source, float[] initialGains)
        {
            this.source = source;
            WaveFormat = source.WaveFormat;
            gains = (float[])initialGains.Clone();
            filters = new BiQuadFilter[WaveFormat.Channels, BandFrequencies.Length];
            for (var channel = 0; channel < WaveFormat.Channels; channel++)
                for (var band = 0; band < BandFrequencies.Length; band++)
                    filters[channel, band] = CreateFilter(band, gains[band]);
        }

        public WaveFormat WaveFormat { get; }

        public void SetBandGain(int bandIndex, float decibels)
        {
            lock (filterGate)
            {
                gains[bandIndex] = decibels;
                for (var channel = 0; channel < WaveFormat.Channels; channel++)
                    filters[channel, bandIndex] = CreateFilter(bandIndex, decibels);
            }
        }

        public int Read(Span<float> samples)
        {
            var count = source.Read(samples);
            lock (filterGate)
            {
                for (var i = 0; i < count; i++)
                {
                    var channel = i % WaveFormat.Channels;
                    var sample = samples[i];
                    for (var band = 0; band < BandFrequencies.Length; band++)
                        if (gains[band] != 0)
                            sample = filters[channel, band].Transform(sample);
                    samples[i] = Math.Clamp(sample, -1f, 1f);
                }
            }
            return count;
        }

        private BiQuadFilter CreateFilter(int band, float gain)
        {
            var frequency = Math.Min(BandFrequencies[band], WaveFormat.SampleRate * 0.45f);
            return BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, frequency, Q, gain);
        }
    }
}
#pragma warning restore CS0618
