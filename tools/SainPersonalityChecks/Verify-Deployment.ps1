param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function SainHashes {
    $result = @{}
    foreach ($folder in @('BepInEx/plugins/SAIN', 'SPT_Runtime/user/mods/Solarint-SAIN-ServerMod')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $SptPath $folder) -Recurse -File) {
            $result[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    $config = Join-Path $SptPath 'BepInEx/config/me.sol.sain.cfg'
    if (Test-Path -LiteralPath $config) {
        $result[$config] = (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash
    }
    return $result
}
$before = SainHashes
foreach ($project in @('tools/LabsBotSpawnChecks', 'tools/PitFireTeamChecks', 'tools/TeamArenaExpansionChecks')) {
    & dotnet run --project (Join-Path $repo $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Existing Arena checks failed: $project" }
}
& dotnet build (Join-Path $repo 'ArenaMode.Client/ArenaMode.Client.csproj') -c Release --no-restore "-p:SPTPath=$SptPath"
if ($LASTEXITCODE -ne 0) { throw 'Arena client Release build failed' }
foreach ($pair in @(
    @((Join-Path $repo 'ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll'), (Join-Path $SptPath 'BepInEx/plugins/maschine-ArenaMode.Client.dll')),
    @((Join-Path $repo '../Redeploy/Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll'), (Join-Path $SptPath 'BepInEx/plugins/maschine-Redeploy.Core.dll'))
)) {
    if ((Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash) { throw "Live/build mismatch: $($pair[1])" }
}
$after = SainHashes
if ($before.Count -ne $after.Count) { throw 'SAIN files changed during validation' }
foreach ($file in $before.Keys) { if ($before[$file] -ne $after[$file]) { throw "SAIN file changed: $file" } }
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $SptPath 'BepInEx/core'))
$resolver.AddSearchDirectory((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed'))
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$client = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/maschine-ArenaMode.Client.dll'), $reader)
try {
    if (@($client.MainModule.AssemblyReferences | Where-Object { $_.Name -like 'SAIN*' }).Count -ne 0) { throw 'Client acquired a hard SAIN assembly dependency' }
    $plugin = $client.MainModule.GetType('ArenaMode.Plugin')
    $awake = (($plugin.Methods | Where-Object Name -eq 'Awake').Body.Instructions.Operand | ForEach-Object { if ($_ -ne $null) { $_.ToString() } }) -join "`n"
    $destroy = (($plugin.Methods | Where-Object Name -eq 'OnDestroy').Body.Instructions.Operand | ForEach-Object { if ($_ -ne $null) { $_.ToString() } }) -join "`n"
    if (!$awake.Contains('ArenaSainPersonalities::Enable(') -or !$destroy.Contains('ArenaSainPersonalities::Shutdown(')) { throw 'Client adapter lifecycle registration missing' }
    $dependency = @($plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'me.sol.sain' })
    if ($dependency.Count -ne 1 -or $dependency[0].ConstructorArguments[1].Value -ne 2) { throw 'SAIN dependency is not soft/optional' }
    Write-Output "ARENA_SAIN_PERSONALITY_DEPLOYMENT_PASSED ($($before.Count) unchanged SAIN files; Release/live client and Core hashes match)"
}
finally { $client.Dispose(); $resolver.Dispose() }
