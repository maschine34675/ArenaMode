$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$evidence = Join-Path $repo ('artifacts/akimbo-laser-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$viewProject = Join-Path $PSScriptRoot 'ViewChecks.csproj'
$apiProject = Join-Path $PSScriptRoot '../NativeApi/NativeApiChecks.csproj'
$sources = @{}
foreach ($entry in @(@('ViewSource', 'AkimboView.cs'), @('LaserSource', 'AkimboAimLaser.cs'), @('GeometrySource', 'AkimboShotGeometry.cs'))) {
    $sources[$entry[0]] = [IO.File]::ReadAllText((Join-Path $repo ('ArenaMode.Client/Combat/Akimbo/' + $entry[1])))
}
$faults = @(
    @('hide', 'ViewSource', '        _aimLaser?.Hide();', '', 'view hide immediately deactivates aiming aids'),
    @('dispose', 'ViewSource', '_aimLaser?.Dispose(); _aimLaser = null;', '_aimLaser = null;', 'laser disposal destroys only owned geometry'),
    @('busy', 'ViewSource', 'if (!Visible || _aimLaser == null)', 'if (_aimLaser == null)', 'hidden busy or retired view cannot resurrect'),
    @('muzzle-owner', 'ViewSource', 'node.Right == null || !Owned(node)', 'node.Right == null', 'transferred native muzzle cannot drive'),
    @('self', 'LaserSource', ' || candidate.collider.transform.IsChildOf(self)', '', 'nearest wall wins over enemy and own body'),
    @('nearest', 'LaserSource', ' || candidate.distance >= closest', '', 'nearest wall wins over enemy and own body'),
    @('stale-dot', 'LaserSource', 'if (_points[side] != null) _points[side].SetActive(false);', 'if (_points[side] != null) _points[side].SetActive(true);', 'ray miss keeps beams but removes stale aim dots'),
    @('drawn-segment', 'LaserSource', 'if (clipped) { hit = obstruction; target = hit.point; struck = true; }', '', 'visible muzzle segment clips before an intervening wall'),
    @('overflow', 'LaserSource', 'if (count >= _hits.Length)', 'if (count > _hits.Length)', 'full unsorted hit buffer fails closed'),
    @('origin-cover', 'LaserSource', '|| Physics.Linecast(camera, origin, environment, QueryTriggerInteraction.Ignore)', '', 'camera to corrected origin wall hides right aiming laser'),
    @('visible-cover', 'LaserSource', '|| Physics.Linecast(camera, visibleMuzzle, environment, QueryTriggerInteraction.Ignore)', '', 'camera to visible muzzle wall hides right aiming laser'),
    @('origin-occupied', 'LaserSource', 'Physics.CheckSphere(origin, .025f, environment, QueryTriggerInteraction.Ignore)', 'false', 'corrected origin inside geometry hides right laser'),
    @('visible-occupied', 'LaserSource', '|| Physics.CheckSphere(visibleMuzzle, .025f, environment, QueryTriggerInteraction.Ignore)', '', 'visible muzzle inside geometry hides right laser'),
    @('triggers', 'LaserSource', '|| candidate.collider.isTrigger && ((1 << candidate.collider.gameObject.layer) & (int)LayersMaskController.HitColliderMask) == 0', '', 'non-combat triggers ignored while hit-collider triggers'),
    @('invalid-distance', 'LaserSource', '|| float.IsNaN(candidate.distance) || float.IsInfinity(candidate.distance)', '', 'non-finite collision distance cannot replace'),
    @('convergence', 'GeometrySource', 'nativeDirection.normalized * 50f', 'nativeDirection.normalized * 10f', 'left aid and projectile converge on exact native fifty metre ray'),
    @('separation', 'GeometrySource', '&& delta.sqrMagnitude <= .65f * .65f', '', 'excessive copied-muzzle separation rejects')
)
$records = @()
try {
    $positive = (& dotnet run --project $viewProject -c Release 2>&1 | Out-String)
    [IO.File]::WriteAllText((Join-Path $evidence 'production.log'), $positive)
    if ($LASTEXITCODE -ne 0 -or !$positive.Contains('AKIMBO_VIEW_CHECKS_PASSED')) { throw $positive }
    Write-Output $positive.Trim()
    foreach ($fault in $faults) {
        $source = $sources[$fault[1]]
        if ($source.Split([string[]]@($fault[2]), [StringSplitOptions]::None).Count -ne 2) { throw "Mutation site changed: $($fault[0])" }
        $file = Join-Path $evidence ($fault[0] + '.cs')
        [IO.File]::WriteAllText($file, $source.Replace($fault[2], $fault[3]))
        $result = (& dotnet run --project $viewProject -c Release "-p:$($fault[1])=$file" 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $evidence ($fault[0] + '.log')), $result)
        if ($exitCode -eq 0 -or $result.Contains('error CS') -or !$result.Contains($fault[4])) { throw $result }
        $records += @{ Name = $fault[0]; ExitCode = $exitCode; Expected = $fault[4]; Rejected = $true }
        Write-Output "NEGATIVE_CONTROL_PASSED $($fault[0])"
    }
    $positive = (& dotnet build $apiProject -c Release --nologo -v:quiet 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) { throw $positive }
    [IO.File]::WriteAllText((Join-Path $evidence 'native-api.log'), $positive)
    $native = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboNativeAim.cs'))
    $file = Join-Path $evidence 'obsolete-player-transform.cs'
    [IO.File]::WriteAllText($file, $native.Replace('body.Transform?.Original', 'body.transform'))
    $result = (& dotnet build $apiProject -c Release --nologo -v:quiet "-p:AimSource=$file" 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence 'obsolete-player-transform.log'), $result)
    if ($exitCode -eq 0 -or !$result.Contains('CS0619') -or !$result.Contains('Player.transform')) { throw $result }
    $records += @{ Name = 'obsolete-player-transform'; ExitCode = $exitCode; Expected = 'CS0619 Player.transform'; Rejected = $true }
    Write-Output 'NEGATIVE_CONTROL_PASSED obsolete-player-transform (installed native API)'
} finally {
    & dotnet build $viewProject -c Release -t:Rebuild --nologo -v:quiet
    if ($LASTEXITCODE -ne 0) { throw 'Production laser check restore failed' }
    & dotnet build $apiProject -c Release -t:Rebuild --nologo -v:quiet
    if ($LASTEXITCODE -ne 0) { throw 'Production native API check restore failed' }
}
& dotnet run --project $viewProject -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Final production laser run failed' }
@{ Controls = $records; SourceSha256 = @{
    View = (Get-FileHash (Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboView.cs')).Hash
    Laser = (Get-FileHash (Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboAimLaser.cs')).Hash
    Geometry = (Get-FileHash (Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboShotGeometry.cs')).Hash
    NativeApi = (Get-FileHash (Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboNativeAim.cs')).Hash
} } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'manifest.json')
Write-Output "AKIMBO_LASER_CHECKS_PASSED ($($records.Count) negative controls; evidence $evidence)"
