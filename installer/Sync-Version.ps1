<#
.SYNOPSIS
    Keeps every place that records the AudioFlow version in step with AudioFlow.csproj.

.DESCRIPTION
    AudioFlow.csproj is the single source of truth. This script rewrites the copies that
    other tools read: the Inno Setup script, the MSIX manifest, and the MSIX build script
    default. Run it after changing <Version> in AudioFlow.csproj, before packaging.

.EXAMPLE
    .\installer\Sync-Version.ps1
    .\installer\Sync-Version.ps1 -Check
#>
[CmdletBinding()]
param(
    # Report drift and exit non-zero without writing anything.
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $projectRoot 'AudioFlow.csproj'
if (!(Test-Path -LiteralPath $csproj)) { throw "Cannot find $csproj" }

[xml]$project = Get-Content -LiteralPath $csproj -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'AudioFlow.csproj has no <Version> element.' }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$version' must be major.minor.patch." }

# MSIX requires exactly four numeric components.
$msixVersion = "$version.0"
$changes = [System.Collections.Generic.List[string]]::new()

function Set-FileText {
    param([string]$Path, [string]$Pattern, [string]$Replacement, [string]$What)

    if (!(Test-Path -LiteralPath $Path)) { return }
    $text = Get-Content -LiteralPath $Path -Raw
    $updated = [regex]::Replace($text, $Pattern, $Replacement)
    if ($updated -eq $text) { return }
    $changes.Add("$What -> $version ($Path)")
    if (!$Check) {
        # Preserve the original encoding and line endings; Inno/MSIX files are ASCII or UTF-8.
        $encoding = if ((Get-Content -LiteralPath $Path -Encoding Byte -TotalCount 3) -contains 0xEF) { 'utf8' } else { 'ascii' }
        Set-Content -LiteralPath $Path -Value $updated -NoNewline -Encoding $encoding
    }
}

$iss = Join-Path $projectRoot 'installer\AudioFlow.iss'
Set-FileText -Path $iss -Pattern '(?m)^#define MyAppVersion\s+"[^"]*"' -Replacement "#define MyAppVersion `"$version`"" -What 'installer/AudioFlow.iss MyAppVersion'

$manifest = Join-Path $projectRoot 'packaging\AppxManifest.xml'
# Scoped to the <Identity> element, and \b stops it from also matching MinVersion="10.0.19041.0".
Set-FileText -Path $manifest -Pattern '(<Identity\b[^>]*?\bVersion=")\d+\.\d+\.\d+\.\d+(")' -Replacement "`${1}$msixVersion`${2}" -What 'packaging/AppxManifest.xml Identity/Version'

$buildScript = Join-Path $projectRoot 'packaging\Build-Package.ps1'
Set-FileText -Path $buildScript -Pattern "(\[string\]\`$Version\s*=\s*')[^']*(')" -Replacement "`${1}$msixVersion`${2}" -What 'packaging/Build-Package.ps1 default -Version'

if ($Check -and $changes.Count -gt 0) {
    Write-Warning "Version $version is not applied everywhere:"
    $changes | ForEach-Object { Write-Warning "  $_" }
    exit 1
}
if ($changes.Count -eq 0) {
    Write-Output "Version $version is already consistent across the project."
} else {
    Write-Output "Updated $($changes.Count) location(s) to ${version}:"
    $changes | ForEach-Object { Write-Output "  $_" }
}
# Set an explicit code so -Check is reliable in CI, where a stale $LASTEXITCODE would mislead.
if ($Check) { exit 0 }
