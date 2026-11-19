[CmdletBinding()]
param([string]$Database)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# The runner sets SPT_DATABASE for every step; run by hand, the installed game is the default.
if (-not $Database) { $Database = if ($env:SPT_DATABASE) { $env:SPT_DATABASE } else { 'D:\SPT41\SPT_Runtime\SPT_Data\database' } }
$Database = [IO.Path]::GetFullPath($Database)
if (-not (Test-Path -LiteralPath (Join-Path $Database 'templates\items.json'))) { throw "No item database under $Database" }
$env:SPT_DATABASE = $Database
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
. (Join-Path $repo 'tools/Assert-ArenaTestRun.ps1')

# The harness links ArenaPresetGuard.cs and LadderGenerator.cs and runs the guard's reason table plus the
# generator over the installed vanilla database (injected broken presets, isolation, numbering, report).
# Every mutant below is a copy of one of the two files with one literal line changed and must fail the check
# it names. There is no mod ProjectReference and no deployment target here.
$guard = Join-Path $repo 'ArenaMode.Server/Services/ArenaPresetGuard.cs'
$generator = Join-Path $repo 'ArenaMode.Server/Services/LadderGenerator.cs'
$faults = @(
    # ---- the guard's rules: each one removed or bent, named by the check of its reason
    @{ Name='cycle-island-accepted'; Path=$guard; Property='GuardSource';
       Find='return "cycle:" + item.Template;'; Replace='break;'; Failure='cycle island rejected' },
    @{ Name='chamber-slot-rejected'; Path=$guard; Property='GuardSource';
       Find='.Concat(props?.Chambers ?? [])'; Replace=''; Failure='chamber slot accepted' },
    @{ Name='required-rule-skips-root'; Path=$guard; Property='GuardSource';
       Find='if (slot.Required == true && slot.Name != null && !filled.Contains((item.Id, slot.Name)))';
       Replace='if (!ReferenceEquals(item, root) && slot.Required == true && slot.Name != null && !filled.Contains((item.Id, slot.Name)))';
       Failure='required slot of root rejected' },
    @{ Name='empty-parent-id-is-a-root'; Path=$guard; Property='GuardSource';
       Find='if (item.ParentId != null)'; Replace='if (!string.IsNullOrEmpty(item.ParentId))'; Failure='empty parent id is not a root' },
    @{ Name='duplicate-slot-rule-added'; Path=$guard; Property='GuardSource';
       Find='filled.Add((parentOf[item.Id], item.SlotId!));';
       Replace='if (!filled.Add((parentOf[item.Id], item.SlotId!))) return "duplicate-slot";';
       Failure='repeated stack slot accepted' },
    @{ Name='parent-id-compared-as-text'; Path=$guard; Property='GuardSource';
       Find='var parent = new MongoId(item.ParentId!);';
       Replace='if (item.ParentId!.Any(char.IsUpper)) return "orphan:" + item.Template; var parent = new MongoId(item.ParentId!);';
       Failure='upper-case parent id accepted' },
    @{ Name='encyclopedia-rule-inverted'; Path=$guard; Property='GuardSource';
       Find='encyclopedia != root.Template'; Replace='encyclopedia == root.Template'; Failure='root mismatch rejected' },
    @{ Name='slot-existence-not-checked'; Path=$guard; Property='GuardSource';
       Find='if (!names.Contains(item.SlotId!))'; Replace='if (false)'; Failure='missing slot rejected' },
    @{ Name='unknown-template-not-checked'; Path=$guard; Property='GuardSource';
       Find='if (!templates.ContainsKey(item.Template))'; Replace='if (false)'; Failure='unknown template rejected' },
    @{ Name='orphan-not-checked'; Path=$guard; Property='GuardSource';
       Find='if (!ids.ContainsKey(parent))'; Replace='if (false)'; Failure='orphan rejected' },
    @{ Name='null-element-not-checked'; Path=$guard; Property='GuardSource';
       Find=' || items.Any(i => i is null)'; Replace=''; Failure='null element rejected' },
    @{ Name='duplicate-id-not-checked'; Path=$guard; Property='GuardSource';
       Find='if (!ids.TryAdd(item.Id, item))'; Replace='ids[item.Id] = item; if (false)'; Failure='duplicate id rejected' },
    @{ Name='bad-parent-id-not-checked'; Path=$guard; Property='GuardSource';
       Find='if (!MongoId.IsValidMongoId(item.ParentId!))'; Replace='if (false)'; Failure='bad parent id rejected' },
    @{ Name='missing-slot-id-not-checked'; Path=$guard; Property='GuardSource';
       Find='if (item.SlotId == null)'; Replace='if (false)'; Failure='child without slot rejected' },
    # ---- the generator's wiring
    @{ Name='guard-not-called'; Path=$generator; Property='GeneratorSource';
       Find='if (GuardPresets && ArenaPresetGuard.Problem(preset, _items) is { } problem)';
       Replace='if (false && ArenaPresetGuard.Problem(preset, _items) is { } problem)';
       Failure='broken default preset costs nothing with the guard' },
    @{ Name='guard-after-plainer'; Path=$generator; Property='GeneratorSource';
       Find=@('if (GuardPresets && ArenaPresetGuard.Problem(preset, _items) is { } problem)', 'var candidates = new List<Candidate>();');
       Replace=@('if (false && ArenaPresetGuard.Problem(preset, _items) is { } problem)', 'foreach (var (late, kept) in presetsByWeapon.ToList()) { if (ArenaPresetGuard.Problem(kept, _items) != null) presetsByWeapon.Remove(late); } var candidates = new List<Candidate>();');
       Failure='broken default preset costs nothing with the guard' },
    @{ Name='consider-not-isolated'; Path=$generator; Property='GeneratorSource';
       Find='$"consider-threw:{ex.GetType().Name}: {ex.Message}"));'; Replace='"x")); throw;'; Failure='consider isolation keeps the pool' },
    @{ Name='build-not-isolated'; Path=$generator; Property='GeneratorSource';
       Find='$"build-threw:{ex.GetType().Name}: {ex.Message}"));'; Replace='"x")); throw;'; Failure='build isolation keeps the pool' },
    @{ Name='preset-loop-not-isolated'; Path=$generator; Property='GeneratorSource';
       Find='$"preset-threw:{ex.GetType().Name}: {ex.Message}"));'; Replace='"x")); throw;'; Failure='null elements contained and only a weapon reported' },
    @{ Name='non-weapon-failure-reported'; Path=$generator; Property='GeneratorSource';
       Find='if (weapon != null)'; Replace='if (true)'; Failure='null elements contained and only a weapon reported' },
    @{ Name='numbering-follows-candidate-index'; Path=$generator; Property='GeneratorSource';
       Find='BuildEntry(pool.Count, candidate)'; Replace='BuildEntry(candidates.IndexOf(candidate), candidate)'; Failure='numbering stays dense after a skipped candidate' },
    @{ Name='report-not-isolated'; Path=$generator; Property='GeneratorSource';
       Find='logger.Debug($"[ArenaMode] Weapon pool: the left-out report failed ({ex.Message}); the pool itself is unaffected.");';
       Replace='throw;'; Failure='failing report keeps the pool' },
    @{ Name='warning-without-left-out-presets'; Path=$generator; Property='GeneratorSource';
       Find='if (_leftOut.Count == 0)'; Replace='if (_leftOut.Count == -1)'; Failure='no Warning when nothing is left out' }
)

Push-Location $repo
try {
    Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'WeaponPoolGuardChecks.csproj') `
        -Token 'WEAPON_POOL_GUARD_PRODUCTION_PASSED' -Properties @() -Faults $faults -RequireSingleSite

    # The xunit side of the same feature (reason table, vanilla proof, golden-equivalent identity, injections).
    # The data tests skip themselves without a database: Assert-ArenaTestRun tolerates no such skip, and the
    # three required families must have PASSED, so a run that did not look at the data cannot pass here. The
    # test project builds the server, whose deploy target writes the live user/mods unless the no-deploy targets
    # and a sentinel that must not exist are passed.
    $results = Join-Path $repo ('artifacts/weapon-pool-guard-checks/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $sentinel = Join-Path $repo 'artifacts/weapon-pool-guard-must-not-deploy'
    if (Test-Path -LiteralPath $sentinel) { throw 'The no-deploy sentinel must not exist.' }
    dotnet test (Join-Path $repo 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --nologo -v:minimal `
        --filter 'FullyQualifiedName~ArenaPresetGuardTests|FullyQualifiedName~LadderGeneratorTests' `
        "-p:CustomAfterMicrosoftCommonTargets=$repo/tools/AirPitChecks/NoDeploy.targets" "-p:SPTUserMods=$sentinel" `
        --logger 'trx;LogFileName=weapon-pool-guard.trx' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw "Preset guard xunit tests failed (exit $LASTEXITCODE)." }
    $run = Assert-ArenaTestRun (Join-Path $results 'weapon-pool-guard.trx') -RequireFamilies `
        'ProblemNamesTheDefectOrNothing', 'GuardRejectsNoVanillaWeaponPreset', 'GuardDoesNotChangeTheVanillaPool',
        'OneBrokenPresetCostsOnlyItself', 'AFailingReportNeverDiscardsAGoodPool', 'NullElementsAreContainedAndOnlyAWeaponIsReported'
    "WEAPON_POOL_GUARD_XUNIT total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)"
    'WEAPON_POOL_GUARD_CHECKS_PASSED'
}
finally { Pop-Location }
