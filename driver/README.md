# AudioFlow virtual audio driver

AudioFlow needs a Windows playback endpoint named **AudioFlow Output** so system audio can enter the existing WASAPI loopback processing engine. The current application works with VB-Audio Cable for development. This workspace contains a render-only SysVAD-derived prototype and test-signed x64 and ARM64 packages. VM testing of the original package exposed SysVAD's synthetic 3 kHz loopback tone when starting AudioFlow.

**The v0.2 source implements real render-to-loopback audio transport.** The driver copies consumed render frames into nonpaged, per-stream circular buffers and mixes them into loopback capture using a shared QPC timeline. Host, offload, engine, and loopback formats are restricted to stereo 48 kHz, 16-bit PCM. Loopback adds 30 ms of buffering to accommodate separate render/capture callbacks; missing or overwritten frames return silence. Multiple capture clients read the same mix independently. Mixing saturates at the PCM limits, and loopback protection/DRM mute clears stored history. No kernel floating point or allocation occurs in the audio callbacks.

The loopback pin advertises a pre-volume/mute tap point using [Microsoft's loopback capability property](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-audioloopback). Post-volume loopback and other device formats are not supported by this version. The sample's synthetic capture generator remains disabled and is bypassed on the active loopback path, even when the old DisableToneGenerator registry setting is present. End-to-end VM testing of this implementation is still required.

## Source and preparation

Base the driver on Microsoft's [SysVAD virtual audio driver sample](https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad). The sample is under the Microsoft Public License. Keep its copyright notices and `LICENSE` when deriving it.

Obtain the official `Windows-driver-samples` repository, then run:

```powershell
.\driver\Prepare-Sysvad.ps1 -WindowsDriverSamplesRoot 'C:\path\to\Windows-driver-samples'
```

The official sample is currently checked out at `driver/upstream` (commit `2dc3fd3a0cc84a2933f2194e7ec0871584979071`). The script copies SysVAD to `driver/src`, applies [AudioFlowOutput.inx](AudioFlowOutput.inx), registers only the built-in stereo speaker render endpoint, disables the sample capture endpoint array, limits the project to the AudioFlow INF, and applies `loopback.patch` for the AudioFlow transport. Git must be available to apply that patch. It stops if expected upstream text differs. Run `driver/Verify-DriverSource.ps1` to check the prepared source. It does **not** produce an installable driver by itself.

The copied sample still contains unused microphone, HDMI, Bluetooth, USB, keyword detector, and APO implementation files. Remove them only after a successful baseline build so their compile and link dependencies can be checked.

## Build status

Visual Studio 2026 and WDK 28000.2526 are installed. Run `driver/Build-Prototype.ps1 -Platform x64` or `-Platform ARM64` to build the common library and `AudioFlowOutput.sys`, validate the INF and universal APIs, and generate `driver/package/<platform>/AudioFlowOutput/audioflowoutput.cat`. Pass `-TestSigningThumbprint` only if a suitable test certificate with a private key is available in the current user's certificate store. Both current packages were built and test signed on this development machine.

The WDK 28000 integrated INF verifier throws a null-reference exception on this project even though standalone `InfVerif /u` and `/w` both pass. Its integrated API validator selects an ARM64 executable on this x64 host. The build script skips those two integrated tasks and runs standalone x64 `InfVerif`, `ApiValidator`, and `Inf2Cat` instead. `Inf2Cat` reported no errors or warnings for either package.

The package is only test signed. It has **not** passed real WASAPI loopback or audio fidelity testing. Do not deploy it on the main workstation or distribute it as a release package.

For VM testing, follow [VM-Test-Guide.md](VM-Test-Guide.md) and transfer `driver/test-bundle/AudioFlowOutput-x64-Test.zip` into the disposable VM. The bundle includes the x64 driver package, the public test certificate, and DevCon. Keep the certificate's private key on the build machine.

Rebuild the complete x64 bundle with `driver/Build-TestBundle.ps1 -TestSigningThumbprint <thumbprint>`. It builds/test-signs the driver, runs the C++ transport tests, publishes a self-contained `LoopbackVmTest.exe`, checks its analyzer, and packages the current files and guide. No .NET installation is needed inside the VM. The deterministic tests cover PCM fidelity, mixing/clipping, silence/underflow, wrap/overflow, restarts, protected-history clearing, multiple readers, source exhaustion, and independent callback schedules. They do not replace running the driver inside the VM.

## Required work before use

1. On a disposable Windows test machine, install the test-signed package with test signing enabled and verify that exactly one **AudioFlow Output** render endpoint appears. Confirm it does not expose microphone or other sample endpoints.
2. Confirm the single active stereo render endpoint and remove unused sample implementation files and identifiers. The INF and active endpoint arrays are already narrowed, but dead sample code remains.
3. Run the bundled loopback test inside the VM, then verify AudioFlow playback, silence, repeated start/stop, sleep/resume, and device reconnect. Verify that supported format negotiation stays at stereo 48 kHz/16-bit PCM. Broader formats, post-volume behavior, and release-quality offload controls require further work. The application must send captured audio to a *different* physical playback device.
4. Submit the completed package for Microsoft signing, then add the signed driver package to the app installer with clean upgrade and uninstall behavior. Set the new endpoint as default only after saving the user's previous choice and giving them a way to restore it.

The application currently accepts both `AudioFlow Output` and VB-Audio Cable as development sources. Remove the legacy fallback after the new driver passes end-to-end audio tests.
