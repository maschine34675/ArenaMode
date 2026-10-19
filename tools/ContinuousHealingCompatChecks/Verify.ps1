param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'ContinuousHealingCompatChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat'
if (!$PublishedAssembly) {
    $PublishedAssembly = Join-Path $repo 'artifacts/health-loot-compat-20261019/releases/continuous-healing/extracted/BepInEx/plugins/ContinuousHealing/ContinuousHealing.dll'
}
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'Supply -PublishedAssembly with the separately downloaded Continuous Healing 1.6.3 DLL. It is inspected as metadata, never installed or executed.'
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/continuous-healing-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaContinuousHealingCompat.cs', 'ArenaContinuousHealingContract.cs', 'ArenaContinuousHealingScope.cs')
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
    & dotnet run --project $project -c "ContinuousHealing_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
    $exit = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Fault did not compile and fail its intended assertion: $Name ($log)`n$output"
        }
        Write-Output "CONTINUOUS_HEALING_MUTANT_REJECTED: $Name"
    } elseif ($exit -ne 0 -or $output -notmatch 'CONTINUOUS_HEALING_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}

Check 'before'
Check 'ordinary-leak' 'ArenaContinuousHealingScope.cs' 'if (!exactActiveWorld || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!ReferenceEquals(bodyWorld, currentWorld)) return false;' 'ordinary bot foreign gate'
Check 'wrong-world' 'ArenaContinuousHealingScope.cs' 'if (!exactActiveWorld || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!exactActiveWorld) return false;' 'foreign body world foreign gate'
Check 'lost-retirement-lease' 'ArenaContinuousHealingScope.cs' 'if (ReferenceEquals(lease.World, bodyWorld)) return true;' 'if (false) return true;' 'delayed callback after session cleanup foreign gate'
Check 'body-reuse-leak' 'ArenaContinuousHealingScope.cs' 'if (ReferenceEquals(lease.World, bodyWorld)) return true;' 'return true;' 'reused body loses old-world ownership foreign gate'
Check 'local-body-identity' 'ArenaContinuousHealingScope.cs' '!ReferenceEquals(body, currentLocalBody)' 'false' 'retired living same-profile body foreign gate'
Check 'dead-local-leak' 'ArenaContinuousHealingScope.cs' '|| !isYourPlayer || !alive;' '|| !isYourPlayer;' 'dead old MainPlayer foreign gate'
Check 'native-veto' 'ArenaContinuousHealingCompat.cs' '__result = true;' '__result = false;' 'Arena bot native fallback'
Check 'old-cancel-leak' 'ArenaContinuousHealingCompat.cs' '=> _foreign == null || !ShouldBypass(__0, __originalMethod.DeclaringType.Name);' '=> true;' 'bot start and cancel guarded'
Check 'game-world-leak' 'ArenaContinuousHealingCompat.cs' 'ReferenceEquals(game.GameWorld, world)' 'true' 'mismatched game world foreign gate'
Check 'bot-game-leak' 'ArenaContinuousHealingCompat.cs' 'ReferenceEquals(Singleton<IBotGame>.Instance, game)' 'true' 'mismatched bot game foreign gate'
Check 'missing-receipt' 'ArenaContinuousHealingCompat.cs' 'if (!OwnsPatches()) throw new InvalidOperationException("Continuous Healing compatibility hooks did not install");' '' 'missing installation receipt rejects Arena'
Check 'native-receipt' 'ArenaContinuousHealingContract.cs' 'info.Prefixes.Count(p => p.PatchMethod == prefixes[i]) != 1' 'false' 'missing foreign native receipt rejects Arena'
Check 'runtime-fail-open' 'ArenaContinuousHealingCompat.cs' 'return owned;' 'return false;' 'proved body survives health-read fault foreign gate'
Check 'scope-cleanup' 'ArenaContinuousHealingScope.cs' 'internal void Clear() => _bodies = new ConditionalWeakTable<object, Lease>();' 'internal void Clear() { }' 'scope cleanup releases ownership'
Check 'after'
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production source changed during checks: $name" }
}
Write-Output "CONTINUOUS_HEALING_COMPAT_VERIFIED; 14 compiled negative controls; evidence: $evidence"
