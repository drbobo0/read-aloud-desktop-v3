$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$runtime = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Raw | ConvertFrom-Json
foreach ($entry in @(@($runtime.helper,'ReadAloudDesktopV3.Program'),@($runtime.preview,'ReadAloudDesktopV3.PreviewProgram'))) {
    & $compiler /nologo /target:winexe ("/main:" + $entry[1]) ("/out:" + (Join-Path $PSScriptRoot $entry[0])) ("/win32manifest:" + (Join-Path $PSScriptRoot 'app.manifest')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'AssemblyInfo.cs') (Join-Path $PSScriptRoot 'DesktopHost.cs') (Join-Path $PSScriptRoot 'Models.cs') (Join-Path $PSScriptRoot 'PillPlayerForm.cs') (Join-Path $PSScriptRoot 'ReaderMenu.cs')
    if ($LASTEXITCODE -ne 0) { throw ('Could not build ' + $entry[0]) }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $PSScriptRoot ($entry[0] + '.config')) -Force
}
