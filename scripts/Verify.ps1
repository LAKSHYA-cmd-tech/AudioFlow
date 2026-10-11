param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-DotnetCheck {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-DotnetCheck -Arguments @('restore', 'AudioFlow.csproj', '--locked-mode', '-p:Platform=x64')
    Invoke-DotnetCheck -Arguments @('build', 'AudioFlow.csproj', '-c', $Configuration, '--no-restore', '-p:Platform=x64')
    foreach ($project in @('driver/tests/DspTests/DspTests.csproj', 'driver/tests/PlaybackBufferTests/PlaybackBufferTests.csproj')) {
        Invoke-DotnetCheck -Arguments @('restore', $project, '--locked-mode')
        Invoke-DotnetCheck -Arguments @('run', '--project', $project, '-c', $Configuration, '--no-restore')
    }
    & (Join-Path $repositoryRoot 'driver/Verify-DriverSource.ps1')
    Write-Host 'PASS: application build, DSP, playback buffer, and prepared-driver source checks.'
}
finally {
    Pop-Location
}
