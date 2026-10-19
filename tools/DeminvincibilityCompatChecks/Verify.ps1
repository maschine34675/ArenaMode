param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'DeminvincibilityCompatChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat'
if (!$PublishedAssembly) { $PublishedAssembly = Join-Path $repo 'artifacts/ammo-raid-health-compat-20261019/releases/demi/extracted/BepInEx/plugins/Deminvincibility.dll' }
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) { throw 'Supply the published Deminvincibility 2.2.0 DLL; only metadata is read, never loaded or installed.' }
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/deminvincibility-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaDeminvincibilityCompat.cs', 'ArenaDeminvincibilityContract.cs', 'ArenaDeminvincibilityScope.cs')
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
    & dotnet run --project $project -c "Demi_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
    $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($LASTEXITCODE -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) { throw "Fault did not compile and fail intended assertion: $Name ($log)`n$output" }
        $script:faults++; Write-Output "DEMINVINCIBILITY_MUTANT_REJECTED: $Name"
    } elseif ($LASTEXITCODE -ne 0 -or $output -notmatch 'DEMINVINCIBILITY_COMPAT_PASSED \(\d+\)') { throw "Production checks failed: $log`n$output" }
    else { Write-Output $output.Trim() }
}
Check 'before'
Check 'ordinary-leak' 'ArenaDeminvincibilityScope.cs' 'if (active) return true;' 'return true;' 'ordinary immortality retained'
Check 'pending-arm' 'ArenaDeminvincibilityScope.cs' 'return armed && pending &&' 'return armed &&' 'loading rejects pending'
Check 'start-marker' 'ArenaDeminvincibilityScope.cs' 'Protocol.HasNativeEndIdentity(nativeId, token)' '(Protocol.HasNativeEndIdentity(nativeId, token) || Protocol.IsNativeStartIdentity(nativeId, token))' 'loading rejects start identity'
Check 'settings-map' 'ArenaDeminvincibilityScope.cs' '|| !string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)' '' 'loading rejects settings map'
Check 'controller-owner' 'ArenaDeminvincibilityScope.cs' '!ReferenceEquals(controllerOwner, body) || ' '' 'scope controller owner required'
Check 'current-controller' 'ArenaDeminvincibilityScope.cs' ' || !ReferenceEquals(currentController, controller)' '' 'replaced controller not authorized'
Check 'body-world' 'ArenaDeminvincibilityScope.cs' ' || !ReferenceEquals(bodyWorld, currentWorld)' '' 'scope exact body world required'
Check 'retired-lease' 'ArenaDeminvincibilityScope.cs' 'if (ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)) return true;' 'if (false) return true;' 'initial-kit retired body retained'
Check 'lease-body' 'ArenaDeminvincibilityScope.cs' 'ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)' 'ReferenceEquals(lease.World, bodyWorld)' 'leased controller new body rejected'
Check 'lease-world' 'ArenaDeminvincibilityScope.cs' 'ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)' 'ReferenceEquals(lease.Body, body)' 'reused world loses lease'
Check 'preview' 'ArenaDeminvincibilityCompat.cs' 'body is not LocalPlayer' 'ReferenceEquals(body, null)' 'preview untouched'
Check 'bot' 'ArenaDeminvincibilityCompat.cs' ' || !body.IsYourPlayer' '' 'bot untouched'
Check 'unregistered-body' 'ArenaDeminvincibilityScope.cs' ' || !currentOrRegistering' '' 'unregistered arbitrary local untouched'
Check 'no-clamp-isolation' 'ArenaDeminvincibilityCompat.cs' '=> !Owns(controller, body, 2) && entry.Value;' '=> entry.Value;' 'Arena clamp bypassed'
Check 'global-clamp-isolation' 'ArenaDeminvincibilityCompat.cs' '=> !Owns(controller, body, 2) && entry.Value;' '=> false;' 'ordinary clamp retained'
Check 'death-veto' 'ArenaDeminvincibilityCompat.cs' '{ if (!Owns(__0, __2, 0)) return true; __result = true; return false; }' '{ if (!Owns(__0, __2, 0)) return true; __result = false; return false; }' 'Arena death reaches native'
Check 'limb-veto' 'ArenaDeminvincibilityCompat.cs' '{ if (!Owns(__0, __1, 1)) return true; __result = true; return false; }' '{ if (!Owns(__0, __1, 1)) return true; __result = false; return false; }' 'Arena limb destruction reaches native'
Check 'missing-registration' 'ArenaDeminvincibilityCompat.cs' 'if (iPlayer is Player body && ReferenceEquals(body.GameWorld, __instance))' 'if (false && iPlayer is Player body && ReferenceEquals(body.GameWorld, __instance))' 'initial-kit retired body retained'
Check 'wrong-bot-game' 'ArenaDeminvincibilityCompat.cs' 'ReferenceEquals(Singleton<IBotGame>.Instance, game)' 'true' 'wrong bot game untouched'
Check 'initial-profile' 'ArenaDeminvincibilityCompat.cs' 'body.Profile != null && ReferenceEquals(body.Profile, game.Profile)' 'body.Profile != null' 'initial loading exact profile required'
Check 'missing-own-hook' 'ArenaDeminvincibilityCompat.cs' 'if (!OwnsPatches()) throw new InvalidOperationException("Deminvincibility isolation hooks did not install");' '' 'missing own hook rejected'
Check 'missing-foreign-hook' 'ArenaDeminvincibilityContract.cs' '|| info?.Prefixes.Count(p => p.PatchMethod == methods[i]) != 1' '' 'missing foreign hook rejected'
Check 'new-foreign-hook' 'ArenaDeminvincibilityContract.cs' 'if (foreign.Length != 0 &&' 'if (false &&' 'unknown foreign hook rejected'
Check 'new-foreign-il-hook' 'ArenaDeminvincibilityContract.cs' '.Concat(info.ILManipulators)' '' 'unknown foreign IL hook rejected'
Check 'damage-helper' 'ArenaDeminvincibilityContract.cs' 'if (code.operand is MethodInfo call &&' 'if (false && code.operand is MethodInfo call &&' 'unknown damage helper rejected'
Check 'damage-field' 'ArenaDeminvincibilityContract.cs' 'if (code.operand is FieldInfo field &&' 'if (false && code.operand is FieldInfo field &&' 'unknown damage field rejected'
Check 'lease-clear' 'ArenaDeminvincibilityScope.cs' 'internal void Clear() => _controllers = new ConditionalWeakTable<object, Lease>();' 'internal void Clear() { }' 'clear releases leases'
Check 'after'
foreach ($name in $files) { if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production source changed during checks: $name" } }
Write-Output "DEMINVINCIBILITY_COMPAT_CHECKS_OK; $faults compiled negative controls; evidence: $evidence"
