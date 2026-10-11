param([Parameter(Mandatory = $true)][string] $TestSigningThumbprint)
$ErrorActionPreference = 'Stop'
$certificate = Join-Path $PSScriptRoot 'package\AudioFlowOutput-Test.cer'
$publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)
try {
    if ($publicCertificate.Thumbprint -ne $TestSigningThumbprint) {
        throw 'The public VM certificate must match the catalog signing certificate.'
    }
} finally { $publicCertificate.Dispose() }
& (Join-Path $PSScriptRoot 'Build-Prototype.ps1') -Platform x64 -TestSigningThumbprint $TestSigningThumbprint
& (Join-Path $PSScriptRoot 'Verify-TestSignatures.ps1') -Platforms x64
$bundle = Join-Path $PSScriptRoot 'test-bundle\x64'
$testOutput = Join-Path $bundle 'LoopbackVmTest'
& dotnet publish (Join-Path $PSScriptRoot 'tests\LoopbackVmTest\LoopbackVmTest.csproj') -c Release -r win-x64 --self-contained true -o $testOutput
if ($LASTEXITCODE -ne 0) { throw 'VM loopback test publish failed.' }
& (Join-Path $testOutput 'LoopbackVmTest.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'VM loopback analyzer self-test failed.' }
$package = Join-Path $PSScriptRoot 'package\x64\AudioFlowOutput'
$destination = Join-Path $bundle 'AudioFlowOutput'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($name in @('AudioFlowOutput.sys', 'ComponentizedAudioSample.inf', 'audioflowoutput.cat')) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination $destination -Force
}
Copy-Item -LiteralPath $certificate -Destination $bundle -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VM-Test-Guide.md') -Destination $bundle -Force
if (-not (Test-Path -LiteralPath (Join-Path $bundle 'devcon.exe'))) { throw 'Missing Microsoft x64 DevCon in the existing bundle.' }
$zip = Join-Path $PSScriptRoot 'test-bundle\AudioFlowOutput-x64-Test.zip'
Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $zip -Force
Write-Host "Updated VM test bundle: $zip"
