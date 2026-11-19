param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 compiler was not found.' }
$outputDirectory = Join-Path $PSScriptRoot ('bin\' + $Configuration)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputFile = Join-Path $outputDirectory 'DualScreenSwap.exe'
$compilerArguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/utf8output', '/codepage:65001',
    '/warn:4', '/warnaserror+', ('/out:' + $outputFile),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\DualScreenSwap.ico')),
    '/reference:System.dll', '/reference:System.Core.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
if ($Configuration -eq 'Release') { $compilerArguments += '/optimize+' }
else { $compilerArguments += '/debug+' }
$compilerArguments += @('Program.cs', 'WindowSwapper.cs', 'Hotkeys.cs', 'Startup.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw ('Build failed with exit code ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'App.config') -Destination ($outputFile + '.config')
Write-Output ('Built: ' + $outputFile)
