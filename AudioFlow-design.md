# AudioFlow dashboard

Figma design source: [AudioFlow — EQ & Spatial Dashboard](https://www.figma.com/design/2Y3Wg9OqmkJgUFakKJuB7n)

Visual system source: [Windows UI kit (Community)](https://www.figma.com/design/k1kRJlvDVP3oarNj7F451P/Windows-UI-kit--Community). Use its dark surface palette, `#60CDFF` accent, Segoe UI Variable typography, and native WinUI Slider, ToggleSwitch, ComboBox, and Button patterns. The kit's component keys are not importable into the AudioFlow file as a linked library in this workspace, so the AudioFlow Figma screen currently mirrors those values without live component instances.

The 1600 × 900 screen contains an AudioFlow tuning library, scaled logarithmic response plot and 20-node filter bank, input/output meters, post-EQ FFT, preamp, and built-in spatial panel. `MainWindow.xaml` implements the panel structure in WinUI 3. Routing controls are hidden from the user-facing sidebar.

## Audio setup

1. Install a virtual playback device and set its playback/render endpoint as the Windows default Multimedia output. The planned product device name is **AudioFlow Output**. The current development build also recognizes VB-Audio Cable.
2. AudioFlow automatically uses that default endpoint for loopback capture and chooses a separate active output for processed playback. It refuses to start when the default device is not a recognized virtual endpoint.
3. Configure the cable for stereo. The highest EQ band is limited by the actual sample rate's Nyquist frequency; at 48 kHz the 24 kHz control is evaluated below Nyquist.

The route is checked every two seconds while AudioFlow is open. If the source or output disappears, or the Windows default playback device changes, playback stops and the route is refreshed. The user can restart audio once a valid route is available.

The repository contains the app-side routing engine and SysVAD-derived x64 and ARM64 prototype packages for **AudioFlow Output**. They build and pass standalone WDK INF, API, and catalog checks, and their catalogs are test signed. The driver has not been installed or tested on a Windows test machine, and there is no installer. The future installer must install a properly release-signed virtual audio driver whose Windows endpoint is actually named **AudioFlow Output**, then configure the default endpoint and choose a safe hardware playback endpoint. Renaming an in-app label does not rename an existing VB-Audio driver in Windows.

## Profiles and live controls

Named profiles save all 20 EQ bands, preamp, spatial settings, node pans, and bypass state. Routing is selected independently of profiles. Import and export use JSON files. Older JSON files containing only an EQ band array remain importable; device IDs in older profiles are ignored.

Each band shows its gain, frequency, filter shape, and Q controls with explanations on hover or focus, Enter commits numeric edits, and Reset restores that band. The bypass switch compares processed and original audio. A stereo-linked peak limiter after EQ, spatial processing, and bypass keeps digital output at or below −1 dBFS; the meter reports when the limiter reduces peaks. Sustained limiting indicates that the selected band boosts need more headroom. Audio already distorted at the source cannot be repaired by the output limiter.

## Spatial rendering

AudioFlow's built-in spatial renderer maps stereo input to eight virtual speaker nodes with adjustable depth, ambience, wet/dry mix, and per-node pan. It does not require an external impulse file.

## Performance design

UI changes publish replacement RBJ biquad filter banks without taking a lock on the playback callback. The response graph uses the same coefficients as the audio processor. The post-EQ audio callback writes stereo samples to a bounded single-producer analyzer ring; a worker computes overlapping 4096-point FFT frames, combines channel energy, and publishes 64 logarithmic frequency bands. The spectrum display adds smoothed bars, peak markers, and a dominant-frequency readout. Meter values are published as scalar snapshots and polled by the UI at 30 fps.
