param([switch]$ProjectionOnly)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'FriendlyMarkerChecks.csproj'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temp = Join-Path ([IO.Path]::GetTempPath()) ('arena-friendly-marker-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$sources = @{
    MarkerSource = Join-Path $repo 'ArenaMode.Client/Hud/ArenaFriendlyMarkers.cs'
    PolicySource = Join-Path $repo 'ArenaMode.Client/Hud/ArenaFriendlyMarkerPolicy.cs'
    TacticalMarkerSource = Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.FriendlyMarkers.cs'
}
$texts = @{}; $snapshots = @{}; $hashes = @{}
foreach ($key in $sources.Keys) {
    $texts[$key] = [IO.File]::ReadAllText($sources[$key])
    $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash
    $snapshots[$key] = Join-Path $temp ($key + '.cs')
    [IO.File]::WriteAllText($snapshots[$key], $texts[$key])
}
$mutants = @(
    @{ Name='EnemyMarker'; Key='MarkerSource'; From='team.TryClassifyCurrentBody(player, out bool friendly) && friendly'; To='team.TryClassifyCurrentBody(player, out bool friendly)'; Failure='only the exact current friendly bot gets one LOS-tested marker' },
    @{ Name='ThroughWall'; Key='MarkerSource'; From='if (Physics.Linecast(origin, headPosition, mask, QueryTriggerInteraction.Ignore))'; To='if (Physics.Linecast(origin, headPosition, mask, QueryTriggerInteraction.Ignore) && false)'; Failure='newly occluded teammates hide immediately without a positive LOS cache' },
    @{ Name='DeadMarker'; Key='MarkerSource'; From='player.HealthController?.IsAlive != true || !_seen.Add(player)'; To='!_seen.Add(player)'; Failure='dead friendly bodies never retain a marker' },
    @{ Name='RearMarker'; Key='PolicySource'; From='viewport.z > 0f && distance'; To='distance'; Failure='invalid display remains hidden: behind' },
    @{ Name='LeakedCallback'; Key='MarkerSource'; From='if (_subscribed) Canvas.preWillRenderCanvases -= BeforeCanvasRebuild;'; To='if (false) Canvas.preWillRenderCanvases -= BeforeCanvasRebuild;'; Failure='dispose unsubscribes the render callback and destroys all views' },
    @{ Name='LateCameraActivation'; Key='MarkerSource'; From='Canvas.preWillRenderCanvases += BeforeCanvasRebuild;'; To='Camera.onPreCull += _ => BeforeCanvasRebuild();'; Failure='friendly geometry must be ready in the same canvas rebuild, before camera culling' },
    @{ Name='UnboundedDiagnostics'; Key='MarkerSource'; From='_samples < 3'; To='true'; Failure='render diagnostics remain capped over long matches' },
    @{ Name='StaleOriginalHead'; Key='MarkerSource'; From='Vector3 headPosition = head.position;'; To='Vector3 headPosition = head.Original.position;'; Failure='virtualized head pose must override the stale Original transform' },
    @{ Name='TacticalRebind'; Key='TacticalMarkerSource'; From='_phase != Phase.Live'; To='false'; Failure='invalid Tactical identity is rejected: staging' },
    @{ Name='HistoricalFollower'; Key='TacticalMarkerSource'; From='!ReferenceEquals(owner.GetPlayer, player)'; To='false'; Failure='invalid Tactical identity is rejected: aliasbody' },
    @{ Name='TacticalWorldAlias'; Key='TacticalMarkerSource'; From='!ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)'; To='false'; Failure='invalid Tactical identity is rejected: alivealias' },
    @{ Name='InputPixels'; Projection=$true; Key='MarkerSource'; From='outputWidth, outputHeight, out Vector2 screen)) continue;'; To="outputWidth, outputHeight, out Vector2 screen)) continue;`n            screen = camera.WorldToScreenPoint(headPosition);"; Failure='DLSS input pixels must expand to full output HUD coordinates' },
    @{ Name='ViewportLost'; Projection=$true; Key='PolicySource'; From='Rect outputRect = scalesToFullScreen ? new Rect(0f, 0f, 1f, 1f) : cameraRect;'; To='Rect outputRect = new Rect(0f, 0f, 1f, 1f);'; Failure='pending DLSS enable retains unscaled viewport offsets' },
    @{ Name='ScaleIgnored'; Projection=$true; Key='MarkerSource'; From='case "DOWNSCALE": scalesToFullScreen = true; return true;'; To='case "DOWNSCALE": scalesToFullScreen = false; return true;'; Failure='DLSS input pixels must expand to full output HUD coordinates' }
)
if ($ProjectionOnly) { $mutants = @($mutants | Where-Object Projection -eq $true) }
foreach ($m in $mutants) {
    if (!$texts[$m.Key].Contains($m.From)) { throw "Missing mutation anchor: $($m.Name)" }
    $path = Join-Path $temp ($m.Name + '.cs'); [IO.File]::WriteAllText($path, $texts[$m.Key].Replace($m.From, $m.To))
    $runArgs = @('run', '--project', $project, '-c', ('MarkerMutant' + $m.Name))
    foreach ($key in $sources.Keys) { $source = if ($key -eq $m.Key) { $path } else { $snapshots[$key] }; $runArgs += "-p:${key}=$source" }
    $output = & dotnet @runArgs 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains($m.Failure)) { throw "Mutant did not fail the intended assertion: $($m.Name)`n$output" }
    Write-Output "Rejected mutant: $($m.Name)"
}
foreach ($key in $sources.Keys) { if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) { throw "Production source changed during checks: $key" } }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Final production marker baseline failed.' }
Write-Output 'ARENA_FRIENDLY_MARKER_REGRESSION_PASSED'
