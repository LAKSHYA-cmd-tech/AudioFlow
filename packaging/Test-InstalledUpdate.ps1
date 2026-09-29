param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$ExpectedSignerThumbprint,
    [version]$FromVersion = '0.2.8.0',
    [version]$ToVersion = '0.2.9.0'
)
$ErrorActionPreference = 'Stop'
$packagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$installed = Get-AppxPackage -Name AudioFlow.LocalTest
if ($ToVersion -le $FromVersion) { throw 'This test only supports a newer version.' }
if (!$installed -or [version]$installed.Version -ne $FromVersion) { throw "Expected installed test version $FromVersion." }
if (Get-Process AudioFlow -ErrorAction SilentlyContinue) { throw 'Exit AudioFlow before updating; no processes will be force-closed.' }
$signature = Get-AuthenticodeSignature -FilePath $packagePath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $ExpectedSignerThumbprint) {
    throw 'Package must have a valid signature from the explicitly supplied test certificate.'
}
$backupPath = [IO.Path]::GetFullPath($BackupDirectory)
if (Test-Path -LiteralPath $backupPath) { throw 'Choose a new backup directory; existing backups are never overwritten.' }
$roots = @(
    (Join-Path $env:LOCALAPPDATA 'AudioFlow'),
    (Join-Path $env:LOCALAPPDATA ('Packages\' + $installed.PackageFamilyName))
)
$files = @($roots | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
    Get-ChildItem -LiteralPath $_ -Filter 'settings.json*' -File -Recurse
})
if ($files.Count -eq 0) { throw 'No saved settings found. Save a test profile before testing data preservation.' }
New-Item -ItemType Directory -Path $backupPath | Out-Null
$index = 0
$snapshots = @($files | ForEach-Object {
    $backupFile = Join-Path $backupPath ("{0}-{1}" -f $index++, $_.Name)
    Copy-Item -LiteralPath $_.FullName -Destination $backupFile
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    if ($hash -ne (Get-FileHash -LiteralPath $backupFile -Algorithm SHA256).Hash) { throw 'Backup verification failed.' }
    [pscustomobject]@{ Path = $_.FullName; Backup = $backupFile; SHA256 = $hash }
})
$snapshots | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupPath 'before.json') -Encoding UTF8
# Normal upgrade only: no uninstall, downgrade, forced shutdown, or settings rewrites.
Add-AppxPackage -Path $packagePath
$updated = Get-AppxPackage -Name AudioFlow.LocalTest
if (!$updated -or [version]$updated.Version -ne $ToVersion -or $updated.PackageFamilyName -ne $installed.PackageFamilyName -or $updated.Status -ne 'Ok') {
    throw 'Updated package identity, version, or health did not match expectations.'
}
foreach ($snapshot in $snapshots) {
    if (!(Test-Path -LiteralPath $snapshot.Path) -or (Get-FileHash -LiteralPath $snapshot.Path -Algorithm SHA256).Hash -ne $snapshot.SHA256) {
        throw "Settings changed during installation. Preserved backup: $($snapshot.Backup)"
    }
}
$result = [pscustomobject]@{
    PreviousVersion = [string]$installed.Version
    InstalledVersion = [string]$updated.Version
    PackageFamilyName = $updated.PackageFamilyName
    PackageStatus = [string]$updated.Status
    PreservedSettingsFiles = $snapshots.Count
    CheckedAt = (Get-Date).ToString('o')
    LaunchVerified = $false
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupPath 'result.json') -Encoding UTF8
$result | Format-List
