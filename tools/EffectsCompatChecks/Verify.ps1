param([string]$PublishedManager, [string]$PublishedBrokenLimbs, [string]$NativeAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$PublishedManager) { $PublishedManager = Join-Path $repo 'artifacts/candidate-audit-20261020/health/effects-release/unpacked/BepInEx/plugins/EFTEffectManager.dll' }
if (!$PublishedBrokenLimbs) { $PublishedBrokenLimbs = Join-Path $repo 'artifacts/candidate-audit-20261020/health/broken-release/unpacked/BepInEx/plugins/BrokenLimbsDontHeal/BrokenLimbsDontHeal.dll' }
if (!$NativeAssembly) { $NativeAssembly = [IO.Path]::GetFullPath((Join-Path $repo '../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) }
foreach ($inputFile in @($PublishedManager, $PublishedBrokenLimbs, $NativeAssembly)) {
    if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing metadata input: $inputFile" }
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/compat-second-20261020/effects/checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaEffectsCompat.cs', 'ArenaEffectsContract.cs', 'ArenaEffectsScope.cs', 'ArenaSkills.cs')
$original = @{}
foreach ($name in $files) {
    $dir = if ($name -eq 'ArenaSkills.cs') { 'Combat' } else { 'Compat' }
    $original[$name] = [IO.File]::ReadAllText((Join-Path $repo "ArenaMode.Client/$dir/$name"))
}
function Check([string]$Name, [string]$File = '', [string]$Find = '', [string]$Replace = '', [string]$Failure = '') {
    $copy = Join-Path $evidence $Name
    [IO.Directory]::CreateDirectory($copy) | Out-Null
    foreach ($sourceName in $files) {
        $content = $original[$sourceName]
        if ($sourceName -eq $File) {
            if ([regex]::Matches($content, [regex]::Escape($Find)).Count -ne 1) { throw "Ambiguous mutation: $Name" }
            $content = $content.Replace($Find, $Replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $sourceName), $content)
    }
    $log = Join-Path $copy 'result.log'
    & dotnet run --project (Join-Path $PSScriptRoot 'EffectsCompatChecks.csproj') -c "Effects_${run}_$Name" "-p:ProductionRoot=$copy" "-p:SkillsSource=$copy/ArenaSkills.cs" -- $PublishedManager $PublishedBrokenLimbs $NativeAssembly *> $log
    $exitCode = $LASTEXITCODE; $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exitCode -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Mutation did not compile and fail its expected assertion: $Name ($log)`n$output"
        }
        Write-Output "EFFECTS_MUTANT_REJECTED: $Name"
    } elseif ($exitCode -ne 0 -or $output -notmatch 'EFFECTS_COMPAT_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
Check 'before'
Check 'old-callbacks' 'ArenaEffectsContract.cs' 'Detach.Invoke(null, null);' '' 'living replacement bound'
Check 'global-skill-clear' 'ArenaEffectsContract.cs' 'pool.Remove(controller);' 'pool.Clear();' 'unrelated controller bonuses preserved'
Check 'skill-subtraction-lost' 'ArenaEffectsContract.cs' 'int expected = checked(skill.Buff - (int)bonuses[key]);' 'int expected = skill.Buff;' 'old temporary skill bonus removed exactly'
Check 'global-effect-clear' 'ArenaEffectsContract.cs' 'if (ReferenceEquals(BonusController.GetValue(effects[key]), controller)) effects.Remove(key);' 'effects.Remove(key);' 'unrelated controller bonuses preserved'
Check 'old-speed-limit' 'ArenaEffectsContract.cs' 'if (context != null) SpeedReset.Invoke(speed, null);' '' 'old speed limit removed and new effect evaluated'
Check 'existing-effects' 'ArenaEffectsContract.cs' 'Strength.Invoke(null, new object[] { effect });' '' 'new existing effects initialized'
Check 'registration-loss' 'ArenaEffectsContract.cs' 'Attach.Invoke(null, new object[] { player });' 'Attach.Invoke(null, new object[] { player }); ((IDictionary)Registrations.GetValue(null)).Clear();' 'global registrations and providers preserved'
Check 'ordinary-capture' 'ArenaEffectsScope.cs' '=> exactRaid && local && sameWorld' '=> local && sameWorld' 'ordinary body replacement untouched'
Check 'wrong-local' 'ArenaEffectsScope.cs' '&& authoritativeWorld && authoritativeGame && alive;' '&& authoritativeWorld && alive;' 'mismatched local not rebound'
Check 'wrong-profile' 'ArenaEffectsScope.cs' 'local && sameWorld && sameProfile' 'local && sameWorld' 'mismatched profile not rebound'
Check 'wrong-world' 'ArenaEffectsScope.cs' 'local && sameWorld && sameProfile' 'local && sameProfile' 'mismatched world not rebound'
Check 'unconfirmed-arm' 'ArenaEffectsScope.cs' 'Protocol.HasNativeEndIdentity(nativeId, token)' 'true' 'unconfirmed arm does not capture'
Check 'duplicate-retired' 'ArenaEffectsScope.cs' 'old != null && !old.Retired && !ReferenceEquals(previous, next)' 'old != null && !ReferenceEquals(previous, next)' 'retired duplicate event does not fault'
Check 'initial-kit-observer' 'ArenaEffectsCompat.cs' 'if (old == null && previous != null && previous.IsYourPlayer && Exact(game, world)' 'if (false && previous != null && previous.IsYourPlayer && Exact(game, world)' 'initial kit replacement before observed start captured'
Check 'pending-create' 'ArenaEffectsCompat.cs' 'if (RespawnApi.IsReplacing || !Current(next, game, world)) return;' 'if (!Current(next, game, world)) return;' 'pending creation not rebound'
Check 'ordinary-skill-lookup' 'ArenaEffectsCompat.cs' 'return owned ? _contract.SkillBonusFor(body, skill) : 0;' 'return _contract.SkillBonusFor(body, skill);' 'ordinary skill ledger never claimed'
Check 'lost-cleanup-lease' 'ArenaEffectsCompat.cs' 'bool owned = lease != null && !lease.Retired' 'bool owned = false && lease != null && !lease.Retired' 'skill cleanup lookup survives cleared Arena flags'
Check 'stale-skill-baseline' 'ArenaSkills.cs' 'BuffSnapshot[skill] = skill.Buff - effectsBonus;' 'BuffSnapshot[skill] = skill.Buff;' 'actual Arena restore retains only live Effects contribution'
Check 'lost-live-skill-bonus' 'ArenaSkills.cs' 'skill.Buff = BuffSnapshot[skill] + Compat.ArenaEffectsCompat.SkillBonusFor(skill);' 'skill.Buff = BuffSnapshot[skill];' 'actual Arena restore retains only live Effects contribution'
Check 'native-receipt' 'ArenaEffectsContract.cs' '(prefix ? info?.Prefixes : info?.Postfixes)?.Count(p => p.PatchMethod == patch) != 1' 'false' 'missing foreign lifecycle receipt rejected'
Check 'written-skill-retried' 'ArenaEffectsContract.cs' 'if (skill.Buff == expected) bonuses.Remove(key);' '' 'failed write ledger follows actual native field state'
Check 'after'
foreach ($name in $files) {
    $dir = if ($name -eq 'ArenaSkills.cs') { 'Combat' } else { 'Compat' }
    if ([IO.File]::ReadAllText((Join-Path $repo "ArenaMode.Client/$dir/$name")) -ne $original[$name]) { throw "Production changed during verification: $name" }
}
Write-Output "EFFECTS_COMPAT_CHECKS_OK; 21 compiled negative controls; evidence: $evidence"
