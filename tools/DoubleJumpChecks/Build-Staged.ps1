param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaGame = [IO.Path]::GetFullPath($SptRoot)
$arenaStage = Join-Path $arenaRepo ('artifacts\double-jump-build\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaStage) | Out-Null
$arenaInstalled = @(
    (Join-Path $arenaGame 'BepInEx\plugins\maschine-ArenaMode.Client.dll'),
    (Join-Path $arenaGame 'SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll'),
    (Join-Path $arenaGame 'BepInEx\plugins\maschine-Redeploy.Core.dll'))
$arenaHashes = @{}
foreach ($arenaPath in $arenaInstalled) {
    $arenaHashes[$arenaPath] = (Get-FileHash -LiteralPath $arenaPath -Algorithm SHA256).Hash
}

# Imported after the real client project. Replace only the external project
# reference with its already installed API, and override deployment targets.
# SPTPath is also redirected globally: even an unexpected deploy target can only
# write into the isolated stage, never into the installation under GPU test.
$arenaTargets = Join-Path $arenaStage 'NoDeploy.targets'
$arenaEscapedGame = [Security.SecurityElement]::Escape($arenaGame)
$arenaXml = @'
<Project>
  <PropertyGroup>
    <AssemblySearchPaths>GAME_ROOT\EscapeFromTarkov_Data\Managed;GAME_ROOT\BepInEx\plugins\spt;$(AssemblySearchPaths)</AssemblySearchPaths>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Remove="@(ProjectReference)" />
    <Reference Include="Redeploy.Core">
      <HintPath>GAME_ROOT\BepInEx\plugins\maschine-Redeploy.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  <Target Name="SPTClientDeploy" />
  <Target Name="DeployArenaAudioAssets" />
</Project>
'@
[IO.File]::WriteAllText($arenaTargets, $arenaXml.Replace('GAME_ROOT', $arenaEscapedGame))
$arenaProject = Join-Path $arenaRepo 'ArenaMode.Client\ArenaMode.Client.csproj'
$arenaOutput = Join-Path $arenaStage 'out'
$arenaBuildExit = -1
try {
    & dotnet build $arenaProject -c Release --nologo `
        "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets" `
        "-p:SPTPath=$arenaStage\install" "-p:SPTUserPath=$arenaStage\install\SPT_Runtime" `
        "-p:OutputPath=$arenaOutput" '-p:BuildProjectReferences=false' 2>&1 |
        Tee-Object -FilePath (Join-Path $arenaStage 'build.log')
    $arenaBuildExit = $LASTEXITCODE
}
finally {
    foreach ($arenaPath in $arenaInstalled) {
        if ((Get-FileHash -LiteralPath $arenaPath -Algorithm SHA256).Hash -ne $arenaHashes[$arenaPath]) {
            throw "Installed artifact changed during staged build: $arenaPath"
        }
    }
}
if ($arenaBuildExit -ne 0) { throw "Staged client build failed: $arenaBuildExit" }
$arenaDll = Join-Path $arenaOutput 'maschine-ArenaMode.Client.dll'
if (-not (Test-Path -LiteralPath $arenaDll)) { throw 'Staged client DLL missing' }
$arenaManifest = [ordered]@{
    SourceCommit = (& git -C $arenaRepo rev-parse HEAD)
    SourceChanges = @(& git -C $arenaRepo status --short)
    ClientPath = $arenaDll
    ClientSha256 = (Get-FileHash -LiteralPath $arenaDll -Algorithm SHA256).Hash
    InstalledUnchanged = $arenaHashes
    Deployed = $false
    LiveGpuTested = $false
}
$arenaManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $arenaStage 'manifest.json')
Write-Output "Staged client: $arenaDll"
Write-Output 'ARENA_DOUBLE_JUMP_STAGED_BUILD_PASSED'
