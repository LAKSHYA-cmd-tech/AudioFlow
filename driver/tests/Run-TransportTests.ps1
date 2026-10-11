$ErrorActionPreference = 'Stop'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild (Join-Path $PSScriptRoot 'LoopbackTransportTests.vcxproj') /p:Configuration=Debug /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Loopback transport test build failed.' }
& (Join-Path $PSScriptRoot 'build\LoopbackTransportTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Loopback transport tests failed.' }
