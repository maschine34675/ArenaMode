param([string]$PublishedClient, [string]$PublishedServer, [string]$NativeAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'StatTrackCompatChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat'
if (!$PublishedClient) { $PublishedClient = Join-Path $repo 'artifacts/movement-stats-compat-20261019/releases/stattrack/extracted/BepInEx/plugins/acidphantasm-stattrack/acidphantasm-stattrack.dll' }
if (!$PublishedServer) { $PublishedServer = Join-Path $repo 'artifacts/movement-stats-compat-20261019/releases/stattrack/extracted/SPT_Runtime/user/mods/acidphantasm-stattrack/acidphantasm-stattrack.dll' }
if (!$NativeAssembly) { $NativeAssembly = [IO.Path]::GetFullPath((Join-Path $repo '../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) }
foreach ($file in @($PublishedClient, $PublishedServer, $NativeAssembly)) { if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Metadata input missing: $file" } }
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/stattrack-compat-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaStatTrackCompat.cs', 'ArenaStatTrackContract.cs', 'ArenaStatTrackScope.cs')
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
    & dotnet run --project $project -c "StatTrack_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedClient $PublishedServer $NativeAssembly *> $log
    $exit = $LASTEXITCODE; $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Fault did not compile and fail intended assertion: $Name ($log)`n$output"
        }
        Write-Output "STATTRACK_MUTANT_REJECTED: $Name"
    } elseif ($exit -ne 0 -or $output -notmatch 'STATTRACK_COMPAT_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
Check 'before'
Check 'leak-shot' 'ArenaStatTrackCompat.cs' 'private static bool BeforeShot(Weapon __0) => Guard(0, () => Owns(__0));' 'private static bool BeforeShot(Weapon __0) => true;' 'Arena shot hit kill do not contaminate ordinary pending batch'
Check 'leak-kill' 'ArenaStatTrackCompat.cs' '=> Guard(1, () => Owns(__0) || Owns(__1 as Player) || Owns(__2.Player?.iPlayer as Player) || Owns(__2.Weapon as Weapon));' '=> true;' 'Arena shot hit kill do not contaminate ordinary pending batch'
Check 'leak-hit' 'ArenaStatTrackCompat.cs' '=> Guard(2, () => Owns(__0) || Owns(__3.Player?.iPlayer as Player) || Owns(__3.Weapon as Weapon));' '=> true;' 'Arena shot hit kill do not contaminate ordinary pending batch'
Check 'leak-merge' 'ArenaStatTrackCompat.cs' 'private static bool BeforeMerge(IPlayer __0) => Guard(3, () => Owns(__0 as Player));' 'private static bool BeforeMerge(IPlayer __0) => true;' 'Arena unregister cannot remerge ordinary pending save'
Check 'ordinary-owned' 'ArenaStatTrackScope.cs' 'if (!exactCurrent || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!ReferenceEquals(bodyWorld, currentWorld)) return false;' 'ordinary instance and template producers run'
Check 'wrong-world-owned' 'ArenaStatTrackScope.cs' 'if (!exactCurrent || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!exactCurrent) return false;' 'foreign world not captured'
Check 'lost-old-body-lease' 'ArenaStatTrackScope.cs' 'if (ReferenceEquals(lease.World, bodyWorld)) return true;' 'if (false) return true;' 'late old world producer and merge excluded'
Check 'body-reuse-leak' 'ArenaStatTrackScope.cs' 'if (ReferenceEquals(lease.World, bodyWorld)) return true;' 'return true;' 'reused body loses old-world lease'
Check 'weapon-owner-leak' 'ArenaStatTrackScope.cs' '(owner == null || ReferenceEquals(owner, lease.Owner))' 'true' 'new nonplayer owner removes detached weapon exemption'
Check 'capture-missing' 'ArenaStatTrackCompat.cs' 'Scope.OwnsBody(body, body.GameWorld, world, true);' ';' 'zero-event body survives session cleanup before unregister'
Check 'early-registration-missing' 'ArenaStatTrackCompat.cs' 'if (__instance.RegisteredPlayers.Contains(body)) CaptureInitialBody(body, body.Profile);' '' 'failed loading exact registration lease none'
Check 'early-constructor-missing' 'ArenaStatTrackCompat.cs' 'try { if (!Owns(__0)) CaptureInitialBody(__0, __1); }' 'try { }' 'pre-registration failure ownership True'
Check 'constructor-profile-leak' 'ArenaStatTrackCompat.cs' 'ReferenceEquals(profile, game.Profile)' 'true' 'failed loading exact registration lease profile'
Check 'pending-marker-leak' 'ArenaStatTrackScope.cs' 'Protocol.HasNativeEndIdentity(nativeIdentity, token)' 'true' 'failed loading exact registration lease token'
Check 'bot-game-leak' 'ArenaStatTrackCompat.cs' '!ReferenceEquals(Singleton<IBotGame>.Instance, game)' 'false' 'wrong bot game fails open'
Check 'native-world-leak' 'ArenaStatTrackCompat.cs' '!ReferenceEquals(game.GameWorld, world)' 'false' 'wrong native game world fails open'
Check 'field-writer-leak' 'ArenaStatTrackContract.cs' 'if (stateAccess && !AllowedStateAccess(method, utility, saveState, loadState))' 'if (false)' 'unsafe contract rejected extra-helper-writer'
Check 'producer-graph-leak' 'ArenaStatTrackContract.cs' '!addCallers.SetEquals(guarded.Take(3))' 'false' 'unsafe contract rejected extra-producer'
Check 'native-receipt-missing' 'ArenaStatTrackContract.cs' 'info.Postfixes.Count(p => p.PatchMethod == methods[i]) != 1' 'false' 'missing native foreign receipt rejected'
Check 'own-receipt-missing' 'ArenaStatTrackCompat.cs' 'if (!OwnsPatches()) throw new InvalidOperationException("StatTrack compatibility hooks did not install");' '' 'missing own installation receipt rejected'
Check 'after'
foreach ($name in $files) { if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production changed during verification: $name" } }
Write-Output "STATTRACK_COMPAT_CHECKS_OK; 20 compiled negative controls; evidence: $evidence"
