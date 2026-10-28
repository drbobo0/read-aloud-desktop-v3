param(
    [string]$Source,
    [string]$Output = (Join-Path $PSScriptRoot 'compatible-reader')
)
$ErrorActionPreference = 'Stop'
if (-not $Source) {
    $readerId = 'hdhinadidafjejdhmfkjgnolgimiaplp'
    $matches = @(Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA "Google\Chrome\User Data\*\Extensions\$readerId\*") -Directory -ErrorAction SilentlyContinue)
    if ($matches.Count -ne 1) { throw 'Run again with -Source pointing to your installed Read Aloud version folder. The original is only read.' }
    $Source = $matches[0].FullName
}
$sourceRoot = (Resolve-Path -LiteralPath $Source).Path
if (Test-Path -LiteralPath $Output) { throw 'The output folder already exists. Choose a new -Output folder; existing copies are never overwritten.' }
$manifest = Get-Content -LiteralPath (Join-Path $sourceRoot 'manifest.json') -Raw | ConvertFrom-Json
$identity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'compatibility\reader-identity.json') -Raw | ConvertFrom-Json
$player = Get-Content -LiteralPath (Join-Path $sourceRoot 'js\player.js') -Raw
$events = Get-Content -LiteralPath (Join-Path $sourceRoot 'js\events.js') -Raw
$background = Get-Content -LiteralPath (Join-Path $sourceRoot 'background.js') -Raw
$playerSection = [regex]::Match($player, '(?s)function playText\(text, opts\).*?(?=function playTab\(\))')
$endMarker = 'if (err) playbackError = err'
if (-not $playerSection.Success -or ([regex]::Matches($playerSection.Value, [regex]::Escape($endMarker))).Count -ne 1) { throw 'This Read Aloud version has an unfamiliar document-end hook. No files were changed.' }
$endHook = @'
if (err) playbackError = err
      if (opts.desktopSessionId) {
        brapi.runtime.sendMessage({
          dest: "desktopBridge", method: "ended", sessionId: opts.desktopSessionId,
          error: err ? { message: err.message || "Reading failed." } : null
        }).catch(() => {});
      }
'@
$patchedSection = $playerSection.Value.Replace($endMarker, $endHook)
$player = $player.Substring(0, $playerSection.Index) + $patchedSection + $player.Substring($playerSection.Index + $playerSection.Length)
$externalMarker = '(request, sender) => {'
if (([regex]::Matches($events, [regex]::Escape($externalMarker))).Count -ne 1 -or
    ([regex]::Matches($background, [regex]::Escape('"js/events.js"'))).Count -ne 1) { throw 'This version has an unfamiliar message loader. No files were changed.' }
$events = $events.Replace($externalMarker, $externalMarker + "`n    if (request && ['desktopHello', 'desktopPlay'].includes(request.method)) return;")
$background = $background.Replace('"js/events.js"', '"js/events.js", "js/desktop-bridge.js"')
$manifest.name = 'Read Aloud - Desktop Compatible V3'
$manifest.short_name = 'Read Aloud Desktop Voice'
$manifest.description = 'Separate Read Aloud copy with confirmed completion for Desktop V3. Based on ken107/read-aloud.'
$manifest.key = $identity.key
$manifest.PSObject.Properties.Remove('update_url')
$manifest | Add-Member -NotePropertyName version_name -NotePropertyValue ($manifest.version + ' Desktop compatibility') -Force
$manifest | Add-Member -NotePropertyName externally_connectable -NotePropertyValue @{ ids = @('jglajehjfkcdfjinbeepbjihpgdfnefg') } -Force
foreach ($command in $manifest.commands.PSObject.Properties) { $command.Value.PSObject.Properties.Remove('suggested_key') }
$null = New-Item -ItemType Directory -Path $Output
foreach ($item in Get-ChildItem -LiteralPath $sourceRoot -Force) {
    if ($item.Name -ne '_metadata') { Copy-Item -LiteralPath $item.FullName -Destination $Output -Recurse }
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $Output 'manifest.json'), ($manifest | ConvertTo-Json -Depth 12), $utf8)
[IO.File]::WriteAllText((Join-Path $Output 'background.js'), $background, $utf8)
[IO.File]::WriteAllText((Join-Path $Output 'js\events.js'), $events, $utf8)
[IO.File]::WriteAllText((Join-Path $Output 'js\player.js'), $player, $utf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'compatibility\desktop-bridge.js') -Destination (Join-Path $Output 'js\desktop-bridge.js')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'compatibility\ReadAloud-LICENSE.txt') -Destination (Join-Path $Output 'ReadAloud-LICENSE.txt')
$notice = "Unofficial desktop-compatible copy of Read Aloud " + $manifest.version + ".`nOriginal project: https://github.com/ken107/read-aloud`nCopyright (c) 2016 Hai Phan. MIT license included.`nChanges: separate identity and name; Desktop V3-only completion adapter; document-end notification; no store updater or default shortcut conflicts.`nOriginal installation and settings are unchanged. Configure voices in this copy separately. Account-linked services may require the official extension.`n"
[IO.File]::WriteAllText((Join-Path $Output 'DESKTOP-COMPATIBILITY.txt'), $notice, $utf8)
Write-Host 'Prepared separate compatible reader:'
Write-Host ([IO.Path]::GetFullPath($Output))
Write-Host 'Load this folder at chrome://extensions, then reload Read Aloud Desktop Bridge V3.'
