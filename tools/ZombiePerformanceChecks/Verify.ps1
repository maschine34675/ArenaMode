$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/zombie-performance-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$project = Join-Path $PSScriptRoot 'ZombiePerformanceChecks.csproj'
function Run-Case([string]$name, [string[]]$properties, [bool]$pass) {
    $log = Join-Path $stage ($name + '.log')
    & dotnet run --project $project -c $name --nologo -p:WarningLevel=0 @properties *> $log
    $code = $LASTEXITCODE
    $content = Get-Content -LiteralPath $log -Raw
    if ($pass) {
        if ($code -ne 0 -or $content -notmatch 'ZOMBIE_PERFORMANCE_CHECKS_PASSED') { throw "Production failed: $log`n$content" }
        Write-Host ([regex]::Match($content, 'ZOMBIE_PERFORMANCE_CHECKS_PASSED.*').Value)
        $script:assertions = [int]([regex]::Match($content, 'ZOMBIE_PERFORMANCE_CHECKS_PASSED (\d+)').Groups[1].Value)
    } elseif ($code -eq 0 -or $content -match 'error CS\d+' -or $content -notmatch 'InvalidOperationException') {
        throw "Mutant must compile and fail a runtime assertion: $log`n$content"
    }
}
Run-Case 'Release' @() $true
$mutants = @(
    @{Name='hearing-no-op';Old='return !skip;';New='return true;'},
    @{Name='social-no-op';Old="checker._nextTimeCheck = Time.time + 1f;`n            return false;";New="checker._nextTimeCheck = Time.time + 1f;`n            return true;"},
    @{Name='all-sound-kinds';Old='type != AISoundType.step || ';New=''},
    @{Name='ordinary-raid-leak';Old='!ZombieNativeEventBridge.IsActiveZombieRaid()';New='false'},
    @{Name='stale-world';Old='&& ArenaSession.IsExactActiveRaid(game, world)';New='&& true'},
    @{Name='boss-and-pmc-leak';Old='=> role == WildSpawnType.infectedAssault';New='=> true || role == WildSpawnType.infectedAssault'},
    @{Name='stale-profile-body';Old='|| !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)';New=''},
    @{Name='stale-sensor';Old='|| !ReferenceEquals(sensor._botOwner.HearingSensor, sensor)';New=''},
    @{Name='unknown-friendship';Old='return group.Contains(sourceOwner) || group.IsAlly(source) || group.IsSuitable(sourceRole);';New='return true;'},
    @{Name='one-way-friendship';Old='&& Friendly(sourceOwner, listenerBody, sensor._botOwner, listenerRole, game)';New='&& true'},
    @{Name='native-id-enemy-lost';Old='|| group.IsEnemy(source)';New=''},
    @{Name='explicit-enemy-lost';Old='group.Enemies.ContainsKey(source) || ';New=''},
    @{Name='combat-inline-path';Old='memory.GoalEnemy != null || ';New=''},
    @{Name='social-every-frame';Old='checker._nextTimeCheck = Time.time + 1f;';New='checker._nextTimeCheck = 0f;'},
    @{Name='stale-checker';Old='|| !ReferenceEquals(checker._owner.FriendChecker, checker)';New=''},
    @{Name='zero-delay';Old='!(delay > 0f) || ';New=''},
    @{Name='cross-raid-counters';Old='_stepCandidates = _friendlyStepsSkipped = _socialSearchesSkipped = 0;';New=';'}
)
$sourcePath = Join-Path $repo 'ArenaMode.Client/Bots/ZombiePerformancePatches.cs'
$source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
foreach ($mutant in $mutants) {
    if (!$source.Contains($mutant.Old)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $copy = Join-Path $stage ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($mutant.Old, $mutant.New))
    Run-Case $mutant.Name @("-p:PerformanceSource=$copy") $false
    Write-Host "Rejected compiled mutant: $($mutant.Name)"
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{
    source = (& git -C $repo rev-parse HEAD); sourceSha256 = (Get-FileHash -LiteralPath $sourcePath).Hash
    assertions = $script:assertions; compiledMutants = $mutants.Count; passed = $true
} | ConvertTo-Json))
Write-Host "ZOMBIE_PERFORMANCE_MUTATION_CHECKS_PASSED $($mutants.Count) compiled mutants; evidence: $stage"
