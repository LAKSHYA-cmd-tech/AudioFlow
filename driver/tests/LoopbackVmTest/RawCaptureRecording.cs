using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static class RawCaptureRecording
{
    public static void SelfTest()
    {
        foreach (var format in new[] { new WaveFormat(48000, 16, 2), WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) })
        {
            float[] expected = [0.25f, -0.25f, 0.5f, -0.5f, 0, 0];
            byte[] bytes = new byte[expected.Length * format.BitsPerSample / 8];
            for (int i = 0; i < expected.Length; i++)
            {
                var value = format.BitsPerSample == 16
                    ? BitConverter.GetBytes((short)(expected[i] * 32768)) : BitConverter.GetBytes(expected[i]);
                value.CopyTo(bytes, i * value.Length);
            }
            var buffer = new BufferedWaveProvider(format) { ReadFully = false };
            buffer.AddSamples(bytes, 0, bytes.Length);
            using var memory = new MemoryStream();
            WriteRecording(memory, buffer);
            using var reader = new WaveFileReader(new MemoryStream(memory.ToArray()));
            if (reader.WaveFormat.BitsPerSample != 16 || reader.WaveFormat.Channels != 2 || reader.Length != 12)
                throw new Exception("Raw recording WAV format or length is incorrect.");
            float[] actual = new float[expected.Length];
            if (reader.ToSampleProvider().Read(actual) != expected.Length ||
                actual.Zip(expected).Any(pair => Math.Abs(pair.First - pair.Second) > 0.0001f))
                throw new Exception("Raw recording WAV changed channel order or signal amplitude.");
        }
        Console.WriteLine("PASS: PCM16 and float raw capture convert to playable stereo WAV.");
    }

    private static void WriteRecording(Stream stream, IWaveProvider buffer)
    {
        using var writer = new WaveFileWriter(stream, new WaveFormat(buffer.WaveFormat.SampleRate, 16, buffer.WaveFormat.Channels));
        var provider = buffer.ToSampleProvider();
        float[] samples = new float[4096 * buffer.WaveFormat.Channels];
        int read;
        while ((read = provider.Read(samples)) > 0) writer.WriteSamples(samples, 0, read);
    }

    public static int Run(MMDevice device)
    {
        const int seconds = 30;
        string path = Path.GetFullPath("AudioFlow-raw-capture.wav");
        if (File.Exists(path))
            throw new IOException($"Recording already exists: {path}. Rename it before recording again.");
        using var capture = new WasapiLoopbackCapture(device);
        var buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(seconds + 10))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = false
        };
        using var stopped = new ManualResetEventSlim();
        Exception? fault = null;
        long lastPacket = 0, largestGap = 0;
        int packets = 0;
        capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded == 0) return;
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (lastPacket != 0) largestGap = Math.Max(largestGap, now - lastPacket);
                lastPacket = now;
                if (e.BytesRecorded % capture.WaveFormat.BlockAlign != 0)
                    throw new InvalidDataException("Capture supplied an incomplete audio frame.");
                // Preallocated memory only: no file I/O on the capture callback.
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                packets++;
            }
            catch (Exception ex) { Interlocked.CompareExchange(ref fault, ex, null); }
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) Interlocked.CompareExchange(ref fault, e.Exception, null);
            stopped.Set();
        };
        Console.WriteLine($"Raw capture from {device.FriendlyName}, {capture.WaveFormat}.");
        Console.WriteLine("Leave AudioFlow running. Play music, then advance to the next song within 30 seconds.");
        Console.WriteLine("This test records the virtual device before the app's buffer, EQ, and speaker output.");
        capture.StartRecording();
        // The event also ends the wait if capture stops unexpectedly.
        bool endedEarly = stopped.Wait(TimeSpan.FromSeconds(seconds));
        capture.StopRecording();
        if (!stopped.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Capture did not stop; no diagnostic WAV was saved.");
        if (fault is not null) throw fault;
        if (endedEarly) throw new IOException("Capture stopped before the 30-second recording finished.");
        double capturedSeconds = buffer.BufferedBytes / (double)capture.WaveFormat.AverageBytesPerSecond;
        if (capturedSeconds == 0) throw new IOException("No audio packets were captured.");
        // Save ordinary PCM16 for convenient playback in the VM after capture ends.
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            WriteRecording(stream, buffer);
        Console.WriteLine($"Saved: {path}");
        Console.WriteLine($"Packets: {packets}; captured audio: {capturedSeconds:0.00} s; largest callback gap: {largestGap * 1000.0 / Stopwatch.Frequency:0.0} ms.");
        Console.WriteLine("Stop AudioFlow, switch Windows output to the physical VM speakers, then listen to the WAV.");
        Console.WriteLine("Report whether crackling is present in this WAV. Callback gaps alone do not prove audio corruption.");
        return 0;
    }
}
