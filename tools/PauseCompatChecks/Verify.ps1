param([string]$PublishedClient, [string]$NativeAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'PauseCompatChecks.csproj'
if (!$PublishedClient) { $PublishedClient = Join-Path $repo 'artifacts/candidate-audit-20261020/systems/Pause-binary/BepInEx/plugins/rejzbihai-Pause/rejzbihai.Pause.dll' }
if (!$NativeAssembly) { $NativeAssembly = [IO.Path]::GetFullPath((Join-Path $repo '../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) }
foreach ($path in @($PublishedClient, $NativeAssembly)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Metadata input missing: $path" }
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/compat-second-20261020/pause/checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$source = Join-Path $repo 'ArenaMode.Client/Compat'
$files = @('ArenaPauseCompat.cs', 'ArenaPauseContract.cs', 'ArenaPauseScope.cs')
$original = @{}
foreach ($name in $files) { $original[$name] = [IO.File]::ReadAllText((Join-Path $source $name)) }
function Check([string]$Name, [string]$File = '', [string]$Find = '', [string]$Replace = '', [string]$Failure = '') {
    $copy = Join-Path $evidence $Name
    [IO.Directory]::CreateDirectory($copy) | Out-Null
    foreach ($sourceName in $files) {
        $content = $original[$sourceName]
        if ($sourceName -eq $File) {
            if ([regex]::Matches($content, [regex]::Escape($Find)).Count -ne 1) { throw "Ambiguous mutation: $Name" }
            $content = $content.Replace($Find, $Replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $sourceName), $content)
    }
    $log = Join-Path $copy 'result.log'
    & dotnet run --project $project -c "Pause_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedClient $NativeAssembly *> $log
    $exitCode = $LASTEXITCODE; $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exitCode -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Mutation did not compile and fail expected assertion: $Name ($log)`n$output"
        }
        Write-Output "PAUSE_MUTANT_REJECTED: $Name"
    } elseif ($exitCode -ne 0 -or $output -notmatch 'PAUSE_COMPAT_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
Check 'before'
Check 'activation-leak' 'ArenaPauseCompat.cs' 'if (!OwnsController(__instance, out var world)) return true;' 'if (OwnsController(__instance, out var world)) return true;' 'Arena request suppressed before flag and time mutation'
Check 'release-blocked' 'ArenaPauseCompat.cs' 'if (_contract.IsPaused()) return true;' 'if (_contract.IsPaused()) return false;' 'Arena unpause always retained'
Check 'no-input-blocked' 'ArenaPauseCompat.cs' 'if (!_contract.IsKeyPressed(key.Value)) return true;' 'if (!_contract.IsKeyPressed(key.Value)) return false;' 'non-activation update retained'
Check 'wrong-controller' 'ArenaPauseCompat.cs' '!ReferenceEquals(component.gameObject, world.gameObject)' 'false' 'wrong controller object untouched'
Check 'wrong-cached-world' 'ArenaPauseCompat.cs' '!ReferenceEquals(_contract.CachedWorld.GetValue(null), world)' 'false' 'wrong cached world untouched'
Check 'wrong-cached-game' 'ArenaPauseCompat.cs' '!ReferenceEquals(_contract.CachedGame.GetValue(instance), game)' 'false' 'wrong cached game untouched'
Check 'wrong-bot-game' 'ArenaPauseCompat.cs' 'ReferenceEquals(Singleton<IBotGame>.Instance, game)' 'true' 'wrong bot game untouched'
Check 'unconfirmed-identity' 'ArenaPauseScope.cs' 'Protocol.HasNativeEndIdentity(nativeIdentity, token)' 'true' 'unconfirmed start untouched'
Check 'ordinary-capture' 'ArenaPauseScope.cs' 'if (!capture) return false;' 'if (!capture) { }' 'ordinary later raid can pause'
Check 'notice-spam' 'ArenaPauseScope.cs' ' || lease.Notified' '' 'repeat request remains blocked without spam'
Check 'old-version' 'ArenaPauseContract.cs' 'version < ReviewedVersion' 'false' 'unsafe contract rejected'
Check 'unguarded-activation' 'ArenaPauseContract.cs' 'pauseCallers.Count != 1 || pauseCallers[0] != Update' 'false' 'unsafe contract rejected'
Check 'after'
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production changed during verification: $name" }
}
Write-Output "PAUSE_COMPAT_CHECKS_OK; 12 compiled negative controls; evidence: $evidence"
