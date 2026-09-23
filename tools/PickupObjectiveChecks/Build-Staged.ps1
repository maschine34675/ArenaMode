param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$pickupRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pickupInstalled = @{}
foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll','SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $path = Join-Path $SptRoot $relative
    $pickupInstalled[$path] = (Get-FileHash -LiteralPath $path).Hash
}
$clientOutput = & (Join-Path $pickupRepo 'tools/DoubleJumpChecks/Build-Staged.ps1') -SptRoot $SptRoot
$clientOutput | Write-Output
$line = @($clientOutput | Where-Object { $_ -like 'Staged client: *' })
if ($line.Count -ne 1) { throw 'No unique staged client artifact' }
$client = $line[0].Substring('Staged client: '.Length)
$stage = Split-Path (Split-Path $client -Parent) -Parent
$manifestPath = Join-Path $stage 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$targets = Join-Path $stage 'CompatNoDeploy.targets'
$native = [Security.SecurityElement]::Escape($SptRoot)
$clientXml = [Security.SecurityElement]::Escape($client)
$xml = @'
<Project>
  <PropertyGroup><AssemblySearchPaths>GAME/EscapeFromTarkov_Data/Managed;GAME/BepInEx/plugins/spt;$(AssemblySearchPaths)</AssemblySearchPaths></PropertyGroup>
  <ItemGroup>
    <ProjectReference Remove="@(ProjectReference)" />
    <Reference Include="maschine-ArenaMode.Client"><HintPath>CLIENT</HintPath><Private>false</Private></Reference>
    <Reference Update="DrakiaXYZ-BigBrain"><HintPath>GAME/BepInEx/plugins/DrakiaXYZ-BigBrain.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
  <Target Name="SPTClientDeploy" />
</Project>
'@
[IO.File]::WriteAllText($targets, $xml.Replace('GAME', $native).Replace('CLIENT', $clientXml))
try {
    & dotnet build (Join-Path $pickupRepo 'ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj') -c Release --nologo `
        "-p:CustomAfterMicrosoftCommonTargets=$targets" "-p:SPTPath=$stage/install" "-p:SPTUserPath=$stage/install/SPT_Runtime" `
        "-p:OutputPath=$stage/compat" '-p:BuildProjectReferences=false' 2>&1 | Tee-Object -FilePath (Join-Path $stage 'compat-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Staged companion build failed' }
    $compat = Join-Path $stage 'compat/maschine-ArenaMode.BigBrainCompat.dll'
    $manifest | Add-Member -NotePropertyName CompatPath -NotePropertyValue $compat
    $manifest | Add-Member -NotePropertyName CompatSha256 -NotePropertyValue (Get-FileHash -LiteralPath $compat).Hash
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
} finally {
    foreach ($path in $pickupInstalled.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $pickupInstalled[$path]) { throw "Installed artifact changed: $path" }
    }
}
if ((& git -C $pickupRepo rev-parse HEAD) -ne $manifest.SourceCommit -or
    (@(& git -C $pickupRepo status --short) -join "`n") -cne (@($manifest.SourceChanges) -join "`n")) { throw 'Source state changed during build' }
Write-Output "Pickup build manifest: $manifestPath"
Write-Output 'PICKUP OBJECTIVE BUILD PASSED'
