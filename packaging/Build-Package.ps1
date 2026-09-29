param(
    [Parameter(Mandatory)][string]$PublishedDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$MakeAppx,
    [string]$IdentityName = 'AudioFlow.LocalTest',
    [string]$Publisher = 'CN=AudioFlow Local Test',
    [string]$PublisherDisplayName = 'AudioFlow development',
    [string]$Version = '0.2.21.0',
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Version must contain four numeric components.' }
foreach ($component in $Version.Split('.')) {
    $number = 0
    if (![int]::TryParse($component, [ref]$number) -or $number -gt 65535) {
        throw 'Each version component must be between 0 and 65535.'
    }
}
$inputPath = (Resolve-Path -LiteralPath $PublishedDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $inputPath 'AudioFlow.exe'))) { throw 'Publish a self-contained win-x64 AudioFlow build first.' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputPath.StartsWith($inputPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output directory must not be inside the published directory.'
}
if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new output directory; existing builds are never overwritten.' }
if (!$StageOnly -and (!$MakeAppx -or !(Test-Path -LiteralPath $MakeAppx))) { throw 'Supply the Windows SDK MakeAppx.exe path, or use -StageOnly to prepare an unvalidated layout.' }
$layout = Join-Path $outputPath 'layout'
New-Item -ItemType Directory -Path $layout -Force | Out-Null
Get-ChildItem -LiteralPath $inputPath | Where-Object { $_.Extension -ne '.pdb' } | Copy-Item -Destination $layout -Recurse
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
# Temporary code-drawn package tiles, not final product branding.
Add-Type -AssemblyName System.Drawing
foreach ($tile in @(@('StoreLogo.png',50), @('Square44x44Logo.png',44), @('Square150x150Logo.png',150))) {
    $size = [int]$tile[1]
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(121,107,255))
    try {
        $graphics.Clear([Drawing.Color]::FromArgb(21,24,33))
        foreach ($bar in 0..3) {
            $height = $size * @(0.25,0.65,0.45,0.3)[$bar]
            $graphics.FillRectangle($brush,[single]($size * (0.18 + $bar * 0.17)),[single](($size-$height)/2),[single]($size*0.1),[single]$height)
        }
        $bitmap.Save((Join-Path $assets $tile[0]),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$manifest.Package.Identity.Name = $IdentityName
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $Version
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $layout 'AppxManifest.xml'))
if ($StageOnly) {
    Write-Output "Layout prepared at $layout. NOT MakeAppx-validated, signed, or install-tested."
    return
}
$package = Join-Path $outputPath "AudioFlow-$Version-x64.msix"
& $MakeAppx pack /d $layout /p $package /no
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }
Write-Output "Unsigned test package: $package. Signing and installation testing remain required."
