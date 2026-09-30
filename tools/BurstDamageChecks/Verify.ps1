param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/burst-damage-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$sourcePath = Join-Path $repo 'ArenaMode.Client/Hud/DamageNumbersElement.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$capturePath = Join-Path $repo 'ArenaMode.Client/Combat/CombatPatches.cs'
$capture = [IO.File]::ReadAllText($capturePath)
if (!$capture.Contains('VictimBodyId = victim.GetInstanceID(),')) { throw 'Physical body identity is not captured from the native victim.' }
$hashes = [ordered]@{}
foreach ($path in @($sourcePath, $capturePath, (Join-Path $repo 'ArenaMode.Client/Combat/ArenaDamage.cs'))) {
 $hashes[[IO.Path]::GetFullPath($path)] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'source-hashes.json')
$project = Join-Path $PSScriptRoot 'BurstDamageChecks.csproj'
function Invoke-Case([string]$name, [string]$sourceFile) {
 $caseRoot = Join-Path $run $name
 $output = & dotnet run --project $project -c Release "-p:PresentationSource=$sourceFile" "-p:BaseIntermediateOutputPath=$caseRoot/obj/" "-p:BaseOutputPath=$caseRoot/bin/" 2>&1
 $code = $LASTEXITCODE
 $text = $output -join "`n"
 [IO.File]::WriteAllText((Join-Path $run ($name + '.log')), $text)
 return @{Code=$code;Text=$text}
}
$baseline = Invoke-Case 'baseline' $sourcePath
Write-Output $baseline.Text
if ($baseline.Code -ne 0 -or !$baseline.Text.Contains('BURST DAMAGE CONTRACTS PASSED')) { throw 'Production burst baseline failed.' }
$mutations = @(
 @{Name='cross-target';Old='candidate.VictimBodyId == damage.VictimBodyId';New='true';Failure='different physical targets never combine'},
 @{Name='ignore-window';Old='gap > BurstGapSeconds';New='gap > 2f';Failure='gap beyond quarter-second starts a new burst'},
 @{Name='round-before-sum';Old='entry.Flesh += Mathf.Max(0f, damage.BodyDamage);';New='entry.Flesh += Mathf.RoundToInt(Mathf.Max(0f, damage.BodyDamage));';Failure='float flesh and armor sums round independently after accumulation'},
 @{Name='armor-channel-mix';Old='entry.Armor += Mathf.Max(0f, damage.ArmorDamage);';New='entry.Armor += Mathf.Max(0f, damage.BodyDamage);';Failure='disabled preserves independent rounded hits'},
 @{Name='merge-after-death';Old='entry.MergeOpen &= !damage.Kill;';New='entry.MergeOpen &= true;';Failure='killing hit seals the old physical life group'},
 @{Name='revive-on-toggle';Old='_pool[i].MergeOpen = false;';New='_pool[i].MergeOpen = _pool[i].MergeOpen;';Failure='off-on toggle cannot revive a previously visible group'},
 @{Name='no-width-reservation';Old='entry.Size = _combining ? _combinedSize : entry.Text.GetPreferredValues();';New='entry.Size = entry.Text.GetPreferredValues();';Failure='reserved burst width prevents growing labels from overlapping'},
 @{Name='too-few-lanes';Old='reserveRise ? PoolSize * 2 : 10';New='10';Failure='full burst pool has enough vertical lanes'},
 @{Name='no-rise-reservation';Old='entry.PendingFootprint.height += reservedRise;';New='entry.PendingFootprint.height += 0f;';Failure='staggered rise cannot enter a persistent burst'},
 @{Name='forget-disabled-burst';Old='reserveRise = _pool[i].Active && _pool[i].BurstLayout';New='reserveRise = false && _pool[i].Active && _pool[i].BurstLayout';Failure='staggered rise cannot enter a persistent burst'},
 @{Name='unscaled-rise-envelope';Old='RisePixels / scaleFactor : 0f';New='RisePixels : 0f';Failure='staggered rise cannot enter a persistent burst'}
)
foreach ($mutation in $mutations) {
 if (!$source.Contains($mutation.Old)) { throw ('Missing mutation anchor: ' + $mutation.Name) }
 $path = Join-Path $run ($mutation.Name + '.cs')
 [IO.File]::WriteAllText($path,$source.Replace($mutation.Old,$mutation.New))
 $result = Invoke-Case $mutation.Name $path
 if ($result.Code -eq 0 -or $result.Text.Contains('error CS') -or !$result.Text.Contains('CHECK: ' + $mutation.Failure)) {
  throw ($mutation.Name + ' did not fail its intended runtime assertion: ' + $result.Text)
 }
 Write-Output ('Negative control rejected: ' + $mutation.Name)
}
$restored = Invoke-Case 'restored' $sourcePath
if ($restored.Code -ne 0 -or !$restored.Text.Contains('BURST DAMAGE CONTRACTS PASSED')) { throw ('Restored production failed: ' + $restored.Text) }
Write-Output $restored.Text
Write-Output ('Evidence: ' + $run)
Write-Output 'BURST_DAMAGE_CHECKS_PASSED'
