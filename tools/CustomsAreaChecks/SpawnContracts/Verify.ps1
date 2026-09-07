param([string]$NativeRoot = 'D:/SPT41/Development/_decompiled')
$ErrorActionPreference = 'Stop'
$spawnRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$spawnSource = Join-Path $spawnRepo 'ArenaMode.Client/Areas/ArenaAreaSpawnPolicy.cs'
$spawnOriginal = Get-Content -LiteralPath $spawnSource -Raw
function Assert-SpawnContract([bool]$Pass, [string]$Message) {
    if (!$Pass) { throw "Spawn contract drift: $Message" }
}
function Read-Native([string]$Relative) { Get-Content -LiteralPath (Join-Path $NativeRoot $Relative) -Raw }
function Check-Enum([string]$Relative, [hashtable]$Members) {
    $native = Read-Native $Relative
    foreach ($name in $Members.Keys) {
        Assert-SpawnContract ($native -match ('\b' + [regex]::Escape($name) + '\s*=\s*' + $Members[$name] + '\b')) "$Relative member $name"
    }
}
Check-Enum 'EFT/EPlayerSide.cs' @{ Usec='1'; Bear='2'; Savage='4' }
Check-Enum 'EFT/EPlayerSideMask.cs' @{ None='0'; Usec='1'; Bear='2'; Savage='4'; Pmc='3'; All='7' }
Check-Enum 'EFT.Game.Spawning/ESpawnCategoryMask.cs' @{ None='0'; Player='1'; Bot='2'; Boss='4'; Coop='8'; Group='0x10'; Opposite='0x20'; BotPmc='0x40'; All='0x47' }
$spawnInterface = Read-Native 'EFT.Game.Spawning/ISpawnPoint.cs'
Assert-SpawnContract ($spawnInterface -match 'EPlayerSideMask\s+Sides\s*\{\s*get;' ) 'native side property'
Assert-SpawnContract ($spawnInterface -match 'ESpawnCategoryMask\s+Categories\s*\{\s*get;') 'native category property'
$spawnLocalGame = Read-Native 'EFT/BaseLocalGame.cs'
Assert-SpawnContract ($spawnLocalGame.Contains('public Profile Profile { get; set; }')) 'loading profile API'
Assert-SpawnContract ($spawnLocalGame.IndexOf('game.Profile = profile;') -ge 0) 'profile assigned during creation'
Assert-SpawnContract ($spawnLocalGame.IndexOf('game.Profile = profile;') -lt $spawnLocalGame.IndexOf('public async Task Run(')) 'creation profile is available before Run'

# These source bindings complement the executable policy checks. They are not
# a claim that this standalone process can run Unity scene loading/respawning.
$spawnRuntime = [regex]::Replace((Get-Content -LiteralPath (Join-Path $spawnRepo 'ArenaMode.Client/Areas/ArenaAreaRuntime.cs') -Raw), '\s+', '')
foreach ($binding in @(
    'ArenaAreaSpawnPolicy.IsPlayerPoint(p,_game?.Profile?.Info?.Side)',
    'allowed.Keys.Any(IsPlayerPoint)',
    'Connected.Where(IsPlayerPoint)',
    'Connected.Where(p=>IsPlayerPoint(p)&&!p.SpawnBlocked&&AllowsSpawn(p))',
    'intrequiredBots=_area.MaxTeamSize*2-1;',
    'bots.Length<requiredBots',
    'BurstSpawnPointSelector.MinSpawnDistance)<requiredBots',
    'ArenaToasts.Show(_area.DisplayName+'
)) { Assert-SpawnContract ($spawnRuntime.Contains($binding)) "production runtime binding: $binding" }
Assert-SpawnContract (!$spawnRuntime.Contains('FACTORYSOUTH')) 'no Factory-only area-ready toast'

function Invoke-SpawnChecks([string]$Project, [string]$Expected, [bool]$Pass) {
    $spawnPrevious = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $spawnOutput = (& dotnet run --project $Project -c Release 2>&1 | ForEach-Object { $_.ToString() } | Out-String)
        $spawnExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $spawnPrevious }
    if (($Pass -and $spawnExit -ne 0) -or (!$Pass -and $spawnExit -eq 0) -or !$spawnOutput.Contains($Expected)) {
        throw "Spawn check outcome mismatch (expectedPass=$Pass, exit=$spawnExit): $spawnOutput"
    }
    Write-Output "Spawn checks verified: expectedPass=$Pass; $Expected"
    if ($Pass) { Write-Output $spawnOutput.Trim() }
}
$spawnProject = Join-Path $PSScriptRoot 'SpawnContracts.csproj'
Invoke-SpawnChecks $spawnProject 'ARENA_CUSTOMS_SPAWN_CONTRACTS_PASSED' $true

# Mutate only isolated, ignored copies, never linked production files.
$spawnWork = Join-Path $PSScriptRoot ('.work/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $spawnWork -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Destination $spawnWork
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $spawnWork 'Mutant.csproj')
$spawnMutants = @(
    @{ Before='(available & required) == required'; After='true'; Expected='ASSERT: Savage-only BotPmc player marker must not become a USEC entry or respawn' },
    @{ Before='(available & required) == required'; After='available == required'; Expected='ASSERT: Pmc mask accepts each PMC faction' },
    @{ Before='required != EPlayerSideMask.None'; After='true'; Expected='ASSERT: Missing, Scav or unsupported profile side must fail closed' },
    @{ Before='(available & ~EPlayerSideMask.All) == 0'; After='true'; Expected='ASSERT: Unknown bits cannot authorize a known faction' }
)
foreach ($mutant in $spawnMutants) {
    Assert-SpawnContract ($spawnOriginal.Contains($mutant.Before)) ('mutation target missing: ' + $mutant.Before)
    $spawnOriginal.Replace($mutant.Before, $mutant.After) | Set-Content -LiteralPath (Join-Path $spawnWork 'ArenaAreaSpawnPolicy.cs')
    Invoke-SpawnChecks (Join-Path $spawnWork 'Mutant.csproj') $mutant.Expected $false
}
Assert-SpawnContract ((Get-Content -LiteralPath $spawnSource -Raw) -ceq $spawnOriginal) 'production source changed during controls'
Invoke-SpawnChecks $spawnProject 'ARENA_CUSTOMS_SPAWN_CONTRACTS_PASSED' $true
Write-Output 'ARENA_CUSTOMS_SPAWN_CHECKS_PASSED (4 targeted mutants; production source unchanged)'
