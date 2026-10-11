# Contributing to AudioFlow

For substantial features, open an issue describing the user problem and proposed behavior before implementing.

## Development

1. Install the .NET 10 SDK and Windows SDK on Windows.
2. Restore and build `AudioFlow.csproj` in Release mode with `-p:Platform=x64`.
3. Run the relevant standalone checks under `driver/tests/` for audio changes. Document hardware and listening checks separately.
4. Use `driver/README.md` for driver development and VM testing.

The main application uses WinUI 3. Its processing sources are under `Audio/`; companion projects are excluded from the main build.

## Pull requests

Explain the problem, resulting behavior, and checks performed. Attach screenshots for visible UI changes using synthetic profiles and device names. Keep changes focused and exclude generated outputs, personal settings, credentials, signing material, and logs.

Include provenance and required notices for new assets or dependencies.

## Reporting bugs

Include Windows/build versions, reproduction steps, expected and actual behavior, and your virtual-device and playback setup. Remove private details from logs. Report vulnerabilities through the security policy and follow the code of conduct.

