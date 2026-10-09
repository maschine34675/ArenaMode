$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/zombie-team-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$files = @('ZombieTeamRuntime.cs', 'ZombieTeamRuntime.Identity.cs', 'ZombieMode.cs', 'ZombieWavePolicy.cs', 'ZombieRefillPolicy.cs')
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
</Project>
'@
function New-Case([string]$name) {
    $dir = Join-Path $stage $name
    [IO.Directory]::CreateDirectory($dir) | Out-Null
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $repo "ArenaMode.Client/Modes/$file") -Destination $dir }
    Copy-Item -LiteralPath (Join-Path $repo 'ArenaMode.Client/Respawn/ArenaReplayWaitPolicy.cs') -Destination $dir
    foreach ($file in @('Program.cs','Stubs.cs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $dir }
    [IO.File]::WriteAllText((Join-Path $dir 'Checks.csproj'), $project)
    return $dir
}
function Run-Case([string]$dir, [bool]$mustPass) {
    $log = Join-Path $dir 'run.log'
    & dotnet run --project (Join-Path $dir 'Checks.csproj') --nologo *> $log
    $code = $LASTEXITCODE
    $content = Get-Content -LiteralPath $log -Raw
    if ($mustPass) {
        if ($code -ne 0 -or $content -notmatch 'ZOMBIE_TEAM_CHECKS_PASSED') { throw "Production failed: $log`n$content" }
        Write-Host ($content.Trim())
        $script:productionAssertions = [int]([regex]::Match($content, 'ZOMBIE_TEAM_CHECKS_PASSED (\d+)').Groups[1].Value)
    } else {
        if ($code -eq 0 -or $content -match 'error CS\d+' -or $content -notmatch 'InvalidOperationException') {
            throw "Mutant must compile and fail a runtime assertion: $log`n$content"
        }
    }
}
Run-Case (New-Case 'production') $true
$mutants = @(
    @{ Name='empty-hostility-aborts-wave'; File='ZombieTeamRuntime.cs'; Old='enemies.Count > 0 && !PitFireTeamCompat.EnsureHostility'; New='!PitFireTeamCompat.EnsureHostility' },
    @{ Name='death-respawns-midwave'; File='ZombieTeamRuntime.cs'; Old='internal bool HoldRespawn => _phase == TeamPhase.Ended || _playerDown;'; New='internal bool HoldRespawn => false;' },
    @{ Name='three-companions-admitted'; File='ZombieTeamRuntime.cs'; Old='private const int TeamCompanionCount = 4;'; New='private const int TeamCompanionCount = 3;' },
    @{ Name='solo-refill-enters-team'; File='ZombieMode.cs'; Old='playerEligible && _team == null'; New='playerEligible' },
    @{ Name='pending-final-infected-ignored'; File='ZombieWavePolicy.cs'; Old='remainingToSpawn == 0 && alive <= 0 && pending == 0'; New='remainingToSpawn == 0 && alive <= 0' },
    @{ Name='lifecycle-epoch-ignored'; File='ZombieTeamRuntime.Identity.cs'; Old='|| status.Epoch != _expectedPitLifecycleEpoch'; New='|| false' },
    @{ Name='local-body-registry-ignored'; File='ZombieTeamRuntime.Identity.cs'; Old='|| !ReferenceEquals(everExisted, _boundLocalPlayer)'; New='|| false' },
    @{ Name='camera-race'; File='ZombieTeamRuntime.cs'; Old='if (localDead && ArenaSession.ReplaySeconds > 0f && !KillcamReplayCompat.CameraReleased) return;'; New='if (false) return;' },
    @{ Name='wave-spawns-before-team-ready'; File='ZombieMode.cs'; Old='if (_team != null && !_team.Tick())'; New='if (false && _team != null && !_team.Tick())' },
    @{ Name='teardown-skips-final-survivor'; File='ZombieTeamRuntime.Identity.cs'; Old='if (_boundLocalPlayer?.HealthController?.IsAlive == false'; New='if (false && _boundLocalPlayer?.HealthController?.IsAlive == false' },
    @{ Name='reusable-respawn-lease'; File='ZombieTeamRuntime.Identity.cs'; Old='|| !_teamRespawnAuthorizationAvailable'; New='|| false'; Old2='|| _teamRespawnAuthorizationConsumed'; New2='|| false' }
)
foreach ($m in $mutants) {
    $dir = New-Case $m.Name
    $path = Join-Path $dir $m.File
    $source = [IO.File]::ReadAllText($path)
    if (-not $source.Contains($m.Old)) { throw "Missing mutant anchor $($m.Name)" }
    $source = $source.Replace($m.Old,$m.New)
    if ($m.Old2) { if (-not $source.Contains($m.Old2)) { throw "Missing second anchor $($m.Name)" }; $source = $source.Replace($m.Old2,$m.New2) }
    [IO.File]::WriteAllText($path,$source)
    Run-Case $dir $false
    Write-Host "Rejected compiled mutant: $($m.Name)"
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{ source = (& git -C $repo rev-parse HEAD); productionAssertions=$script:productionAssertions; compiledMutants=$mutants.Count; passed=$true } | ConvertTo-Json))
Write-Host "ZOMBIE_TEAM_MUTATION_CHECKS_PASSED $($mutants.Count) mutants; evidence: $stage"
