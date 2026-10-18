$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Menu polish 2026-10-08, item 6: "PVE ZONE" -> "ARENA ZONE" on the Arena setup
# screens. The pure rule and the set/restore ledger are executed by the harness
# and mutated through ProductionSessionModeCaptionSource; the Harmony patches are
# Unity-bound, so their source texts are mutated through the ARENA_CAPTION_*
# variables CaptionChecks.cs reads. Every mutant must be rejected by its named
# assertion; production sources are hash-checked afterwards.
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'NativeMenuChecks.csproj'
$paths = [ordered]@{
    helper = Join-Path $repoRoot 'ArenaMode.Client\Session\ArenaSessionModeCaption.cs'
    patch  = Join-Path $repoRoot 'ArenaMode.Client\Session\ArenaSessionModeCaptionPatch.cs'
    side   = Join-Path $repoRoot 'ArenaMode.Client\Session\ArenaSideSelectionPatch.cs'
    plugin = Join-Path $repoRoot 'ArenaMode.Client\Plugin.cs'
}
$sources = @{}
$hashes = @{}
foreach ($key in $paths.Keys) {
    $sources[$key] = [System.IO.File]::ReadAllText($paths[$key]).Replace("`r`n", "`n")
    $hashes[$key] = (Get-FileHash -LiteralPath $paths[$key] -Algorithm SHA256).Hash
}
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaCaptionMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null
$env:ARENA_CAPTION_REPO_ROOT = $repoRoot

# $kind: helper (MSBuild property) | patch | side | plugin (environment variable)
function Invoke-CaptionMutant([string]$name, [string]$kind, [string]$from, [string]$to, [string]$expected, [switch]$all) {
    $source = $sources[$kind]
    $count = ($source.Split([string[]]@($from), [System.StringSplitOptions]::None)).Count - 1
    if ($all) { if ($count -lt 1) { throw "Missing mutation anchor: $name" } }
    elseif ($count -ne 1) { throw "Expected exactly one mutation site for $name; found $count." }
    $copy = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copy, $source.Replace($from, $to))
    $arguments = @('run', '--project', $project, '-c', 'Release',
        "-p:OutputPath=$(Join-Path $scratch ($name + '-bin\'))",
        "-p:IntermediateOutputPath=$(Join-Path $scratch ($name + '-obj\'))")
    $variable = switch ($kind) {
        'patch' { 'ARENA_CAPTION_PATCH_SOURCE' }
        'side' { 'ARENA_CAPTION_SIDE_SOURCE' }
        'plugin' { 'ARENA_CAPTION_PLUGIN_SOURCE' }
        default { $null }
    }
    if ($kind -eq 'helper') { $arguments += "-p:ProductionSessionModeCaptionSource=$copy" }
    else { Set-Item -Path ('Env:' + $variable) -Value $copy }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet @arguments 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
        if ($variable) { Remove-Item -Path ('Env:' + $variable) -ErrorAction SilentlyContinue }
    }
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    if ($exit -eq 0 -or $text -match 'error CS' -or $text -notmatch [regex]::Escape($expected)) {
        throw "$name did not fail its intended caption assertion (exit $exit). Evidence: $scratch`n$text"
    }
    Write-Output "Rejected mutant: $name"
}

Invoke-CaptionMutant 'resolve-never-arena' 'helper' `
    'arenaSelected ? ArenaText : nativeText' 'nativeText' `
    'Resolve returns the Arena caption while Arena is selected'
Invoke-CaptionMutant 'resolve-always-arena' 'helper' `
    'arenaSelected ? ArenaText : nativeText' 'ArenaText' `
    'Resolve leaves the native text unchanged outside Arena'
Invoke-CaptionMutant 'location-ungated' 'patch' `
    'if (ArenaMenuSelection.IsSelectedFor(raidSettings))' 'if (true)' `
    'location caption is gated on the exact Arena RaidSettings'
Invoke-CaptionMutant 'location-negated' 'patch' `
    'if (ArenaMenuSelection.IsSelectedFor(raidSettings))' 'if (!ArenaMenuSelection.IsSelectedFor(raidSettings))' `
    'location caption is gated on the exact Arena RaidSettings'
Invoke-CaptionMutant 'accept-ungated' 'patch' `
    'if (controller != null && ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))' 'if (controller != null)' `
    'accept caption is gated on the exact Arena RaidSettings'
Invoke-CaptionMutant 'accept-negated' 'patch' `
    'if (controller != null && ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))' `
    'if (controller != null && !ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))' `
    'accept caption is gated on the exact Arena RaidSettings'
Invoke-CaptionMutant 'deploying-reconnect' 'patch' `
    '!controller.IsReconnecting' 'true' `
    'deploying caption excludes a reconnect to an ordinary raid'
# The deploying gate is `!IsReconnecting && IsSelectedFor(...)` on two lines; the
# anchor below is its second line, which only the deploying patch has (it follows
# the line-ending `&&`).
$deployingGate = "&&`n                    ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))"
Invoke-CaptionMutant 'deploying-ungated' 'patch' `
    $deployingGate "&&`n                    true)" `
    'deploying caption is gated on the Arena flow'
# The next ones keep every substring the old pin looked for and break only the
# whole condition: a negated Arena group would show ARENA ZONE on every ordinary
# raid's loading screen and never in Arena; `||` ungates every non-reconnect screen.
Invoke-CaptionMutant 'deploying-gate-negated' 'patch' `
    $deployingGate "&&`n                    !ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))" `
    'deploying caption is gated on the Arena flow'
# Round 2 (R2-L5): the arm flag is set AFTER the deploying screen is shown, so it is
# only ever stale there. Re-adding it as a fallback, or reading it anywhere in the
# deploying patch, must fail.
Invoke-CaptionMutant 'deploying-arm-flag-fallback' 'patch' `
    $deployingGate "&&`n                    (ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid))" `
    'deploying caption is gated on the Arena flow'
Invoke-CaptionMutant 'deploying-arm-flag-read' 'patch' `
    'Plugin.ModLogger?.LogWarning($"Arena deploying caption left native: {ex.Message}");' `
    'Plugin.ModLogger?.LogWarning($"Arena deploying caption left native: {ex.Message}"); bool stale = ArenaSession.ArmedForNextRaid;' `
    'the deploying caption never trusts the arm flag'
Invoke-CaptionMutant 'deploying-gate-or' 'patch' `
    '!controller.IsReconnecting &&' '!controller.IsReconnecting ||' `
    'deploying caption is gated on the Arena flow'
Invoke-CaptionMutant 'direct-write' 'patch' `
    'Plugin.ModLogger?.LogWarning($"Arena location caption left native: {ex.Message}");' `
    'Plugin.ModLogger?.LogWarning($"Arena location caption left native: {ex.Message}"); __instance._subCaption.text = "ARENA ZONE";' `
    'the screens never write the caption directly; only Apply does, and only once'
Invoke-CaptionMutant 'apply-detached-ledger' 'patch' `
    'Ledger.Apply(caption, arena,' 'new ArenaSessionModeCaptionLedger<TextMeshProUGUI>().Apply(caption, arena,' `
    'every caption goes through the one static ledger'

# The set/restore state machine is executed with a stand-in label (CheckCaptionLedger),
# so its mutants are mutations of the compiled helper, not of a source-text pin.
Invoke-CaptionMutant 'ledger-branch-flipped' 'helper' `
    'if (arena)' 'if (!arena)' `
    'an ordinary flow''s caption is never touched'
Invoke-CaptionMutant 'ledger-already-arena-rewrites' 'helper' `
    'if (current == ArenaSessionModeCaption.ArenaText)' 'if (false)' `
    'a second Arena apply writes nothing and keeps the remembered native text'
Invoke-CaptionMutant 'ledger-forgets-native' 'helper' `
    '_native.Add(label, current);' '' `
    'switching Arena off restores exactly the native text EFT wrote'
Invoke-CaptionMutant 'ledger-restore-unconditional' 'helper' `
    'if (read(label) == ArenaSessionModeCaption.ArenaText)' 'if (true)' `
    'a fresh native rewrite is never clobbered by the restore'
Invoke-CaptionMutant 'ledger-never-forgets' 'helper' `
    "_native.Remove(label);`n            // A fresh native Show" '// A fresh native Show' `
    'a restore forgets the remembered text, so it cannot be applied twice'
Invoke-CaptionMutant 'ledger-keeps-stale-slot' 'helper' `
    "_native.Remove(label);`n            _native.Add(label, current);" '_native.Add(label, current);' `
    'arming again after a native rewrite completes and shows ARENA ZONE'

Invoke-CaptionMutant 'side-toggle-ignored' 'side' `
    'ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);' `
    'ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, true);' `
    'side screen caption follows the ARENA toggle state in Show and SetSelectedSide' -all
Invoke-CaptionMutant 'side-toggle-negated' 'side' `
    'ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);' `
    'ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, !ArenaMenuSelection.IsSelected);' `
    'side screen caption follows the ARENA toggle state in Show and SetSelectedSide' -all
Invoke-CaptionMutant 'plugin-not-enabled' 'plugin' `
    'ArenaSessionModeCaptionPatch.Enable();' '' `
    'Plugin enables the session caption patches'

# Round 2 (R2-L1): every source-text pin reads the sources with comments removed, so
# a COMMENTED-OUT statement must fail the pin of the statement it disables. The
# earlier mutants delete or replace text and never comment it out.
Invoke-CaptionMutant 'plugin-enable-commented-out' 'plugin' `
    'ArenaSessionModeCaptionPatch.Enable();' '// ArenaSessionModeCaptionPatch.Enable();' `
    'Plugin enables the session caption patches'
foreach ($patchName in @('Location', 'Accept', 'Deploying')) {
    Invoke-CaptionMutant ('enable-' + $patchName.ToLowerInvariant() + '-commented-out') 'patch' `
        "new ArenaSessionModeCaption${patchName}Patch().Enable();" "// new ArenaSessionModeCaption${patchName}Patch().Enable();" `
        'all three caption patches are enabled'
}

# Round 2 (R2-L3 / R2-T3): Apply is the cosmetic call made INSIDE the side screen's
# own postfixes (ArenaSideSelectionShowPatch.Postfix's catch clears the Arena selection; ArenaSideSelectionSelectPatch.Postfix
# has no try), so it must swallow everything. Multi-line anchors: sources are LF-normalised.
$applyLog = 'Plugin.ModLogger?.LogWarning($"Arena session caption left native: {ex.Message}");'
$applyCatch = @('catch (Exception ex)', '        {', ('            ' + $applyLog), '        }') -join "`n"
Invoke-CaptionMutant 'apply-catch-removed' 'patch' `
    $applyCatch (@('finally', '        {', '        }') -join "`n") `
    'Apply holds its ledger call in a try with a catch (Exception) and never throws'
Invoke-CaptionMutant 'apply-catch-narrowed' 'patch' `
    $applyCatch (@('catch (InvalidOperationException ex)', '        {', ('            ' + $applyLog), '        }') -join "`n") `
    'Apply holds its ledger call in a try with a catch (Exception) and never throws'
Invoke-CaptionMutant 'apply-ledger-outside-try' 'patch' `
    (@('            Ledger.Apply(caption, arena, label => label.text, (label, text) => label.text = text);', '        }', ('        ' + $applyCatch)) -join "`n") `
    (@('        }', ('        ' + $applyCatch), '        Ledger.Apply(caption, arena, label => label.text, (label, text) => label.text = text);') -join "`n") `
    'Apply holds its ledger call in a try with a catch (Exception) and never throws'
Invoke-CaptionMutant 'apply-catch-rethrows' 'patch' `
    $applyLog 'throw;' `
    'Apply never rethrows what its catch swallowed'

# Round 2 (R2-L1 / R2-T4): the side-screen caption sync. Commented out, it must fail
# the presence pin; wrapped in a guard it keeps every substring but drops the restore
# half, so it must fail the unguarded-statement pin. The 12-space anchor is the
# SetSelectedSide postfix, the 16-space anchor the Show postfix (neither contains the other).
$syncCall = 'ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);'
$selectSync = "`n            " + $syncCall
$showSync = "`n                " + $syncCall
Invoke-CaptionMutant 'side-select-sync-commented-out' 'side' `
    $selectSync ("`n            // " + $syncCall) `
    'side screen caption follows the ARENA toggle state in Show and SetSelectedSide'
Invoke-CaptionMutant 'side-show-sync-commented-out' 'side' `
    $showSync ("`n                // " + $syncCall) `
    'side screen caption follows the ARENA toggle state in Show and SetSelectedSide'
Invoke-CaptionMutant 'side-select-sync-guarded' 'side' `
    $selectSync ("`n            if (ArenaMenuSelection.IsSelected) " + $syncCall) `
    'the side screen caption sync is an unguarded statement'
Invoke-CaptionMutant 'side-show-sync-guarded' 'side' `
    $showSync ("`n                if (ArenaMenuSelection.IsSelected) " + $syncCall) `
    'the side screen caption sync is an unguarded statement'

Remove-Item Env:ARENA_CAPTION_REPO_ROOT -ErrorAction SilentlyContinue
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production native-menu checks failed after caption mutation testing.'
}
foreach ($key in $paths.Keys) {
    if ((Get-FileHash -LiteralPath $paths[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) {
        throw "Mutation verification changed $($paths[$key])"
    }
}
Write-Output "ARENA_SESSION_CAPTION_VERIFIED (35 mutants rejected: pure rule x2, location/accept/deploying gates incl. negated and operator-flipped conditions, reconnect, direct write, detached ledger, six set/restore ledger faults, side toggle x2, plugin wiring; six commented-out Plugin/Enable/side-sync statements, four Apply fail-open faults, two guarded side syncs; two arm-flag faults on the deploying screen; evidence: $scratch)"
