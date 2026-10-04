param($Context)
# Native RA lottery walkthrough: one real client and six dummies, six native SCP slots.
# Vanilla supplies all six ordinary SCP types; an extra 173 reservation and an unspawned 3114 are skipped.
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
if ($forceActors.Count -ne 1) { throw 'Run with --clients 1' }
$firstId=$forceActors[0].id
# The native RoleAssigner consumes this queue: six SCP slots then one Class-D.
$gameConfig=Join-Path $Context.Root "server-state/$($Context.Port)/config/config_gameplay.txt"
Add-Content $gameConfig "`nteam_respawn_queue: 0000004" -Encoding utf8
Require-Reply '/reloadconfig' 'Reloaded all configs'
foreach ($n in 1..6) { Require-Reply "/dummies spawn LotteryDummy$n" 'dummy has been spawned' }
$deadline=(Get-Date).AddSeconds(15)
do {
    $forceDummies=@(Observe | Where-Object { $_.dummy -and $_.ready } | Sort-Object id)
    if ($forceDummies.Count -eq 6) { break }
    Start-Sleep -Milliseconds 400
} while ((Get-Date) -lt $deadline)
if ($forceDummies.Count -ne 6) { throw 'Six ready native dummy actors are required' }
$overflowId=$forceDummies[0].id; $missingId=$forceDummies[1].id
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
Require-Reply "/warmupforce $firstId 173" '已预定'
Require-Reply "/warmupforce $overflowId 173" '已预定'
Require-Reply "/warmupforce $missingId SCP-3114" '已预定'
$otherRoles=@('049','096','106','939')
foreach ($i in 0..3) { Require-Reply "/warmupforce $($forceDummies[$i+2].id) $($otherRoles[$i])" '已预定' }
$reservations=Server '/warmupforce list'
if ($reservations -notmatch "$firstId .*Scp173" -or $reservations -notmatch "$missingId .*Scp3114") {
    throw "Reservations did not replace correctly: $reservations"
}
Require-Reply '/forcestart' 'Forced round start'
$deadline=(Get-Date).AddSeconds(25)
do {
    $after=@(Observe)
    if (@($after | Where-Object id -eq $firstId)[0].role -eq 'Scp173' -and
        @($after | Where-Object { $_.role -like 'Scp*' }).Count -eq 6) { break }
    Start-Sleep -Milliseconds 400
} while ((Get-Date) -lt $deadline)
if (@($after | Where-Object id -eq $firstId)[0].role -ne 'Scp173') {
    throw "The reserved player did not win vanilla's SCP-173 slot: $($after | ConvertTo-Json -Depth 4 -Compress)"
}
$nativeRoles=@('Scp049','Scp079','Scp096','Scp106','Scp173','Scp939')
foreach ($role in $nativeRoles) {
    if (@($after | Where-Object role -eq $role).Count -ne 1) { throw "Native slot count changed for $role" }
}
if (@($after | Where-Object id -eq $overflowId)[0].role -eq 'Scp173') { throw 'An excess reservation created another SCP-173' }
if (@($after | Where-Object role -eq 'Scp3114').Count -ne 0) { throw 'An unspawned SCP-3114 was added' }
foreach ($i in 0..3) {
    $expectedRole='Scp'+$otherRoles[$i]
    if (@($after | Where-Object id -eq $forceDummies[$i+2].id)[0].role -ne $expectedRole) {
        throw "Dummy reservation did not win the existing $expectedRole slot"
    }
}
Require-Reply '/warmupforce list' '没有'
Require-Reply "/warmupforce $firstId 939" '只能在等待'
$claims=Server '/roundroles'
if ($claims -notmatch "claim $firstId warmup.scp") { throw "Lottery winner was not claimed as an SCP: $claims" }
$forceShot=Invoke-LabScreenshot -Name lottery-winner-scp173 -ClientId $Context.ClientIds[0]
$forceClip=Invoke-LabInput @{id='lottery-winner-scp173';frames=180;keys=@(119);capture=$true;audio=$true}
@{before=$forceActors;after=$after;claims=$claims;reservations=$reservations;screenshot=$forceShot;clip=$forceClip} |
    ConvertTo-Json -Depth 8 | Set-Content "$($Context.Evidence)\scenario.json" -Encoding utf8
