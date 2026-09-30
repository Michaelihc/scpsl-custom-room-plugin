param($Context)
# Real-client walkthrough for the persistent lobby countdown (run with --clients 2 so the native
# countdown runs). Package: this WarmupScpSelector build + HsmAdapter 1.3.1 over the OA1 mirror config.
# Client 1 walks natively from the gallery spawn into the Aim bay and then into the parkour shaft.
# Acceptance is visual: the full panel shows the enlarged countdown in the gallery, and inside each
# activity the panel gives way to the lane HUD while a compact countdown strip stays on screen.
$ErrorActionPreference='Stop'
$log="$($Context.Evidence)\scenario-server.txt"
function Server([string]$Command) {
    $reply=Invoke-LabServer $Command
    "> $Command`n$($reply -join "`n")" | Add-Content $log -Encoding utf8
    return ($reply -join "`n")
}
function Me { @(Observe | Where-Object id -eq $Context.Actor.id)[0] }
# Walk with W along one heading until $Done holds for the observed actor, in short native-input chunks.
function Walk-Until([double]$Yaw,[scriptblock]$Done,[string]$What,[int]$Chunks=10) {
    $null=Set-LabLook -Yaw $Yaw -Pitch 0
    for ($i=0; $i -lt $Chunks; $i++) {
        $a=Me
        if (& $Done $a) { return $a }
        $null=Invoke-LabInput @{frames=75;keys=@(119)}
    }
    $a=Me
    if (& $Done $a) { return $a }
    throw "Did not reach $What (at $($a.position.x), $($a.position.z))"
}

if (@($Context.ClientIds).Count -lt 2) { throw "Run with --clients 2 (got $(@($Context.ClientIds).Count))" }
$spawn=Me
if (!$spawn -or $spawn.role -ne 'Tutorial') { throw 'Client 1 is not a Tutorial in the station' }
if ((Server '/roundtime') -notmatch 'not started') { throw 'The round already started' }
# Station-local origin from the gallery spawn (WarmupHallLayout.SpawnPosition = local 0, 0.5, -31.5).
$ox=$spawn.position.x; $oz=$spawn.position.z+31.5

# 1. Gallery: the full panel with the enlarged countdown.
Start-Sleep -Seconds 3
$panel=Invoke-LabScreenshot -Name 'panel-gallery'

# 2. Aim bay (local x 11..36; the shooting counter stops walkers near x 14): the lane HUD replaces the panel; the compact countdown strip remains.
$null=Walk-Until 0 { param($a) $a.position.z -ge $oz-3 } 'the hub'
$aim=Walk-Until 90 { param($a) $a.position.x -ge $ox+12.5 } 'the Aim bay'
Start-Sleep -Seconds 2
$aimShot=Invoke-LabScreenshot -Name 'strip-aim'

# 3. Parkour shaft (local x -4.5..4.5, z 17.5..71): same strip beside the Pulse Line HUD.
$null=Walk-Until 270 { param($a) $a.position.x -le $ox+1 } 'the hub centre line'
$shaft=Walk-Until 0 { param($a) $a.position.z -ge $oz+19.5 } 'the parkour shaft'
Start-Sleep -Seconds 2
$shaftShot=Invoke-LabScreenshot -Name 'strip-parkour'

$roundtime=Server '/roundtime'
@{spawn=$spawn.position; aim=$aim.position; shaft=$shaft.position; roundtime=$roundtime
  screenshots=@($panel,$aimShot,$shaftShot)} | ConvertTo-Json -Depth 5 | Set-Content "$($Context.Evidence)\scenario.json" -Encoding utf8
if ($roundtime -notmatch 'not started') { throw 'The round started before the walkthrough finished' }
