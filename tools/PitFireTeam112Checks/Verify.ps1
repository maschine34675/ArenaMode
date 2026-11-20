param([string]$ReleaseRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ReleaseRoot) { $ReleaseRoot = Join-Path $repo 'artifacts/pft-100-contracts/release-1.1.2' }
$ReleaseRoot = (Resolve-Path -LiteralPath $ReleaseRoot).Path
foreach ($item in @(
    @{ Path='BepInEx/plugins/pitFireTeam/pitFireTeam.dll'; Hash='F0C01D472379ECD3F79D8F799400992093187162E75FB3716A2B6E133891B05D' },
    @{ Path='SPT_Runtime/user/mods/pitFireTeam-ServerMod/pitFireTeam.Server.dll'; Hash='D8E1EC74FD5EC85BF599BBE2886F46F73487623FDA10BE97983E328BC93B5BDC' },
    @{ Path='SPT_Runtime/user/mods/pitFireTeam-ServerMod/LiteDB.dll'; Hash='AE31AC6A93549217B9E8B81497D1EE831658ADE6DF9E1EE20F2838F22DE5E218' }
)) {
    if ((Get-FileHash -LiteralPath (Join-Path $ReleaseRoot $item.Path) -Algorithm SHA256).Hash -ne $item.Hash) {
        throw ('Expected unmodified PitFireTeam 1.1.2 release bytes: ' + $item.Path)
    }
}
Set-Location -LiteralPath $repo
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
$policy = Join-Path $repo 'ArenaMode.Client/Compat/PitFireTeamRoundLifecyclePolicy.cs'
$faults = @(
    @{ Name='explicit-roster-substitution'; Path=$policy; Property='ProductionPolicySource';
       Find='accounts.Any(account => account == null || !frozenRoster.ContainsKey(account))'; Replace='accounts.Any(account => account == null)';
       Failure='1: same-size account substitution rejected' },
    @{ Name='explicit-roster-duplicates'; Path=$policy; Property='ProductionPolicySource';
       Find='accounts.Distinct(StringComparer.Ordinal).Count() != accounts.Length'; Replace='false';
       Failure='2: duplicate accounts rejected' },
    @{ Name='explicit-roster-null-task'; Path=$policy; Property='ProductionPolicySource';
       Find='if (entry.Item2 == null)'; Replace='if (false)'; Failure='1: null cached task rejected' },
    @{ Name='explicit-roster-task-replaced'; Path=$policy; Property='ProductionPolicySource';
       Find='return entry;'; Replace='return (entry.Item1, Task.FromResult(default(T)));';
       Failure='1: explicit-roster cache retains the exact pending task and native account order' }
)
Test-ArenaSourceFaults -Project (Join-Path $repo 'tools/PitFireTeamRoundLifecycleChecks/PitFireTeamRoundLifecycleChecks.csproj') `
    -Token 'PITFIRETEAM_ROUND_LIFECYCLE_CHECKS_PASSED' -Faults $faults -FailurePrefix '' `
    -FailureMarker 'PITFIRETEAM_ROUND_LIFECYCLE_CHECKS_FAILED' -RequireSingleSite

$server = Join-Path $repo 'ArenaMode.Server/Patches/PitFireTeamDynamicPersistencePatches.cs'
$faults = @(
    @{ Name='drop-result-null'; Path=$server; Property='ProductionPersistenceSource';
       Find='resultType.IsArray && resultType.GetArrayRank() == 1'; Replace='false';
       Failure='Protected dropped-companion transaction returns a non-null empty array.' },
    @{ Name='companion-register-unprotected'; Path=$server; Property='ProductionPersistenceSource';
       Find="public static bool ShouldRunOriginal(object[] arguments, MethodBase originalMethod)`n    {";
       Replace="public static bool ShouldRunOriginal(object[] arguments, MethodBase originalMethod)`n    {`n        if (originalMethod.Name == `"RegisterCompanionSpawnProtection`") return true;";
       Failure='Protected registration, raid clearing and drop release preserve the entire companion cache.' },
    @{ Name='companion-begin-unprotected'; Path=$server; Property='ProductionPersistenceSource';
       Find="public static bool ShouldRunOriginal(object[] arguments, MethodBase originalMethod)`n    {";
       Replace="public static bool ShouldRunOriginal(object[] arguments, MethodBase originalMethod)`n    {`n        if (originalMethod.Name == `"BeginCompanionRaid`") return true;";
       Failure='Protected companion raid start does not enter native response parsing.' },
    @{ Name='companion-parameter-drift'; Path=$server; Property='ProductionPersistenceSource';
       Find='contract.ParameterTypeNames == null || method.GetParameters()'; Replace='true || method.GetParameters()';
       Failure='companion-register-parameter must reject the changed transaction.' },
    @{ Name='companion-static-drift'; Path=$server; Property='ProductionPersistenceSource';
       Find='(contract.IsStatic ? BindingFlags.Static : BindingFlags.Instance) | visibility'; Replace='(BindingFlags.Static | BindingFlags.Instance) | visibility';
       Failure='companion-begin-instance must reject the changed transaction.' }
)
Test-ArenaSourceFaults -Project (Join-Path $repo 'tools/PitFireTeam100Checks/PitFireTeam100Checks.csproj') `
    -Token 'PFT_100_CONTRACTS_VERIFIED' -Faults $faults -FailurePrefix '' -Arguments @($ReleaseRoot) -RequireSingleSite

Write-Output 'PFT_112_CHECKS_PASSED (released contracts; nine compiled source faults rejected)'
