# Atoll <-> Claude Code bridge.
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

# the island's secret (only this Windows user can read it) proves the call comes from Claude Code
$tokenFile = Join-Path $env:LOCALAPPDATA 'DynamicIsland\bridge.token'
$token = if (Test-Path $tokenFile) { (Get-Content $tokenFile -Raw).Trim() } else { '' }
if (-not $token) { exit 0 }

try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $resp = Invoke-WebRequest -Uri "http://localhost:47811/$path" -Method Post -Body $bytes -Headers @{ 'X-Island-Token' = $token } `
        -ContentType 'application/json; charset=utf-8' -TimeoutSec $timeout -UseBasicParsing
    if ($isPermission -and $resp.Content) { [Console]::Out.Write($resp.Content) }
} catch { }

exit 0
