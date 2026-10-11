param(
    [string] $PublishDirectory = (Join-Path $PSScriptRoot '..\build-vm-smooth'),
    [string] $ZipPath,
    [switch] $SmokeTest
)
$ErrorActionPreference = 'Stop'
$required = @(
    'AudioFlow.exe', 'AudioFlow.dll', 'AudioFlow.deps.json',
    'AudioFlow.runtimeconfig.json', 'AudioFlow.pri',
    'App.xbf', 'MainWindow.xbf', 'MainPage.xbf', 'Assets/AppIcon.ico',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll',
    'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll'
)
foreach ($relative in $required) {
    $file = Join-Path $PublishDirectory $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Missing or empty app dependency: $relative"
    }
}
if ($ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath))
    try {
        foreach ($relative in $required) {
            $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $relative }
            if (-not $entry -or $entry.Length -ne (Get-Item -LiteralPath (Join-Path $PublishDirectory $relative)).Length) {
                throw "Missing or wrong-size ZIP dependency: $relative"
            }
        }
    } finally { $archive.Dispose() }
}
Write-Host 'PASS: app executable, compiled WinUI resources, icon, and self-contained runtime files are present.'
if ($SmokeTest) {
    $app = Start-Process -FilePath (Join-Path (Resolve-Path $PublishDirectory) 'AudioFlow.exe') -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Seconds 8
        $app.Refresh()
        if ($app.HasExited) { throw "App exited during startup with code $($app.ExitCode)." }
        if ($app.MainWindowHandle -eq 0) { throw 'App stayed running but did not create a main window.' }
        Write-Host "PASS: app created its main window (process $($app.Id))."
    } finally {
        if (-not $app.HasExited) { $null = $app.CloseMainWindow() }
    }
}
