param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaWork = Join-Path $arenaRepo ('artifacts\customs-contour\negative-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaNoDeploy = Join-Path $arenaWork 'must-not-exist-no-deploy'
$env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime\SPT_Data\database'
$env:SPT_RUNTIME = Join-Path $SptRoot 'SPT_Runtime'

# Inject copies into just the server's compiler input. The source checkout,
# project files and live install are never edited by a fault control.
$arenaTargets = Join-Path $arenaWork 'Mutation.targets'
@'
<Project>
  <Target Name="ContourMutationInput" BeforeTargets="CoreCompile" Condition="'$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="@(Compile)" Condition="'%(Compile.Filename)%(Compile.Extension)' == '$(ContourMutationFile)'" />
      <Compile Include="$(ContourMutationSource)" />
    </ItemGroup>
  </Target>
</Project>
'@ | Set-Content -LiteralPath $arenaTargets

function Test-ContourMutation([string]$Name, [string]$Source, [string]$Before, [string]$After,
    [string]$Filter, [string]$Assertion) {
    $arenaText = [IO.File]::ReadAllText((Join-Path $arenaRepo $Source))
    if (!$arenaText.Contains($Before)) { throw "Mutation anchor drifted: $Name" }
    $arenaMutant = Join-Path $arenaWork ($Name + '.cs')
    [IO.File]::WriteAllText($arenaMutant, $arenaText.Replace($Before, $After))
    $arenaTrxFile = "$Name.trx"
    $arenaOutput = @(& dotnet test (Join-Path $arenaRepo 'ArenaMode.Tests\ArenaMode.Tests.csproj') -c Release --nologo -v:minimal `
        "-p:SPTPath=$SptRoot" "-p:SPTUserMods=$arenaNoDeploy" "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets" `
        "-p:ContourMutationFile=$([IO.Path]::GetFileName($Source))" "-p:ContourMutationSource=$arenaMutant" `
        --filter $Filter --logger "trx;LogFileName=$arenaTrxFile" --results-directory $arenaWork 2>&1)
    $arenaExit = $LASTEXITCODE
    $arenaOutput | Set-Content -LiteralPath (Join-Path $arenaWork "$Name.log")
    if ($arenaExit -eq 0 -or !(Test-Path -LiteralPath (Join-Path $arenaWork $arenaTrxFile))) {
        throw "Mutation must execute and fail its behavioral test: $Name"
    }
    [xml]$arenaTrx = Get-Content -Raw -LiteralPath (Join-Path $arenaWork $arenaTrxFile)
    $arenaFailures = @($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' })
    if ($arenaFailures.Count -eq 0 -or !($arenaFailures | Where-Object { $_.Output.ErrorInfo.Message -like "*$Assertion*" })) {
        throw "Mutation failed somewhere other than the expected assertion: $Name"
    }
    Write-Output "Contour negative control rejected: $Name -> $Assertion"
}

try {
    Test-ContourMutation 'height-limit-restored' 'ArenaMode.Shared\ArenaAreaCatalog.cs' 'limitsHeight: false' 'limitsHeight: true' `
        'FullyQualifiedName~CustomsContourTests' 'Assert.False() Failure'
    Test-ContourMutation 'sniper-id-omitted' 'ArenaMode.Server\Services\ArenaLocationMutator.cs' `
        'area.AllowsSpawn(p.X, p.Y, p.Z, point.Id)' 'area.AllowsSpawn(p.X, p.Y, p.Z)' `
        'FullyQualifiedName~CustomsContourFinalSpawnCheckRejectsLateTampering' 'Assert.DoesNotContain() Failure'
}
finally {
    # The original source is older than a mutant output. Force compilation;
    # an incremental build alone can incorrectly leave that mutant cached.
    & dotnet build (Join-Path $arenaRepo 'ArenaMode.Server\ArenaMode.Server.csproj') -t:Rebuild -c Release --nologo -v:minimal `
        "-p:SPTPath=$SptRoot" "-p:SPTUserMods=$arenaNoDeploy"
    if ($LASTEXITCODE -ne 0) { throw 'Could not replace mutation output with an original-source server build.' }
    & (Join-Path $PSScriptRoot 'Verify.ps1') -Stage Policy -SptRoot $SptRoot
}
Write-Output 'CUSTOMS_CONTOUR_NEGATIVE_CONTROLS_PASSED (2 isolated compiler-input mutants)'
