$ErrorActionPreference = 'Stop'
$keyPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\local.read_aloud_desktop_v3'
if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath }
Write-Host 'V3 helper registration removed. Disable/remove only Read Aloud Desktop Bridge V3 from Chrome.'
Write-Host 'V1, V2 and your original Read Aloud extension were not changed.'
