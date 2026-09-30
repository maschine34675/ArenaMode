param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/barbed-death-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$capturePath = Join-Path $repo 'ArenaMode.Client/Combat/ArenaBarbedDeathPatch.cs'
$hudPath = Join-Path $repo 'ArenaMode.Client/Hud/ArenaHud.cs'
$capture = [IO.File]::ReadAllText($capturePath)
$hud = [IO.File]::ReadAllText($hudPath)

function Get-Method([string]$source, [string]$signature) {
 $begin = $source.IndexOf($signature, [StringComparison]::Ordinal)
 if ($begin -lt 0 -or $source.IndexOf($signature, $begin + $signature.Length, [StringComparison]::Ordinal) -ge 0) {
  throw ('Expected exactly one production method: ' + $signature)
 }
 $brace = $source.IndexOf('{', $begin)
 if ($brace -lt 0) { throw ('Missing opening brace: ' + $signature) }
 $depth = 0
 for ($cursor = $brace; $cursor -lt $source.Length; ++$cursor) {
  if ($source[$cursor] -eq '{') { ++$depth }
  if ($source[$cursor] -eq '}') { --$depth; if ($depth -eq 0) { return $source.Substring($begin, $cursor - $begin + 1) } }
 }
 throw ('Unbalanced method: ' + $signature)
}

# Assert the production wiring and the native reason this separate UI path is
# needed. These source-contract checks are not a simulation of native damage.
$combat = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/CombatPatches.cs'))
if (!$combat.Contains('new ArenaBarbedDeathPatch().Enable();') -or !$combat.Contains('ArenaBarbedDeathPatch.Reset();')) {
 throw 'Barbed capture is not enabled and reset with Arena combat patches.'
}
$nativePlayerPath = Join-Path $repo '../_decompiled/EFT/Player.cs'
$nativeColliderPath = Join-Path $repo '../_decompiled/BodyPartCollider.cs'
$nativePlayer = [IO.File]::ReadAllText($nativePlayerPath)
$nativeCollider = [IO.File]::ReadAllText($nativeColliderPath)
if (!$nativePlayer.Contains('protected IPlayer LastAggressor;')) { throw 'Native protected aggressor field contract changed.' }
$nativeBarb = Get-Method $nativeCollider 'public bool ProceedBarb()'
if (!$nativeBarb.Contains('DamageType = EDamageType.Barbed') -or !$nativeBarb.Contains('Player = null')) {
 throw 'Native wire damage no longer has the verified null attacker.'
}
$nativeDamage = Get-Method $nativePlayer 'public virtual void ApplyDamageInfo('
$clear = $nativeDamage.IndexOf('LastAggressor = player?.iPlayer;')
$apply = $nativeDamage.IndexOf('ActiveHealthController.ApplyDamage(')
if ($clear -lt 0 -or $apply -le $clear) { throw 'Native damage no longer clears aggressor before applying HP loss.' }
$nativeDeath = Get-Method $nativePlayer 'public virtual void OnDead(EDamageType damageType)'
if ($nativeDeath -notmatch 'if\s*\(LastAggressor != null\)\s*\{\s*OnBeenKilledByAggressor\(') {
 throw 'Native death no longer omits kill callback without an aggressor.'
}

$adapterPath = Join-Path $run 'HudAdapter.cs'
$adapter = "using ArenaMode.Combat;`nusing ArenaMode.Config;`nusing ArenaMode.Modes;`nnamespace ArenaMode.Hud;`ninternal static partial class ArenaHud {`n" +
 (Get-Method $hud 'internal static void ShowBarbedDeath(') + "`n}`n"
[IO.File]::WriteAllText($adapterPath, $adapter)
$hashes = [ordered]@{}
foreach ($path in @($capturePath, $hudPath, $nativePlayerPath, $nativeColliderPath)) {
 $hashes[[IO.Path]::GetFullPath($path)] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'source-hashes.json')
$project = Join-Path $PSScriptRoot 'BarbedDeathChecks.csproj'
function Invoke-Case([string]$name, [string]$source, [bool]$probe = $false) {
 $caseRoot = Join-Path $run $name
 $arguments = @('run', '--project', $project, '-c', 'Release', "-p:CaptureSource=$source", "-p:HudAdapterSource=$adapterPath",
  "-p:BaseIntermediateOutputPath=$caseRoot/obj/", "-p:BaseOutputPath=$caseRoot/bin/")
 if ($probe) { $arguments += @('-p:UseRealHarmony=true', '--', '--probe-harmony') }
 $out = & dotnet @arguments 2>&1
 $code = $LASTEXITCODE
 $text = $out -join "`n"
 [IO.File]::WriteAllText((Join-Path $run ($name + '.log')), $text)
 return @{Code = $code; Text = $text}
}
$baseline = Invoke-Case 'baseline' $capturePath
Write-Output $baseline.Text
if ($baseline.Code -ne 0 -or !$baseline.Text.Contains('BARBED DEATH CONTRACTS PASSED')) { throw 'Production capture baseline failed.' }

# Separate process: old Unity HarmonyX detours can be unsupported on desktop
# CLR. Its absence is recorded explicitly and never called native acceptance.
$probe = Invoke-Case 'harmony-probe' $capturePath $true
if ($probe.Code -eq 0 -and $probe.Text.Contains('BARBED HARMONY PROTECTED FIELD PROBE PASSED')) {
 Write-Output $probe.Text
 $probeStatus = 'Passed on desktop CLR; not native Unity/Mono acceptance.'
} else {
 # Only this observed HarmonyX initialization incompatibility is an unavailable
 # probe. A failed target lookup, field injection, detour or assertion is fatal.
 $knownStartupFailure = $probe.Text.Contains("System.TypeInitializationException: The type initializer for 'HarmonyLib.AccessTools'") -and
  $probe.Text.Contains('System.NullReferenceException:') -and
  $probe.Text.Contains('HarmonyLib.MethodInvoker.GetHandler(MethodInfo methodInfo, Boolean directBoxValueAccess)') -and
  $probe.Text.Contains('HarmonyLib.AccessTools..cctor()')
 if (!$knownStartupFailure -or $probe.Text.Contains('error CS') -or $probe.Text.Contains('CHECK:')) {
  throw ('Harmony probe failed outside the known desktop startup limitation: ' + $probe.Text)
 }
 $probeStatus = 'Unavailable on desktop CLR: installed HarmonyX AccessTools static initialization fails in MethodInvoker.GetHandler before patch binding. Native field and injection signature checked; Unity binding remains untested.'
 Write-Output $probeStatus
}
[IO.File]::WriteAllText((Join-Path $run 'harmony-probe-status.txt'), $probeStatus)

$mutations = @(
 @{Name='missing-body-dedupe'; Old='|| _seen.TryGetValue(__instance, out _)'; New='|| false';
   ExtraOld='_seen.Add(__instance, new Reported());'; ExtraNew='// negative control: no presentation dedupe';
   Failure='same physical death displayed once'},
 @{Name='missing-aggressor-guard'; Old='|| ___LastAggressor != null'; New=''; Failure='existing aggressor avoids duplicate credited kill notice'}
)
foreach ($mutation in $mutations) {
 if (!$capture.Contains($mutation.Old)) { throw ('Missing negative-control anchor: ' + $mutation.Name) }
 $changed = $capture.Replace($mutation.Old, $mutation.New)
 if ($mutation.ExtraOld) {
  if (!$changed.Contains($mutation.ExtraOld)) { throw ('Missing second negative-control anchor: ' + $mutation.Name) }
  $changed = $changed.Replace($mutation.ExtraOld, $mutation.ExtraNew)
 }
 $path = Join-Path $run ($mutation.Name + '.cs')
 [IO.File]::WriteAllText($path, $changed)
 $result = Invoke-Case $mutation.Name $path
 if ($result.Code -eq 0 -or $result.Text.Contains('error CS') -or !$result.Text.Contains('CHECK: ' + $mutation.Failure)) {
  throw ($mutation.Name + ' did not fail the intended runtime assertion: ' + $result.Text)
 }
 Write-Output ('Negative control rejected: ' + $mutation.Name)
}
$restored = Invoke-Case 'restored' $capturePath
if ($restored.Code -ne 0 -or !$restored.Text.Contains('BARBED DEATH CONTRACTS PASSED')) { throw $restored.Text }
Write-Output ('Evidence: ' + $run)
Write-Output 'BARBED_DEATH_CHECKS_PASSED'
