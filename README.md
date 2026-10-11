# AudioFlow

AudioFlow is a Windows desktop audio equalizer built with WinUI 3, .NET 10, and NAudio. The current source matches the Desktop development version uploaded on October 11, 2026.

## Build and run

Use Windows with the .NET 10 SDK and the Windows SDK installed.

```powershell
dotnet restore AudioFlow.csproj
dotnet build AudioFlow.csproj -c Release -p:Platform=x64
dotnet run --project AudioFlow.csproj -p:Platform=x64
```

The main application is `AudioFlow.csproj`. `MyWinUIApp/` and `SystemEqualizer/` are companion development projects and are excluded from the main application build.

## Audio setup

Set a supported virtual playback endpoint as the Windows default Multimedia output. The development application recognizes VB-Audio Cable and the prototype AudioFlow Output endpoint. AudioFlow captures that endpoint with WASAPI loopback and sends processed audio to a separate hardware output.

See [AudioFlow-design.md](AudioFlow-design.md) for development design notes and [driver/README.md](driver/README.md) for virtual-driver source, setup, and testing guidance. Driver binaries and test-signing credentials are excluded from this repository update.

## Repository update

This WinUI version replaces the earlier WPF prototype. Existing Git history is preserved. The previous WPF implementation, regression runner, installer, and packaging scripts were removed because they target the earlier application. Screenshots and documents already under `docs/` may describe that earlier version.

GitHub Actions restores and builds the current application in Release mode. Generated build folders, dependencies, and local publish output are excluded from source control.


## Validation of this upload

The Windows x64 Release application build and playback-buffer regression runner passed locally. The existing DSP runner passed its 1,000 spatial-configuration checks, then failed its render-thread memory-allocation assertion. That DSP failure remains in the Desktop source and needs investigation.
