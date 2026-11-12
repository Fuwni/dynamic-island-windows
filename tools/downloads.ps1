# How many times Atoll was downloaded, per release and in total (public GitHub API, no sign-in needed).
#
#   powershell -ExecutionPolicy Bypass -File tools\downloads.ps1
#
param([string]$Repo = "Fuwni/dynamic-island-windows")

$headers = @{ "User-Agent" = "Atoll-Stats"; "Accept" = "application/vnd.github+json" }
$releases = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases?per_page=100" -Headers $headers
$info = Invoke-RestMethod "https://api.github.com/repos/$Repo" -Headers $headers

$rows = foreach ($r in $releases) {
    foreach ($a in $r.assets) {
        [pscustomobject]@{ Release = $r.tag_name; File = $a.name; Downloads = $a.download_count }
    }
}
$rows | Format-Table -AutoSize
$apps = $rows | Where-Object { $_.File -match '\.(exe|zip)$' }
"Installers and zips downloaded: {0}" -f ($apps | Measure-Object Downloads -Sum).Sum
"Stars: {0} | Watchers: {1} | Forks: {2}" -f $info.stargazers_count, $info.subscribers_count, $info.forks_count
