using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace AudioFlow.Audio;

#pragma warning disable CS0618 // The requested NAudio loopback API is still supported in 3.1.
public sealed class EqualizerEngine : IDisposable
{
    private readonly object lifecycle = new();
    private readonly EqBand[] bands = EqBand.CreateDefaults();
    private readonly SpatialProcessor spatial = new();
    private WasapiLoopbackCapture? capture;
    private WasapiOut? output;
    private WasapiOut? sourceKeepAlive;
    private MMDevice? cableDevice;
    private MMDevice? hardwareDevice;
    private PlaybackBuffer? captureBuffer;
    private SpectrumAnalyzer? analyzer;
    private EqualizerProcessor? processor;
    private SmartMacros macros = SmartMacros.Default;
    private BufferLatencyMode latencyMode = BufferLatencyMode.Balanced;
    private float preampDb;
    private bool bypass;
    private bool disposed;
    private bool stopping;
    private int faultReported;

    public event EventHandler<string>? AudioFaulted;
    public int BandCount => bands.Length;

    public bool IsRunning => Volatile.Read(ref capture) is not null;
    public int SampleRate => Volatile.Read(ref processor)?.WaveFormat.SampleRate ?? 0;
    public long ClippedFrames => Volatile.Read(ref processor)?.ClippedFrames ?? 0;
    public long LimitedFrames => Volatile.Read(ref processor)?.LimitedFrames ?? 0;
    public float LimiterGain => Volatile.Read(ref processor)?.LimiterGain ?? 1;
    public float SpectrumPeakFrequency => Volatile.Read(ref analyzer)?.DominantFrequency ?? 0;
    public BufferLatencyMode LatencyMode => latencyMode;
    public SmartMacros Macros => macros;
    public (double BufferedMilliseconds, bool Priming, long Underruns, long Overruns) GetBufferStatus() =>
        Volatile.Read(ref captureBuffer)?.GetStatus() ?? (0, false, 0, 0);

    public static IReadOnlyList<(string Id, string Name)> GetOutputDevices()
    {
        using var devices = new MMDeviceEnumerator();
        return devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => (device.ID, device.FriendlyName)).ToArray();
    }

    public EqBand GetBand(int index)
    {
        if ((uint)index >= bands.Length) throw new ArgumentOutOfRangeException(nameof(index));
        lock (lifecycle) return bands[index];
    }

    public void Start(string? sourceOutputId, string? hardwareOutputId)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (capture is not null) return;

            using var enumerator = new MMDeviceEnumerator();
            MMDevice? cable = null;
            MMDevice? hardware = null;
            WasapiLoopbackCapture? newCapture = null;
            WasapiOut? newOutput = null;
            WasapiOut? newKeepAlive = null;
            SpectrumAnalyzer? newAnalyzer = null;
            var startStage = "checking selected devices";
            try
            {
                if (string.IsNullOrWhiteSpace(sourceOutputId))
                    throw new InvalidOperationException("Select the virtual cable used as the Windows default playback device.");
                cable = enumerator.GetDevice(sourceOutputId);
                if (cable.State != DeviceState.Active)
                    throw new InvalidOperationException("The selected virtual cable is unavailable.");
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice.ID != cable.ID)
                    throw new InvalidOperationException("Set the selected virtual cable as the Windows default playback device, then refresh devices.");
                if (string.IsNullOrWhiteSpace(hardwareOutputId))
                    throw new InvalidOperationException("Select an active hardware output.");
                hardware = enumerator.GetDevice(hardwareOutputId);
                if (hardware.ID == cable.ID || hardware.State != DeviceState.Active)
                    throw new InvalidOperationException("Choose an active hardware output distinct from the default virtual cable.");

                startStage = $"opening loopback capture on {cable.FriendlyName}";
                newCapture = new WasapiLoopbackCapture(cable);
                var format = newCapture.WaveFormat;
                if (format.Channels != 2 || format.SampleRate <= 0 || format.BlockAlign <= 0 ||
                    format.BlockAlign != format.Channels * format.BitsPerSample / 8 ||
                    format.Encoding is not (WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible))
                    throw new NotSupportedException("The virtual cable must expose a valid stereo PCM or float shared-mode format.");
                var newBuffer = new PlaybackBuffer(format, latencyMode);
                // The VM's browser track-change fault disappears while a silent
                // render client keeps this endpoint active. Do not route physical
                // speaker output back into the virtual input.
                startStage = $"initializing silent playback on {cable.FriendlyName}";
                newKeepAlive = new WasapiOut(cable, AudioClientShareMode.Shared, true, 20);
                newKeepAlive.Init(new SilentPlaybackProvider(format.SampleRate, format.Channels));
                newAnalyzer = new SpectrumAnalyzer(format.SampleRate);
                var newProcessor = new EqualizerProcessor(newBuffer.ToSampleProvider(), newAnalyzer, spatial, bands);
                newProcessor.SetPreamp(autoGainEnabled ? Math.Clamp(preampDb - CalculateAutoGainTrim(), -24f, 15f) : preampDb);
                newProcessor.SetBypass(bypass);
                newProcessor.SetMacros(macros);
                startStage = $"opening speaker output on {hardware.FriendlyName}";
                newOutput = new WasapiOut(hardware, AudioClientShareMode.Shared, true, 50);
                startStage = $"initializing speaker output on {hardware.FriendlyName}";
                newOutput.Init(newProcessor);

                captureBuffer = newBuffer;
                processor = newProcessor;
                analyzer = newAnalyzer;
                cableDevice = cable;
                hardwareDevice = hardware;
                capture = newCapture;
                output = newOutput;
                sourceKeepAlive = newKeepAlive;
                newCapture.DataAvailable += OnDataAvailable;
                newCapture.RecordingStopped += OnRecordingStopped;
                newOutput.PlaybackStopped += OnPlaybackStopped;
                newKeepAlive.PlaybackStopped += OnKeepAliveStopped;
                stopping = false;
                Volatile.Write(ref faultReported, 0);
                startStage = $"starting silent playback on {cable.FriendlyName}";
                newKeepAlive.Play();
                startStage = $"starting loopback capture on {cable.FriendlyName}";
                newCapture.StartRecording();
                startStage = $"starting speaker output on {hardware.FriendlyName}";
                newOutput.Play();
            }
            catch (Exception ex)
            {
                if (newCapture is not null)
                {
                    newCapture.DataAvailable -= OnDataAvailable;
                    newCapture.RecordingStopped -= OnRecordingStopped;
                }
                if (newOutput is not null) newOutput.PlaybackStopped -= OnPlaybackStopped;
                if (newKeepAlive is not null) newKeepAlive.PlaybackStopped -= OnKeepAliveStopped;
                capture = null;
                output = null;
                sourceKeepAlive = null;
                captureBuffer = null;
                processor = null;
                analyzer = null;
                cableDevice = null;
                hardwareDevice = null;
                try { newOutput?.Stop(); } catch { }
                newOutput?.Dispose();
                newCapture?.Dispose();
                try { newKeepAlive?.Stop(); } catch { }
                newKeepAlive?.Dispose();
                newAnalyzer?.Dispose();
                cable?.Dispose();
                hardware?.Dispose();
                throw new InvalidOperationException($"Could not start audio while {startStage}: {ex.Message} (0x{ex.HResult:X8}).", ex);
            }
        }
    }

    private bool autoGainEnabled;
    public bool AutoGainEnabled
    {
        get => autoGainEnabled;
        set
        {
            lock (lifecycle)
            {
                autoGainEnabled = value;
                ApplyEffectivePreamp();
            }
        }
    }

    public float CalculateAutoGainTrim()
    {
        lock (lifecycle)
        {
            if (bands.Length == 0) return 0f;
            float weightedSum = 0f;
            for (int i = 0; i < bands.Length; i++)
            {
                var b = bands[i];
                float weight = b.Frequency switch
                {
                    < 100 => 0.6f,
                    >= 100 and < 500 => 0.9f,
                    >= 500 and < 4000 => 1.3f,
                    _ => 0.8f
                };
                weightedSum += b.GainDb * weight;
            }
            return (weightedSum / bands.Length) * 0.7f;
        }
    }

    private void ApplyEffectivePreamp()
    {
        float trim = autoGainEnabled ? CalculateAutoGainTrim() : 0f;
        float effective = Math.Clamp(preampDb - trim, -24f, 15f);
        processor?.SetPreamp(effective);
    }

    public void SetBand(int index, EqBand band)
    {
        if ((uint)index >= bands.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!IsValidBand(band)) throw new ArgumentOutOfRangeException(nameof(band));
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            processor?.UpdateBand(index, band); // Builds the replacement off the audio thread.
            bands[index] = band;
            ApplyEffectivePreamp();
        }
    }

    public static bool IsValidBand(EqBand band) =>
        float.IsFinite(band.Frequency) && band.Frequency is >= 20 and <= 24_000 &&
        float.IsFinite(band.GainDb) && band.GainDb is >= -24 and <= 24 &&
        float.IsFinite(band.Q) && band.Q is >= 0.1f and <= 12 &&
        Enum.IsDefined(band.Type);

    public void SetBandGain(int index, float decibels)
    {
        if ((uint)index >= (uint)BandCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (!float.IsFinite(decibels) || decibels is < -24 or > 24)
            throw new ArgumentOutOfRangeException(nameof(decibels));
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var band = bands[index] with { GainDb = decibels };
            processor?.UpdateBand(index, band);
            bands[index] = band;
            ApplyEffectivePreamp();
        }
    }

    public void SetPreamp(float decibels)
    {
        if (!float.IsFinite(decibels) || decibels is < -15 or > 15)
            throw new ArgumentOutOfRangeException(nameof(decibels));
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            preampDb = decibels;
            ApplyEffectivePreamp();
        }
    }

    public void SetBypass(bool enabled)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bypass = enabled;
            processor?.SetBypass(enabled);
        }
    }

    public void SetLatencyMode(BufferLatencyMode mode)
    {
        lock (lifecycle)
        {
            latencyMode = mode;
            captureBuffer?.SetLatencyMode(mode);
        }
    }

    public void SetMacros(SmartMacros newMacros)
    {
        lock (lifecycle)
        {
            macros = newMacros;
            processor?.SetMacros(newMacros);
        }
    }

    public void ConfigureSpatial(bool enabled, float depth, float ambience, float mix) =>
        spatial.Configure(enabled, depth, ambience, mix);

    public void SetNodePan(int node, float percent) => spatial.SetPan(node, percent);

    public (float InputL, float InputR, float OutputL, float OutputR) GetPeaks() =>
        Volatile.Read(ref processor)?.Peaks ?? default;

    public void CopySpectrum(Span<float> destination)
    {
        var current = Volatile.Read(ref analyzer);
        if (current is null) destination.Clear();
        else current.CopyBins(destination);
    }

    public void Stop()
    {
        lock (lifecycle)
        {
            if (capture is null) return;
            stopping = true;
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            if (output is not null) output.PlaybackStopped -= OnPlaybackStopped;
            if (sourceKeepAlive is not null) sourceKeepAlive.PlaybackStopped -= OnKeepAliveStopped;
            try { capture.StopRecording(); } catch (COMException) { }
            try { output?.Stop(); } catch (COMException) { }
            output?.Dispose();
            capture.Dispose();
            try { sourceKeepAlive?.Stop(); } catch (COMException) { }
            sourceKeepAlive?.Dispose();
            analyzer?.Dispose();
            cableDevice?.Dispose();
            hardwareDevice?.Dispose();
            capture = null;
            output = null;
            sourceKeepAlive = null;
            captureBuffer = null;
            processor = null;
            analyzer = null;
            cableDevice = null;
            hardwareDevice = null;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (lifecycle) disposed = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var target = Volatile.Read(ref captureBuffer);
        if (target is null || e.BytesRecorded == 0) return;
        var alignment = target.WaveFormat.BlockAlign;
        if (alignment <= 0 || e.BytesRecorded < 0 || e.BytesRecorded > e.Buffer.Length ||
            e.BytesRecorded % alignment != 0)
        {
            ReportFault("The virtual cable supplied a misaligned audio packet. Check its shared-mode format.");
            return;
        }
        try { target.AddSamples(e.Buffer, 0, e.BytesRecorded); }
        catch (Exception ex) { ReportFault($"Audio capture buffer failed: {ex.Message}"); }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!Volatile.Read(ref stopping))
            ReportFault($"Virtual cable capture stopped: {e.Exception?.Message ?? "device unavailable"}");
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!Volatile.Read(ref stopping))
            ReportFault($"Hardware output stopped: {e.Exception?.Message ?? "device unavailable"}");
    }

    private void OnKeepAliveStopped(object? sender, StoppedEventArgs e)
    {
        if (!Volatile.Read(ref stopping))
            ReportFault($"Virtual cable silent playback stopped: {e.Exception?.Message ?? "device unavailable"}");
    }

    private void ReportFault(string message)
    {
        if (Volatile.Read(ref stopping) || Interlocked.Exchange(ref faultReported, 1) != 0) return;
        try { AudioFaulted?.Invoke(this, message); }
        catch { } // Never let a UI subscriber unwind a WASAPI callback.
    }
}
#pragma warning restore CS0618
