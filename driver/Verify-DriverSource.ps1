$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'src\TabletAudioSample'
$infPath = Join-Path $source 'ComponentizedAudioSample.inx'
$pairsPath = Join-Path $source 'minipairs.h'
$projectPath = Join-Path $source 'TabletAudioSample.vcxproj'

foreach ($path in @($infPath, $pairsPath, $projectPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing prepared source: $path" }
}

$inf = Get-Content -LiteralPath $infPath -Raw
$pairs = Get-Content -LiteralPath $pairsPath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AudioFlowOutput.inx') -Raw
$adapter = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\adapter.cpp') -Raw
$stream = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\EndpointsCommon\minwavertstream.cpp') -Raw
$formats = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\EndpointsCommon\speakerwavtable.h') -Raw
if ($stream -notmatch 'm_pMiniport->PublishLoopback\(' -or $stream -notmatch 'm_pMiniport->CaptureLoopback\(' -or
    $formats -notmatch 'KSPROPERTY_AUDIOLOOPBACK_TAPPOINT_CAPS' -or
    -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'src\EndpointsCommon\AudioFlowLoopback.h'))) {
    throw 'The real AudioFlow render-to-loopback implementation is missing.'
}
foreach ($name in @('HOST_MIN_SAMPLE_RATE', 'HOST_MAX_SAMPLE_RATE', 'OFFLOAD_MIN_SAMPLE_RATE', 'OFFLOAD_MAX_SAMPLE_RATE')) {
    if ($formats -notmatch "#define SPEAKER_$name\s+48000\b") { throw 'All active pins must use the supported 48 kHz transport format.' }
}

if ($adapter -notmatch 'DWORD g_DisableToneGenerator = 1;' -or
    $inf -notmatch '(?m)^AddReg=AudioFlow.Service.AddReg\s*$' -or
    $inf -notmatch '(?m)^HKR,Parameters,DisableToneGenerator,0x00010001,1\s*$') {
    throw 'The sample tone generator must be disabled in both source defaults and service settings.'
}

if ($inf.TrimEnd() -cne $template.TrimEnd()) { throw 'Prepared INF differs from AudioFlowOutput.inx.' }
if ($inf -notmatch 'Root\\AudioFlow_Output' -or $inf -notmatch 'AudioFlow.OutputName="AudioFlow Output"') {
    throw 'AudioFlow device identity or endpoint name is missing.'
}
if ([regex]::Matches($inf, '(?m)^AddInterface=').Count -ne 5) {
    throw 'The driver INF must register exactly one wave/topology render pair.'
}
if ($inf -match '(?im)^AddInterface=.*(CAPTURE|Headphone|Hdmi|Spdif|Mic|Bth|Usb)') {
    throw 'An unrelated interface is still registered.'
}
if ($pairs -notmatch '(?s)g_RenderEndpoints\[\].*?\{\s*&SpeakerMiniports,\s*\}' -or
    $pairs -notmatch '(?s)g_CaptureEndpoints\[\].*?\{\s*nullptr,\s*\}' -or
    $pairs -notmatch 'static ULONG g_cCaptureEndpoints = 0;') {
    throw 'Miniport endpoint arrays are not render-only.'
}
if ($project -notmatch '<Inf Include="ComponentizedAudioSample.inx" />') {
    throw 'The driver project is not limited to the AudioFlow INF.'
}
if ($project -notmatch '<TargetName>AudioFlowOutput</TargetName>' -or $inf -notmatch 'AudioFlowOutput.sys') {
    throw 'The driver binary is not named AudioFlowOutput.sys.'
}

Write-Host 'AudioFlow Output source checks passed.'
