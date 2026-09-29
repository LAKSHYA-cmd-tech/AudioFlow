# MSIX development packaging

The manifest is a **local-test definition**, not a Store-certified release. Replace identity, publisher, and assets with verified distribution values before public binary distribution.

Publish self-contained win-x64 output first:

```powershell
dotnet publish AudioFlow.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
powershell -NoProfile -File packaging/Build-Package.ps1 -PublishedDirectory publish/win-x64 -OutputDirectory outputs/msix-layout -StageOnly
```

StageOnly creates a layout without validation/signing. For package validation, omit StageOnly and supply -MakeAppx with the Windows SDK x64 MakeAppx.exe path. Use a new output directory each time. The script generates temporary package tiles.

The manifest targets desktop Windows 10 build 19041 or newer, requests runFullTrust, and registers an initially disabled startup task. AudioFlow is a per-user desktop process with a tray icon.

## Release gates

- Replace local identity/publisher and placeholder assets.
- Sign with appropriate distribution credentials kept outside the repository.
- Verify install/update/uninstall, single-instance activation, startup opt-in, and minimized launch.
- Verify packaged audio controls, automation, USB/Bluetooth, and sleep/resume.
- Validate settings/log location and migration for the intended deployment.
- Review default-output switching's undocumented API dependency.
- Complete the target distribution's current certification and privacy requirements.

Historical local notes record signed development installs and updates through 0.2.21, with settings-file preservation. They are not clean-machine or Store certification evidence. This source publication does not include packages, certificates, machine identities, or private backups.

Test-InstalledUpdate.ps1 performs a real installation and backs up user settings. It requires an explicitly supplied trusted signing-certificate thumbprint and version pair. It is excluded from CI. Review the script and use a test machine/profile; backups must remain private.

The publication verification report documents only checks rerun for this source snapshot.
