$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work=Join-Path $root ('artifacts/pft-kit-integration/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work)|Out-Null
$noDeploy='-p:CustomAfterMicrosoftCommonTargets='+(Join-Path $root 'tools/AirPitChecks/NoDeploy.targets')
function Check([string]$name,[string[]]$arguments,[string]$token) {
    $result=& dotnet @arguments 2>&1; $code=$LASTEXITCODE
    $result|Set-Content -LiteralPath (Join-Path $work ($name+'.log'))
    $text=$result -join "`n"
    if($code -ne 0 -or ($token -and $text -notmatch $token)) {throw "$name failed: $text"}
    Write-Output "$name passed"
    if($token) { $result | Where-Object {$_ -match $token} | Select-Object -Last 1 }
}
Push-Location $root
try {
    Check 'client-library' @('build','ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj','-c','Release',$noDeploy,'--nologo') ''
    Check 'unit-suite' @('test','ArenaMode.Tests/ArenaMode.Tests.csproj','-c','Release',$noDeploy,'--nologo') ''
    Check 'supplies' @('run','--project','tools/KitPolishChecks/Supplies','-c','Release','--',$root,
        'D:/SPT41/SPT_Runtime/SPT_Data/database/templates/items.json') 'KIT_SUPPLIES_ASSERTIONS_PASSED'
    foreach($pair in @(
        @('TeamArenaLoadoutChecks','Team Arena loadout checks passed'),
        @('TeamArenaExpansionChecks','TEAM_ARENA_EXPANSION_CHECKS_PASSED'),
        @('PitFireTeamRoundLifecycleChecks','PITFIRETEAM_ROUND_LIFECYCLE_CHECKS_PASSED'),
        @('ZombieTeamChecks','ZOMBIE_TEAM_CHECKS_PASSED'),
        @('ZombieKitWarmupChecks','ZOMBIE_KIT_WARMUP_CHECKS_PASSED'),
        @('OpticPreloadChecks','ARENA_OPTIC_PRELOAD_CONTRACTS_PASSED'))){
        Check $pair[0] @('run','--project',('tools/'+$pair[0]),'-c','Release','--nologo') $pair[1]
    }
    Check 'preview-native-api' @('build','tools/ArenaCharacterPreviewChecks/HookCompile.csproj','-c','Release','--nologo') ''
} finally { Pop-Location }
Write-Output "PFT_TACTICAL_KIT_INTEGRATION_OK logs=$work"
