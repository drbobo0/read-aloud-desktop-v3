$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$output = Join-Path $PSScriptRoot 'PlayerChecks.exe'
& $compiler /nologo /target:exe /main:ReadAloudDesktopV3.PlayerChecks ("/out:" + $output) ("/win32manifest:" + (Join-Path $root 'app.manifest')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $root 'AssemblyInfo.cs') (Join-Path $root 'DesktopHost.cs') (Join-Path $root 'Models.cs') (Join-Path $root 'PillPlayerForm.cs') (Join-Path $root 'ReaderMenu.cs') (Join-Path $PSScriptRoot 'PlayerChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Player check build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'app.config') -Destination ($output + '.config') -Force
& $output
if ($LASTEXITCODE -ne 0) { throw 'Player checks failed.' }
