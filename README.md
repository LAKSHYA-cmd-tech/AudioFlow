# AudioFlow

[![Windows build and audio checks](https://github.com/LAKSHYA-cmd-tech/AudioFlow/actions/workflows/ci.yml/badge.svg)](https://github.com/LAKSHYA-cmd-tech/AudioFlow/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A Windows audio equalizer for shaping stereo playback through a virtual audio endpoint. Built with C#, WinUI 3, .NET 10, and NAudio.

**Status: development prototype.** The application builds from source. AudioFlow's own virtual driver still needs end-to-end VM validation and release signing; a supported installer is not available.

## What it does

- Eleven EQ bands, from 31 Hz to 16 kHz, with selectable filter shapes and Q.
- Preamp, processed/original comparison, stereo-linked peak limiting, and input/output meters.
- Eight-node stereo spatial rendering with depth, ambience, mix, and pan controls.
- Post-EQ FFT spectrum analysis on a background worker.
- Built-in presets, named profiles, and JSON profile import/export.
- WASAPI loopback capture from a supported virtual endpoint, with processed playback on a separate hardware output.

## Start here

| I want to… | Guide |
| --- | --- |
| Build and verify the app | [Development setup](docs/development.md) |
| Configure playback or diagnose silence | [Audio setup and troubleshooting](docs/audio-setup.md) |
| Understand the source and audio path | [Architecture](docs/ARCHITECTURE.md) |
| Contribute a useful change | [Contributing](CONTRIBUTING.md) and [roadmap](ROADMAP.md) |
| Work on the virtual driver | [Driver guide](driver/README.md) |
| Report a bug or security issue | [Issues](https://github.com/LAKSHYA-cmd-tech/AudioFlow/issues/new/choose) or [security policy](SECURITY.md) |

## Build

On Windows, install the .NET 10 SDK and Windows SDK 10.0.26100 or newer. Visual Studio with Windows application development tooling is recommended.

```powershell
dotnet restore AudioFlow.csproj --locked-mode -p:Platform=x64
dotnet build AudioFlow.csproj -c Release --no-restore -p:Platform=x64
```

Run all local build, DSP, buffer, and prepared-driver-source checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify.ps1
```

To launch the application from source:

```powershell
dotnet run --project AudioFlow.csproj -p:Platform=x64
```

## Playback requirements

Set a recognized virtual playback endpoint as Windows' default Multimedia output. The development application recognizes **VB-Audio Cable** and the prototype **AudioFlow Output** endpoint. It captures that endpoint and chooses a separate active output for processed playback. Use a stereo format and start at a low playback volume.

See the [setup guide](docs/audio-setup.md) before using the app. Build and synthetic DSP checks do not establish driver fidelity, hardware compatibility, or listening quality.

## Project layout

`Audio/` contains processing and routing; `MainWindow.xaml` and its code-behind contain the main WinUI interface; `driver/` contains the prototype virtual-driver sources and standalone tests. `MyWinUIApp/` and `SystemEqualizer/` are companion development projects excluded from the main application build.

## Contributing

Useful next contributions include a fresh WinUI screenshot walkthrough, hardware setup reports, profile round-trip coverage, and accessible keyboard interaction checks. See [ROADMAP.md](ROADMAP.md) for scope and acceptance criteria. Each pull request should explain the behavior change and include checks that support it.

## License and history

AudioFlow application code is under the [MIT license](LICENSE). The SysVAD-derived driver retains its [Microsoft Public License](driver/src/LICENSE) and upstream notices. See [third-party notices](THIRD_PARTY_NOTICES.md).

The current WinUI version replaced an earlier WPF prototype while preserving Git history. Existing screenshots and older documents under `docs/` may describe that earlier application; [docs/README.md](docs/README.md) identifies the current guides.
