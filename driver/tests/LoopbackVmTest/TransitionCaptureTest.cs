using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static class TransitionCaptureTest
{
    internal delegate (int A, int Gap, int B, int Bad) Analyzer(ReadOnlySpan<float> samples);

    // Ignore the intentional one-second silence and stream boundaries. A zero
    // run inside either sustained stereo tone is missing audio, not a zero crossing.
    internal static (int Runs, int Frames) FindDropouts(ReadOnlySpan<float> samples)
    {
        int runs = 0, frames = 0;
        const int minimumFrames = 12; // 0.25 ms at 48 kHz.
        for (int frame = 0; frame < samples.Length / 2;)
        {
            if (samples[frame * 2] != 0 || samples[frame * 2 + 1] != 0) { frame++; continue; }
            int start = frame;
            while (frame < samples.Length / 2 && samples[frame * 2] == 0 && samples[frame * 2 + 1] == 0) frame++;
            int length = frame - start;
            // Boundaries and intentional silence have a different tone on either
            // side (or no tone). Inspect 100 ms of surrounding signal to classify it.
            const int contextFrames = 4800;
            if (length < minimumFrames || start < contextFrames || frame + contextFrames > samples.Length / 2) continue;
            int before = ToneIdentity(samples.Slice((start - contextFrames) * 2, contextFrames * 2));
            int after = ToneIdentity(samples.Slice(frame * 2, contextFrames * 2));
            if (before != 0 && before == after) { runs++; frames += length; }
        }
        return (runs, frames);
    }

    private static int ToneIdentity(ReadOnlySpan<float> samples)
    {
        return Matches(samples, 997, 1499) ? 1 : Matches(samples, 733, 1223) ? 2 : 0;
    }

    private static bool Matches(ReadOnlySpan<float> samples, double left, double right)
    {
        for (int channel = 0; channel < 2; channel++)
        {
            double real = 0, imaginary = 0;
            double hz = channel == 0 ? left : right;
            int frames = samples.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                double phase = 2 * Math.PI * hz * i / 48000;
                real += samples[i * 2 + channel] * Math.Cos(phase);
                imaginary += samples[i * 2 + channel] * Math.Sin(phase);
            }
            if (2 * Math.Sqrt(real * real + imaginary * imaginary) / frames < 0.035) return false;
        }
        return true;
    }

    public static void SelfTest()
    {
        var bytes = new byte[48000 * 5 * 4];
        new TestAudio().Read(bytes, 0, bytes.Length);
        var samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        if (FindDropouts(samples) != (0, 0)) throw new Exception("Transition analyzer counted intentional silence as a dropout.");
        // One millisecond in tone A, ten milliseconds in tone B.
        Array.Clear(samples, 48000, 48 * 2);
        Array.Clear(samples, 48000 * 8, 480 * 2);
        if (FindDropouts(samples) != (2, 528)) throw new Exception("Transition analyzer missed injected short dropouts.");
        if (FindDropouts(new float[samples.Length]) != (0, 0)) throw new Exception("Transition analyzer classified all-silent capture as tone dropouts.");
        Console.WriteLine("PASS: transition analyzer detects 1/10 ms dropouts and excludes intentional silence.");
    }

    public static int Run(MMDevice device, Analyzer analyze)
    {
        const int rounds = 6;
        string path = Path.GetFullPath("AudioFlow-transition-capture.wav");
        if (File.Exists(path)) throw new IOException($"Recording already exists: {path}. Rename it before testing again.");
        using var capture = new WasapiLoopbackCapture(device);
        if (capture.WaveFormat.Channels != 2 || capture.WaveFormat.SampleRate != 48000)
            throw new InvalidOperationException($"Set AudioFlow to stereo 48 kHz. Got {capture.WaveFormat}.");
        var buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(50))
        { ReadFully = false, DiscardOnBufferOverflow = false };
        Exception? fault = null;
        using var stopped = new ManualResetEventSlim();
        capture.DataAvailable += (_, e) =>
        {
            try { buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); }
            catch (Exception ex) { Interlocked.CompareExchange(ref fault, ex, null); }
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) Interlocked.CompareExchange(ref fault, e.Exception, null);
            stopped.Set();
        };
        var ranges = new List<(int Start, int End)>();
        Console.WriteLine("Stop AudioFlow and all music players. Test tones render only to the virtual device.");
        Console.WriteLine("Six render streams will open/close while the same capture stream stays running (~36 seconds).");
        capture.StartRecording();
        try
        {
            for (int round = 0; round < rounds; round++)
            {
                int start = buffer.BufferedBytes;
                using (var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20))
                {
                    output.PlaybackStopped += (_, e) =>
                    { if (e.Exception is not null) Interlocked.CompareExchange(ref fault, e.Exception, null); };
                    output.Init(new TestAudio());
                    output.Play();
                    if (stopped.Wait(TimeSpan.FromMilliseconds(5500)))
                        throw new IOException("Capture stopped during a render transition.");
                    output.Stop();
                }
                if (stopped.Wait(TimeSpan.FromMilliseconds(400))) throw new IOException("Capture stopped between streams.");
                ranges.Add((start, buffer.BufferedBytes));
                Console.WriteLine($"Captured transition round {round + 1}/{rounds}.");
                if (fault is not null) throw fault;
            }
        }
        finally
        {
            capture.StopRecording();
            if (!stopped.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Capture did not stop.");
        }
        if (fault is not null) throw fault;
        var samples = new float[buffer.BufferedBytes / capture.WaveFormat.BlockAlign * 2];
        var provider = buffer.ToSampleProvider();
        int count = 0, read;
        while (count < samples.Length && (read = provider.Read(samples.AsSpan(count))) > 0) count += read;
        using (var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 2)))
            writer.WriteSamples(samples, 0, count);
        bool passed = true;
        for (int i = 0; i < ranges.Count; i++)
        {
            int start = ranges[i].Start / capture.WaveFormat.BlockAlign * 2;
            int end = Math.Min(count, ranges[i].End / capture.WaveFormat.BlockAlign * 2);
            var segment = samples.AsSpan(start, end - start);
            var states = analyze(segment);
            var dropouts = FindDropouts(segment);
            bool ok = states.A >= 8 && states.Gap >= 4 && states.B >= 8 && states.Bad <= 6 && dropouts.Runs == 0;
            passed &= ok;
            Console.WriteLine($"Round {i + 1}: A={states.A}, gap={states.Gap}, B={states.B}, unexpected={states.Bad}; tone dropouts={dropouts.Runs}, missing={dropouts.Frames / 48.0:0.00} ms; {(ok ? "PASS" : "FAIL")}.");
        }
        Console.WriteLine($"Saved: {path}");
        Console.WriteLine(passed ? "PASS: six render stream transitions with continuous capture and no detected tone dropouts."
            : "FAIL: render transitions corrupted or lost the expected signal. Save the console output and WAV.");
        Console.WriteLine("This test covers stream recreation; it does not reproduce every music player's Next behavior.");
        return passed ? 0 : 1;
    }
}
