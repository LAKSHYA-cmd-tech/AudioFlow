# Regression checks

Run from the AudioFlow source directory using a .NET 8 SDK on Windows:

```powershell
dotnet restore tests/Regression/Regression.csproj --configfile tests/NuGet.Config
dotnet build tests/Regression/Regression.csproj -c Release --no-restore
dotnet tests/Regression/bin/Release/net8.0-windows/Regression.dll
```

The runner uses fake audio and startup services and isolated settings; it does not
change the system output, audio levels, or Windows startup registry entry.
Set AUDIOFLOW_TEST_ROOT to choose its test-data location. Each run gets a unique directory.
No external test-framework packages are needed. Exit code is nonzero on failure.
Real COM callbacks, physical devices, and installed-MSIX behavior remain separate checks.

The suite now includes dispatcher-driven notifications before startup, during suspension,
after resume, after device loss/reconnection, during app replacement, and after disposal.
See Hardware-checklist.md for the remaining manual tests.

The live EQ pipeline is covered by detached-snapshot, block-processing, flat/dry
bypass, parameter-change and input-validation checks. Set AUDIOFLOW_SMOKE_OUTPUT=1
only when a brief quiet playback test on the current Windows output is acceptable.
That opt-in check starts the native stream, changes EQ during playback, stops it,
repeats start/stop, and verifies a disposed stream cannot restart. It does not
change the system default output or third-party audio configuration.
