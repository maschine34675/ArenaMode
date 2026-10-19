param([switch]$Mutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'NativeModelDiagnosticsChecks.csproj'
$sourcePath = Join-Path $repo 'ArenaMode.Client/Diagnostics/ArenaNativeModelDiagnostics.cs'

function Run-Check([string]$Source) {
    $arguments = @('run', '--project', $project, '-c', 'Release', '--nologo')
    if ($Source) { $arguments += "-p:NativeModelSource=$Source" }
    $output = (& dotnet @arguments 2>&1 | Out-String)
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
}
$positive = Run-Check ''
if ($positive.Code -ne 0 -or $positive.Output -notmatch 'NATIVE_MODEL_DIAGNOSTICS_CHECKS_PASSED') {
    throw "Positive control failed: $($positive.Output)"
}
Write-Output $positive.Output.Trim()
if ($Mutants) {
    $original = [IO.File]::ReadAllText($sourcePath)
    $mutationCases = @(
        @('developer-guard', '!ArenaLog.DeveloperEnabled || _harmony', 'false || _harmony', 'disabled probes'),
        @('exact-world', 'ArenaSession.IsExactActiveRaid(game, world)', 'ArenaSession.IsActive', 'stale Arena latch'),
        @('loading-map', 'string.Equals(ArenaSession.ArmedMap, world.LocationId, StringComparison.OrdinalIgnoreCase)', 'true', 'reject loading mismatch: map'),
        @('original-exception', 'return __exception;', 'return null;', 'original exception instance'),
        @('per-signature-cap', 'if (count >= 2) return false;', 'if (count >= 9999) return false;', 'limited to two reports'),
        @('per-raid-cap', 'if (_total >= 24) return false;', 'if (_total >= 9999) return false;', 'object churn bounded'),
        @('branch-labels', 'first.labels.AddRange(instruction.labels);', '// lost labels', 'Bad label|not marked|unmarked|label'),
        @('native-instructions', 'yield return instruction; // Original string, Error and fallback intact.', 'yield return new CodeInstruction(OpCodes.Nop); // broken native instructions', 'original instructions retained'),
        @('owner-fallback-lost', 'string lookupProfile = ownerBodyMatch && !string.IsNullOrEmpty(ownerProfile) ? ownerProfile : bodyProfile;', 'string lookupProfile = bodyProfile;', 'empty body ID uses exact live owner'),
        @('stale-owner-borrow', 'owner != null && body != null && ReferenceEquals(owner.PlayerBody, body)', 'owner != null && body != null', 'stale owner cannot lend identity'),
        @('unknown-unregistered', '!checkedIdentity ? "not-checked"', '!checkedIdentity ? "unregistered"', 'destroyed body cannot imply failed registration'),
        @('destroyed-body-lookup', 'bool canCheckBody = body != null &&', 'bool canCheckBody = !ReferenceEquals(body, null) &&', 'destroyed body cannot imply failed registration'),
        @('destroyed-result-unregistered', ': ReferenceEquals(registered, null) ? "unregistered"', ': registered == null ? "unregistered"', 'lookup returning a destroyed player is distinct from no entry'),
        @('uninitialized-animator-read', 'if (!initialized) return text.Append("; animatorState=not-initialized").ToString();', '// missing initialization guard', 'uninitialized Unity animator is never queried'),
        @('destroyed-animator-read', 'if (native == null) return text.Append("; animatorState=unavailable").ToString();', '// missing Unity validity guard', 'destroyed Unity animator is never queried'),
        @('unbounded-layer-read', 'layer < Math.Min(2, layers)', 'layer < layers', 'only first two animator layers sampled'),
        @('eager-capped-detail', 'if (!Budget.TryTake(game, world, ArenaSession.ArmEpoch, family + "/" + id, out bool reachedCap)) return;', 'if (!Budget.TryTake(game, world, ArenaSession.ArmEpoch, family + "/" + id, out bool reachedCap)) { detail(); return; }', 'limited to two reports before costly reads|signature budget rejects utility before animator reads')
    )
    $folder = Join-Path $PSScriptRoot 'obj/native-model-mutants'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    foreach ($mutant in $mutationCases) {
        if (-not $original.Contains($mutant[1])) { throw "Mutant anchor missing: $($mutant[0])" }
        $path = Join-Path $folder "$($mutant[0]).cs"
        [IO.File]::WriteAllText($path, $original.Replace($mutant[1], $mutant[2]))
        $negative = Run-Check $path
        if ($negative.Code -eq 0 -or $negative.Output -notmatch $mutant[3] -or $negative.Output -match 'error CS\d+') {
            throw "Mutant did not fail its behavioral oracle: $($mutant[0])`n$($negative.Output)"
        }
        Write-Output "MUTANT_REJECTED $($mutant[0])"
    }
    $restored = Run-Check ''
    if ($restored.Code -ne 0 -or $restored.Output -notmatch 'NATIVE_MODEL_DIAGNOSTICS_CHECKS_PASSED') {
        throw "Restored production control failed: $($restored.Output)"
    }
    Write-Output "NATIVE_MODEL_DIAGNOSTICS_MUTANTS_PASSED ($($mutationCases.Count) rejected; production restored)"
}
