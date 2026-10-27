param()
$ErrorActionPreference = 'Stop'
$domRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$domEvidence = Join-Path $domRepo ('artifacts/domination-bot-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($domEvidence)
$domClient = Join-Path $domRepo 'ArenaMode.Client/Bots'
$domCompat = Join-Path $domRepo 'ArenaMode.BigBrainCompat'
$domInputs = @{}
$domFiles = @((Join-Path $domClient 'DominationObjectiveBridge.cs')) + @(Get-ChildItem -LiteralPath $domCompat -Filter 'ArenaDomination*.cs' | ForEach-Object FullName) + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object FullName)
foreach ($domFile in $domFiles) { $domInputs[$domFile] = (Get-FileHash -LiteralPath $domFile).Hash }
function Invoke-DominationCheck([string]$name,[string]$client,[string]$compat,[string]$reject='') {
    $stage = Join-Path $domEvidence $name
    [void][IO.Directory]::CreateDirectory($stage)
    $build = & dotnet build (Join-Path $PSScriptRoot 'DominationBotChecks.csproj') -c Release --nologo `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $domRepo 'tools/AirPitChecks/NoDeploy.targets')" `
        "-p:SPTUserMods=$(Join-Path $domRepo 'artifacts/nodeploy/must-not-exist')" `
        "-p:ClientSourceRoot=$client" "-p:ObjectiveSourceRoot=$compat" "-p:BaseIntermediateOutputPath=$stage/obj/" "-p:OutputPath=$stage/out/" 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $stage 'build.log'),$build)
    if ($LASTEXITCODE -ne 0) { throw "$name did not compile: $build" }
    # Windows PowerShell represents expected native stderr as NativeCommandError.
    # Capture the mutant's nonzero exit and named assertion instead of throwing early.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $run = & dotnet (Join-Path $stage 'out/DominationBotChecks.dll') 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    [IO.File]::WriteAllText((Join-Path $stage 'result.log'),$run)
    if ($reject) {
        if ($code -eq 0 -or !$run.Contains('FAIL: '+$reject)) { throw "$name did not reject intended defect: $run" }
        Write-Output "Rejected $name at: $reject"
    } else {
        if ($code -ne 0 -or !$run.Contains('Domination bot checks passed:')) { throw "$name failed: $run" }
        Write-Output $run.Trim()
    }
}
Invoke-DominationCheck 'production' $domClient $domCompat
$faults = @(
    @{Name='stale-return-target';File='ArenaDominationLogic.cs';Find='(point - _ownedDestination).sqrMagnitude';Replace='(point - _destination).sqrMagnitude';Reject='failed alternate preflights clear the original owned return target'},
    @{Name='no-center-fallback';File='DominationObjectiveBridge.cs';Find='if (index == ApproachCount - 1) return Center;';Replace='if (index == ApproachCount - 1) return Destination;';Reject='blocked ring offsets fall back to a reachable center'},
    @{Name='unsafe-approach-projection';File='ArenaDominationRuntime.cs';Find='|| Math.Abs(hit.position.y - candidate.y) > .5f';Replace='';Reject='approach projection cannot change floor'},
    @{Name='unprojected-move';File='ArenaDominationRuntime.cs';Find='destination = hit.position;';Replace='destination = candidate;';Reject='native movement receives the projected capture-valid floor'},
    @{Name='fixed-three-points';File='DominationObjectiveBridge.cs';Find='points.Count != mode.ObjectiveCount';Replace='points.Count != 3';Reject='authored point count supports current bot assignments: 1'},
    @{Name='publisher-count-drift';File='DominationObjectiveBridge.cs';Find='points.Count != mode.ObjectiveCount || ';Replace='';Reject='publisher count mismatch revokes existing leases'},
    @{Name='oversized-objective-list';File='DominationObjectiveBridge.cs';Find='|| points.Count > 16';Replace='';Reject='more than sixteen points rejected even when mode agrees'},
    @{Name='stale-bigbrain-action';File='ArenaDominationLayer.cs';Find='|| !ReferenceEquals(_action.Lease, _lease)';Replace='';Reject='planner reassignment ends previous BigBrain action'},
    @{Name='unowned-body';File='DominationObjectiveBridge.cs';Find='&& _match.Mode is TeamDeathmatchMode mode && mode.TryClassifyCurrentBody(body, out _)';Replace='';Reject='foreign live body cannot claim objective'},
    @{Name='tdm-leak';File='DominationObjectiveBridge.cs';Find='mode.IsDomination &&';Replace='';Reject='ordinary TDM excluded'},
    @{Name='point-stacking';File='DominationObjectiveBridge.cs';Find='18 * claimed';Replace='0 * claimed';Reject='distribute each team across three points'},
    @{Name='capture-through-wall';File='DominationObjectiveBridge.cs';Find='&& ArenaPickupGeometry.LineClear(Center + Vector3.up * .8f, position + Vector3.up * .8f)';Replace='';Reject='wall prevents false holding'},
    @{Name='shootable-enemy-ignored';File='ArenaDominationRuntime.cs';Find='(enemy.IsVisible && enemy.CanShoot)';Replace='false';Reject='yield visible shootable enemy'},
    @{Name='medical-initiation-starved';File='ArenaDominationRuntime.cs';Find='|| bot.Medecine?.FirstAid?.Have2Do == true';Replace='';Reject='yield pending first aid before SAIN starts'},
    @{Name='partial-nav';File='ArenaDominationRuntime.cs';Find='|| path.status != NavMeshPathStatus.PathComplete';Replace='';Reject='partial preflight denied with bounded attempts'},
    @{Name='cover-fallback';File='ArenaDominationLogic.cs';Find='onlyShortTrie: true';Replace='onlyShortTrie: false';Reject='no cover reconstruction or teleport fallback'},
    @{Name='teleport-fallback';File='ArenaDominationLogic.cs';Find='mustHaveWay: false';Replace='mustHaveWay: true';Reject='no cover reconstruction or teleport fallback'},
    @{Name='foreign-route-cleanup';File='ArenaDominationLogic.cs';Find='(!ReferenceEquals(_state.PathController.CurPath, _path) && _state.PathController.CurPath != null)';Replace='false';Reject='cleanup preserves newer path'},
    @{Name='defender-travel-timeout';File='ArenaDominationLogic.cs';Find='if (lease.Inside(BotOwner.Position))';Replace='if (false)';Reject='holding does not churn routes'}
)
foreach ($fault in $faults) {
    $sourceRoot = Join-Path $domEvidence ($fault.Name+'/source')
    $client = Join-Path $sourceRoot 'client'; $compat = Join-Path $sourceRoot 'compat'
    [void][IO.Directory]::CreateDirectory($client); [void][IO.Directory]::CreateDirectory($compat)
    Copy-Item -LiteralPath (Join-Path $domClient 'DominationObjectiveBridge.cs') -Destination $client
    foreach ($file in Get-ChildItem -LiteralPath $domCompat -Filter 'ArenaDomination*.cs') { Copy-Item -LiteralPath $file.FullName -Destination $compat }
    $path = if ($fault.File -eq 'DominationObjectiveBridge.cs') { Join-Path $client $fault.File } else { Join-Path $compat $fault.File }
    $source = [IO.File]::ReadAllText($path)
    if ($source.Split(@($fault.Find),[StringSplitOptions]::None).Length -ne 2) { throw "Nonunique mutation anchor: $($fault.Name)" }
    [IO.File]::WriteAllText($path,$source.Replace($fault.Find,$fault.Replace))
    Invoke-DominationCheck $fault.Name $client $compat $fault.Reject
}
Invoke-DominationCheck 'production-final' $domClient $domCompat
foreach ($path in $domInputs.Keys) { if ((Get-FileHash -LiteralPath $path).Hash -ne $domInputs[$path]) { throw "Input changed: $path" } }
[ordered]@{ Inputs=$domInputs; Faults=$faults.Count; CompletedUtc=[DateTime]::UtcNow.ToString('o');
    Limitation='Production-linked source with native doubles and compilable mutations; not Unity movement, rendering, real BigBrain scheduling or live NavMesh acceptance.' } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $domEvidence 'manifest.json')
Write-Output "DOMINATION BOT CHECKS PASSED; evidence: $domEvidence"
