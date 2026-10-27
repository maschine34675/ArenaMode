$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaSessionVersionChecks.csproj'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaSessionVersionChecks-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$sources = @{
    AdmissionSource = Join-Path $repo 'ArenaMode.Client\Session\ArenaWhitelistAdmission.cs'
    DecisionSource = Join-Path $repo 'ArenaMode.Shared\Session\ArenaWhitelistDecision.cs'
    ContractSource = Join-Path $repo 'ArenaMode.Shared\Session\ArenaSessionContract.cs'
    LogicSource = Join-Path $repo 'ArenaMode.Session\SessionLogic.cs'
    ModeSource = Join-Path $repo 'ArenaMode.Client\Session\ArenaSessionMode.cs'
}
$hashes = @{}
foreach ($path in $sources.Values) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }

function Run-Harness([string]$name, [string]$property, [string]$copy) {
    $buildArgs = @('run', '--project', $project, '-c', 'Release', '--disable-build-servers', '-p:UseSharedCompilation=false')
    if ($property) { $buildArgs += "-p:${property}=$copy" }
    $buildArgs += "-p:OutputPath=$(Join-Path $scratch ($name + '-bin\'))"
    $buildArgs += "-p:IntermediateOutputPath=$(Join-Path $scratch ($name + '-obj\'))"
    $result = & dotnet @buildArgs 2>&1
    return @{ Exit = $LASTEXITCODE; Text = ($result -join "`n") }
}

function Mutant([string]$name, [string]$property, [string]$from, [string]$to, [string]$assertion) {
    $source = [IO.File]::ReadAllText($sources[$property]).Replace("`r`n", "`n")
    if (($source.Split([string[]]@($from), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Non-unique mutation site: $name" }
    $copy = Join-Path $scratch ($name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($from, $to))
    $result = Run-Harness $name $property $copy
    if ($result.Exit -eq 0 -or $result.Text -match 'error CS' -or $result.Text -notlike "*ASSERT: $assertion*") {
        throw "$name did not fail its named behavioral assertion; evidence $scratch`n$($result.Text)"
    }
    Write-Output "Rejected mutant: $name"
}

$baseline = Run-Harness 'baseline' '' ''
if ($baseline.Exit -ne 0 -or $baseline.Text -notmatch 'ARENA_SESSION_VERSION_BASELINE_PASSED') { throw $baseline.Text }
Write-Output $baseline.Text
Mutant 'duplicate-version-notes' 'AdmissionSource' 'if (!identities.Add(identity)) continue;' 'identities.Add(identity);' 'client/server alias dedup'
Mutant 'collapse-distinct-guids' 'DecisionSource' '(Guid ?? string.Empty).Trim().ToLowerInvariant() + "|" + VersionIdentity(Installed)' 'VersionIdentity(Installed)' 'distinct GUIDs retained'
Mutant 'restore-automatic-version-filter' 'AdmissionSource' 'policyHash = outcome.Policy.Hash;' 'versionPlan = ArenaSessionVersionPlan.Create(outcome.Policy, ClientUnits(), ServerUnits(ArenaServerClient.ServerMods)); policyHash = versionPlan.PolicyHash;' 'client version drift retains matching server counterpart and ordinary policy hash'
Mutant 'drop-drifted-server-mod' 'AdmissionSource' 'serverMods = new List<ArenaSessionUnit>(outcome.AllowedServerMods);' 'serverMods = outcome.AllowedServerMods.FindAll(unit => !ArenaWhitelistPolicy.VersionDiffers(unit.Version, outcome.Policy.ReviewedVersion(unit.Guid, ArenaWhitelistSide.Server)));' 'private server retains drifted UIFixes and dependent while excluding unknown mod'
Mutant 'drop-server-drift' 'ContractSource' 'AddDrift(policy, ArenaWhitelistSide.Server, plan.ServerUnits, omitted);' '' 'server-only drift excludes both sides'
Mutant 'drop-dependant-closure' 'ContractSource' 'changed = AddDependants(policy, plan.ClientUnits, omitted);' 'changed = false;' 'client drift and cross-side dependant closure'
Mutant 'accept-changed-policy' 'ContractSource' 'if (!string.Equals(BasePolicyHash, policy.Hash, StringComparison.Ordinal))' 'if (false)' 'tampered base policy identity refused'
Mutant 'accept-omission-tamper' 'ContractSource' '|| string.Join("|", ExcludedGuids.ToArray()) != string.Join("|", rebuilt.ExcludedGuids.ToArray())' '' 'changed omissions rejected'
Mutant 'accept-missing-server-hash' 'AdmissionSource' '|| !string.Equals(versionPlan.PolicyHash, serverHash, StringComparison.Ordinal)' '' 'strict session requires server hash'
Mutant 'old-gate-accepted' 'ModeSource' '(_versionPlanHash != null || parsed.VersionPlan != null)' 'false' 'old gate cannot admit strict session'
foreach ($path in $sources.Values) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]) { throw "Production input changed during verification: $path" }
}
Write-Output 'ARENA_SESSION_VERSION_CHECKS_PASSED (10 rejected mutants; linked production behavior)'
