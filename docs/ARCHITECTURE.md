# Architecture and implementation limits

AudioFlow is one .NET 8 WPF application with a separate executable regression project.

| Component | Responsibility |
| --- | --- |
| MainViewModel | UI state, profile selection, lifecycle, and refresh orchestration |
| IAudioService / WindowsAudioService | Audio contract and Windows Core Audio COM adapter |
| IAudioChangeNotifier | Optional device/master change signals |
| AutomationService | Ordered rule evaluation from a process/device snapshot |
| ProfileService | Output availability and saved app-level restoration |
| SettingsStore | Versioned JSON, validation, atomic persistence, backup/recovery |
| IProcessingEngine | Boundary for future system-wide processing |
| PassthroughProcessingEngine | Reports system processing unavailable |
| PeqProcessor | Per-channel filter state over floating-point sample buffers |
| LiveEqPreviewStream | App-owned sample processing and waveform output |
| StartupService / TrayService | Startup preferences and tray lifecycle |

## Notifications and polling

The current Windows adapter registers device and endpoint-volume callbacks. It does not register audio-session creation callbacks. Device/master signals are debounced and dispatched to the UI owner. A five-second view-model timer refreshes sessions and evaluates automation.

The local COM declarations have prototype compatibility assumptions. Treat them as an area requiring targeted integration tests, rather than a general prescription for Windows COM implementations. Source-level fake tests cannot validate ABI or physical endpoints.

## Profiles and processing

Profiles store mixer values and DSP settings. Automation uses displayed priority order. Manual profile selection remains until the winning rule changes or rules are explicitly changed; no matching rule leaves the current profile selected.

The default processing engine reports NotAvailable. The separate preview stream processes only its own synthesized stereo sample. It applies the visible EQ bands, crossfades settings changes, and bounds PCM output. It does not render the saved compressor, limiter, or crossfeed stages, route third-party audio, install a driver, or alter Equalizer APO configuration.

## Deployment boundaries

Default-output switching relies on undocumented PolicyConfig COM interfaces. The MSIX manifest uses a local development identity and full-trust desktop execution. Portable startup uses the current-user Run key; packaged startup is managed through Windows.

Future native system processing needs its own supported Windows integration and packaging evaluation. A WPF executable or MSIX package alone does not establish an audio driver, system-wide DSP, or Store eligibility.
