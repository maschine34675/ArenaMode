param(
    [string]$SptPath=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))),
    [string]$ForeignDll=(Join-Path $PSScriptRoot '../../artifacts/compat-seven-20261025/items/UseItemsAnywhere-release/BepInEx/plugins/UseItemsAnywhere/UseItemsAnywhere.dll')
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work=Join-Path $repo ('artifacts/use-items-anywhere-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$snapshot=Read-ArenaInstalledSnapshot $SptPath
$inputs=@{}
foreach($path in @((Join-Path $repo 'ArenaMode.Client/Kits/ArenaBackpackReload.cs'),(Join-Path $repo 'ArenaMode.Client/Kits/ArenaReachPatches.cs')) + @(Get-ChildItem -LiteralPath (Join-Path $repo 'ArenaMode.Client/Compat/UseItemsAnywhere') -File | ForEach-Object FullName) + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object FullName)) {
    $inputs[$path]=(Get-FileHash -LiteralPath $path).Hash
}
function Build([string]$name,[string]$property='', [string]$replacement='') {
    $folder=Join-Path $work $name
    $args=@('build',(Join-Path $PSScriptRoot 'UseItemsAnywhereChecks.csproj'),'-c','Release','--nologo',"-p:SptRoot=$SptPath","-p:OutputPath=$folder/bin/","-p:BaseIntermediateOutputPath=$folder/obj/")
    if($property){$args+="-p:${property}=$replacement"}
    $output=& dotnet @args 2>&1 | Out-String
    [void][IO.Directory]::CreateDirectory($folder)
    [IO.File]::WriteAllText((Join-Path $folder 'build.log'),$output)
    if($LASTEXITCODE){throw $output}
    return (Join-Path $folder 'bin/UseItemsAnywhereChecks.exe')
}
function Run([string]$exe,[string[]]$case,[string]$failure='') {
    $output=& $exe @case 2>&1 | Out-String
    $exit=$LASTEXITCODE
    $log=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($exe))) (($case -join '-')+'.log')
    [IO.File]::WriteAllText($log,$output)
    if($failure){
        if(!$exit -or !$output.Contains('ASSERT: '+$failure)){throw "Mutant did not fail the intended assertion: $output"}
        Write-Output "Rejected mutant: $failure"
    } else {
        if($exit -or !$output.Contains('USE_ITEMS_ANYWHERE_CHECKS_PASSED')){throw $output}
        Write-Output $output.Trim()
    }
}
try {
    $native=& (Join-Path $PSScriptRoot 'Verify-Native.ps1') -SptPath $SptPath -ForeignDll $ForeignDll 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $work 'native.log'),$native)
    Write-Output $native.Trim()
    $exe=Build 'production'
    foreach($case in @(@('absent'),@('fixed','arena-first','first'),@('fixed','arena-first','last'),@('fixed','foreign-first','first'),@('fixed','foreign-first','last'),@('old','arena-first','last'),@('old','foreign-first','first'),@('drift'),@('newer','foreign-first','first'))) { Run $exe $case }
    foreach($mutation in @(
        @('foreign-append-removed','ArenaMode.Client/Kits/ArenaBackpackReload.cs','ReloadSource',' || UseItemsAnywhereCompat.OwnsItemAccess','','foreign restricted slots survive Arena on/off','last'),
        @('foreign-reach-removed','ArenaMode.Client/Kits/ArenaReachPatches.cs','ReachSource',' && !UseItemsAnywhereCompat.OwnsItemAccess','','foreign reach refusal survives either postfix order','first')
    )) {
        $source=[IO.File]::ReadAllText((Join-Path $repo $mutation[1]))
        if($source.Split(@($mutation[3]),[StringSplitOptions]::None).Count -ne 2){throw "Mutation anchor not unique: $($mutation[0])"}
        $changed=Join-Path $work ($mutation[0]+'.cs')
        [IO.File]::WriteAllText($changed,$source.Replace($mutation[3],$mutation[4]))
        $mutant=Build $mutation[0] $mutation[2] $changed
        Run $mutant @('fixed','arena-first',$mutation[6]) $mutation[5]
    }
    foreach($path in $inputs.Keys){if((Get-FileHash -LiteralPath $path).Hash -ne $inputs[$path]){throw "Input changed: $path"}}
    [ordered]@{completedUtc=[DateTime]::UtcNow.ToString('o');sourceHashes=$inputs;foreignHash=(Get-FileHash -LiteralPath $ForeignDll).Hash;harmonyHash=(Get-FileHash -LiteralPath (Join-Path $SptPath 'BepInEx/core/0Harmony.dll')).Hash;runtime='net472 desktop CLR with installed Harmony and explicit EFT/loader doubles';scenarios=9;mutantsRejected=2;nativeGameplay='root-owned, not tested here'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'manifest.json')
    Write-Output "USE ITEMS ANYWHERE CHECKS PASSED; evidence: $work"
} finally { Assert-ArenaInstalledSnapshotUnchanged $snapshot $SptPath }
