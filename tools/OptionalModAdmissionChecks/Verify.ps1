param([switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'OptionalModAdmissionChecks.csproj'
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production optional-mod admission checks failed.' }
if ($SkipMutants) { return }

$source = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client\Compat\ArenaOptionalMods.cs'))
$mutants = @(
    @{
        Name = 'skip-whitelist-admission'
        Before = 'if (whitelistFailure != null) return whitelistFailure;'
        After = ''
    },
    @{
        Name = 'whitelist-behind-server-rejection'
        Before = 'var whitelistFailure = ArenaWhitelistAdmission.GetRejectionReason();'
        After = 'var whitelistFailure = ArenaServerClient.ServerAdmissionFailure != null ? null : ArenaWhitelistAdmission.GetRejectionReason();'
    },
    @{
        Name = 'ignore-server-only-rejection'
        Before = 'if (ArenaServerClient.ServerAdmissionFailure != null)'
        After = 'if (false)'
    },
    @{
        Name = 'server-rejection-behind-adapter'
        Before = 'return ArenaServerClient.ServerAdmissionFailure;'
        After = 'return BodyReplacementCompatibility.GetRejectionReason() ?? ArenaServerClient.ServerAdmissionFailure;'
    },
    @{
        Name = 'skip-shared-core-contract'
        Before = 'return BodyReplacementCompatibility.GetRejectionReason();'
        After = 'return (string)null;'
    },
    # Negative controls for the 2026-10-22 decision: a code-level refusal of a named mod, reintroduced next to
    # the admission chain, must fail on its own assertion.
    @{
        Name = 'refuse-orbit-by-code'
        Before = 'if (ArenaServerClient.ServerAdmissionFailure != null)'
        After = 'if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.chazut.orbit")) return "Arena is currently unavailable with ORBIT."; if (ArenaServerClient.ServerAdmissionFailure != null)'
    },
    @{
        Name = 'refuse-isb-by-code'
        Before = 'if (ArenaServerClient.ServerAdmissionFailure != null)'
        After = 'if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.samc137.aishi")) return "Arena is currently unavailable with ISB Aishi."; if (ArenaServerClient.ServerAdmissionFailure != null)'
    }
)
$evidence = Join-Path $repo ('artifacts\optional-mod-admission-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
foreach ($mutant in $mutants) {
    $matches = ([regex]::Matches($source, [regex]::Escape($mutant.Before))).Count
    if ($matches -ne 1) { throw "Mutation '$($mutant.Name)' expected exactly one production seam; got $matches." }
    $folder = Join-Path $evidence $mutant.Name
    New-Item -ItemType Directory -Path $folder | Out-Null
    foreach ($name in @('OptionalModAdmissionChecks.csproj', 'Program.cs', 'StandIns.cs')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $folder $name)
    }
    # Keep the mutant outside the project glob; only the linked production include owns it.
    $mutatedSource = Join-Path $evidence ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($mutatedSource, $source.Replace($mutant.Before, $mutant.After), [Text.UTF8Encoding]::new($false))
    $output = & dotnet run --project (Join-Path $folder 'OptionalModAdmissionChecks.csproj') -c Release "-p:ProductionAdmissionSource=$mutatedSource" 2>&1
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $folder 'result.log') -Encoding utf8
    if ($exitCode -eq 0 -or ($output -join "`n") -notmatch 'OPTIONAL_MOD_ADMISSION_CHECKS_FAILED') {
        throw "Mutation '$($mutant.Name)' was not rejected by a behavioral assertion; see $folder."
    }
    Write-Host "OPTIONAL_MOD_ADMISSION_MUTANT_REJECTED: $($mutant.Name)"
}
Write-Host "OPTIONAL_MOD_ADMISSION_VERIFY_PASSED: production plus $($mutants.Count) behavioral negative controls; $evidence"
