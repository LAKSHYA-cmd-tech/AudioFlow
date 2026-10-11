# AudioFlow roadmap

AudioFlow is an early WinUI audio-processing prototype. These are contribution opportunities and completion criteria, not shipped-feature claims or promised dates.

## Make source builds dependable

- [x] Replace stale WPF build/documentation references with the current WinUI/.NET 10 setup.
- [x] Pin primary application dependencies and commit restore lockfiles.
- [x] Provide one-command local verification and CI for application build, DSP, buffers, and prepared-driver source.
- [ ] Add a fresh WinUI screenshot walkthrough. Done when screenshots match current `main`, explain setup, and contain no private device/profile information.

## Validate everyday audio use

- [ ] Collect reproducible hardware reports for at least two virtual-endpoint/hardware-output combinations. Record formats, Windows build, bypass behavior, and results without implying untested compatibility.
- [ ] Validate device loss/default-device changes. Done when processing stops/restarts predictably with a documented route and no feedback loop.
- [ ] Verify profile round trips. Done when automated checks cover EQ, preamp, spatial settings, bypass, and malformed/legacy JSON behavior.
- [ ] Audit keyboard interaction and accessibility. Done when controls expose useful names, focus order is documented, and numeric edits work without a pointer.

## Validate the virtual driver

- [ ] Complete the VM checklist against the current driver source. Preserve raw-capture analysis and explicit pass/fail results with synthetic audio.
- [ ] Verify real render-to-loopback transport, multiple readers, format negotiation, protected-history clearing, and restart behavior.
- [ ] Prepare release signing and an installer only after source, package, VM, and hardware validation.

## Contributor-sized starting points

| Task | Starting files | Acceptance |
| --- | --- | --- |
| Current UI walkthrough | `docs/`, `MainWindow.xaml` | Accurate screenshots and a complete first playback path |
| Profile compatibility tests | `AudioFlowProfile.cs`, `Audio/UserSettings.cs` | Round trip plus malformed/legacy data cases |
| Hardware report | `docs/audio-setup.md` | Reproducible setup and sanitized listening/device evidence |
| Keyboard audit | `MainWindow.xaml`, `MainWindow.xaml.cs` | Concrete focus/navigation findings and a verified correction |

Open a scoped issue before larger changes. Include validation results in pull requests and keep generated output out of commits.
