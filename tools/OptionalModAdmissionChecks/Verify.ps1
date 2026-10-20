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
        Name = 'skip-recoil-legacy-contract'
        Before = '?? ArenaRecoilLegacyCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-skills-extended-contract'
        Before = '?? ArenaSkillsExtendedCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'allow-isb-client'
        Before = 'if (isbFailure != null) return isbFailure;'
        After = ''
    },
    @{
        Name = 'ignore-server-only-rejection'
        Before = 'if (ArenaServerClient.ServerAdmissionFailure != null)'
        After = 'if (false)'
    },
    @{
        Name = 'allow-adaptive-bot-culling'
        Before = 'if (Chainloader.PluginInfos.ContainsKey(AdaptiveBotCullingGuid))'
        After = 'if (false)'
    },
    @{
        Name = 'skip-continuous-load-ammo-contract'
        Before = '?? ArenaContinuousLoadAmmoCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-deminvincibility-contract'
        Before = '?? ArenaDeminvincibilityCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-classic-movement-contract'
        Before = '?? ArenaClassicMovementCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-stattrack-contract'
        Before = '?? ArenaStatTrackCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-continuous-healing-contract'
        Before = '?? ArenaContinuousHealingCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-late-to-the-party-contract'
        Before = '?? ArenaLateToThePartyCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-looting-bots-contract'
        Before = '?? ArenaLootingBotsCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-remove-the-dead-contract'
        Before = '?? ArenaRemoveTheDeadCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'skip-smoothie-contract'
        Before = '?? ArenaSmoothieCompat.GetRejectionReason()'
        After = ''
    },
    @{
        Name = 'allow-orbit'
        Before = 'if (Chainloader.PluginInfos.ContainsKey(OrbitGuid))'
        After = 'if (false)'
    },
    @{
        Name = 'allow-active-whole-mod'
        Before = 'if (enabled?.PropertyType != typeof(bool) || (bool)enabled.GetValue(config)) return false;'
        After = 'if (enabled?.PropertyType == typeof(bool) && (bool)enabled.GetValue(config)) return true;'
    },
    @{
        Name = 'ignore-installed-gameplay-patches'
        Before = '&& p.PatchMethod.DeclaringType.FullName != "QuestingBots.Patches.MenuShowPatch"'
        After = '&& false'
    },
    @{
        Name = 'forget-sticky-activation'
        Before = 'return !_questingBlocked;'
        After = 'return _questingEnabled != null && !(bool)_questingEnabled.GetValue(_questingConfig);'
    },
    @{
        Name = 'invoke-network-getter'
        Before = 'configType.GetField("_currentConfig", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)'
        After = 'configType.GetProperty("CurrentConfig", BindingFlags.Instance | BindingFlags.Public)?.GetValue(owner)'
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
    # The shared production policy stays linked when the harness lives in a
    # scratch directory several levels away from the source repository.
    $projectText = [IO.File]::ReadAllText((Join-Path $folder 'OptionalModAdmissionChecks.csproj'))
    $projectText = $projectText.Replace('../../ArenaMode.Shared/ArenaIsbAdmission.cs',
        (Join-Path $repo 'ArenaMode.Shared\ArenaIsbAdmission.cs'))
    [IO.File]::WriteAllText((Join-Path $folder 'OptionalModAdmissionChecks.csproj'), $projectText)
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
