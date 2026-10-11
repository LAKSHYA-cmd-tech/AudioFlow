using NAudio.CoreAudioApi;
using NAudio.Wave;

if (args.Contains("--self-test"))
{
    TransitionCaptureTest.SelfTest();
    byte[] silentPlayback = Enumerable.Repeat((byte)255, 960).ToArray();
    if (new SilentAudio().Read(silentPlayback, 8, 944) != 944 ||
        silentPlayback.AsSpan(8, 944).ContainsAnyExcept((byte)0) ||
        silentPlayback[0] != 255 || silentPlayback[^1] != 255)
        throw new Exception("Silent keep-alive playback changed data outside its requested buffer.");
    Console.WriteLine("PASS: silent keep-alive playback returns only zero samples within the requested range.");
    RawCaptureRecording.SelfTest();
    var bytes = new byte[48000 * 5 * 4];
    new TestAudio().Read(bytes, 0, bytes.Length);
    var samples = new float[bytes.Length / 2];
    for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
    var expected = Analyze(samples);
    if (expected != (20, 10, 20, 0)) throw new Exception($"Analyzer rejected expected signal: {expected}");
    var silent = Analyze(new float[samples.Length]);
    if (silent.A != 0 || silent.B != 0) throw new Exception("Analyzer accepted silence.");
    for (int i = 0; i < samples.Length; i++) samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 3000 * (i / 2) / 48000));
    var tone = Analyze(samples);
    if (tone.A != 0 || tone.B != 0 || tone.Bad != 50) throw new Exception("Analyzer accepted the old test tone.");
    if (!IsAudioFlowEndpointName("Speakers (AudioFlow Virtual Audio Device)") ||
        !IsAudioFlowEndpointName("AudioFlow Output") ||
        IsAudioFlowEndpointName("Speakers (High Definition Audio Device)"))
        throw new Exception("AudioFlow endpoint name recognition failed.");
    Console.WriteLine("PASS: VM analyzer and AudioFlow endpoint name recognition.");
    return 0;
}

// Sends audio only to AudioFlow Output. Stop the EQ app and other playback first.
// Frequencies switch with a silent gap, so a constant sample tone cannot pass.
var testStage = "finding the AudioFlow Output endpoint";
try
{
    using var enumerator = new MMDeviceEnumerator();
    var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All).ToArray();
    void ListDevices()
    {
        Console.WriteLine("Playback endpoints (including disabled/disconnected devices):");
        if (allDevices.Length == 0) Console.WriteLine("  No playback endpoints found. Check Windows Audio services and Device Manager.");
        foreach (var endpoint in allDevices)
            Console.WriteLine($"  [{endpoint.State}] {endpoint.FriendlyName}\n    ID: {endpoint.ID}");
    }
    if (args.Contains("--list"))
    {
        ListDevices();
        foreach (var endpoint in allDevices) endpoint.Dispose();
        return 0;
    }
    var matches = allDevices.Where(d => d.State == DeviceState.Active)
        .Where(d => IsAudioFlowEndpointName(d.FriendlyName)).ToArray();
    if (matches.Length != 1)
    {
        ListDevices();
        foreach (var endpoint in allDevices) endpoint.Dispose();
        throw new InvalidOperationException($"Expected one active AudioFlow virtual playback endpoint, found {matches.Length}. " +
            "In Administrator Command Prompt run: devcon.exe status Root\\AudioFlow_Output. " +
            "If the endpoint is disabled, enable it in mmsys.cpl. If it is absent, check the device status before reinstalling.");
    }
    foreach (var endpoint in allDevices) if (endpoint.ID != matches[0].ID) endpoint.Dispose();
    using var device = matches[0];
    if (args.Contains("--keep-alive"))
    {
        testStage = "keeping the virtual render endpoint active";
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
        Exception? fault = null;
        output.PlaybackStopped += (_, e) =>
        { if (e.Exception is not null) Interlocked.CompareExchange(ref fault, e.Exception, null); };
        output.Init(new SilentAudio());
        output.Play();
        Console.WriteLine($"Keeping {device.FriendlyName} active with silent stereo 48 kHz playback.");
        Console.WriteLine("Leave AudioFlow running and test YouTube Music first song -> Next several times.");
        Console.WriteLine("No tones or physical output are opened. Press Enter here when finished.");
        Console.ReadLine();
        output.Stop();
        if (fault is not null) throw fault;
        Console.WriteLine("Silent playback stopped. This experiment does not establish a fix.");
        return 0;
    }
    if (args.Contains("--record"))
    {
        testStage = "recording raw virtual-device audio";
        return RawCaptureRecording.Run(device);
    }
    if (args.Contains("--transitions"))
    {
        testStage = "testing repeated render stream transitions";
        return TransitionCaptureTest.Run(device, Analyze);
    }
    Console.WriteLine($"Testing {device.FriendlyName}. Stop AudioFlow and other playback before running this test.");
    Console.WriteLine("VM test build: capture-probe-1");
    if (args.Contains("--probe")) return CaptureProbe.Run(device);
    foreach (var renderFirst in new[] { false, true })
    {
        testStage = "opening loopback capture";
        using var capture = new WasapiLoopbackCapture(device);
        if (capture.WaveFormat.Channels != 2 || capture.WaveFormat.SampleRate != 48000)
            throw new InvalidOperationException($"Set AudioFlow Output to stereo 48 kHz in Sound settings. Got {capture.WaveFormat}.");
        var buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(12)) { ReadFully = false };
        Exception? fault = null;
        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) fault = e.Exception; };
        testStage = "opening render stream on AudioFlow Output";
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
        output.PlaybackStopped += (_, e) => { if (e.Exception != null) fault = e.Exception; };
        testStage = "initializing render stream on AudioFlow Output";
        output.Init(new TestAudio());
        if (renderFirst)
        {
            testStage = "starting render stream";
            output.Play();
            Thread.Sleep(250);
            testStage = "starting loopback capture";
            capture.StartRecording();
        }
        else
        {
            testStage = "starting loopback capture";
            capture.StartRecording();
            Thread.Sleep(250);
            testStage = "starting render stream";
            output.Play();
        }
        testStage = "collecting render and loopback data";
        Thread.Sleep(6500);
        output.Stop();
        capture.StopRecording();
        Thread.Sleep(200);
        if (fault != null) throw fault;
        int sampleCount = buffer.BufferedBytes / capture.WaveFormat.BlockAlign * 2;
        var samples = new float[sampleCount];
        var provider = buffer.ToSampleProvider();
        int count = 0, read;
        while (count < samples.Length && (read = provider.Read(samples.AsSpan(count))) > 0) count += read;
        var states = Analyze(samples.AsSpan(0, count));
        Console.WriteLine($"{(renderFirst ? "render first" : "capture first")}: tone A={states.A}, silence gap={states.Gap}, tone B={states.B}, unexpected blocks={states.Bad}");
        if (states.A < 8 || states.Gap < 4 || states.B < 8 || states.Bad > 6)
            throw new InvalidOperationException("Loopback did not reproduce both stereo signals and the silent gap cleanly.");
    }
    Console.WriteLine("PASS: real stereo playback loopback, silence, and both stream start orders.");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAIL while {testStage}: {e.Message} (0x{e.HResult:X8})");
    Console.Error.WriteLine(e.StackTrace);
    return 1;
}

static bool IsAudioFlowEndpointName(string name) =>
    name.Contains("AudioFlow Output", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("AudioFlow Virtual Audio Device", StringComparison.OrdinalIgnoreCase);

static (int A, int Gap, int B, int Bad) Analyze(ReadOnlySpan<float> samples)
{
    const int frames = 4800; // 100 ms blocks
    int a = 0, gap = 0, b = 0, bad = 0;
    bool seenA = false, seenB = false;
    for (int offset = 0; offset + frames * 2 <= samples.Length; offset += frames * 2)
    {
        var block = samples.Slice(offset, frames * 2);
        double sum = 0;
        foreach (float value in block) sum += value * value;
        double rms = Math.Sqrt(sum / block.Length);
        bool first = Amplitude(block, 0, 997) > 0.035 && Amplitude(block, 1, 1499) > 0.035
            && Amplitude(block, 0, 1499) < 0.01 && Amplitude(block, 1, 997) < 0.01;
        bool second = Amplitude(block, 0, 733) > 0.035 && Amplitude(block, 1, 1223) > 0.035
            && Amplitude(block, 0, 1223) < 0.01 && Amplitude(block, 1, 733) < 0.01;
        if (first) { a++; seenA = true; }
        else if (second) { b++; seenB = true; }
        else if (rms < 0.001) { if (seenA && !seenB) gap++; }
        else bad++;
    }
    return (a, gap, b, bad);
}

static double Amplitude(ReadOnlySpan<float> block, int channel, double hz)
{
    double real = 0, imaginary = 0;
    int frames = block.Length / 2;
    for (int i = 0; i < frames; i++)
    {
        double phase = 2 * Math.PI * hz * i / 48000;
        real += block[i * 2 + channel] * Math.Cos(phase);
        imaginary += block[i * 2 + channel] * Math.Sin(phase);
    }
    return 2 * Math.Sqrt(real * real + imaginary * imaginary) / frames;
}

sealed class TestAudio : IWaveProvider
{
    private int frame;
    public WaveFormat WaveFormat { get; } = new(48000, 16, 2);
    public int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));
    public int Read(Span<byte> buffer)
    {
        int count = buffer.Length;
        count -= count % 4;
        for (int i = 0; i < count; i += 4, frame++)
        {
            double seconds = frame / 48000.0;
            for (int channel = 0; channel < 2; channel++)
            {
                double hz = seconds < 2 ? (channel == 0 ? 997 : 1499) : (channel == 0 ? 733 : 1223);
                short sample = seconds is >= 2 and < 3 || seconds >= 5 ? (short)0
                    : (short)(3276 * Math.Sin(2 * Math.PI * hz * seconds));
                buffer[i + channel * 2] = (byte)sample;
                buffer[i + channel * 2 + 1] = (byte)(sample >> 8);
            }
        }
        return count; // Keep rendering zeroes after the test signal.
    }
}

sealed class SilentAudio : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = new(48000, 16, 2);
    public int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));
    public int Read(Span<byte> buffer)
    {
        buffer.Clear();
        return buffer.Length;
    }
}
