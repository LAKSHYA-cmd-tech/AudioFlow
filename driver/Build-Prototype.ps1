param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',
    [string] $TestSigningThumbprint
)

$ErrorActionPreference = 'Stop'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
$kit = 'C:\Program Files (x86)\Windows Kits\10'
$version = '10.0.28000.0'
$infverif = Join-Path $kit "Tools\$version\x64\infverif.exe"
$apiValidator = Join-Path $kit "bin\$version\x64\apivalidator.exe"
$apiTools = Join-Path $kit "bin\$version\x64"
$inf2cat = Join-Path $kit "bin\$version\x86\Inf2Cat.exe"
$signtool = Join-Path $kit "bin\$version\x64\signtool.exe"
$ddi = Join-Path $kit "build\$version\universalDDIs\$Platform"
$common = Join-Path $PSScriptRoot 'src\EndpointsCommon\EndpointsCommon.vcxproj'
$driver = Join-Path $PSScriptRoot 'src\TabletAudioSample\TabletAudioSample.vcxproj'
$output = Join-Path $PSScriptRoot "src\TabletAudioSample\$Platform\Debug"
$package = Join-Path $PSScriptRoot "package\$Platform\AudioFlowOutput"
$os = if ($Platform -eq 'ARM64') { '10_NI_ARM64,10_GE_ARM64' } else { '10_NI_X64,10_GE_X64' }

foreach ($path in @($msbuild, $infverif, $apiValidator, $inf2cat, $driver, $common)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing build tool or source: $path" }
}

function Run([string] $name, [string] $executable, [string[]] $arguments) {
    Write-Host "Running $name..."
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}

& (Join-Path $PSScriptRoot 'Verify-DriverSource.ps1')
& (Join-Path $PSScriptRoot 'tests\Run-TransportTests.ps1')
Run 'EndpointsCommon build' $msbuild @($common, '/p:Configuration=Debug', "/p:Platform=$Platform", '/m', '/v:minimal')
# The WDK 28000 integrated verifier throws an exception on this INF. The standalone
# verifier and API validator are run explicitly below instead.
Run 'AudioFlowOutput build' $msbuild @($driver, '/p:Configuration=Debug', "/p:Platform=$Platform", '/p:SkipPackageVerification=true', '/p:ApiValidator_Enable=false', '/m', '/v:minimal')

New-Item -ItemType Directory -Force -Path $package | Out-Null
foreach ($name in @('AudioFlowOutput.sys', 'ComponentizedAudioSample.inf', 'audioflowoutput.cat')) {
    $path = Join-Path $package $name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
Copy-Item -LiteralPath (Join-Path $output 'AudioFlowOutput.sys') -Destination $package
Copy-Item -LiteralPath (Join-Path $output 'ComponentizedAudioSample.inf') -Destination $package

Run 'INF validation' $infverif @('/u', (Join-Path $package 'ComponentizedAudioSample.inf'))
Run 'Universal API validation' $apiValidator @(
    "-DriverPackagePath:$(Join-Path $package 'AudioFlowOutput.sys')",
    "-SupportedApiXmlFiles:$(Join-Path $ddi 'UniversalDDIs.xml')",
    "-ModuleWhiteListXmlFiles:$(Join-Path $ddi 'ModuleWhiteList.xml')",
    "-ApiExtractorExePath:$apiTools"
)
Run 'Catalog signability' $inf2cat @("/driver:$package", "/os:$os")

if ($TestSigningThumbprint) {
    Run 'Test catalog signing' $signtool @('sign', '/fd', 'sha256', '/sha1', $TestSigningThumbprint, (Join-Path $package 'audioflowoutput.cat'))
}

Write-Host "AudioFlow Output prototype package: $package"
