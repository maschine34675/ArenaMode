param(
    [Parameter(Mandatory = $true)][ValidateSet('Scope', 'Build')][string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaArtifacts = Join-Path $arenaRepo 'artifacts\pmc-capture'
New-Item -ItemType Directory -Path $arenaArtifacts -Force | Out-Null
function Get-PmcHash([string]$Path) {
    $arenaStream = [IO.File]::OpenRead($Path)
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash($arenaStream)).Replace('-', '') }
    finally { $arenaSha.Dispose(); $arenaStream.Dispose() }
}
function Invoke-PmcDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "PMC capture dotnet failure ($LASTEXITCODE)." }
}
Push-Location $arenaRepo
Start-Transcript -Path (Join-Path $arenaArtifacts "$Stage.log") -Force | Out-Null
try {
    if ($Stage -eq 'Scope') {
        Invoke-PmcDotnet @('run', '--project', 'tools\PolygonAreaChecks\Scope\Scope.csproj', '-c', 'Release', "-p:SPTPath=$arenaSpt")
        & (Join-Path $PSScriptRoot 'NegativeControls.ps1') -SptRoot $arenaSpt
    }
    else {
        # A fresh staging root keeps all automatic deployment outside Syncthing.
        # The only junctions are read-only build REFERENCES. Outputs go to the
        # staging plugin directory, never into either referenced directory.
        $arenaStage = Join-Path $arenaArtifacts ('staging-' + [Guid]::NewGuid().ToString('N'))
        $arenaStageData = New-Item -ItemType Directory -Path (Join-Path $arenaStage 'EscapeFromTarkov_Data')
        $arenaStagePlugins = New-Item -ItemType Directory -Path (Join-Path $arenaStage 'BepInEx\plugins')
        New-Item -ItemType Junction -Path (Join-Path $arenaStageData.FullName 'Managed') -Target (Join-Path $arenaSpt 'EscapeFromTarkov_Data\Managed') | Out-Null
        New-Item -ItemType Junction -Path (Join-Path $arenaStagePlugins.FullName 'spt') -Target (Join-Path $arenaSpt 'BepInEx\plugins\spt') | Out-Null
        $arenaClientInstall = Join-Path $arenaSpt 'BepInEx\plugins\maschine-ArenaMode.Client.dll'
        $arenaCoreInstall = Join-Path $arenaSpt 'BepInEx\plugins\maschine-Redeploy.Core.dll'
        $arenaClientBefore = Get-PmcHash $arenaClientInstall
        $arenaCoreBefore = Get-PmcHash $arenaCoreInstall
        Invoke-PmcDotnet @('build', 'ArenaMode.Client\ArenaMode.Client.csproj', '-c', 'Release',
            "-p:SPTPath=$arenaStage", "-p:SPTUserPath=$(Join-Path $arenaSpt 'SPT_Runtime')", '--nologo', '-v:minimal')
        if ((Get-PmcHash $arenaClientInstall) -ne $arenaClientBefore -or (Get-PmcHash $arenaCoreInstall) -ne $arenaCoreBefore) {
            throw 'Staged verification changed an installed DLL.'
        }
        $arenaBuilt = Join-Path $arenaRepo 'ArenaMode.Client\bin\Release\maschine-ArenaMode.Client.dll'
        $arenaStaged = Join-Path $arenaStagePlugins.FullName 'maschine-ArenaMode.Client.dll'
        $arenaHash = Get-PmcHash $arenaBuilt
        if ($arenaHash -ne (Get-PmcHash $arenaStaged)) { throw 'Staged client does not match built client.' }
        [pscustomobject]@{ Built=$arenaBuilt; Staged=$arenaStaged; SHA256=$arenaHash; PreviousInstalledSHA256=$arenaClientBefore } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaArtifacts 'staged-client.json')
        Write-Output "Staged client matches build ($arenaHash); installed client and core unchanged."
    }
    Write-Output ('ARENA_PMC_CAPTURE_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
