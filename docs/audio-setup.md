# Audio setup and troubleshooting

## Route

```text
Applications -> default virtual playback endpoint -> WASAPI loopback capture
             -> EQ / preamp / spatial mix / limiter -> hardware playback output
```

1. Install a supported virtual endpoint. The current development build recognizes VB-Audio Cable or the prototype AudioFlow Output endpoint.
2. Select its playback/render endpoint as the Windows default Multimedia output.
3. Configure stereo playback. Use a format supported by the virtual device; the prototype driver's active transport is stereo 48 kHz, 16-bit PCM.
4. Keep a separate active hardware playback output available. AudioFlow uses it for processed playback.
5. Launch AudioFlow and start playback at low volume.

Do not route the processed output back into the same captured virtual endpoint. The application rejects unsupported default sources and checks for route/device changes while running.

## Common problems

| Symptom | Check |
| --- | --- |
| Audio will not start | The default Multimedia output must be a recognized virtual endpoint, with a separate active output available. |
| Playback becomes silent after changing devices | Let the route refresh, confirm both endpoints exist, and restart processing. |
| Only one channel behaves correctly | Confirm the virtual endpoint is configured for stereo. |
| Frequent limiting or audible distortion | Reduce boosts/preamp and confirm the source is clean. The limiter cannot repair distortion already present at the source. |
| A high-frequency band differs between devices | Filters are constrained by sample rate/Nyquist. Include the format in your bug report. |
| The spectrum drops analysis frames during load | The analyzer may drop analysis data to keep playback from waiting; distinguish the display from an actual listening dropout. |

## Hardware reports

Include the AudioFlow commit, Windows build, virtual endpoint type, hardware output model, sample rate/channels, reproduction steps, and whether bypass also fails. Redact personal paths and device IDs. Use [the hardware report template](https://github.com/LAKSHYA-cmd-tech/AudioFlow/issues/new/choose).

## Driver status

The AudioFlow Output driver is a SysVAD-derived prototype. Source checks and synthetic transport tests do not establish successful installation or audio fidelity. Follow [the driver VM guide](../driver/VM-Test-Guide.md) for isolated validation. Release signing and a supported installer remain roadmap work.
