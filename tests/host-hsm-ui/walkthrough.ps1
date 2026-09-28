param($Context)

# Run with one client and the committed WarmupScpSelector + HsmAdapter candidate.
# Review captures for the status panel on arrival and its removal at round handoff.
$ErrorActionPreference = 'Stop'
$actorId = $Context.Actor.id
function Server([string]$Command) { return ((Invoke-LabServer $Command) -join "`n") }
function Actor { return @(Observe | Where-Object id -eq $actorId)[0] }

$ready = Server '/roundroles'
if ($ready -notmatch 'ROUNDROLES host=active') { throw "Warmup draft is not ready: $ready" }
$before = Actor
if (-not $before.ready) { throw 'Client did not enter the warmup room' }

$null = Invoke-LabInput @{id='warmup-status';frames=120;capture=$true}
$null = Invoke-LabScreenshot -Name 'warmup-status'
$null = Invoke-LabInput @{id='warmup-status-steady';frames=120;capture=$true}

$start = Server '/forcestart'
if ($start -notmatch 'Forced round start') { throw "Round start failed: $start" }
$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 400
    $draft = Server '/roundroles'
} until ($draft -match 'draft=done' -or (Get-Date) -ge $deadline)
if ($draft -notmatch 'draft=done') { throw "Warmup handoff did not finish: $draft" }

$null = Invoke-LabInput @{id='warmup-handoff-cleanup';frames=120;capture=$true}
$null = Invoke-LabScreenshot -Name 'warmup-status-cleared'
