$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $PSScriptRoot 'BunnyhopChecks.csproj'
$source=[IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Movement/ArenaBunnyhop.cs'))
$evidence=Join-Path $repo ('artifacts/bunnyhop-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
function Run([string]$name,[string[]]$extra=@()) {
    $out= & dotnet run --project $project -c ('Bunnyhop'+$name) @extra 2>&1 | Out-String
    $code=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence ($name+'.log')),$out)
    return @{Text=$out;Code=$code}
}
$r=Run 'Baseline'
if($r.Code -ne 0 -or !$r.Text.Contains('ARENA_BUNNYHOP_CHECKS_PASSED')) { throw $r.Text }
Write-Output $r.Text
$faults=@(
    @{Name='PulseTime';From='pulse ? f.ContactAt : Time.time';To='Time.time';Failure='input window uses impact time not delayed observer'},
    @{Name='DispatchSupport';From='!GroundState(state) || !Supported(f.Player)';To='!GroundState(state)';Failure='first contact also requires supported dispatch'},
    @{Name='Proximity';From='player.MovementContext.IsGrounded && player.CharacterController.isGrounded';To='player.MovementContext.IsGrounded';Failure='proximity alone never hops'},
    @{Name='Compounding';From='next.BaseSpeed = f.BaseSpeed;';To='next.BaseSpeed = f.BaseSpeed * Gain;';Failure='chain cap cannot compound beyond base'},
    @{Name='DoubleScale';From='f.Carry.x * dt / scale';To='f.Carry.x * dt';Failure='carry compensates scale exactly once'},
    @{Name='Vertical';From='motion.x = f.Carry.x * dt / scale;';To='motion.y = 0; motion.x = f.Carry.x * dt / scale;';Failure='carry preserves vertical motion'},
    @{Name='Expiry';From='f.LandedAt - f.BufferedAt > InputWindow';To='false';Failure='expired prelanding input cannot dispatch'},
    @{Name='LostContact';From='if (f.LandedAt >= 0f && !Supported(f.Player)) { Reset(); return; }';To='';Failure='lost support before dispatch discards chain'},
    @{Name='HardFall';From='context.TransformPosition.y < _flight.LaunchY - .35f';To='false';Failure='hard landing cancels chain'},
    @{Name='AllFalls';From='context.TransformPosition.y < _flight.LaunchY - .35f';To='true';Failure='ordinary native fall slowdown does not cancel same-floor hop'},
    @{Name='CallbackSignal';From='f.SignalOwner = state; ReleaseSignal(f);';To='ReleaseSignal(f);';Failure='noise callback reset cannot leak native signal'},
    @{Name='Wall';From='requested > 1f && speed < requested * .6f';To='false';Failure='wall contact discards banked speed'},
    @{Name='CollisionLoss';From='if (f.HasCarry && speed < Planar(f.Carry)) f.Carry = velocity;';To='';Failure='collision loss is not restored next frame'}
)
foreach($fault in $faults) {
    if([regex]::Matches($source,[regex]::Escape($fault.From)).Count -ne 1) {throw ('Ambiguous fault anchor '+$fault.Name)}
    $file=Join-Path $evidence ($fault.Name+'.cs')
    [IO.File]::WriteAllText($file,$source.Replace($fault.From,$fault.To))
    $r=Run $fault.Name @("-p:BunnyhopSource=$file")
    if($r.Code -eq 0 -or $r.Text -match 'error CS\d+' -or !$r.Text.Contains('ASSERT: '+$fault.Failure)) { throw ($fault.Name+': '+$r.Text) }
    Write-Output ('Rejected Bunnyhop fault: '+$fault.Name)
}
if([IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Movement/ArenaBunnyhop.cs')) -cne $source) {throw 'Source changed during checks'}
Write-Output "ARENA_BUNNYHOP_REGRESSION_PASSED ($($faults.Count) negative controls; $evidence)"
