param([switch]$IncludeWindowIntegration)
$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $sourceDirectory 'bin\Tests'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
$testNames = @('HotkeyTests', 'SettingsStoreTests', 'SettingsCancelTest', 'StartupTests')
if ($IncludeWindowIntegration) { $testNames += 'WindowIntegration' }
foreach ($testName in $testNames) {
    $executable = Join-Path $testOutput ($testName + '.exe')
    $implementation = if ($testName -eq 'WindowIntegration') { 'WindowSwapper.cs' } else { 'Hotkeys.cs' }
    $arguments = @('/nologo', '/target:exe', '/platform:x64', '/codepage:65001', '/warnaserror+',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
        ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')), ('/out:' + $executable),
        (Join-Path $PSScriptRoot ($testName + '.cs')), (Join-Path $sourceDirectory $implementation))
    if ($testName -ne 'WindowIntegration') { $arguments += Join-Path $sourceDirectory 'Startup.cs' }
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw ('Test build failed: ' + $testName) }
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'App.config') -Destination ($executable + '.config')
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $testProcess = [System.Diagnostics.Process]::Start($start)
    try {
        $output = $testProcess.StandardOutput.ReadToEndAsync()
        $errors = $testProcess.StandardError.ReadToEndAsync()
        if (-not $testProcess.WaitForExit(45000)) { $testProcess.Kill(); throw ('Test timed out: ' + $testName) }
        Write-Output $output.Result
        if ($errors.Result) { Write-Output $errors.Result }
        if ($testProcess.ExitCode -ne 0) { throw ('Test failed: ' + $testName) }
    }
    finally { $testProcess.Dispose() }
}
