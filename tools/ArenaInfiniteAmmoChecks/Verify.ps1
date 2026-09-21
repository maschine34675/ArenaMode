param([ValidateSet('Ammo','Backpack')][string]$Mode='Ammo',
    [string]$SptPath=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$isAmmo=$Mode -eq 'Ammo'
$project=if($isAmmo){Join-Path $PSScriptRoot 'ArenaInfiniteAmmoChecks.csproj'}else{Join-Path $PSScriptRoot '../BackpackReloadChecks/BackpackReloadChecks.csproj'}
$sourcePath=Join-Path $repo $(if($isAmmo){'ArenaMode.Client/Pickups/ArenaInfiniteAmmo.cs'}else{'ArenaMode.Client/Kits/ArenaBackpackReload.cs'})
$property=if($isAmmo){'AmmoSource'}else{'ReloadSource'}
$assemblyName=if($isAmmo){'ArenaInfiniteAmmoChecks'}else{'BackpackReloadChecks'}
$token=if($isAmmo){'ARENA_INFINITE_AMMO_BEHAVIOR_PASSED'}else{'ARENA_BACKPACK_RELOAD_BEHAVIOR_PASSED'}
$work=Join-Path $repo ('artifacts/inventory-extension-checks/'+$Mode+'/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
$inputs=@{}
$paths=@($sourcePath,(Join-Path $repo 'ArenaMode.Client/Plugin.cs'),(Join-Path $repo 'ArenaMode.Client/Pickups/ArenaPowerupState.cs'))
$paths+=@(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object FullName)
if (!$isAmmo) { $paths+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../BackpackReloadChecks') -File | ForEach-Object FullName) }
foreach($path in $paths){$inputs[$path]=(Get-FileHash -LiteralPath $path).Hash}
$installed=@{}
foreach($rel in @('BepInEx/plugins/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-Redeploy.Core.dll','SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll','EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) {$p=Join-Path $SptPath $rel;$installed[$p]=(Get-FileHash -LiteralPath $p).Hash}
$source=[IO.File]::ReadAllText($sourcePath).Replace("`r`n","`n")
function Run([string]$name,[string]$path,[string]$failure='') {
    $out=Join-Path $work $name;[void][IO.Directory]::CreateDirectory($out)
    $build=& dotnet build $project -c Release --nologo "-p:$property=$path" "-p:BaseIntermediateOutputPath=$out/obj/" "-p:OutputPath=$out/bin/" 2>&1 | Out-String
    $code=$LASTEXITCODE;[IO.File]::WriteAllText((Join-Path $out 'build.log'),$build);if($code){throw $build}
    $result=& dotnet (Join-Path $out "bin/$assemblyName.dll") 2>&1 | Out-String
    $code=$LASTEXITCODE;[IO.File]::WriteAllText((Join-Path $out 'result.log'),$result)
    if($failure){if(!$code -or !$result.Contains('ASSERT: '+$failure)){throw "Fault $name did not fail correctly: $result"};Write-Output "Rejected ${name}: $failure"}
    else{if($code -or !$result.Contains($token)){throw $result};Write-Output $result.Trim()}
}
try {
    $wire=if($isAmmo){'Pickups.ArenaInfiniteAmmo.Enable();'}else{'Kits.ArenaBackpackReload.Enable();'}
    if(![IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Plugin.cs')).Contains($wire)){throw 'Missing plugin wiring'}
    $native=& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Native.ps1') -Mode $Mode -SptPath $SptPath 2>&1 | Out-String
    $code=$LASTEXITCODE;[IO.File]::WriteAllText((Join-Path $work 'native.log'),$native);if($code){throw $native};Write-Output $native.Trim()
    Run 'production' $sourcePath
    $faults=if($isAmmo){@(
        @('spent-source','shot.IsUsed = true;','source.IsUsed = true; shot.IsUsed = true;','loaded gun bypasses only consumption'),
        @('zero-mag-counter','AmmoCountInMagAfterShot = rounds','AmmoCountInMagAfterShot = 0','native counters retain magazine rounds'),
        @('expiry-removed','&& ArenaPowerups.Read(player).AmmoSeconds > 0f','','exact expiry restores native consumption'),
        @('hands-identity-removed','&& ReferenceEquals(player.HandsController, op.Controller)','','unsupported or foreign scope uses native consumption')
    )}else{@(
        @('ordinary-leak','!ArenaSession.IsActive','false','ordinary foreign or inactive inventory untouched'),
        @('duplicate-results','if (!output.Any(existing => ReferenceEquals(existing, magazine)))','if (true)','other reach mods cannot duplicate magazine candidates'),
        @('predicate-ignored','Backpack, candidates, (Predicate<Magazine>)predicate','Backpack, candidates, null','native predicate respected'),
        @('late-body-recheck-removed','if (!Wanted(__instance) || !ReferenceEquals(backpack,','if (!ReferenceEquals(backpack,','predicate body replacement prevents publication')
    )}
    foreach($fault in $faults) {
        if($source.Split(@($fault[1]),[StringSplitOptions]::None).Count -ne 2){throw "Fault anchor not unique: $($fault[0])"}
        $path=Join-Path $work ($fault[0]+'.cs');[IO.File]::WriteAllText($path,$source.Replace($fault[1],$fault[2]));Run $fault[0] $path $fault[3]
    }
    foreach($p in $inputs.Keys){if((Get-FileHash -LiteralPath $p).Hash -ne $inputs[$p]){throw "Source changed: $p"}}
} finally {foreach($p in $installed.Keys){if((Get-FileHash -LiteralPath $p).Hash -ne $installed[$p]){throw "Installed file changed: $p"}}}
[ordered]@{sourceHashes=$inputs;installedHashes=$installed;faultsRejected=$faults.Count;completedUtc=[DateTime]::UtcNow.ToString('o');limitation='Source-linked adapters, explicit native doubles and installed IL; no Unity/Harmony execution or deployment.'} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $work 'manifest.json')
$label=if($isAmmo){'INFINITE AMMO'}else{'BACKPACK RELOAD'}
Write-Output "ARENA $label CHECKS PASSED; evidence: $work"
