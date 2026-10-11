# AudioFlow Output test VM

Use a disposable Windows 11 x64 VM. This PC runs Windows 11 Home with hardware virtualization enabled and 16 GB RAM, so [VirtualBox](https://www.virtualbox.org/wiki/Downloads) is a practical local VM option. Download the [official Windows 11 x64 ISO](https://www.microsoft.com/en-us/software-download/windows11) for the guest. A 4-vCPU, 6 GB RAM, 64 GB dynamic-disk VM is a starting configuration. Enable EFI and TPM 2.0, then take a snapshot after Windows setup. Keep test-signing changes inside the VM.

The x64 transfer bundle is `driver/test-bundle/AudioFlowOutput-x64-Test.zip`. It contains the built INF, SYS, and test-signed CAT, the public test certificate, Microsoft's x64 DevCon tool, and a self-contained `LoopbackVmTest` executable. The certificate's private key is **not** in the bundle. Do not change the INF or SYS after catalog generation; changing either invalidates the catalog.

## Install inside the VM

Extract the ZIP inside the VM. In an elevated Command Prompt, run:

```cmd
bcdedit /set testsigning on
```

Restart the VM. If Secure Boot policy rejects this setting, turn off Secure Boot **in the VM's settings** and retry. Then, from the extracted bundle folder in an elevated Command Prompt, run:

```cmd
certutil -addstore -f Root AudioFlowOutput-Test.cer
certutil -addstore -f TrustedPublisher AudioFlowOutput-Test.cer
devcon.exe install AudioFlowOutput\ComponentizedAudioSample.inf Root\AudioFlow_Output
```

The test certificate and test-signing procedure follow [Microsoft's driver test-signing guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/test-signing). Microsoft uses [DevCon to create the SysVAD root device](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/).

## Checks before using AudioFlow

### Verified VM result — October 4, 2026

The user reported a successful functional loopback test after the 0.2.2.0 update
and restart workflow, using test build `capture-probe-1`:

```text
capture first: tone A=20, silence gap=9, tone B=20, unexpected blocks=2
render first: tone A=18, silence gap=9, tone B=20, unexpected blocks=1
PASS: real stereo playback loopback, silence, and both stream start orders.
```

This confirms stereo signal identity, the silent gap, and both stream start orders
within the test's acceptance limits on this VM. It does not establish complete
fidelity or stability. AudioFlow processed playback, repeated start/stop,
sleep/resume, and device reconnect remain to be verified.

### App playback tearing follow-up

After the functional loopback PASS, the user reported periodic tearing during
music playback. The app now keeps an 80 ms capture reserve before consuming audio,
rebuilds that reserve after an underrun, and requests 50 ms speaker buffering
instead of 10 ms. Capture starts before speaker playback. This increases latency
to tolerate uneven VM callbacks; it does not fix audio gaps already present in
the driver's captured data or compensate continuously for device clock drift.

`AudioFlow-VM-Buffered-x64.zip` contains a self-contained app build for this test.
Extract into a new folder inside the VM, close the previous AudioFlow instance,
and launch the new `AudioFlow.exe`. Keep the 0.2.2.0 driver installed. First test
with EQ bypassed and spatial processing off, then enable processing. The buffer
tests pass for startup, uneven capture delivery, underrun recovery, and overflow
recovery. The user subsequently confirmed music playback without tearing in the
selectors build described below.

`AudioFlow-VM-Selectors-x64.zip` includes the same buffering changes and restores
the two visible device selectors. The earlier buffered build hid these controls
and chose the Windows default endpoint as its input, which could incorrectly
report AudioFlow as unavailable when the default was the VM's physical speakers.
In the updated build, choose **Speakers (AudioFlow Virtual Audio Device)** under
**AudioFlow input** and **Speakers (High Definition Audio Device)** under
**Speakers / headphones output**. Set the virtual device as Windows' default
playback device, then press **Start audio**. Device refreshes preserve the selected
input while it remains available. Test music with EQ bypassed and spatial processing
off before enabling processing.

On October 4, 2026, the user reported that the selectors build played music
without audible tearing in the VM. The processing settings and session duration
were not recorded. Longer sessions, repeated start/stop,
sleep/resume, and device reconnect still need dedicated verification.

### Track-transition tearing follow-up

#### Saved status — October 4, 2026 (work deferred; Adaptive VM test pending)

**Paused at the user's request. Resume here:** keep driver 0.2.4.0 installed
and test `driver/test-bundle/AudioFlow-VM-Adaptive-x64.zip`. Close Edge and the
old AudioFlow, extract the ZIP into a new VM folder, launch AudioFlow.exe, select
the virtual input and physical speaker output, and Start audio before reopening
Edge. Repeat YouTube Music song changes and the bass-test frequency transition,
initially with EQ bypassed and spatial off. Report remaining popping/stuttering
and whether Shortages or Overflows increase. No driver reinstall is needed.

The Adaptive build starts with 160 ms of capture reserve instead of 80 ms,
and can increase it up to 300 ms after larger callback gaps. Initial latency
increases by 80 ms. It retains silent endpoint playback, the callback CPU
optimizations, and the 50 ms speaker buffering request. Local buffer regressions
(140 ms delayed delivery, 220 ms adaptive recovery, and the 300 ms cap), app
publish, runtime/ZIP checks, and main-window startup smoke test pass. Its VM
result is not yet known. Do not treat it as a confirmed fix.

Latest evidence: driver 0.2.4 reduced but did not eliminate stuttering; the same
source is clean on direct physical playback. The new raw WAV also sounds clean
on physical playback and has no detected short exact-silence gaps or clipped
samples. Recorder callback gaps were 84.9 ms and 130.2 ms, exceeding the old
80 ms app reserve. The current WAV is 29.89 seconds, 5,738,924 bytes, modified
October 4 at 18:24:19; rechecking the subsequently attached path gave the same
metadata and analysis. No new Adaptive-build recording or listening result has
been supplied. Report: `driver/test-results/raw-capture-2026-10-04-latest.json`.

**Earlier improvement:** the user confirmed that `AudioFlow-VM-KeepAlive-x64.zip`
plays YouTube Music in Edge without stuttering across song changes on the VM,
with driver 0.2.3 installed and without the separate keep-alive command.
Session duration and number of transitions were not recorded. The app workaround
is verified for the reported reproduction; the underlying driver defect and
longer-term stability remain unconfirmed.

**Original fault still unresolved:** the user clarified that popping/crackling
has been present since the start of the investigation, not introduced by the
KeepAlive or Optimized builds. The Optimized build reduces stuttering slightly
but still produces stuttering and popping/crackling. The user reports it during a bass test's
low-frequency to high-frequency transition, including with EQ bypassed and
spatial processing off. The earlier song-change success does not establish that
all transitions are clean. The fault's duration and source level have not been
recorded. The user subsequently confirmed this bass test is clean on direct
physical VM playback.

**0.2.4 VM result:** the user reports reduced stuttering after the notification
timing correction, but stuttering remains across all types of frequencies.
This is partial improvement, not a fix. The same bass test is clean through
direct physical VM playback. No fresh raw capture or callback statistics from
the 0.2.4 test have been supplied. The earlier WAV predates both the latest
driver and app changes, so it cannot locate the remaining fault in this build.

**Earlier fault:** with the previous AudioFlow builds active, the first song plays cleanly, but pressing
**Next** triggers persistent stuttering/crackling. The user reports that stopping
and restarting AudioFlow does not clear it. Shortage/overflow counters do not
increase during the fault, and direct playback through the VM's physical Windows
speaker output is normal.

The user installed the 0.2.3.0 driver update and explicitly confirmed restarting
the VM afterward. The same crackling still occurs. The render-history fix below
passes its regression checks but **did not resolve the reported VM problem**.
The user subsequently confirmed that installed driver version is 0.2.3;
the problem remains unresolved. This confirmation is user-reported.

Available artifacts in `driver/test-bundle/`:

- `AudioFlowOutput-x64-0.2.4-Test.zip`: latest test-signed x64 driver bundle,
  with the delayed-notification correction; VM result pending.
- `AudioFlowOutput-x64-0.2.3-Test.zip`: preceding driver build, which did not
  resolve the user's original crackling.
- `AudioFlow-VM-Transitions-x64.zip`: app with visible input/output selectors,
  80 ms capture reserve, 50 ms speaker buffering, and buffer diagnostics.
- `AudioFlow-RawCapture-x64.zip`: standalone 30-second raw virtual-capture recorder.
- `AudioFlow-TransitionDiagnostics-x64.zip`: repeated render stream transition
  test with detection of short exact-silence runs inside known stereo tones.
- `AudioFlow-YouTubeDiagnostics-x64.zip`: includes silent `--keep-alive` playback
  for testing YouTube Music in Edge across track changes.
- `AudioFlow-VM-KeepAlive-x64.zip`: app that automatically keeps its selected
  virtual input endpoint active with silent playback while audio is running.
- `AudioFlow-VM-Optimized-x64.zip`: same keep-alive behavior with reduced
  spectrum publication synchronization and precomputed spatial routing gains.
- `AudioFlow-VM-Adaptive-x64.zip`: keeps the optimizations and raises the initial
  capture reserve to 160 ms, with bounded adaptation up to 300 ms after larger
  delivery gaps.

The earlier recording supplied by the user is
`C:\Users\lakshya sahu\Music\AudioFlow-raw-capture.wav`; its analysis is recorded
below. The user supplied a new recording at that same path after the 0.2.4 test;
its analysis is recorded below. The file now represents the newer capture.
The exact remaining cause is unconfirmed; avoid treating larger app buffers or
the 0.2.3.0 regression PASS as proof of a complete fix.

**Current work:** the user resumed the investigation and reports that the fault
is unchanged from the previous WAV, and confirmed installed version 0.2.3.
Continue using that recording as existing evidence; it has not been independently
confirmed against the installed 0.2.3.0 driver. A repeat recording was requested
but has not been supplied.

On resumption, all 2,446 standalone transport checks, the raw recorder/analyzer
self-tests, playback buffer tests (including 20 track gaps), and prepared driver
source checks passed again. These checks do not reproduce the VM fault.
Source review found that entering RUN reanchors the stream's QPC timeline, so
a simple end-of-stream position stall alone does not establish the cause of
persistent crackling across stream restarts. No new driver fix is confirmed.

**Latest comparison:** the user confirmed the same bass-test transition is
clean when played directly through the physical VM speakers, with AudioFlow
stopped. The popping/crackling is specific to the tested virtual audio route.

**Next investigation:** test the Adaptive app build against measured late
capture delivery. The user confirmed the new WAV sounds clean on physical
playback, while live processed playback stutters. The recorder statistics show
delivery gaps larger than the previous 80 ms app reserve.
The previous raw WAV already establishes short dropouts
before app processing; CPU optimization and keep-alive are partial mitigations,
not a complete repair. Compare the same bass source directly to physical VM
speakers when available. Longer playback sessions, repeated app start/stop,
sleep/resume, and device reconnect also remain to be tested.
If installed version details are needed later, VM PowerShell can report them with:

```powershell
Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceName -like '*AudioFlow*' } |
    Format-List DeviceName, DriverVersion, InfName, DeviceID
```

If a repeat recording becomes available, rename the old WAV and record the first
song followed by **Next** with AudioFlow active using the command below. Collect
the WAV and console output (packet count, captured duration, largest callback
gap), then compare its dropouts with the earlier recording. Continue diagnosing
the virtual capture path if the new raw recording is damaged.

#### Automated render stream transition diagnostic

Extract `AudioFlow-TransitionDiagnostics-x64.zip` into a new VM folder. Keep
driver 0.2.3.0 installed. Stop AudioFlow and all music players, then run:

```powershell
.\LoopbackVmTest\LoopbackVmTest.exe --transitions 2>&1 |
    Tee-Object -FilePath transition-test.txt
```

The test takes about 36 seconds. It opens and closes six render streams on the
virtual endpoint while keeping one loopback capture stream running. Each stream
plays the existing known stereo tone sequence and intentional silence. No physical
speaker output is opened. In addition to signal identity, it detects exact stereo
silence lasting at least 0.25 ms surrounded by the same tone; the analyzer self-test
detects injected 1 ms and 10 ms dropouts and excludes the intentional silent gap.
This addresses a limitation of the earlier 100 ms signal-identity checks, which
can accept a tone block containing a short dropout.

Save `transition-test.txt` and `AudioFlow-transition-capture.wav`. Rename an
existing diagnostic WAV before rerunning; the test refuses to overwrite it.
This is a diagnostic tool, not a driver fix. A PASS covers these controlled stream
recreations, not every music player's Next behavior or all possible corruption.
The new executable has passed local self-tests; its transition mode still needs
full console results from the VM. The user subsequently reported no stuttering
with this test, while YouTube Music in Edge still starts cleanly and becomes
stuttery after Next. The full test output was not supplied. Controlled stream
recreation alone therefore has not reproduced the reported browser fault.

#### YouTube Music in Edge: silent endpoint activity experiment

**Current next test:** extract `AudioFlow-YouTubeDiagnostics-x64.zip` into a new
VM folder. Keep driver 0.2.3.0 and the existing AudioFlow app. If audio is already
stuttering, first restore a clean starting state using the workflow that normally
makes the first song play cleanly; restarting AudioFlow alone was reported not to
clear the fault. Start the following command before playing the first song:

```powershell
.\LoopbackVmTest\LoopbackVmTest.exe --keep-alive
```

Leave the console open, run AudioFlow normally, and play YouTube Music in Edge.
Advance through several songs, then press Enter in the test console when done.
Report whether Next still starts the persistent stuttering with this command
running. This mode renders only digital silence to the virtual endpoint; it does
not generate tones, open physical speakers, or make a recording.

The experiment tests whether keeping a render client active changes the fault
across browser playback gaps. It does not prove Edge actually idles the endpoint,
and a change in behavior would not by itself establish the complete cause or a
production fix. The silent provider and the existing analyzer/recorder self-tests
pass locally. The user subsequently confirmed the standalone command prevents
stuttering, as recorded below. The user confirmed that closing
and reopening Edge restores clean first-song playback, then Next triggers the
stuttering again. This is browser restart recovery; tab reload alone has not
been tested. It suggests a playback-session interaction without proving whether
the defect is in the browser or driver.

#### Integrated silent playback workaround — October 4, 2026

The user reported that YouTube Music in Edge did not stutter across Next while
`--keep-alive` was running. After stopping that command, the next song change
triggered the stuttering again. This supports keeping the virtual render endpoint
active as a workaround on this VM. It does not independently identify the exact
idle/restart defect or establish complete driver stability.

`AudioFlow-VM-KeepAlive-x64.zip` integrates the same silent playback behavior.
Start audio opens a separate shared-mode silent render client on the selected
virtual input before starting capture and physical output. It emits PCM16 zeros
at the capture sample rate and channel count, with the diagnostic tool's 20 ms
buffer request. Stop audio and startup-error cleanup release it; unexpected
termination is reported as an audio fault. The silence mixes with browser audio
on the virtual endpoint. It does not route processed speaker audio back to input.

Extract this app ZIP into a new VM folder and close the previous AudioFlow app.
Keep driver 0.2.3.0 installed. Stop the external keep-alive command and close Edge
to clear the already-triggered fault. Launch the new AudioFlow.exe, select the
virtual input and the physical VM speaker output, and press Start audio before
reopening Edge. Test several YouTube Music song changes without the separate
command, first with EQ bypassed and spatial processing off, then with processing.
Also test stopping and starting the new app, reopening Edge before each fresh
trial when necessary. Report whether stuttering returns and the session duration.

The integrated app publishes successfully and the playback buffer regressions
pass. Package/runtime checks pass and the app creates its main window in the
local startup smoke test. The user subsequently confirmed that the integrated
KeepAlive app works without stuttering in the VM's YouTube Music song-change
reproduction. The separate console command is no longer needed while this app
is running. Session duration, transition count, and processing settings were not
recorded. Longer playback, repeated app start/stop, sleep/resume, and device
reconnect remain to be verified. A driver-level repair remains separate from
this verified app workaround.

#### App callback optimization — bass-test follow-up

`AudioFlow-VM-Optimized-x64.zip` retains silent keep-alive playback. Spectrum
frames now publish once per output callback instead of once per audio frame.
Spatial pan square roots and node mix coefficients are calculated when settings
change; each processed frame uses a stereo matrix. Clipping telemetry aggregates
once per callback. Buffer sizes, limiter behavior, and source audio are unchanged.

The offline DSP regression checks 1,000 spatial configurations against the
original routing, then runs 30 seconds of alternating 50 Hz/6 kHz tones in both
bypass and EQ/spatial modes. These checks pass with finite bounded output, no
inserted exact-silence dropouts, bit-identical low-level bypass output, and no
allocations on the render thread. They do not exercise WASAPI, the VM driver,
Edge, or the user's bass-test source. This build improves callback work; it is
not a confirmed fix for the original fault.

Close the old app, extract to a new VM folder, keep driver 0.2.3.0, and reopen Edge
after starting audio in the new app. Repeat the bass test and report whether the
stutter is brief at the frequency change or continues afterward, and whether the
app's Shortages/Overflows counters increase. The user subsequently reported
slightly reduced stuttering, with persistent popping/crackling, and clarified
that these artifacts existed from the beginning. Current-build counters and
the duration of each artifact have not been supplied. The original issue
remains unresolved; do not mark the optimization as a successful repair.

#### New raw capture analysis after 0.2.4 — October 4, 2026

The supplied WAV at `C:\Users\lakshya sahu\Music\AudioFlow-raw-capture.wav`
is now 5,738,924 bytes (file modified at 18:24:19). It contains 1,434,720 stereo
PCM16 frames at 48 kHz, lasting 29.89 seconds. Peak amplitude is approximately
0.9850 full scale, overall RMS 0.3670, and no samples reach either PCM clipping
limit. Its only exact-stereo-silence run of at least 0.25 ms is an initial
6.192375 seconds. No short interior exact-silence gaps are detected, unlike the
older recording's repeated 1–10 ms gaps. This does not rule out phase/timing
discontinuities, missing callbacks, or other audible corruption, and it does not
prove the raw capture sounds clean.

The user confirmed this new WAV sounds clean when played through physical
speakers with AudioFlow stopped. Two recorder summaries were supplied:

```text
Packets: 2591; captured audio: 29.96 s; largest callback gap: 84.9 ms.
Packets: 2571; captured audio: 29.89 s; largest callback gap: 130.2 ms.
```

The second duration matches the supplied file. These delivery gaps exceed the
old app's 80 ms reserve. A recorder can concatenate intact late packets while
live speaker playback runs out waiting for them. This makes live buffering and
output timing the current target, without proving every artifact has one cause.

The report is `driver/test-results/raw-capture-2026-10-04-latest.json`.
`driver/tests/Analyze-RawCapture.ps1` performs this analysis; its synthetic
fixture detects 1 ms and 10 ms interior gaps while distinguishing a longer
100 ms silent interval. Direct physical playback is user-reported clean, and
the recorder statistics are recorded above.
The earlier WAV observations apply to the older capture, not this replacement.

#### Adaptive app reserve for measured capture delivery gaps

`AudioFlow-VM-Adaptive-x64.zip` keeps driver 0.2.4.0, silent endpoint playback,
callback optimizations, and the existing 50 ms speaker buffer request. It raises
the initial app capture reserve from 80 ms to 160 ms to cover the reported
130.2 ms capture delivery pause with headroom. Observed longer delivery gaps
can increase the reserve in 10 ms increments, with 20 ms headroom and a 300 ms
cap. The larger reserve applies on startup or the next underrun re-prime; it
does not discard audio or interrupt running playback just to increase latency.
It does not shrink during a session. This adds 80 ms of initial app buffering
relative to the previous build, with additional latency possible after larger
stalls. Audio already missing from capture cannot be recreated.

The buffer regressions pass, including intact queued capture delivery after a
140 ms pause without live silence, and adaptive recovery after a 220 ms pause
without discarding queued audio. VM verification remains pending. Close the
old app and Edge, extract this app into a new VM folder, select the same input
and physical output, Start audio, then reopen Edge and repeat the bass test.
Report whether stuttering/popping remains and whether Shortages or Overflows
increase. No driver reinstall is needed for this app test.

#### Driver 0.2.4.0: discard missed notification periods after a late callback

Source review found that a delayed timer callback retains all missed notification
periods and repays them one per subsequent tick. For a 10 ms period, a callback
at 55 ms leaves 45 ms of debt, triggering duplicate notifications at 56–59 ms
even though the position-based packet counter has not advanced. The latest
completed packet has already been reported; these duplicate events are not
new audio. This is a reproduced scheduling defect, not yet a confirmed cause
of the user's crackling.

Version 0.2.4.0 retains only the fractional period after a delayed callback on
the AudioFlow virtual endpoint. The same example retains 5 ms, then resumes
notifications at 60 ms instead of generating a catch-up burst. Missed DMA
audio is not reconstructed. The source preparation patch has also been refreshed
to include the current render-history fix and timing correction.

All 2,451 transport/timing regression checks, the x64 driver build, INF and
Universal API validation, catalog generation, and test-signature verification
pass. The refreshed patch applies to the upstream source. The user subsequently
reported reduced stuttering, but it remains across all frequencies in the VM.
The correction has not resolved the original fault.
Use `AudioFlowOutput-x64-0.2.4-Test.zip`, close AudioFlow and Edge, extract the
driver bundle to a new VM folder, and update from elevated PowerShell:

```powershell
.\devcon.exe update .\AudioFlowOutput\ComponentizedAudioSample.inf 'Root\AudioFlow_Output'
```

Restart the VM and confirm driver version 0.2.4.0 in Device Manager. Keep the
Optimized app build, then repeat the standalone functional loopback test and
the same bass-test transition, initially with EQ bypassed and spatial off.
Record whether popping/crackling still occurs. This driver build is a targeted
test correction; do not mark the original problem resolved before the VM result.

#### Driver 0.2.3.0: preserve delayed capture history across render gaps

Analysis of the user's `AudioFlow-raw-capture.wav` (30.01 seconds, stereo 48 kHz)
found repeated exact-silence runs of 1–10 ms after the track-change gap, starting
at about 15.53 seconds. These gaps occur before AudioFlow's EQ and speaker output.
There were no PCM samples at the clipping limit. The recording establishes
dropouts in the virtual capture path; it does not identify every underlying cause.

A driver regression test reproduced a related transport bug: a forward render
gap discarded valid earlier history, even though loopback capture reads 30 ms
behind render. Version 0.2.3.0 retains that earlier history and silences only the
missing interval. Rewinds, explicit resets, and gaps beyond ring capacity still
discard stale history. This fix does not recreate missing render samples.
All 2,446 standalone transport checks, x64 driver build, INF/API checks, catalog
generation, and test-signature verification pass. The subsequent VM test still
produced crackling after update and restart; this is not a complete fix.

Use `AudioFlowOutput-x64-0.2.3-Test.zip`. Stop AudioFlow, extract to a new VM
folder, and run the following in an elevated Command Prompt from that folder:

```cmd
devcon.exe update AudioFlowOutput\ComponentizedAudioSample.inf Root\AudioFlow_Output
```

In elevated PowerShell, use the explicit current-directory executable path:

```powershell
.\devcon.exe update .\AudioFlowOutput\ComponentizedAudioSample.inf 'Root\AudioFlow_Output'
```

Restart the VM. Confirm Device Manager's driver version is **0.2.3.0**, run the
standalone loopback test, and test several song transitions with the existing
Transitions app build, EQ bypassed, and spatial processing off. If crackling
persists, rename the previous raw WAV and repeat `--record` with this driver.

The user subsequently clarified that tearing starts when advancing to the next
song and continues even after stopping and restarting AudioFlow. The earlier
successful listening report therefore does not establish that tearing is fixed.
The cause remains unconfirmed.

`AudioFlow-VM-Transitions-x64.zip` retains the selectors and buffering settings
and adds a visible buffer status with shortage and overflow counts. This is a
diagnostic build, not a confirmed tearing fix. The buffer regression test passes
20 simulated track gaps, checking silence and preservation of each new track's
start; it does not exercise the VM driver or music player.

Extract the app into a new folder and close the old app. The original diagnostic
comparison used driver 0.2.2.0; continue the investigation with installed 0.2.3.0.
Test the same track transition with EQ bypassed and spatial processing off.
Record whether Shortages or Overflows increase during the tearing. A shortage
at a silent gap alone does not establish the cause. Then stop AudioFlow, select
the VM's physical speakers as Windows' default playback device, and restart the
music player so it uses that device. Compare the same transition directly.
Restore the virtual default playback device before testing AudioFlow again.

The user reported that the app's shortage/overflow counters do not increase
during crackling, and direct playback to the VM's physical speakers is normal.
This reduces the likelihood of repeated app-buffer starvation but does not
establish whether the virtual capture, DSP, or processed speaker output is faulty.

#### Record the virtual audio before processing

Extract `AudioFlow-RawCapture-x64.zip` into a new VM folder. Keep AudioFlow
running with Windows' default playback set to the virtual device. In PowerShell
from the extracted folder, run:

```powershell
.\LoopbackVmTest\LoopbackVmTest.exe --record
```

Play music and trigger a song change during the 30-second recording. This mode
does not generate test tones or play to any speaker. It captures the virtual
device into memory and saves `AudioFlow-raw-capture.wav` in the current folder
after recording ends. Rename that file before recording again.

Stop AudioFlow, change Windows' default playback to the physical VM speakers,
and open the WAV in a freshly started player. Report whether the recording also
crackles and copy the tool's packet count, captured duration, and largest callback
gap. A crackling recording implicates the virtual capture path; a clean recording
while the live processed audio crackles points toward app processing/output.
Missing capture callbacks can shorten the WAV, so the duration and callback gap
also matter. Restore the virtual default output before running AudioFlow again.

The v0.2 driver implements stereo 48 kHz, 16-bit PCM render-to-loopback transport. The original package returned a synthetic 3 kHz tone at 50% amplitude, and the intermediate tone-disable build returned silence. Neither old build implements real audio transport. Update to the current package before running the loopback test.

### Silence the original installed package

Stop AudioFlow. Inside the VM, run this in an elevated Command Prompt, then restart the VM:

```cmd
reg add "HKLM\SYSTEM\CurrentControlSet\Services\audioflowoutput\Parameters" /v DisableToneGenerator /t REG_DWORD /d 1 /f
```

This only silences the original sample-based package. It does not add real loopback to that old binary. The current driver bypasses the tone generator on loopback, so this setting can remain enabled after upgrading.

### Update an existing test installation

Extract the corrected ZIP into a new folder in the VM. From that folder in an elevated Command Prompt, update the existing device instead of creating a duplicate:

```cmd
devcon.exe update AudioFlowOutput\ComponentizedAudioSample.inf Root\AudioFlow_Output
```

Restart the VM and repeat the checks below. Keep the signed INF, SYS, and CAT together.

### If loopback initialization fails with 0x88890016

The earlier v0.2 package reported the 30 ms software loopback delay as a 5,760-byte hardware FIFO. The next build moved it to hardware codec latency, but the VM still rejected all 24 capture initialization requests. Version 0.2.2.0 reports zero hardware FIFO, chipset, and codec latency, matching the virtual hardware path. The software transport still buffers about 30 ms. The user subsequently reported the functional loopback PASS recorded above.

Replace the entire extracted bundle with the newly built ZIP, run the update command above, and restart the VM before rerunning `LoopbackVmTest\LoopbackVmTest.exe`. Updating only the test executable does not change the installed driver. If initialization still fails, save the complete test output and run `devcon.exe driverfiles Root\AudioFlow_Output` to confirm the installed driver path.

### Verify real loopback before listening

If `0x88890016` persists after updating and restarting, use the diagnostic test
build marked `capture-probe-1`. From the extracted bundle in PowerShell, run:

```powershell
.\LoopbackVmTest\LoopbackVmTest.exe --probe 2>&1 | Tee-Object -FilePath loopback-probe.txt
```

The probe tries 24 shared-mode loopback initialization requests with different
buffer durations, event/polling settings, and format-conversion flags. It reports
the exact failing API stage. An accepted request is not a functional audio PASS.
Send the full output, and confirm that the VM was restarted after the driver
update. The standalone `LoopbackVmTest-Diagnostics-x64.zip` contains the updated
test folder and can be extracted over the existing test folder without reinstalling
the driver.

Windows may display the endpoint as **Speakers (AudioFlow Virtual Audio Device)** rather than **AudioFlow Output**. Both names refer to our virtual driver. Select that virtual device as the app's input and Windows default playback device, and use **Speakers (High Definition Audio Device)** as the separate processed output. The current test recognizes both virtual endpoint names; older test executables only recognized AudioFlow Output and incorrectly reported zero matching endpoints.

Stop AudioFlow and other audio playback. In Sound settings, select stereo 48 kHz for AudioFlow Output (its driver pin format is 16-bit PCM). From the extracted bundle folder, run:

```cmd
LoopbackVmTest\LoopbackVmTest.exe
```

The test renders two different low-level stereo tone pairs separated by silence, captures the same endpoint, and checks both channel identities, the silent gap, and unexpected audio. It repeats with capture starting before playback and playback starting before capture. A constant 3 kHz tone or an all-silent driver fails. The executable renders only to AudioFlow Output and does not open a physical output. Expect `PASS: real stereo playback loopback, silence, and both stream start orders.` Save the output if it reports FAIL.

This is a functional routing test, not a complete fidelity or driver stability certification. VM callback jitter exceeding the 30 ms buffering allowance may produce gaps; report the test output rather than boosting EQ to compensate.

### If the test finds zero AudioFlow Output endpoints

This error happens before streaming starts: the test cannot find an active playback endpoint with the expected name. It does not establish whether the cause is driver startup, a disabled endpoint, or a renamed endpoint. Keep AudioFlow stopped. From the extracted folder in Administrator Command Prompt, run:

```cmd
devcon.exe status Root\AudioFlow_Output
```

Open Device Manager and inspect **AudioFlow Virtual Audio Device** under **Sound, video and game controllers**. Copy the exact Device status text and any problem code from Properties. If DevCon finds no matching device, report that result before creating another device.

Run `mmsys.cpl`, open Playback, and enable **Show Disabled Devices** and **Show Disconnected Devices** in the list's right-click menu. If AudioFlow Output is disabled, enable it and rerun the test. If no AudioFlow endpoint appears, the driver/device status is needed to investigate endpoint creation.

The latest test executable can list every playback endpoint, including disabled ones:

```cmd
LoopbackVmTest\LoopbackVmTest.exe --list
```

Report that output together with DevCon's status. Do not select the VM's physical speakers as the loopback test endpoint to work around a missing virtual device.

1. In Device Manager, confirm **AudioFlow Virtual Audio Device** has no error indicator.
2. In Windows Sound settings, confirm exactly one new **AudioFlow Output** playback device and no new sample microphone or other SYSVAD playback endpoints.
3. Play audio to AudioFlow Output and confirm its Windows level meter moves.
4. Run the standalone loopback test above and confirm PASS. Then run AudioFlow inside the VM, set AudioFlow Output as the VM's default playback device, and select the VM's separate sound device as AudioFlow's processed output. Start with low physical output volume. Press Start Audio and play music: the processed output should follow the music, and stopping the music should produce silence. Test repeated start/stop, sleep/resume, and device reconnect. Driver loopback adds about 30 ms before the application's own buffering.
5. Revert to the clean VM snapshot when finished. Do not use this test-signed package for a production install.
