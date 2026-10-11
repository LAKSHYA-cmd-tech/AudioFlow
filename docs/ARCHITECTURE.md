# Architecture

AudioFlow is a local WinUI application. Its primary project is `AudioFlow.csproj`; it excludes the companion applications and standalone driver test code from its compilation.

## Source map

| Area | Files | Responsibility |
| --- | --- | --- |
| Application entry | `App.xaml`, `App.xaml.cs` | WinUI startup and resources |
| Main interface | `MainWindow.xaml`, `MainWindow.xaml.cs` | EQ, meters, spatial controls, profiles, and UI events |
| Routing | `Audio/EqualizerEngine.cs` | Capture/playback endpoints and audio lifecycle |
| Callback processing | `Audio/EqualizerProcessor.cs` | Filter bank, preamp, spatial processing, bypass transition, limiter, metering |
| Filters and defaults | `Audio/Biquad.cs`, `Audio/EqBand.cs` | Biquad coefficients and eleven default bands |
| Spatial mix | `Audio/SpatialProcessor.cs` | Eight-node settings converted to a stereo matrix |
| Output ceiling | `Audio/PeakLimiter.cs` | Stereo-linked limiting and limiter telemetry |
| Spectrum | `Audio/SpectrumAnalyzer.cs` | Bounded analysis ring and background FFT worker |
| Playback buffering | `Audio/PlaybackBuffer.cs` | Reserve and recovery for capture/playback timing differences |
| Profiles and presets | `AudioFlowProfile.cs`, `Audio/UserSettings.cs`, `Audio/PresetLibrary.cs`, `Audio/AutoEqDatabase.cs` | Profile data, persistence, and tuning library |
| Virtual endpoint | `driver/src/`, `driver/AudioFlowOutput.inx` | SysVAD-derived prototype transport and INF |
| Regression checks | `driver/tests/DspTests/`, `driver/tests/PlaybackBufferTests/` | Standalone checks against real application processing sources |

## Thread boundaries

The UI publishes replacement filter-bank snapshots rather than taking a lock inside `EqualizerProcessor.Read`. Spatial settings are converted to a stereo matrix when settings change. The callback processes stereo samples, records scalar meter values, and pushes post-EQ samples into the analyzer ring.

The analyzer computes windowed FFT frames on a background worker. If its ring fills, analysis data is dropped rather than blocking audio playback. The UI reads published meters and spectrum bins independently of the callback.

## Playback stages

```text
source -> EQ filters and macro filters -> preamp -> spatial matrix
       -> optional night-mode gain -> bypass blend -> limiter -> output
                                                 -> post-EQ spectrum analysis
```

Input/output meters are calculated inside the callback. Filter updates blend old and new filter state across a short transition. Profile state and route selection have different lifecycles; importing a profile should not silently restore stale device identifiers.

## Validation boundaries

The standalone test projects compile the application DSP/buffer files directly. They can detect changes in those components without launching WinUI. The driver and actual WASAPI route require separate Windows/VM/hardware evidence. See [development.md](development.md) for the automated coverage and its limits.
