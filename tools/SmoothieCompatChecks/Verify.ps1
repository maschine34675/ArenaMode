param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'SmoothieCompatChecks.csproj'
$sources = @{
    Scope = Join-Path $repo 'ArenaMode.Client/Compat/ArenaSmoothieScope.cs'
    Compat = Join-Path $repo 'ArenaMode.Client/Compat/ArenaSmoothieCompat.cs'
    Contract = Join-Path $repo 'ArenaMode.Client/Compat/ArenaSmoothieContract.cs'
    View = Join-Path $repo 'ArenaMode.Client/Pickups/ArenaPickupView.cs'
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/smoothie-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
if ($PublishedAssembly -and !(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'PublishedAssembly must name the separately downloaded Smoothie 1.0.8 DLL, read as metadata only.'
}

function Check([string]$Name, [hashtable]$Files, [string]$ExpectedFailure = '') {
    $log = Join-Path $evidence "$Name.log"
    $arguments = @('run', '--project', $project, '-c', "Smoothie_${run}_$Name")
    foreach ($kind in @('Scope','Compat','Contract','View')) { $arguments += "-p:$($kind)Source=$($Files[$kind])" }
    if ($PublishedAssembly) { $arguments += @('--', $PublishedAssembly) }
    & dotnet @arguments *> $log
    $code = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($ExpectedFailure) {
        if ($code -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $ExpectedFailure")) {
            throw "Mutation must compile and fail its intended behavior: $Name ($log)`n$output"
        }
        Write-Output "Rejected compiled fault: $Name"
    } elseif ($code -ne 0 -or $output -notmatch 'SMOOTHIE_COMPAT_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output ($output.Trim()) }
}

Check 'before' $sources
$faults = @(
    @{ Name='ordinary-gc'; Source='Scope'; Find='exactLocalWorld && exactActiveRaid && started && pmc'; Replace='exactLocalWorld && started && pmc'; Failure='ordinary PMC keeps enabled foreign GC writer' },
    @{ Name='scav-gc'; Source='Scope'; Find='exactLocalWorld && exactActiveRaid && started && pmc'; Replace='exactLocalWorld && exactActiveRaid && started'; Failure='stale active flag cannot suppress Scav' },
    @{ Name='stopped-gc'; Source='Scope'; Find='exactLocalWorld && exactActiveRaid && started && pmc'; Replace='exactLocalWorld && exactActiveRaid && pmc'; Failure='stopping raid runs original' },
    @{ Name='settings-map'; Source='Scope'; Find='&& string.Equals(worldMap, settingsMap, StringComparison.OrdinalIgnoreCase)'; Replace='&& true'; Failure='native map mismatch runs original' },
    @{ Name='foreign-world'; Source='Compat'; Find='ReferenceEquals(game.GameWorld, world)'; Replace='true'; Failure='foreign world runs original' },
    @{ Name='foreign-bot-game'; Source='Compat'; Find='ReferenceEquals(Singleton<IBotGame>.Instance, game)'; Replace='true'; Failure='foreign bot game runs original' },
    @{ Name='arena-gc-unprotected'; Source='Scope'; Find='=> exactLocalWorld && exactActiveRaid'; Replace='=> false && exactLocalWorld && exactActiveRaid'; Failure='exact Arena isolates optional GC writer' },
    @{ Name='optic-mask'; Source='Scope'; Find='mainMask & (opticMask ?? -1)'; Replace='mainMask'; Failure='both cameras must see the selected excluded layer' },
    @{ Name='main-mask'; Source='Scope'; Find='mainMask & (opticMask ?? -1)'; Replace='(opticMask ?? -1)'; Failure='main camera mask cannot be bypassed by optic visibility' },
    @{ Name='missing-layer'; Source='Scope'; Find='layer >= 0 && layer < 32'; Replace='true'; Failure='missing and out of range layers are rejected' },
    @{ Name='initial-marker-layer'; Source='View'; Find='UpdateMarkerLayer(CameraManager.Exist ? CameraManager.Instance.Camera : null);'; Replace='// omitted construction exclusion'; Failure='all private marker descendants excluded at construction' },
    @{ Name='early-active-marker'; Source='View'; Find='        _root.SetActive(false);'; Replace='        // exposed before first camera checked Tick'; Failure='unknown camera creates inactive Default marker' },
    @{ Name='camera-change'; Source='View'; Find='UpdateMarkerLayer(camera);'; Replace='// omitted camera-change exclusion'; Failure='camera changes reselect visible excluded layer' },
    @{ Name='private-descendants'; Source='View'; Find='_root.GetComponentsInChildren<Transform>(true)'; Replace='new[] { _root.transform }'; Failure='all private marker descendants excluded at construction' },
    @{ Name='pooled-weapon-layer'; Source='View'; Find='weapon.transform.position = _floor'; Replace='weapon.layer = _markerLayer; weapon.transform.position = _floor'; Failure='pool and wall layers and parent untouched' },
    @{ Name='shutdown-inert'; Source='Compat'; Find='if (_cleanup == null || _failed) return true;'; Replace='if (_failed) return true;'; Failure='failed unpatch leaves surviving prefix inert' },
    @{ Name='metadata-mismatch'; Source='Contract'; Find='Normalize(name.Version) != version'; Replace='false'; Failure='invalid optional contract installs nothing' }
    @{ Name='pending-plugin'; Source='Compat'; Find='if (info?.Instance == null) return;'; Replace='if (info?.Instance == null) throw new InvalidOperationException("pending metadata");'; Failure='pending plugin can finish loading before admission' }
    @{ Name='inactive-tmp-awake'; Source='View'; Find='_root = new GameObject("Arena pickup marker " + point.Id);'; Replace='_root = new GameObject("Arena pickup marker " + point.Id); _root.SetActive(false);'; Failure='native TMP awakens before font configuration' }
)
try {
    foreach ($fault in $faults) {
        $original = [IO.File]::ReadAllText($sources[$fault.Source])
        if ([regex]::Matches($original, [regex]::Escape($fault.Find)).Count -ne 1) { throw "Ambiguous fault anchor: $($fault.Name)" }
        $path = Join-Path $evidence ($fault.Name + '.cs')
        [IO.File]::WriteAllText($path, $original.Replace($fault.Find, $fault.Replace))
        $changed = $sources.Clone(); $changed[$fault.Source] = $path
        Check $fault.Name $changed $fault.Failure
    }
} finally { Check 'after' $sources }
Write-Output "SMOOTHIE_COMPAT_FAULTS_PASSED ($($faults.Count)); evidence: $evidence"
Write-Output 'SMOOTHIE_COMPAT_VERIFIED'
