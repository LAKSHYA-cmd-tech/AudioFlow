using AudioFlow.Audio;
using NAudio.Wave;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
// Compare the optimized routing against the original eight-node calculation.
var random = new Random(731);
for (int trial = 0; trial < 1000; trial++)
{
    var spatial = new SpatialProcessor();
    float depth = random.NextSingle(), ambience = random.NextSingle(), mix = random.NextSingle();
    spatial.Configure(true, depth * 100, ambience * 100, mix * 100);
    float[] pans = new float[8];
    for (int n = 0; n < 8; n++) { pans[n] = random.NextSingle(); spatial.SetPan(n, pans[n] * 100); }
    float l = random.NextSingle() * 2 - 1, r = random.NextSingle() * 2 - 1;
    float[] nodes = [l, (l + r) * 0.35f, r, (l + r) * 0.08f,
        l * ambience * 0.3f, r * ambience * 0.3f, l * ambience * 0.2f, r * ambience * 0.2f];
    float wetL = 0, wetR = 0;
    for (int n = 0; n < 8; n++)
    {
        float pan = 0.5f + (pans[n] - 0.5f) * depth;
        wetL += nodes[n] * MathF.Sqrt(1 - pan);
        wetR += nodes[n] * MathF.Sqrt(pan);
    }
    float expectedL = l * (1 - mix) + wetL * mix * 0.65f;
    float expectedR = r * (1 - mix) + wetR * mix * 0.65f;
    spatial.Process(ref l, ref r);
    Check(MathF.Abs(l - expectedL) < 0.000001f && MathF.Abs(r - expectedR) < 0.000001f,
        "Spatial routing changed beyond float rounding tolerance.");
}
Console.WriteLine("PASS: 1,000 spatial configurations preserve the original mix.");

foreach (bool processed in new[] { false, true })
{
    using var analyzer = new SpectrumAnalyzer(48000);
    var source = new AlternatingTones();
    var spatial = new SpatialProcessor();
    spatial.Configure(processed, 60, 45, 80);
    var bands = EqBand.CreateDefaults();
    if (processed) bands[3] = bands[3] with { GainDb = 12 };
    var processor = new EqualizerProcessor(source, analyzer, spatial, bands);
    processor.SetBypass(!processed);
    var samples = new float[960];
    for (int warm = 0; warm < 100; warm++) processor.Read(samples);
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    int silentRun = 0;
    for (int block = 0; block < 3000; block++)
    {
        Check(processor.Read(samples) == samples.Length, "DSP shortened a callback.");
        for (int i = 0; i < samples.Length; i += 2)
        {
            Check(float.IsFinite(samples[i]) && float.IsFinite(samples[i + 1]) &&
                MathF.Abs(samples[i]) <= 1 && MathF.Abs(samples[i + 1]) <= 1, "DSP output is invalid or clipped.");
            silentRun = samples[i] == 0 && samples[i + 1] == 0 ? silentRun + 1 : 0;
            Check(silentRun < 12, "DSP inserted a silence dropout at a frequency transition.");
            if (!processed)
                Check(samples[i] == source.LastBlock[i] && samples[i + 1] == source.LastBlock[i + 1],
                    "Bypassed low-level playback changed the source signal.");
        }
    }
    Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "DSP allocated memory on the render thread.");
    Console.WriteLine($"PASS: 30 s of 50 Hz/6 kHz transitions, {(processed ? "EQ + spatial" : "bypassed")}; no inserted silence or render-thread allocations.");
}

sealed class AlternatingTones : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public float[] LastBlock { get; } = new float[960];
    private long frame;
    private double phase;
    public int Read(Span<float> samples)
    {
        for (int i = 0; i < samples.Length; i += 2, frame++)
        {
            double hz = frame / 14400 % 2 == 0 ? 50 : 6000;
            samples[i] = LastBlock[i] = (float)(0.25 * Math.Sin(phase));
            samples[i + 1] = LastBlock[i + 1] = (float)(0.25 * Math.Sin(phase + 0.7));
            phase = (phase + 2 * Math.PI * hz / 48000) % (2 * Math.PI);
        }
        return samples.Length;
    }
}
