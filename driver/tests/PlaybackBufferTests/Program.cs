using AudioFlow.Audio;
using NAudio.Wave;

var format = new WaveFormat(48000, 16, 2);
int bytesPerMs = format.AverageBytesPerSecond / 1000;
var buffer = new PlaybackBuffer(format);
byte[] audio = Enumerable.Repeat((byte)37, bytesPerMs * 300).ToArray();
byte[] output = new byte[bytesPerMs * 10];
void Check(bool value, string message) { if (!value) throw new Exception(message); }
buffer.AddSamples(audio, 0, bytesPerMs * 40);
buffer.Read(output);
Check(output.All(b => b == 0), "Must prime before consuming partial reserve");
buffer.AddSamples(audio, 0, bytesPerMs * 120);
buffer.Read(output);
Check(output.All(b => b == 37), "Must play after priming");
// Ten-millisecond output callbacks and irregular capture deliveries of 40/60 ms.
for (int ms = 0; ms < 5000; ms += 10)
{
    if (ms % 100 == 0) buffer.AddSamples(audio, 0, bytesPerMs * 40);
    if (ms % 100 == 60) buffer.AddSamples(audio, 0, bytesPerMs * 60);
    buffer.Read(output);
    Check(output.All(b => b == 37), "Jitter caused a gap despite reserve");
}
Check(buffer.Underruns == 0, "Unexpected steady-state underrun");
for (int i = 0; i < 20; i++) buffer.Read(output);
Check(buffer.Underruns == 1, "Must count one underrun then re-prime");
buffer.AddSamples(audio, 0, bytesPerMs * 40);
buffer.Read(output);
Check(output.All(b => b == 0), "Must rebuild reserve after underrun");
buffer.AddSamples(audio, 0, bytesPerMs * 120);
buffer.Read(output);
Check(output.All(b => b == 37), "Must recover after underrun");
byte[] overflow = Enumerable.Repeat((byte)81, bytesPerMs * 600).ToArray();
buffer.AddSamples(overflow, 0, overflow.Length);
buffer.Read(output);
Check(buffer.Overruns == 1 && output.All(b => b == 81), "Must recover with recent audio after overflow");
// Repeated track changes: drain the old track, leave a callback-free gap,
// then deliver a distinct next track in small packets. Verify its start is
// preserved during re-priming and that no previous-track bytes leak through.
var transitions = new PlaybackBuffer(format);
for (int track = 0; track < 20; track++)
{
    byte marker = (byte)(track + 1);
    byte[] nextTrack = Enumerable.Repeat(marker, bytesPerMs * 100).ToArray();
    for (int packet = 0; packet < PlaybackBuffer.InitialReserveMilliseconds / 10 - 1; packet++)
    {
        transitions.AddSamples(nextTrack, 0, output.Length);
        transitions.Read(output);
        Check(output.All(b => b == 0), "Next track must wait for its reserve");
    }
    transitions.AddSamples(nextTrack, 0, output.Length);
    for (int packet = 0; packet < PlaybackBuffer.InitialReserveMilliseconds / 10; packet++)
    {
        transitions.Read(output);
        Check(output.All(b => b == marker), "Track start lost or previous track leaked");
    }
    for (int gap = 0; gap < 100; gap++)
    {
        transitions.Read(output);
        Check(output.All(b => b == 0), "Track gap must remain silent");
    }
    Check(transitions.GetStatus().Priming, "Track gap must re-prime");
}
Check(transitions.Underruns == 20 && transitions.Overruns == 0,
    "Each track gap should count one shortage and no overflow");
Console.WriteLine("PASS: startup reserve, callback jitter, underrun/overflow recovery, and 20 track gaps with preserved starts.");

// Capture stalls for 140 ms, then supplies the queued audio intact. A raw
// recording can be clean while live playback with an 80 ms reserve underruns.
var delayed = new PlaybackBuffer(format);
long ticks = System.Diagnostics.Stopwatch.Frequency;
delayed.AddSamples(audio, 0, bytesPerMs * 160, ticks);
delayed.Read(output);
for (int packet = 0; packet < 14; packet++)
{
    delayed.Read(output);
    Check(output.All(b => b == 37), "Measured VM callback gap caused live silence");
}
delayed.AddSamples(audio, 0, bytesPerMs * 140, ticks + ticks * 140 / 1000);
delayed.Read(output);
Check(delayed.Underruns == 0 && output.All(b => b == 37), "Late capture burst did not recover continuously");

// A longer stall adapts the reserve on the next re-prime, without discarding
// captured data. At 220 ms gap, require 240 ms before resuming.
var adaptive = new PlaybackBuffer(format);
adaptive.AddSamples(audio, 0, bytesPerMs * 160, ticks);
for (int packet = 0; packet < 23; packet++) adaptive.Read(output);
Check(adaptive.Underruns == 1, "Long stall should cause one underrun");
adaptive.AddSamples(audio, 0, bytesPerMs * 220, ticks + ticks * 220 / 1000);
adaptive.Read(output);
Check(output.All(b => b == 0), "Adaptive reserve resumed before its headroom was ready");
adaptive.AddSamples(audio, 0, bytesPerMs * 20, ticks + ticks * 240 / 1000);
adaptive.Read(output);
Check(output.All(b => b == 37), "Adaptive recovery discarded captured audio");
for (int packet = 0; packet < 40; packet++) adaptive.Read(output);
adaptive.AddSamples(audio, 0, bytesPerMs * 280, ticks + ticks * 1240 / 1000);
adaptive.Read(output);
Check(output.All(b => b == 0), "Long gap should increase reserve to its cap");
adaptive.AddSamples(audio, 0, bytesPerMs * 20, ticks + ticks * 1260 / 1000);
adaptive.Read(output);
Check(output.All(b => b == 37), "Reserve cap must allow recovery at 300 ms");
Console.WriteLine("PASS: clean delayed capture survives 140 ms gap; 220 ms stall rebuilds adaptive reserve without losing audio.");
