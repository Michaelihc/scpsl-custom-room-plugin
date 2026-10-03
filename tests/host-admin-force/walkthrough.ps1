param($Context)
# Native RA command walkthrough with two real clients. SCP-3114 is forced with its normal draft disabled.
$ErrorActionPreference='Stop'
$commandLog="$($Context.Evidence)\force-commands.txt"
function Server([string]$Command) {
    $reply=(Invoke-LabServer $Command) -join "`n"
    "> $Command`n$reply" | Add-Content $commandLog -Encoding utf8
    return $reply
}
function Require-Reply([string]$Command,[string]$Expected) {
    $reply=Server $Command
    if ($reply -notmatch $Expected) { throw "Unexpected reply to $Command : $reply" }
}
$forceActors=@($Context.Actors)
if ($forceActors.Count -ne 2) { throw 'Run with --clients 2' }
$firstId=$forceActors[0].id; $secondId=$forceActors[1].id
Require-Reply '/roundtime' 'not started'
# Exercise the actual LabAPI config switch and reload on this runner-owned server only.
$forceConfig=Join-Path $Context.Root "Server/AppData/SCP Secret Laboratory/LabAPI/configs/$($Context.Port)/WarmupScpSelector/config.yml"
$enabledConfig=Get-Content $forceConfig -Raw -Encoding utf8
if ($enabledConfig -notmatch '(?m)^admin_force_selection_enabled: true\s*$') { throw 'Enabled config fixture is missing' }
$disabledConfig=$enabledConfig -replace '(?m)^admin_force_selection_enabled: true\s*$', 'admin_force_selection_enabled: false'
$disabledConfig | Set-Content $forceConfig -Encoding utf8
Require-Reply '/labapi reload configs' 'Successfully reloaded'
Require-Reply "/warmupforce $firstId 173" '已关闭'
$enabledConfig | Set-Content $forceConfig -Encoding utf8
Require-Reply '/labapi reload configs' 'Successfully reloaded'
Require-Reply "/warmupforce $firstId 049-2" '请选择'
Require-Reply '/warmupforce 99999 173' '目标玩家'
Require-Reply "/warmupforce $firstId 096" '已预定'
Require-Reply "/warmupforce clear $firstId" '已取消'
Require-Reply '/warmupforce list' '没有'
Require-Reply "/warmupforce $firstId 049" '已预定'
Require-Reply '/warmupforce clear all' '已取消全部'
Require-Reply '/warmupforce list' '没有'
Require-Reply "/warmupforce $firstId 096" '已预定'
Require-Reply "/warmupforce $firstId SCP-3114" '已预定'
Require-Reply "/warmupforce $secondId 173" '已预定'
$reservations=Server '/warmupforce list'
if ($reservations -notmatch "$firstId .*Scp3114" -or $reservations -notmatch "$secondId .*Scp173" -or
    $reservations -match 'Scp096') { throw "Reservations did not replace correctly: $reservations" }
Require-Reply '/forcestart' 'Forced round start'
$deadline=(Get-Date).AddSeconds(25)
do {
    $after=@(Observe -ClientsOnly)
    if (@($after | Where-Object id -eq $firstId)[0].role -eq 'Scp3114' -and
        @($after | Where-Object id -eq $secondId)[0].role -eq 'Scp173') { break }
    Start-Sleep -Milliseconds 400
} while ((Get-Date) -lt $deadline)
if (@($after | Where-Object id -eq $firstId)[0].role -ne 'Scp3114' -or
    @($after | Where-Object id -eq $secondId)[0].role -ne 'Scp173') {
    throw "Forced roles were not assigned: $($after | ConvertTo-Json -Depth 4 -Compress)"
}
Require-Reply '/warmupforce list' '没有'
Require-Reply "/warmupforce $firstId 939" '只能在等待'
$claims=Server '/roundroles'
if ($claims -notmatch "claim $firstId warmup.scp" -or $claims -notmatch "claim $secondId warmup.scp") {
    throw "Forced recipients were not claimed as SCPs: $claims"
}
$forceShots=@(
    Invoke-LabScreenshot -Name forced-scp3114 -ClientId $Context.ClientIds[0]
    Invoke-LabScreenshot -Name forced-scp173 -ClientId $Context.ClientIds[1]
)
# Record actual SCP movement after assignment, alongside the native HUD.
$forceClip=Invoke-LabInput @{id='forced-scp3114';frames=180;keys=@(119);capture=$true;audio=$true}
@{before=$forceActors;after=$after;claims=$claims;reservations=$reservations;screenshots=$forceShots;clip=$forceClip} |
    ConvertTo-Json -Depth 8 | Set-Content "$($Context.Evidence)\scenario.json" -Encoding utf8
