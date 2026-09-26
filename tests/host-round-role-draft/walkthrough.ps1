param($Context)
# Real-client walkthrough for the round role draft (run with --clients 4).
# Package: WarmupScpSelector + ReinforcementsSystem + SCP999 candidates, SCP999.config.yml beside this file.
# 1. Round start with 4 clients + 3 dummies: the draft claims every SCP and fills rs.facility_manager,
#    rs.goc_spy (when the campaign is on) and scp999 on different players; scp999 lands on a real client.
# 2. .human swap: client X becomes SCP-173 at 97% health and offers a swap; the SCP-999 client's
#    .volunteer is refused (claimed); unclaimed client Y accepts and becomes SCP-173 where X stood with
#    X's health; X becomes Y's Class-D. The SCP claim moves to Y.
# 3. Admin scp999 onto a claimed player is refused without force.
. 'C:/Users/Michael/source-from-fsp9-2026-08-15/scpsl-plugins-metarepo/.tests/offline-clients/tools/host-aim.ps1'
$ErrorActionPreference='Stop'
$log="$($Context.Evidence)\scenario-server.txt"
$marks=[System.Collections.Generic.List[object]]::new()
function Server([string]$Command) {
    $reply=Invoke-LabServer $Command
    "> $Command`n$($reply -join "`n")" | Add-Content $log -Encoding utf8
    return ($reply -join "`n")
}
function Mark([string]$Name) { $marks.Add([pscustomobject]@{name=$Name;utc=(Get-Date).ToUniversalTime().ToString('o')}) }
function Get-Actor([int]$Id) { @(Observe | Where-Object id -eq $Id)[0] }
function Wait-For([scriptblock]$Condition,[string]$Message,[int]$Seconds=20) {
    $deadline=(Get-Date).AddSeconds($Seconds)
    do { $value=& $Condition; if($value) { return $value }; Start-Sleep -Milliseconds 400 } while((Get-Date) -lt $deadline)
    throw $Message
}
function Get-Claims {
    $text=Server '/roundroles'
    $claims=@{}
    foreach($line in ($text -split "`n")) {
        if ($line -match '^claim (\d+) (\S+) ') { $claims[[int]$Matches[1]]=$Matches[2] }
    }
    return @{text=$text;claims=$claims;done=($text -match 'draft=done')}
}

$clients=@($Context.ClientIds)
$actors=@($Context.Actors | ForEach-Object { $_.id })
if ($clients.Count -lt 4) { throw "Run with --clients 4 (got $($clients.Count))" }
function ClientOf([int]$ActorId) { $clients[[Array]::IndexOf($actors,$ActorId)] }

# 1. Round start through the draft.
$null=Server '/roundlock on'
foreach($n in 1..3) { $null=Server "/dummies spawn RrdDummy$n" }
Start-Sleep -Seconds 2
$null=Server '/forcestart'
$draft=Wait-For { $c=Get-Claims; if($c.done) { $c } } 'Round role draft did not complete' 30
Start-Sleep -Seconds 2
$draft=Get-Claims
Mark 'drafted'
$claims=$draft.claims
$byId=@{}; foreach($k in $claims.Keys) { if(-not $byId.ContainsKey($claims[$k])) { $byId[$claims[$k]]=@() }; $byId[$claims[$k]]+=$k }
if (-not $byId.ContainsKey('scp999')) { throw "No scp999 claim after the draft:`n$($draft.text)" }
if ($byId['scp999'].Count -ne 1) { throw "Expected one scp999 claim:`n$($draft.text)" }
$scp999=$byId['scp999'][0]
if ($actors -notcontains $scp999) { throw "scp999 was drafted onto a non-client (dummy?) $scp999" }
$scp999Actor=Wait-For { $x=Get-Actor $scp999; if($x.role -eq 'Tutorial' -and $x.health -ge 998) { $x } } 'Drafted SCP-999 was not set up'
$fm=if ($byId.ContainsKey('rs.facility_manager')) { $byId['rs.facility_manager'][0] } else { $null }
$spy=if ($byId.ContainsKey('rs.goc_spy')) { $byId['rs.goc_spy'][0] } else { $null }
$status999=Server '/scp999 status'

# 2. .human swap between two clients that are not SCP-999.
# Prefer clients the draft left unclaimed so the Facility Manager / spy claims survive for step 3.
$others=@($actors | Where-Object { $_ -ne $scp999 } | Sort-Object { if ($claims.ContainsKey($_)) { 1 } else { 0 } })
$x=$others[0]; $y=$others[1]
$null=Server "/forcerole $y ClassD 0"
$null=Wait-For { $a=Get-Actor $y; if($a.ready -and $a.role -eq 'ClassD') { $a } } 'Y did not become ClassD'
$null=Server "/forcerole $x Scp173 0"
$xScp=Wait-For { $a=Get-Actor $x; if($a.ready -and $a.role -eq 'Scp173') { $a } } 'X did not become SCP-173'
$targetHealth=[Math]::Floor($xScp.health*0.97)
$null=Server "/hp $x $targetHealth"
Start-Sleep -Milliseconds 800
$beforeSwap=@{x=(Get-Actor $x);y=(Get-Actor $y)}
Mark 'swap-arranged'
$offer=Invoke-LabSetup '.human' -ClientId (ClientOf $x)
Start-Sleep -Seconds 1
$refused=Invoke-LabSetup '.volunteer 173' -ClientId (ClientOf $scp999)
Start-Sleep -Seconds 1
if ((Get-Actor $scp999).role -ne 'Tutorial') { throw 'SCP-999 was pulled into the SCP slot' }
$accept=Invoke-LabSetup '.volunteer 173' -ClientId (ClientOf $y)
$yScp=Wait-For { $a=Get-Actor $y; if($a.role -eq 'Scp173') { $a } } 'Y did not become SCP-173 after accepting' 10
$xHuman=Wait-For { $a=Get-Actor $x; if($a.role -eq 'ClassD') { $a } } 'X did not take the Class-D role' 10
Mark 'swapped'
$dx=$yScp.position.x-$beforeSwap.x.position.x; $dz=$yScp.position.z-$beforeSwap.x.position.z
$swapDistance=[Math]::Sqrt($dx*$dx+$dz*$dz)
if ([Math]::Abs($yScp.health-$beforeSwap.x.health) -gt 2) { throw "Health did not carry over: $($beforeSwap.x.health) -> $($yScp.health)" }
if ($swapDistance -gt 1.5) { throw "New SCP-173 did not take X's position: $swapDistance m" }
$afterSwap=Get-Claims
if (-not ($afterSwap.text -match "claim $y warmup\.scp")) { throw "SCP claim did not move to Y:`n$($afterSwap.text)" }
$ownerShot=Invoke-LabScreenshot -Name new-scp173 -ClientId (ClientOf $y)
$humanShot=Invoke-LabScreenshot -Name former-scp-now-classd -ClientId (ClientOf $x)

# 3. Admin assignment onto a claimed player is refused without force.
$claimedTarget=if ($fm -and $fm -ne $x -and $fm -ne $y) { $fm } else { $y }
$adminRefusal=Server "/scp999 $claimedTarget"
if ($adminRefusal -notmatch 'force') { throw "Admin scp999 onto claimed $claimedTarget was not refused: $adminRefusal" }
Mark 'admin-refused'

@{
    marks=$marks; draft=$draft.text; scp999=$scp999; scp999Actor=$scp999Actor; facilityManager=$fm; spy=$spy
    status999=$status999; x=$x; y=$y; beforeSwap=$beforeSwap; yScp=$yScp; xHuman=$xHuman; swapDistance=$swapDistance
    offer=$offer; refused=$refused; accept=$accept; afterSwap=$afterSwap.text; adminRefusal=$adminRefusal
    screenshots=@{newScp=$ownerShot;formerScp=$humanShot}
} | ConvertTo-Json -Depth 8 | Set-Content "$($Context.Evidence)\scenario.json" -Encoding utf8
