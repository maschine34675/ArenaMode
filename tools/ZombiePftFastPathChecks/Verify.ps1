$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $PSScriptRoot ('evidence/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$project = Join-Path $PSScriptRoot 'ZombiePftFastPathChecks.csproj'
$sourcePath = Join-Path $repo 'ArenaMode.Client/Compat/ZombiePftFastPath.cs'
$initialHash = (Get-FileHash -LiteralPath $sourcePath).Hash
function Run-Case([string]$name, [string[]]$properties, [bool]$pass) {
    $log = Join-Path $stage ($name + '.log')
    & dotnet run --project $project -c $name --nologo -p:WarningLevel=0 @properties *> $log
    $code = $LASTEXITCODE
    $content = Get-Content -LiteralPath $log -Raw
    if ($pass) {
        if ($code -ne 0 -or $content -notmatch 'ZOMBIE_PFT_FAST_PATH_CHECKS_PASSED') { throw "Production failed: $log`n$content" }
        Write-Host ([regex]::Match($content, 'ZOMBIE_PFT_FAST_PATH_CHECKS_PASSED.*').Value)
        $script:assertions = [int]([regex]::Match($content, 'ZOMBIE_PFT_FAST_PATH_CHECKS_PASSED (\d+)').Groups[1].Value)
    } elseif ($code -eq 0 -or $content -match 'error CS\d+' -or $content -notmatch 'InvalidOperationException') {
        throw "Mutant must compile and fail a runtime assertion: $log`n$content"
    }
}
Run-Case 'Release' @() $true
$mutants = @(
    @{Name='no-op';Old="_skipped++;`n            return false;";New="_skipped++;`n            return true;"},
    @{Name='static-argument-binding';Old='private static bool Prefix(object __0) => ShouldRunOriginal(__0);';New='private static bool Prefix(object __instance) => ShouldRunOriginal(__instance);'},
    @{Name='ordinary-raid-leak';Old='|| !ZombieNativeEventBridge.IsActiveZombieRaid()';New=''},
    @{Name='disabled-toggle-leak';Old='Settings.ZombiePftFastPathEnabled?.Value != true';New='false'},
    @{Name='stale-world';Old='|| !ArenaSession.IsExactActiveRaid(game, world)';New=''},
    @{Name='boss-role-leak';Old='=> role == WildSpawnType.infectedAssault';New='=> true || role == WildSpawnType.infectedAssault'},
    @{Name='stale-brain';Old='|| !ReferenceEquals(owner.Brain?.BaseBrain, brain)';New=''},
    @{Name='stale-body';Old='|| !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)';New=''},
    @{Name='pft-follower-leak';Old='|| _isFollower(owner, null)';New=''},
    @{Name='native-follower-leak';Old='owner.BotFollower?.HaveBoss == true';New='false'},
    @{Name='lost-wake';Old='if (standby != null && standby.StandByType != BotStandByType.none';New='if (false && standby != null && standby.StandByType != BotStandByType.none'},
    @{Name='custom-layer-leak';Old='proof.NativeOnly = NativeLayersOnly(layers) && _listVersion(layers) == version;';New='proof.NativeOnly = true;'},
    @{Name='native-duplicate-limit';Old='if (layers == null || layers.Count == 0) return true;';New='if (layers == null || layers.Count == 0 || layers.Count > 7) return true;'},
    @{Name='same-count-mutation';Old='|| proof.Version != version';New=''},
    @{Name='list-replacement';Old='!ReferenceEquals(proof.List, layers) || proof.Version != version';New='proof.List == null'},
    @{Name='foreign-layer-owner';Old='|| !ReferenceEquals(first._owner, owner)';New=''},
    @{Name='cross-raid-counters';Old='_calls = _skipped = _standbyFallbacks = _layerChecks = _shapeFallbacks = _errors = 0;';New=';'},
    @{Name='metadata-mismatch';Old='&& PitFireTeamVersions.IsSupportedIdentity(pluginVersion, assemblyVersion)';New='&& true'},
    @{Name='unknown-pft-version';Old="=> PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion)`n            && PitFireTeamVersions.IsSupportedAssemblyVersion(assemblyVersion)`n            && PitFireTeamVersions.IsSupportedIdentity(pluginVersion, assemblyVersion)";New='=> assemblyVersion != null'}
)
$source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
foreach ($mutant in $mutants) {
    if (!$source.Contains($mutant.Old)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $copy = Join-Path $stage ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($mutant.Old, $mutant.New))
    Run-Case $mutant.Name @("-p:FastPathSource=$copy") $false
    Write-Host "Rejected compiled mutant: $($mutant.Name)"
}
& (Join-Path $PSScriptRoot 'Verify-NativeContract.ps1')
if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne $initialHash) { throw 'Production source changed during verification; rerun before claiming evidence' }
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{
    source = (& git -C $repo rev-parse HEAD); sourceSha256 = $initialHash
    assertions = $script:assertions; compiledMutants = $mutants.Count; passed = $true
} | ConvertTo-Json))
Write-Host "ZOMBIE_PFT_FAST_PATH_MUTATIONS_PASSED $($mutants.Count) compiled mutants; evidence: $stage"
