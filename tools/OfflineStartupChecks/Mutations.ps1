$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/offline-startup-mutations/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$mutations = @(
    @{ Name = 'live-owner-bypassed'; File = 'RewardOfflineRecovery.cs';
       Old = 'if (ProcessStillAlive(activation)) throw new RewardDataException("offline-private-process-still-running");';
       New = '// mutation: no process ownership check';
       Test = 'ReleasedLockDoesNotProveThePrivateProcessHasFinishedShutdown' },
    @{ Name = 'discard-receipt-rejection-lost'; File = 'RewardOfflineRecovery.cs';
       Old = 'if (IsDiscarded(root, authority)) throw new RewardOfflineDiscardedException();';
       New = '// mutation: old delegation is no longer terminal';
       Test = 'LateDiscardedReceiptCannotPayOrBlockANewHealthySession' },
    @{ Name = 'lost-pointer-drops-unapplied-discard'; File = 'RewardOfflineRecovery.cs';
       Old = 'if (!DiscardCompleted(root, lease, discard)) pending.Add(lease);';
       New = '// mutation: an unfinished discard is treated as terminal';
       Test = 'InterruptedDiscardAndLostPointerFinishOnceWithoutRestoringOldOn' }
)
$originals = @{}
$restoreRequired = $false
Push-Location $repo
try {
    foreach ($case in $mutations) {
        $source = Join-Path $repo ('ArenaMode.Server/Rewards/' + $case.File)
        $originals[$source] = (Get-FileHash -LiteralPath $source).Hash
        $text = [IO.File]::ReadAllText($source)
        if (($text.Split([string[]]@($case.Old), [StringSplitOptions]::None)).Length -ne 2) { throw "Mutation anchor mismatch: $($case.Name)" }
        $copy = Join-Path $run ($case.Name + '.cs')
        [IO.File]::WriteAllText($copy, $text.Replace($case.Old, $case.New))
        $targets = Join-Path $run ($case.Name + '.targets')
        $noDeploy = [Security.SecurityElement]::Escape((Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'))
        $include = [Security.SecurityElement]::Escape($copy)
        $name = [Security.SecurityElement]::Escape($case.File)
        $xml = @'
<Project>
  <Import Project="__NODEPLOY__" />
  <Target Name="OfflineStartupMutation" BeforeTargets="CoreCompile" Condition="'$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="Rewards\__FILE__;$(MSBuildProjectDirectory)\Rewards\__FILE__" />
      <Compile Include="__COPY__" />
    </ItemGroup>
  </Target>
</Project>
'@
        [IO.File]::WriteAllText($targets, $xml.Replace('__NODEPLOY__', $noDeploy).Replace('__FILE__', $name).Replace('__COPY__', $include))
        $trx = Join-Path $run ($case.Name + '.trx')
        $restoreRequired = $true
        & dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo -v:minimal `
            "-p:CustomAfterMicrosoftCommonTargets=$targets" "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" `
            --filter "FullyQualifiedName~$($case.Test)" --logger "trx;LogFileName=$($case.Name).trx" --results-directory $run *> (Join-Path $run ($case.Name + '.log'))
        if ($LASTEXITCODE -eq 0 -or !(Test-Path -LiteralPath $trx)) { throw "Mutation was not rejected by tests: $($case.Name)" }
        [xml]$results = Get-Content -LiteralPath $trx -Raw
        $failed = @($results.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' -and $_.testName -like "*$($case.Test)*" })
        if ($failed.Count -eq 0) { throw "Named assertion did not fail: $($case.Name)" }
        Write-Output "REJECTED $($case.Name)"
    }
    foreach ($source in $originals.Keys) {
        if ((Get-FileHash -LiteralPath $source).Hash -ne $originals[$source]) { throw 'Production source changed.' }
    }
    # Restore the real candidate and prove the same named tests pass without faults.
    # Removed compile inputs alone may leave MSBuild's incremental output current.
    & dotnet build ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --no-incremental --nologo -v:minimal `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" `
        "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" *> (Join-Path $run 'restore-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not rebuild the original production candidate.' }
    $restoreRequired = $false
    $filter = ($mutations | ForEach-Object { 'FullyQualifiedName~' + $_.Test }) -join '|'
    & dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --no-build --nologo -v:minimal `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" `
        "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" --filter $filter `
        --logger 'trx;LogFileName=baseline.trx' --results-directory $run *> (Join-Path $run 'baseline.log')
    if ($LASTEXITCODE -ne 0) { throw 'Positive baseline failed after restoring production.' }
    . (Join-Path $repo 'tools/Assert-ArenaTestRun.ps1')
    $baseline = Assert-ArenaTestRun (Join-Path $run 'baseline.trx') -AllowedSkipReasons @() -RequireFamilies @($mutations.Test)
    Write-Output "baseline passed=$($baseline.Passed)"
    Write-Output 'OFFLINE_STARTUP_MUTATIONS_PASSED'
} finally {
    try {
        if ($restoreRequired) {
            & dotnet build ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --no-incremental --nologo -v:minimal `
                "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" `
                "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" *> (Join-Path $run 'failure-restore.log')
            if ($LASTEXITCODE -ne 0) { throw 'Mutation failure cleanup could not restore the original build.' }
        }
    } finally { Pop-Location }
}
