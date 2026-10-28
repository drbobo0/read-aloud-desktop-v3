$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$runtime = Get-Content -LiteralPath (Join-Path $taskRoot 'runtime.json') -Raw | ConvertFrom-Json
$output = Join-Path $taskRoot $runtime.helper
if (-not (Test-Path -LiteralPath $output)) { & (Join-Path $taskRoot 'Build.ps1') }
$identity = Get-Content -LiteralPath (Join-Path $taskRoot 'identity.json') -Raw | ConvertFrom-Json
$manifestPath = Join-Path $taskRoot 'native-host-v3.json'
$manifest = @{ name = 'local.read_aloud_desktop_v3'; description = 'Read Aloud Desktop V3 helper'; path = $output; type = 'stdio'; allowed_origins = @("chrome-extension://$($identity.id)/") }
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $utf8)
$keyPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\local.read_aloud_desktop_v3'
New-Item -Path $keyPath -Force | Out-Null
Set-Item -LiteralPath $keyPath -Value $manifestPath
Write-Host ''
Write-Host 'V3 helper installed. Your V1 and V2 files and registration were not changed.'
Write-Host 'Disable V2 desktop shortcuts before enabling V3. Keep the original Read Aloud extension enabled.'
Write-Host 'Open chrome://extensions, enable Developer mode, click Load unpacked, and choose:'
Write-Host (Join-Path $taskRoot 'extension')
Write-Host 'Open Read Aloud Desktop Bridge V3 and click Enable / retry.'
