param(
    [Parameter(Mandatory = $true)]
    [string] $WindowsDriverSamplesRoot,
    [string] $DestinationPath
)

$ErrorActionPreference = 'Stop'
$sampleRoot = Join-Path $WindowsDriverSamplesRoot 'audio\sysvad'
$license = Join-Path $WindowsDriverSamplesRoot 'LICENSE'
$destination = if ($DestinationPath) { $DestinationPath } else { Join-Path $PSScriptRoot 'src' }

if (-not (Test-Path -LiteralPath (Join-Path $sampleRoot 'TabletAudioSample\ComponentizedAudioSample.inx'))) {
    throw 'Expected the official Windows-driver-samples/audio/sysvad source tree.'
}
if (-not (Test-Path -LiteralPath $license)) {
    throw 'Expected the Windows-driver-samples LICENSE file.'
}
if (Test-Path -LiteralPath $destination) {
    throw "Driver source already exists at $destination. Review it before preparing again."
}

function Replace-Once([string] $source, [string] $old, [string] $new) {
    $matches = [regex]::Matches($source, [regex]::Escape($old)).Count
    if ($matches -ne 1) { throw "Expected one occurrence of '$old'; found $matches. Check the upstream sample version." }
    return $source.Replace($old, $new)
}

$miniPairsPath = Join-Path $sampleRoot 'TabletAudioSample\minipairs.h'
$projectPath = Join-Path $sampleRoot 'TabletAudioSample\TabletAudioSample.vcxproj'
$miniPairs = Get-Content -LiteralPath $miniPairsPath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw
$adapter = Get-Content -LiteralPath (Join-Path $sampleRoot 'adapter.cpp') -Raw
$adapter = Replace-Once $adapter 'DWORD g_DisableToneGenerator = 0;  // default is to generate tones.' 'DWORD g_DisableToneGenerator = 1;  // AudioFlow must never emit sample tones by default.'
$miniPairs = Replace-Once $miniPairs "    &SpeakerMiniports,`r`n    &SpeakerHpMiniports,`r`n    &HdmiMiniports,`r`n    &SpdifMiniports," '    &SpeakerMiniports,'
$miniPairs = Replace-Once $miniPairs "    &MicInMiniports,`r`n    &MicArray1Miniports,`r`n    &MicArray2Miniports,`r`n    &MicArray3Miniports," '    nullptr,'
$miniPairs = Replace-Once $miniPairs '#define g_cCaptureEndpoints (SIZEOF_ARRAY(g_CaptureEndpoints))' 'static ULONG g_cCaptureEndpoints = 0;'
$project = Replace-Once $project '<Inf Exclude="@(Inx)" Include="*.inx" />' '<Inf Include="ComponentizedAudioSample.inx" />'
$targetName = '<TargetName>TabletAudioSample</TargetName>'
if ([regex]::Matches($project, [regex]::Escape($targetName)).Count -ne 4) {
    throw 'Expected four TabletAudioSample target names in the upstream project.'
}
$project = $project.Replace($targetName, '<TargetName>AudioFlowOutput</TargetName>')

Copy-Item -LiteralPath $sampleRoot -Destination $destination -Recurse
Copy-Item -LiteralPath $license -Destination (Join-Path $destination 'LICENSE')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AudioFlowOutput.inx') -Destination (Join-Path $destination 'TabletAudioSample\ComponentizedAudioSample.inx')
Set-Content -LiteralPath (Join-Path $destination 'TabletAudioSample\minipairs.h') -Value $miniPairs -Encoding utf8
Set-Content -LiteralPath (Join-Path $destination 'TabletAudioSample\TabletAudioSample.vcxproj') -Value $project -Encoding utf8
Set-Content -LiteralPath (Join-Path $destination 'adapter.cpp') -Value $adapter -Encoding utf8
$loopbackPatch = Join-Path $PSScriptRoot 'loopback.patch'
& git -C $destination apply --ignore-whitespace $loopbackPatch
if ($LASTEXITCODE -ne 0) { throw 'Failed to apply AudioFlow loopback implementation. Check the upstream sample version.' }
Write-Host "Prepared AudioFlow SysVAD prototype at $destination"
Write-Host 'This is source preparation only. Build, test, sign, and install the driver separately.'
