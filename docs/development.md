# Development setup

## Requirements

- Windows for the WinUI application build and playback.
- .NET 10 SDK. `global.json` selects a stable .NET 10 feature band.
- Windows SDK 10.0.26100 or newer; Visual Studio Windows application development tooling is recommended.
- A supported virtual audio endpoint and a separate hardware output for listening checks.
- Visual Studio C++ tools and WDK for driver compilation. They are not needed for the application's standalone DSP/buffer checks.

## One-command verification

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify.ps1
```

The script stops on the first failure. It restores locked dependencies, builds the x64 Release application, runs both standalone audio regression projects, and verifies the prepared driver source. It does not install a driver or change Windows audio settings.

## Individual checks

```powershell
dotnet restore AudioFlow.csproj --locked-mode -p:Platform=x64
dotnet build AudioFlow.csproj -c Release --no-restore -p:Platform=x64
dotnet run --project driver/tests/DspTests/DspTests.csproj -c Release
dotnet run --project driver/tests/PlaybackBufferTests/PlaybackBufferTests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File driver/Verify-DriverSource.ps1
```

The standalone DSP and buffer projects target `net10.0` and are also checked on Linux in CI. The WinUI application runs on Windows.

## What the checks establish

| Check | Coverage | Limit |
| --- | --- | --- |
| WinUI Release build | Source, XAML, dependency restoration | Does not launch or listen to the app |
| DSP regression runner | 1,000 spatial configurations, tone transitions, finite bounded samples, bypass fidelity, callback allocations | Uses synthetic in-memory input |
| Playback buffer runner | Startup reserve, jitter, underflow/overflow, track gaps, adaptive reserve recovery | Does not exercise WASAPI devices |
| Prepared driver source checks | Required transport wiring, supported format declarations, tone-generator defaults | Does not compile, install, or validate the kernel driver |

The allocation assertion measures each warmed `EqualizerProcessor.Read` callback and excludes test assertions from the measured region. A deliberate allocating source checks that the measurement detects allocations.

## Dependencies

Main application PackageReference versions are pinned and the three primary .NET projects have committed `packages.lock.json` files. CI restores them with `--locked-mode`. When intentionally updating dependencies, restore without locked mode, review the lockfile changes, run verification, and include those changes in the pull request.

Generated output, driver packages, signing material, local settings, and diagnostic logs belong outside Git. Do not copy an entire build/publish folder into a commit.
