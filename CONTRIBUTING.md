# Contributing to AudioFlow

Small, focused changes are welcome. For substantial features, open an issue describing the user problem and proposed behavior before implementing.

## Development

1. Install a .NET 8 SDK on Windows.
2. Follow the build and regression commands in the README.
3. Keep Windows interop in services and UI logic in view models/views.
4. Add meaningful regression coverage for behavior changes using the existing runner.
5. Run the Release build, regression runner, and version synchronization check before a pull request.

Do not change the version for unrelated contributions. If a version change is needed, update AudioFlow.csproj, run installer/Sync-Version.ps1, and verify with -Check.

## Pull requests

Explain the problem, resulting behavior, and checks performed. Attach screenshots for visible UI changes using synthetic profiles and device names. Clearly distinguish automated checks from listening/hardware tests.

Keep changes focused. Avoid unrelated formatting churn and generated outputs. Do not commit personal settings, credentials, signing material, logs, or installed-package backups.

## Audio and licensing boundaries

The current EQ processes the app-owned sample only. Preserve that distinction in UI, docs, and issue descriptions. A new system-wide backend needs deployment, compatibility, and physical-device evidence.

Do not bundle Dolby/DTS components, proprietary HRIR captures, or third-party binaries without a license and distribution review. Include provenance and required notices for new assets or dependencies.

## Reporting bugs

Use the bug template with Windows/build versions, reproduction steps, expected/actual behavior, and whether the problem concerns mixer controls, preview, or packaging. Remove personal paths, process lists, device IDs, and other private details from logs.

Report vulnerabilities through the security policy. Follow the code of conduct in all project discussions.
