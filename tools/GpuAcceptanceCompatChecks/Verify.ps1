param([switch]$ProductionOnly)
$ErrorActionPreference = 'Stop'
$compatRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$compatProject = Join-Path $PSScriptRoot 'GpuAcceptanceCompatChecks.csproj'
$compatSource = [IO.File]::ReadAllText((Join-Path $compatRepo 'ArenaMode.Client\Compat\ArenaGpuAcceptanceCompat.cs')).Replace("`r`n", "`n")
$compatEvidence = Join-Path $PSScriptRoot ('artifacts\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $compatEvidence -Force | Out-Null
function Invoke-Checks([string]$SourcePath = '') {
    $compatArgs = @('build', $compatProject, '-t:Rebuild', '-c', 'Release', '--nologo')
    if ($SourcePath) { $compatArgs += "-p:CompatSource=$SourcePath" }
    $compatPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $compatBuild = @(& dotnet @compatArgs 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($compatBuild -join [Environment]::NewLine) }
        $compatLines = @(& dotnet run --project $compatProject -c Release --no-build 2>&1)
        $compatExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $compatPreference }
    return @{ Exit = $compatExit; Text = ($compatLines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine }
}
function Assert-Production {
    $compatResult = Invoke-Checks
    Write-Output $compatResult.Text
    if ($compatResult.Exit -ne 0 -or $compatResult.Text -notmatch 'GPU_ACCEPTANCE_COMPAT_CONTRACTS_PASSED') {
        throw 'Production GPU acceptance compatibility checks failed.'
    }
}
function Reject-Mutant([string]$Name, [string]$Before, [string]$After, [string]$Assertion) {
    if (-not $compatSource.Contains($Before)) { throw "Mutation anchor missing: $Name" }
    $compatPath = Join-Path $compatEvidence ($Name + '.cs')
    [IO.File]::WriteAllText($compatPath, $compatSource.Replace($Before, $After))
    $compatResult = Invoke-Checks $compatPath
    [IO.File]::WriteAllText((Join-Path $compatEvidence ($Name + '.log')), $compatResult.Text)
    if ($compatResult.Exit -eq 0 -or -not $compatResult.Text.Contains('ASSERT: ' + $Assertion)) {
        throw "Mutant $Name did not fail its behavioral assertion: $($compatResult.Text)"
    }
    Write-Output "GPU acceptance compat negative control rejected: $Name"
}
try {
    Assert-Production
    if (-not $ProductionOnly) {
        Reject-Mutant 'late-native-rebind' '__instance.InitializeCameraData();' '// missing early camera binding' 'replacement camera binds before native update'
        Reject-Mutant 'normal-raid-leak' '&& ArenaSession.IsExactActiveRaid(game, world)' '&& true' 'ordinary raid remains untouched'
        Reject-Mutant 'optic-camera-hijack' '|| __instance.IsOptic' '' 'optic manager remains untouched'
        Reject-Mutant 'live-camera-hijack' 'ReferenceEquals(previous, null) || previous != null' 'ReferenceEquals(previous, null)' 'native SSAA change remains authoritative'
        Reject-Mutant 'unassigned-camera-hijack' 'ReferenceEquals(previous, null) || previous != null' 'previous != null' 'unassigned manager remains native'
        Reject-Mutant 'wrong-player-camera' '|| !ReferenceEquals(world.MainPlayer, Singleton<PlayerCameraController>.Instance.Player)' '' 'wrong local body remains untouched'
        Reject-Mutant 'warmup-never-retries' '|| host.isActiveAndEnabled)' ')' 'active Show retry starts original warmup'
        Reject-Mutant 'inactive-warmup-runs' "return false;`n    }`n}" "return true;`n    }`n}" 'inactive Arena host rejects original coroutine scheduling'
    }
} finally { Assert-Production }
Write-Output "GPU acceptance compat evidence: $compatEvidence"
Write-Output 'GPU_ACCEPTANCE_COMPAT_CHECKS_PASSED'
