#requires -Version 7.0
# Runs the grenade-pick checks against deliberately broken copies of the
# production file; each must fail at the assertion that names its fault. Then
# the unchanged production file must pass. No repository file is replaced.
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaGrenadePickChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Kits\ArenaReachPatches.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaGrenadePickMutation-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$rejected = 0

function Get-Mutant([string] $pattern, [string] $replacement) {
    $found = [regex]::Matches($source, $pattern)
    if ($found.Count -ne 1) {
        throw "Expected exactly one match for /$pattern/, found $($found.Count); refusing an ambiguous mutation."
    }
    return [regex]::Replace($source, $pattern, $replacement)
}

function Invoke-Mutant([string] $name, [string] $mutant, [string] $expectedFailure) {
    $copy = Join-Path $scratch ($name + '.cs')
    [IO.File]::WriteAllText($copy, $mutant)
    $output = & dotnet run --project $project -c Release `
        "-p:ProductionReachSource=$copy" `
        "-p:OutputPath=$(Join-Path $scratch ($name + '-bin\'))" `
        "-p:IntermediateOutputPath=$(Join-Path $scratch ($name + '-obj\'))" 2>&1
    $exit = $LASTEXITCODE
    $text = $output -join "`n"
    if ($exit -eq 0 -or $text -notmatch [regex]::Escape("Assertion failed: $expectedFailure")) {
        $output | Write-Output
        throw "Mutant '$name' did not fail at '$expectedFailure' (exit $exit). Evidence: $scratch"
    }
    $script:rejected++
    Write-Output "Mutant rejected: $name -> $expectedFailure"
}

# Isolation and wiring.
Invoke-Mutant 'ordinary-raid-leak' `
    (Get-Mutant 'private static bool PickGuardWanted => ArenaSession\.IsActive;' 'private static bool PickGuardWanted => true;') `
    'ordinary raid keeps the native stale pick, icon and fallback exactly'
Invoke-Mutant 'enable-aborts-plugin' `
    (Get-Mutant 'EnablePickPatch\(\(\) => new PickAfterRemovalPatch\(\)\);' 'new PickAfterRemovalPatch().Enable();') `
    'a pick patch that cannot be enabled never stops the rest of the plugin'

# The removal cleanup.
Invoke-Mutant 'no-view-resync' `
    (Get-Mutant '\r?\n\s*__instance\.SetNewTopPriorityGrenade\(\);' '') `
    'throwing the picked backpack grenade moves the pick to the next rig grenade'
Invoke-Mutant 'no-container-case' `
    (Get-Mutant 'ReferenceEquals\(removed, pick\) \|\| \(removed is CompoundItem container && container\.Contains\(pick\)\)' 'ReferenceEquals(removed, pick)') `
    'a pouch dropped with the pick inside moves the pick'
Invoke-Mutant 'drops-reachable-pick' `
    (Get-Mutant '!LeftWith\(obj\.Item, pick\) \|\| controller\.IsAtReachablePlace\(pick\)\)' '!LeftWith(obj.Item, pick))') `
    'a grenade that only moved and is still reachable keeps the pick'
Invoke-Mutant 'carried-not-drawable' `
    (Get-Mutant '!LeftWith\(obj\.Item, pick\) \|\| controller\.IsAtReachablePlace\(pick\)\)' '!LeftWith(obj.Item, pick) || controller.IsAtReachablePlace(pick) || equipment.Contains(pick))') `
    'with reach off a picked grenade moved inside the backpack (carried, not drawable) moves the pick'
Invoke-Mutant 'foreign-owner' `
    (Get-Mutant ' \|\| controller\.ID != obj\.OwnerId\)' ')') `
    'an event for another owner leaves the pick alone'
Invoke-Mutant 'begin-status' `
    (Get-Mutant 'obj == null \|\| obj\.Status != CommandStatus\.Succeed\)' 'obj == null)') `
    'a Begin-only removal leaves the pick alone'
Invoke-Mutant 'throws-into-multicast' `
    (Get-Mutant '(SetNewTopPriorityGrenade\(\);\s*\}\s*catch \(Exception ex\)\s*\{\s*)ReportPickFailure\(ex\);' '${1}throw;') `
    'a failing pick cleanup never escapes into the event multicast'

# The key backstop.
Invoke-Mutant 'no-key-backstop' `
    (Get-Mutant 'equipment\.TopPriorityGrenade = null;' '_ = equipment;') `
    'the key clears a pick the setting stranded'
Invoke-Mutant 'bindable-not-reachable' `
    (Get-Mutant 'if \(pick == null \|\| controller\.IsAtReachablePlace\(pick\)\)' 'if (pick == null || (controller.IsAtReachablePlace(pick) && controller.IsAtBindablePlace(pick)))') `
    'with reach off a natively drawable rig-pouch pick (not bindable) is still drawn'
Invoke-Mutant 'null-pick-probe' `
    (Get-Mutant 'if \(pick == null \|\| controller\.IsAtReachablePlace\(pick\)\)' 'if (controller.IsAtReachablePlace(pick))') `
    'a tap of G with nothing picked logs nothing'
Invoke-Mutant 'prefix-escapes' `
    (Get-Mutant '(TopPriorityGrenade = null;\s*\}\s*catch \(Exception ex\)\s*\{\s*)ReportPickFailure\(ex\);' '${1}throw;') `
    'a failing key backstop never escapes into the input translator'

# Reporting.
Invoke-Mutant 'report-once-forever' `
    (Get-Mutant 'if \(_reportedPickFailureEpoch == ArenaSession\.ArmEpoch\)' 'if (_reportedPickFailureEpoch != null)') `
    'a failure in a later raid is reported again'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production grenade-pick checks failed after mutation testing.'
}
Write-Output "ARENA_GRENADE_PICK_REGRESSION_VERIFIED ($rejected mutants rejected; evidence: $scratch)"
