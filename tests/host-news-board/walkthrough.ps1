param($Context)
# Real-client walkthrough for the recent-updates board (one client).
# Package: WarmupScpSelector + HsmAdapter 1.3.1 candidates, WarmupScpSelector.config.yml beside this file
# (feed_url -> http://127.0.0.1:18931/updates.json) and feed-cached.json installed as news-board-cache.json.
# 1. The server boots with the feed site down: the board shows the cached copy and logs one warning.
# 2. The feed site comes up (feed-live/) and the round restarts: the rebuilt board fetches the live feed,
#    swaps its text and rewrites the cache. The client walks up to the board for the visual check.
# 3. warmupexport leaves the board's toys out of the schematic while still exporting the station's text.
$ErrorActionPreference='Stop'
$feedPort=18931
$log="$($Context.Evidence)\scenario-server.txt"
$pluginConfig=Join-Path $Context.Root "Server\AppData\SCP Secret Laboratory\LabAPI\configs\$($Context.Port)\WarmupScpSelector"
$cachePath=Join-Path $pluginConfig 'news-board-cache.json'
$liveFeed=Join-Path $PSScriptRoot 'feed-live\updates.json'
function Server([string]$Command) {
    $reply=Invoke-LabServer $Command
    "> $Command`n$($reply -join "`n")" | Add-Content $log -Encoding utf8
    return ($reply -join "`n")
}
function Wait-For([scriptblock]$Condition,[string]$Message,[int]$Seconds=30) {
    $deadline=(Get-Date).AddSeconds($Seconds)
    do { $value=& $Condition; if($value) { return $value }; Start-Sleep -Milliseconds 500 } while((Get-Date) -lt $deadline)
    throw $Message
}
function Server-Log { (Get-ChildItem "$($Context.Evidence)\server-logs" -Filter *.txt | Sort-Object Name | ForEach-Object { Get-Content $_.FullName -Raw -Encoding utf8 }) -join "`n" }
function Feed-Titles([string]$Path) { @((Get-Content $Path -Raw -Encoding utf8 | ConvertFrom-Json).entries | ForEach-Object { $_.title }) }
function Ready-Actor { @(Observe | Where-Object { $_.ready -and $_.role -eq 'Tutorial' })[0] }
# Board centre in station-local metres (NewsBoardConfig.Position), and the spawn's local offset.
function Board-Target($Actor) {
    @{x=$Actor.position.x+7.75; y=$Actor.camera.y+0.8; z=$Actor.position.z+31.5-25.65}
}

if (!(Test-Path $pluginConfig)) { throw "Plugin config folder not found: $pluginConfig" }
$cachedTitles=Feed-Titles (Join-Path $PSScriptRoot 'feed-cached.json')
$liveTitles=Feed-Titles $liveFeed

# 1. Feed site down at boot: the cached copy is shown and the failure is logged, not thrown.
$bootLog=Server-Log
if ($bootLog -notmatch "News board feed 'http://127\.0\.0\.1:$feedPort/updates\.json': .*showing the cached copy") {
    throw 'Boot with the feed site down did not log the cached-copy fallback'
}
$spawn=Wait-For { Ready-Actor } 'No ready Tutorial actor in the station'
$null=Set-LabAim -Target (Board-Target $spawn) -ActorId $spawn.id
$cachedShot=Invoke-LabScreenshot -Name 'board-cached-from-spawn'

# 2. Feed site up, round restart: the rebuilt board reads the live feed.
$site=Start-Process python -ArgumentList @('-m','http.server',$feedPort,'--bind','127.0.0.1','-d',"`"$(Split-Path $liveFeed)`"") `
    -PassThru -WindowStyle Hidden -RedirectStandardError "$($Context.Evidence)\feed-site.txt" -RedirectStandardOutput "$($Context.Evidence)\feed-site-out.txt"
try {
    # Probe the directory, not the feed, so every logged updates.json request is the plugin's.
    Wait-For { try { (Invoke-WebRequest "http://127.0.0.1:$feedPort/" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $false } } 'Feed site did not start' 15 | Out-Null
    $null=Server '/roundrestart'
    Start-Sleep -Seconds 5
    $actor=$null
    try {
        $actor=Wait-For { $a=Ready-Actor; if($a -and $a.id -ne $spawn.id) { $a } } 'Client did not rejoin after the round restart' 60
    } catch {
        $null=Invoke-LabSetup 'disconnect'
        Start-Sleep -Seconds 2
        $null=Invoke-LabSetup "connect 127.0.0.1:$($Context.Port)"
        $actor=Wait-For { Ready-Actor } 'Client did not reconnect after the round restart' 90
    }
    $cache=Wait-For { if ((Test-Path $cachePath) -and (Get-Content $cachePath -Raw -Encoding utf8) -eq (Get-Content $liveFeed -Raw -Encoding utf8)) { $true } } 'The live feed was not written to the cache' 30
    $requests=Get-Content "$($Context.Evidence)\feed-site.txt" -Raw -ErrorAction SilentlyContinue
    if ($requests -notmatch 'GET /updates\.json HTTP/1\.1" 200') { throw 'The feed site served no updates.json request' }
} finally {
    if ($site -and !$site.HasExited) { Stop-Process -Id $site.Id -Force }
}

# Walk east along the gallery until the board is ahead, then look at it.
$null=Set-LabLook -Yaw 90 -Pitch 0 -ActorId $actor.id
$walk=Invoke-LabInput @{id='walk-to-board';frames=120;keys=@(119);capture=$true}
$null=Set-LabAim -Target (Board-Target $actor) -ActorId $actor.id
$liveShot=Invoke-LabScreenshot -Name 'board-live-close'
$pan=Invoke-LabInput @{id='board-pan';frames=150;dx=0.04;capture=$true}

# 3. Export: the station's own signage is exported, the board is not.
$export=Server '/warmupexport newsboardcheck'
if ($export -notmatch 'Exported .* to (.+\.json)\.') { throw "Export failed:`n$export" }
$schematic=Get-Content $Matches[1] -Raw -Encoding utf8 | ConvertFrom-Json
$texts=@($schematic.Blocks | Where-Object { $_.Properties.Text } | ForEach-Object { [string]$_.Properties.Text })
if ($texts.Count -eq 0) { throw 'The export contains no text blocks at all' }
foreach ($needle in @('最近更新') + $liveTitles + $cachedTitles) {
    if ($texts | Where-Object { $_.Contains($needle) }) { throw "The export baked in board text '$needle'" }
}

$finalLog=Server-Log
$warnings=@([regex]::Matches($finalLog,'News board feed') | ForEach-Object { $_ })
@{
    cachedTitles=$cachedTitles; liveTitles=$liveTitles; exportTexts=$texts.Count; feedWarnings=$warnings.Count
    screenshots=@($cachedShot,$liveShot); walk=$walk; pan=$pan
} | ConvertTo-Json -Depth 6 | Set-Content "$($Context.Evidence)\scenario.json" -Encoding utf8
if ($warnings.Count -ne 1) { throw "Expected exactly one feed warning (the boot fallback), found $($warnings.Count)" }
