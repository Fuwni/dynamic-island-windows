# Atoll <-> Claude Code / Codex bridge.
# Claude Code runs this for PermissionRequest / Notification / Stop hooks, Codex for PermissionRequest / Stop.
# If the island is not running, it silently does nothing and the agent behaves as usual.
param([string]$Event = "Notification", [string]$Source = "claude")

$ErrorActionPreference = 'SilentlyContinue'
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$body = [Console]::In.ReadToEnd()
$isPermission = $Event -eq 'PermissionRequest'
$path = if ($isPermission) { 'permission' } else { 'notify' }
$timeout = if ($isPermission) { 115 } else { 3 }
$src = if ($Source -eq 'codex') { 'codex' } else { 'claude' }

# the island's secret (only this Windows user can read it) proves the call comes from the agent's hook
$tokenFile = Join-Path $env:LOCALAPPDATA 'DynamicIsland\bridge.token'
$token = if (Test-Path $tokenFile) { (Get-Content $tokenFile -Raw).Trim() } else { '' }
if ($token) {
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($body)
        $resp = Invoke-WebRequest -Uri "http://localhost:47811/$path`?src=$src" -Method Post -Body $bytes -Headers @{ 'X-Island-Token' = $token } `
            -ContentType 'application/json; charset=utf-8' -TimeoutSec $timeout -UseBasicParsing
        if ($isPermission -and $resp.Content) { [Console]::Out.Write($resp.Content); exit 0 }
    } catch { }
}

# Codex wants JSON (not plain text) on stdout from a Stop hook
if ($src -eq 'codex' -and $Event -eq 'Stop') { [Console]::Out.Write('{}') }
exit 0
