param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'RemoveTheDeadCompatChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat'
if (!$PublishedAssembly) {
    $PublishedAssembly = Join-Path $repo 'artifacts/compat-audit-20261019b/remove-the-dead/release/BepInEx/plugins/RemoveTheDead.dll'
}
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'Supply -PublishedAssembly with the separately downloaded Remove The Dead 4.1.3 DLL. It is read as metadata only; this harness never installs or executes it.'
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/remove-the-dead-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaRemoveTheDeadCompat.cs', 'ArenaRemoveTheDeadContract.cs', 'ArenaRemoveTheDeadScope.cs')
$original = @{}
foreach ($name in $files) { $original[$name] = [IO.File]::ReadAllText((Join-Path $source $name)) }

function Check([string]$Name, [string]$File = '', [string]$Find = '', [string]$Replace = '', [string]$Failure = '') {
    $copy = Join-Path $evidence $Name
    [IO.Directory]::CreateDirectory($copy) | Out-Null
    foreach ($sourceName in $files) {
        $content = $original[$sourceName]
        if ($sourceName -eq $File) {
            if ([regex]::Matches($content, [regex]::Escape($Find)).Count -ne 1) { throw "Ambiguous mutation anchor: $Name" }
            $content = $content.Replace($Find, $Replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $sourceName), $content)
    }
    $log = Join-Path $copy 'result.log'
    & dotnet run --project $project -c "RemoveTheDead_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
    $exit = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Fault did not compile and fail its intended assertion: $Name ($log)`n$output"
        }
        Write-Output "REMOVE_THE_DEAD_MUTANT_REJECTED: $Name"
    } elseif ($exit -ne 0 -or $output -notmatch 'REMOVE_THE_DEAD_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}

Check 'before'
Check 'ordinary-suppressed' 'ArenaRemoveTheDeadScope.cs' 'if (exactActiveRaid) return true;' 'return true;' 'ordinary PMC manual'
Check 'live-unprotected' 'ArenaRemoveTheDeadScope.cs' 'if (exactActiveRaid) return true;' 'if (exactActiveRaid) return false;' 'active Arena after token consumption manual'
Check 'request-only-marker' 'ArenaRemoveTheDeadScope.cs' '&& Protocol.HasNativeEndIdentity(nativeIdentity, token)' '&& true' 'request-only marker manual'
Check 'unconfirmed-arm' 'ArenaRemoveTheDeadScope.cs' 'return nativeStarted && armed && pendingArm' 'return nativeStarted && armed' 'unconfirmed arm manual'
Check 'foreign-world' 'ArenaRemoveTheDeadCompat.cs' '!ReferenceEquals(game.GameWorld, world)' 'false' 'foreign same-map world manual'
Check 'foreign-bot-game' 'ArenaRemoveTheDeadCompat.cs' '!ReferenceEquals(Singleton<IBotGame>.Instance, game)' 'false' 'foreign bot game manual'
Check 'scav-inheritance' 'ArenaRemoveTheDeadCompat.cs' 'settings?.playerSide == ESideType.Pmc' 'true' 'Scav cannot inherit arm manual'
Check 'no-pause' 'ArenaRemoveTheDeadCompat.cs' 'if (!pause) return true;' 'if (true) return true;' 'newer contract protects Arena manual'
Check 'runtime-fail-open' 'ArenaRemoveTheDeadCompat.cs' 'return !exactActive;' 'return true;' 'proved active Arena survives settings-read fault manual'
Check 'missing-receipt' 'ArenaRemoveTheDeadCompat.cs' 'if (!OwnsPatch()) throw new InvalidOperationException("Remove The Dead compatibility hook did not install");' '' 'unsafe hook installation refuses Arena receipt'
Check 'unchecked-entry' 'ArenaRemoveTheDeadContract.cs' 'RequiresCall(manual, remove);' '' 'changed metadata or cleanup chain rejects Arena'
Check 'unchecked-bypass' 'ArenaRemoveTheDeadContract.cs' 'if (IsDestructive(call))' 'if (false)' 'changed metadata or cleanup chain rejects Arena'
Check 'active-settings-loss' 'ArenaRemoveTheDeadScope.cs' 'if (exactActiveRaid) return true;' 'if (exactActiveRaid && pmc) return true;' 'proved active Arena survives missing settings manual'
Check 'after'
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production source changed during checks: $name" }
}
Write-Output "REMOVE_THE_DEAD_COMPAT_VERIFIED; 13 compiled negative controls; evidence: $evidence"
