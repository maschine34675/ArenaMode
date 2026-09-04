$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$noDeployRoot = Join-Path $repoRoot '.no-live-deploy'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    Write-Host "[$Label] dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE"
    }
}

Push-Location $repoRoot
try {
    Invoke-DotNet 'Release build without deployment' @(
        'build', 'ArenaMode.sln',
        '-c', 'Release',
        '--nologo',
        '-p:SPTModType=NoDeploy',
        "-p:SPTUserPath=$noDeployRoot"
    )

    Invoke-DotNet 'Lighting policy and lifecycle' @(
        'run', '--project', 'tools\ArenaLightingChecks\ArenaLightingChecks.csproj', '-c', 'Release'
    )
    Invoke-DotNet 'Tracer palette and lifecycle' @(
        'run', '--project', 'tools\ArenaTracerChecks\ArenaTracerChecks.csproj', '-c', 'Release'
    )
    Invoke-DotNet 'Announcer cue lifecycle' @(
        'run', '--project', 'tools\AnnouncerChecks\AnnouncerChecks.csproj', '-c', 'Release'
    )
    Invoke-DotNet 'Native mode selection' @(
        'run', '--project', 'tools\NativeModeSelectionChecks\NativeModeSelectionChecks.csproj', '-c', 'Release'
    )
    Invoke-DotNet 'PitFireTeam compatibility' @(
        'run', '--project', 'tools\PitFireTeamChecks\PitFireTeamChecks.csproj', '-c', 'Release'
    )
    Invoke-DotNet 'Protected team expansion' @(
        'run', '--project', 'tools\TeamArenaExpansionChecks\TeamArenaExpansionChecks.csproj', '-c', 'Release'
    )
    Invoke-DotNet 'Server and shared unit tests' @(
        'test', 'ArenaMode.Tests\ArenaMode.Tests.csproj', '-c', 'Release', '--no-build', '--nologo'
    )

    Write-Host 'ARENA_LIGHTING_REGRESSION_CHECKS_PASSED'
}
finally {
    Pop-Location
}
