param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'ClassicMovementCompatChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat'
if (!$PublishedAssembly) {
    $PublishedAssembly = Join-Path $repo 'artifacts/movement-stats-compat-20261019/releases/classic/extracted/BepInEx/plugins/ClassicMovement/ClassicMovement.dll'
}
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'Supply the separately downloaded Classic Movement 1.1.3 DLL. Only metadata is read; it is never loaded or installed.'
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/classic-movement-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaClassicMovementCompat.cs', 'ArenaClassicMovementContract.cs', 'ArenaClassicMovementScope.cs')
$original = @{}
foreach ($name in $files) { $original[$name] = [IO.File]::ReadAllText((Join-Path $source $name)) }
$faults = 0

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
    & dotnet run --project $project -c "ClassicMovement_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
    $exit = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Fault did not compile and fail the intended assertion: $Name ($log)`n$output"
        }
        $script:faults++
        Write-Output "CLASSIC_MOVEMENT_MUTANT_REJECTED: $Name"
    } elseif ($exit -ne 0 -or $output -notmatch 'CLASSIC_MOVEMENT_COMPAT_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}

Check 'before'
Check 'ordinary-leak' 'ArenaClassicMovementScope.cs' 'if (active) return true;' 'return true;' 'ordinary PMC state authority'
Check 'requires-started-too-late' 'ArenaClassicMovementCompat.cs' 'game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started' 'game.Status == GameStatus.Started' 'pre-IBotGame initial local construction'
Check 'start-marker-authority' 'ArenaClassicMovementScope.cs' 'Protocol.HasNativeEndIdentity(nativeId, token)' '(Protocol.HasNativeEndIdentity(nativeId, token) || Protocol.IsNativeStartIdentity(nativeId, token))' 'unconfirmed start marker state authority'
Check 'pending-request' 'ArenaClassicMovementScope.cs' 'return armed && pending &&' 'return armed &&' 'no pending request state authority'
Check 'validated-token' 'ArenaClassicMovementScope.cs' '&& Protocol.IsLifecycleRequestId(token) &&' '&&' 'invalid arm token state authority'
Check 'native-map' 'ArenaClassicMovementScope.cs' '|| !string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)' '' 'wrong native map state authority'
Check 'body-world' 'ArenaClassicMovementScope.cs' 'if (!exactRaid || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!exactRaid) return false;' 'foreign body world state authority'
Check 'lost-retired-context' 'ArenaClassicMovementScope.cs' 'if (ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)) return true;' 'if (false) return true;' 'retired context stays native'
Check 'reused-body-world' 'ArenaClassicMovementScope.cs' 'ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)' 'ReferenceEquals(lease.Body, body)' 'reused body world loses lease'
Check 'reused-context-body' 'ArenaClassicMovementScope.cs' 'ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)' 'ReferenceEquals(lease.World, bodyWorld)' 'context owner reuse loses lease'
Check 'context-identity' 'ArenaClassicMovementScope.cs' 'if (!ReferenceEquals(currentContext, context) && !(stateConstruction && currentContext == null)) return false;' '' 'unrelated context state authority'
Check 'preview-leak' 'ArenaClassicMovementCompat.cs' 'if (body is not LocalPlayer || body.Profile == null) return false;' 'if (body == null || body.Profile == null) return false;' 'scene preview Player state authority'
Check 'initial-profile' 'ArenaClassicMovementCompat.cs' 'ReferenceEquals(body.Profile, game.Profile) &&' '' 'initial local requires exact profile object'
Check 'initial-owner' 'ArenaClassicMovementCompat.cs' 'ReferenceEquals(game.LocalPlayer, null) &&' '' 'initial local requires missing native owner'
Check 'initial-local-identity' 'ArenaClassicMovementCompat.cs' '&& body.IsYourPlayer' '' 'initial local requires IsYourPlayer'
Check 'native-veto' 'ArenaClassicMovementCompat.cs' '__result = true;' '__result = false;' 'Arena local state authority'
Check 'foreign-speed-leak' 'ArenaClassicMovementCompat.cs' '=> _foreign == null || !ShouldBypass(__0, false);' '=> true;' 'Arena local speed authority'
Check 'wrong-bot-game' 'ArenaClassicMovementCompat.cs' 'ReferenceEquals(Singleton<IBotGame>.Instance, game)' 'true' 'wrong bot game state authority'
Check 'missing-own-receipt' 'ArenaClassicMovementCompat.cs' 'if (!OwnsPatches()) throw new InvalidOperationException("Classic Movement compatibility hooks did not install");' '' 'missing own receipt rejects'
Check 'missing-foreign-receipt' 'ArenaClassicMovementContract.cs' '|| registered != (expected ? 1 : 0)' '' 'missing foreign state receipt rejects'
Check 'unexpected-foreign-hook' 'ArenaClassicMovementContract.cs' '|| all.Count(p => p.PatchMethod.DeclaringType?.Assembly == plugin.Assembly) != registered' '' 'unexpected foreign hook rejects'
Check 'unsafe-legacy' 'ArenaClassicMovementContract.cs' 'if (ConfigBool(config, "NostalgiaMode"))' 'if (false)' 'unsafe legacy mode rejected explicitly'
Check 'lease-cleanup' 'ArenaClassicMovementScope.cs' 'internal void Clear() => _contexts = new ConditionalWeakTable<object, Lease>();' 'internal void Clear() { }' 'scope cleanup releases lease'
Check 'after'
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production source changed during checks: $name" }
}
Write-Output "CLASSIC_MOVEMENT_COMPAT_CHECKS_OK; $faults compiled negative controls; evidence: $evidence"
