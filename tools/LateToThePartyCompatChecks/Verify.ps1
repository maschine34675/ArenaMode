param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'LateToThePartyCompatChecks.csproj'
if (!$PublishedAssembly) { $PublishedAssembly = Join-Path $repo 'artifacts/health-loot-compat-20261019/releases/late-to-the-party/extracted/BepInEx/plugins/LateToTheParty/LateToTheParty-Client.dll' }
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'Supply -PublishedAssembly with the separately downloaded Late To The Party 3.1.1 DLL. It is read as metadata only; this harness never installs or executes it.'
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/late-to-the-party-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaLateToThePartyCompat.cs', 'ArenaLateToThePartyContract.cs', 'ArenaLateToThePartyScope.cs', 'ArenaWeaponPickup.cs')
$paths = @{}; $original = @{}
foreach ($name in $files) {
    $directory = if ($name -eq 'ArenaWeaponPickup.cs') { 'Pickups' } else { 'Compat' }
    $paths[$name] = Join-Path $repo "ArenaMode.Client/$directory/$name"
    $original[$name] = [IO.File]::ReadAllText($paths[$name])
}
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
    & dotnet run --project $project -c "LateToTheParty_${run}_$Name" "-p:ProductionRoot=$copy" "-p:PickupSource=$(Join-Path $copy 'ArenaWeaponPickup.cs')" -- $PublishedAssembly *> $log
    $exit = $LASTEXITCODE; $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Fault did not compile and fail its intended assertion: $Name ($log)`n$output"
        }
        Write-Output "LATE_TO_THE_PARTY_MUTANT_REJECTED: $Name"
    } elseif ($exit -ne 0 -or $output -notmatch 'LATE_TO_THE_PARTY_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
Check 'before'
Check 'destruction-unguarded' 'ArenaLateToThePartyCompat.cs' 'BeforeDestruction(Item __0) => !Protects(__0);' 'BeforeDestruction(Item __0) => true;' 'owned weapon outer deletion'
Check 'eligibility-unguarded' 'ArenaLateToThePartyCompat.cs' 'if (!Protects(__0)) return true;' 'return true;' 'owned weapon eligibility'
Check 'discovery-unguarded' 'ArenaLateToThePartyCompat.cs' 'BeforeDiscovery(LootItem __0) => __0 == null || !Protects(__0.Item, __0);' 'BeforeDiscovery(LootItem __0) => true;' 'owned weapon discovery'
Check 'ordinary-suppressed' 'ArenaLateToThePartyCompat.cs' 'bool protects = ArenaWeaponPickup.ProtectsFromForeignCleanup(world, item, source);' 'bool protects = true;' 'ordinary PMC loot outer deletion'
Check 'foreign-bot-game' 'ArenaLateToThePartyScope.cs' 'ReferenceEquals(game, botGame)' 'true' 'foreign bot game outer deletion'
Check 'foreign-world' 'ArenaLateToThePartyScope.cs' 'ReferenceEquals(world, gameWorld)' 'true' 'foreign local world outer deletion'
Check 'stale-world-registry' 'ArenaWeaponPickup.cs' '!ReferenceEquals(handle._world, world)' 'false' 'next ordinary world cannot inherit source outer deletion'
Check 'generated-id-only' 'ArenaWeaponPickup.cs' 'ReferenceEquals(generated, item)' 'generated.Id == item.Id' 'same id different instance outer deletion'
Check 'foreign-attachment' 'ArenaWeaponPickup.cs' '_generatedItems.Any(generated => ReferenceEquals(generated, item))' 'true' 'same id different instance outer deletion'
Check 'detached-attachment' 'ArenaWeaponPickup.cs' '&& _weapon.GetAllItems().Any(attached => ReferenceEquals(attached, item))' '&& true' 'detached generated attachment outer deletion'
Check 'reused-model' 'ArenaWeaponPickup.cs' '!ReferenceEquals(_loot.Item, _weapon)' 'false' 'reused native loot root outer deletion'
Check 'foreign-owner' 'ArenaWeaponPickup.cs' '!ReferenceEquals(_loot.ItemOwner, _owner)' 'false' 'foreign native source owner outer deletion'
Check 'pending-retirement' 'ArenaWeaponPickup.cs' '|| IsPending)) return false;' ')) return false;' 'retiring pending source outer deletion'
Check 'native-inprocess' 'ArenaWeaponPickup.cs' 'bool transferring = IsPending && _transferOwner != null' 'bool transferring = false && _transferOwner != null' 'native InProcess owner outer deletion'
Check 'registry-retention' 'ArenaWeaponPickup.cs' '|| ReferenceEquals(handle, this)' '|| false' 'source registry retires without optional plugin'
Check 'receipt-unchecked' 'ArenaLateToThePartyCompat.cs' 'if (!OwnsPatches()) throw new InvalidOperationException("Late To The Party compatibility hooks did not install");' '' 'patch failure rejects Arena receipt'
Check 'destroy-edge-unchecked' 'ArenaLateToThePartyContract.cs' 'RequiresCall(start, destroy);' '' 'unsafe contract rejected destroy-edge'
Check 'destructive-bypass-unchecked' 'ArenaLateToThePartyContract.cs' 'if (IsItemDestruction(call))' 'if (false)' 'unsafe contract rejected bypass'
Check 'concrete-bypass-unchecked' 'ArenaLateToThePartyContract.cs' 'typeof(IItemOwner).IsAssignableFrom(call.DeclaringType)' 'call.DeclaringType == typeof(IItemOwner)' 'unsafe contract rejected concrete-bypass'
Check 'async-helper-unchecked' 'ArenaLateToThePartyContract.cs' 'if (state != null)' 'if (false)' 'unsafe contract rejected delayed-bypass'
Check 'after'
foreach ($name in $files) { if ([IO.File]::ReadAllText($paths[$name]) -ne $original[$name]) { throw "Production source changed during checks: $name" } }
Write-Output "LATE_TO_THE_PARTY_COMPAT_VERIFIED; 20 compiled negative controls; evidence: $evidence"
