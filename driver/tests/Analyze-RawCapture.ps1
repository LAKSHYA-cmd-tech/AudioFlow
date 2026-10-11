param([Parameter(Mandatory = $true)][string] $Path, [string] $ReportPath)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
public class AudioFlowSilenceRun {
    public double StartSeconds, DurationMilliseconds, BeforeRms, AfterRms;
    public int Frames;
}
public class AudioFlowWavResult {
    public int SampleRate, Channels, BitsPerSample;
    public long Frames, ClippedSamples;
    public double DurationSeconds, Peak, Rms;
    public List<AudioFlowSilenceRun> SilenceRuns = new List<AudioFlowSilenceRun>();
    public List<AudioFlowSilenceRun> ShortInteriorGaps = new List<AudioFlowSilenceRun>();
}
public static class AudioFlowWavAnalyzer {
    static double Rms(short[] pcm, int start, int frames) {
        double sum = 0;
        for (int i = start * 2; i < (start + frames) * 2; ++i) {
            double value = pcm[i] / 32768.0; sum += value * value;
        }
        return Math.Sqrt(sum / (frames * 2));
    }
    public static AudioFlowWavResult Analyze(string path) {
        var result = new AudioFlowWavResult();
        byte[] data = null;
        ushort encoding = 0;
        using (var reader = new BinaryReader(File.OpenRead(path))) {
            if (new string(reader.ReadChars(4)) != "RIFF") throw new Exception("Expected RIFF WAV.");
            reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") throw new Exception("Expected WAVE data.");
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length) {
                string id = new string(reader.ReadChars(4)); uint size = reader.ReadUInt32();
                long end = reader.BaseStream.Position + size;
                if (end > reader.BaseStream.Length) throw new Exception("Truncated WAV chunk.");
                if (id == "fmt ") {
                    encoding = reader.ReadUInt16(); result.Channels = reader.ReadUInt16();
                    result.SampleRate = reader.ReadInt32(); reader.ReadUInt32(); reader.ReadUInt16();
                    result.BitsPerSample = reader.ReadUInt16();
                } else if (id == "data") data = reader.ReadBytes(checked((int)size));
                reader.BaseStream.Position = end + (size & 1);
            }
        }
        if (encoding != 1 || result.Channels != 2 || result.BitsPerSample != 16 || result.SampleRate <= 0 || data == null || data.Length % 4 != 0)
            throw new Exception("Analyzer requires complete stereo PCM16 WAV data.");
        var pcm = new short[data.Length / 2]; Buffer.BlockCopy(data, 0, pcm, 0, data.Length);
        int total = pcm.Length / 2;
        result.Frames = total; result.DurationSeconds = total / (double)result.SampleRate;
        double energy = 0;
        foreach (short sample in pcm) {
            double value = sample / 32768.0; energy += value * value;
            result.Peak = Math.Max(result.Peak, Math.Abs(value));
            if (sample == short.MinValue || sample == short.MaxValue) result.ClippedSamples++;
        }
        result.Rms = pcm.Length == 0 ? 0 : Math.Sqrt(energy / pcm.Length);
        int minimum = Math.Max(1, result.SampleRate / 4000); // 0.25 ms
        int context = Math.Max(1, result.SampleRate / 200); // 5 ms
        for (int frame = 0; frame < total;) {
            if (pcm[frame * 2] != 0 || pcm[frame * 2 + 1] != 0) { frame++; continue; }
            int start = frame;
            while (frame < total && pcm[frame * 2] == 0 && pcm[frame * 2 + 1] == 0) frame++;
            int length = frame - start;
            if (length < minimum) continue;
            var run = new AudioFlowSilenceRun {
                StartSeconds = start / (double)result.SampleRate,
                DurationMilliseconds = length * 1000.0 / result.SampleRate, Frames = length,
                BeforeRms = start >= context ? Rms(pcm, start - context, context) : 0,
                AfterRms = frame + context <= total ? Rms(pcm, frame, context) : 0
            };
            result.SilenceRuns.Add(run);
            if (run.DurationMilliseconds <= 20 && run.BeforeRms > 0.001 && run.AfterRms > 0.001)
                result.ShortInteriorGaps.Add(run);
        }
        return result;
    }
}
'@
$source = Get-Item -LiteralPath $Path
$analysis = [AudioFlowWavAnalyzer]::Analyze($source.FullName)
$report = [ordered]@{ SourcePath = $source.FullName; SourceBytes = $source.Length;
    SourceModified = $source.LastWriteTime.ToString('o'); Analysis = $analysis;
    Interpretation = 'Exact silence inside non-silent audio is a dropout candidate; source edits/silence are not excluded by this analysis. No callback timestamps are stored in WAV.' }
if ($ReportPath) {
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
$analysis | Select-Object SampleRate, Channels, BitsPerSample, Frames, DurationSeconds, Peak, Rms, ClippedSamples | Format-List
Write-Output "Exact-silence runs >= 0.25 ms: $($analysis.SilenceRuns.Count)"
Write-Output "Short interior gaps with signal on both sides: $($analysis.ShortInteriorGaps.Count)"
$analysis.ShortInteriorGaps | Select-Object -First 30 StartSeconds,DurationMilliseconds,BeforeRms,AfterRms | Format-Table -AutoSize
Write-Output 'Silence runs by duration:'
$analysis.SilenceRuns | Sort-Object DurationMilliseconds -Descending | Select-Object -First 12 StartSeconds,DurationMilliseconds | Format-Table -AutoSize
Write-Output 'Short interior gaps per second:'
$analysis.ShortInteriorGaps | Group-Object { [Math]::Floor($_.StartSeconds) } | Select-Object Name,Count | Format-Table -AutoSize
