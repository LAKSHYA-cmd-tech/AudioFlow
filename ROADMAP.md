# Roadmap

These are development intentions, not commitments or delivery dates.

## Implemented foundations

- [x] Windows audio-session mixer, master volume/mute, and output enumeration
- [x] Prototype default-output switching
- [x] Saved profiles and ordered process/device automation
- [x] Tray operation, startup handling, and single-instance activation
- [x] Versioned settings, backups, and recovery
- [x] Editable EQ and an app-owned real-time sample preview
- [x] Executable regression checks and Windows CI configuration

## Next: reliability and reproducibility

- [ ] Complete physical USB/DAC/Bluetooth and sleep/resume checks
- [ ] Validate actual COM callback and output-switching behavior across Windows versions
- [ ] Handle output invalidation and default-device changes during preview
- [ ] Repeat clean-machine install/update/uninstall and startup tests
- [ ] Improve interop documentation based on verified Windows API behavior
- [ ] Review the .NET support lifecycle and plan the next supported target

## System-wide processing

- [ ] Design optional integration with separately installed Equalizer APO, with backup and bypass
- [ ] Review licensing and packaging before any redistribution
- [ ] Evaluate a native system-wide backend and its deployment requirements
- [ ] Add a production WASAPI app-owned output backend
- [ ] Connect supported processing stages to a real renderer and validate on physical devices

System-wide DSP is not implemented today. An app-owned preview or saved chain is not evidence of system-wide processing.

## Later product work

- [ ] Refine accessibility, themes, navigation, and onboarding after reliability work
- [ ] Create verified product screenshots and a signed release process
- [ ] Replace development package identity/assets and complete Store submission checks
- [ ] Evaluate original or appropriately licensed HRTF/spatial components
- [ ] Consider Free/Pro boundaries after beta feedback; no payment implementation yet

No proprietary Dolby/DTS technology or captured proprietary virtualization responses are planned for redistribution.
