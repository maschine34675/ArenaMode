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
Invoke-CaptionMutant 'deploying-ungated' 'patch' `
    '(ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid)' 'true' `
    'deploying caption is gated on the Arena flow'
# The next two keep every substring the old pin looked for and break only the
# whole condition: a negated Arena group would show ARENA ZONE on every ordinary
# raid's loading screen and never in Arena; `||` ungates every non-reconnect screen.
Invoke-CaptionMutant 'deploying-gate-negated' 'patch' `
    '(ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid)' `
    '!(ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid)' `
    'deploying caption is gated on the Arena flow'
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
Write-Output "ARENA_SESSION_CAPTION_VERIFIED (21 mutants rejected: pure rule x2, location/accept/deploying gates incl. negated and operator-flipped conditions, reconnect, direct write, detached ledger, six set/restore ledger faults, side toggle x2, plugin wiring; evidence: $scratch)"
