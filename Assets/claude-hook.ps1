# Dynamic Island <-> Claude Code bridge.
# Claude Code runs this for PermissionRequest / Notification / Stop hooks.
# If the island is not running, it silently does nothing and Claude Code behaves as usual.
param([string]$Event = "Notification")

$ErrorActionPreference = 'SilentlyContinue'
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$body = [Console]::In.ReadToEnd()
$isPermission = $Event -eq 'PermissionRequest'
$path = if ($isPermission) { 'permission' } else { 'notify' }
$timeout = if ($isPermission) { 115 } else { 3 }

try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $resp = Invoke-WebRequest -Uri "http://localhost:47811/$path" -Method Post -Body $bytes `
        -ContentType 'application/json; charset=utf-8' -TimeoutSec $timeout -UseBasicParsing
    if ($isPermission -and $resp.Content) { [Console]::Out.Write($resp.Content) }
} catch { }

exit 0
