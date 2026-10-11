using NAudio.CoreAudioApi;
using NAudio.Wave;

static class CaptureProbe
{
    public static int Run(MMDevice device)
    {
        Console.WriteLine("Initialization probe only; this does not verify audio transport.");
        using (var info = device.CreateAudioClient())
        {
            Console.WriteLine($"Mix format: {info.MixFormat}");
            Console.WriteLine($"Device periods: default={info.DefaultDevicePeriod / 10000.0:F3} ms, minimum={info.MinimumDevicePeriod / 10000.0:F3} ms");
        }
        int accepted = 0;
        foreach (bool convert in new[] { false, true })
        foreach (bool events in new[] { false, true })
        foreach (int milliseconds in new[] { 0, 10, 20, 40, 100, 200 })
        {
            string label = $"buffer={milliseconds} ms, events={events}, conversion={convert}";
            string stage = "activate";
            try
            {
                // Failed initialization clients must not be reused for another attempt.
                using var client = device.CreateAudioClient();
                using var ready = new EventWaitHandle(false, EventResetMode.AutoReset);
                var flags = AudioClientStreamFlags.Loopback;
                if (events) flags |= AudioClientStreamFlags.EventCallback;
                if (convert) flags |= AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
                // Match WasapiCapture's standard format in the conversion case;
                // otherwise preserve the exact endpoint mix format.
                var format = convert ? client.MixFormat.AsStandardWaveFormat() : client.MixFormat;
                stage = "Initialize";
                client.Initialize(AudioClientShareMode.Shared, flags, milliseconds * 10000L, 0, format, Guid.Empty);
                stage = "SetEventHandle";
                if (events) client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
                stage = "GetBufferSize";
                int frames = client.BufferSize;
                stage = "GetCaptureClient";
                _ = client.AudioCaptureClient;
                stage = "Start";
                client.Start();
                stage = "Stop";
                client.Stop();
                accepted++;
                Console.WriteLine($"ACCEPTED {label}: {frames} frames");
            }
            catch (Exception e)
            {
                Console.WriteLine($"REJECTED {label} at {stage}: 0x{e.HResult:X8} {e.Message}");
            }
        }
        Console.WriteLine($"Probe complete: {accepted}/24 requests accepted. Run the normal test to verify audio transport.");
        return accepted > 0 ? 0 : 1;
    }
}
