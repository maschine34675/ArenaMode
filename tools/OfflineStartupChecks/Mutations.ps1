param([string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/offline-startup-mutations/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$mutations = @(
    @{ Name = 'live-owner-bypassed'; File = 'RewardOfflineRecovery.cs';
       Old = 'RequirePrivateProcessGone(activation);';
       New = '// mutation: no process ownership check';
       Test = 'ReleasedLockDoesNotProveThePrivateProcessHasFinishedShutdown' },
    @{ Name = 'discard-receipt-rejection-lost'; File = 'RewardOfflineRecovery.cs';
       Old = 'if (IsDiscarded(root, authority)) throw new RewardOfflineDiscardedException();';
       New = '// mutation: old delegation is no longer terminal';
       Test = 'LateDiscardedReceiptCannotPayOrBlockANewHealthySession' },
    @{ Name = 'lost-pointer-drops-unapplied-discard'; File = 'RewardOfflineRecovery.cs';
       Old = '                    pending.Add(lease);';
       New = '// mutation: an unfinished discard is treated as terminal';
       Test = 'InterruptedDiscardAndLostPointerFinishOnceWithoutRestoringOldOn' },
    # The 0.4.1 bug (maschine34675/ArenaMode#1): every other pre-load callback counts as "earlier", whatever its position.
    @{ Name = 'callback-position-ignored'; File = 'ArenaRewardStartup.cs';
       Old = 'return self < 0 ? new() : preLoad.Take(self).Where(c => c.Parent != typeof(Watermark)).ToList();';
       New = 'return self < 0 ? new() : preLoad.Where(c => c.Parent != typeof(Watermark) && c.Parent != typeof(ArenaRewardStartup)).ToList();';
       Test = 'AdmissionFollowsTheRealCallbackOrderNotAPriorityRange' },
    # The reason is not recorded next to the lease: the session's card falls back to "Activation pending".
    @{ Name = 'lease-reason-dropped'; File = 'RewardOfflineSession.cs';
       Old = 'if (reason is not null) RewardRuntimeFiles.WriteNew(root, AuthorityUnavailablePath(root, lease.SessionId), new RewardOfflineAuthorityUnavailable(1, lease.SessionId, reason));';
       New = '// mutation: the reason is not recorded next to the lease';
       Test = 'ARefusedStartIsRecordedNextToTheLeaseAndShownOnTheSessionCardOfANeverEnrolledProfile' },
    # The step back at Watermark + 1 (0.3.0 to 0.4.1): the stable sort then puts every mod at 0 before it.
    @{ Name = 'priority-back-at-watermark-plus-one'; File = 'ArenaRewardStartup.cs';
       Old = '[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Watermark)]';
       New = '[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Watermark + 1)]';
       Test = 'TheRewardStepRunsAtTheEarliestSlot' },
    # A callback SPT runs after the profiles are loaded (priority 200000) counts as pre-load.
    @{ Name = 'pre-load-upper-bound-inclusive'; File = 'ArenaRewardStartup.cs';
       Old = 'c.Priority < OnLoadOrder.GameCallbacks).ToList();';
       New = 'c.Priority <= OnLoadOrder.GameCallbacks).ToList();';
       Test = 'AdmissionFollowsTheRealCallbackOrderNotAPriorityRange' },
    # The step missing from the list is admitted although its position, and so the order, is unknown.
    @{ Name = 'unlisted-step-admitted'; File = 'ArenaRewardStartup.cs';
       Old = 'return "startup-callback-unlisted";';
       New = 'return null;';
       Test = 'NativeAdmissionRejectsContractDrift' },
    # A private process whose liveness cannot be read counts as gone: "unreadable is not proof of death".
    @{ Name = 'unverifiable-process-treated-as-gone'; File = 'RewardOfflineSession.cs';
       Old = '{ throw new RewardLiveOwnerException("offline-private-process-unverifiable", e); }';
       New = '{ alive = false; }';
       Test = 'APrivateProcessWhoseLivenessCannotBeReadStillStopsStartup' },
    # Profile write pipeline (docs/reviews/PROFILE-WRITE-PIPELINE-20261120.md): the proof accepts a transformed save
    # only as input == RAM, output == disk, subtractive projection and unchanged protected subtrees.
    @{ Name = 'pipeline-output-not-compared-with-disk'; File = 'ProfileWriteProof.cs';
       Old = 'if (!outputMatchesDisk) return ProfileProof.Strict(diskText, "transformed", "false", input, output);';
       New = '// mutation: the captured output is not compared with the disk';
       Test = 'StaleMd5PlusExternalWriterIsRejected' },
    @{ Name = 'pipeline-ram-not-compared-with-input'; File = 'ProfileWriteProof.cs';
       Old = '|| proof.Mode == ProfileProofMode.Pipeline && RewardRuntimeFiles.ExactSameProfile(ram, proof.Comparable);';
       New = '|| proof.Mode == ProfileProofMode.Pipeline;';
       Test = 'RamChangedAfterWriteIsRejected' },
    @{ Name = 'pipeline-projection-check-removed'; File = 'ProfileWriteProof.cs';
       Old = 'if (!IsSubtractive(before, after)) return false;';
       New = '// mutation: a changed value passes as a projection';
       Test = 'ValueChangingTransformIsNotAccepted' },
    @{ Name = 'pipeline-protected-subtrees-unchecked'; File = 'ProfileWriteProof.cs';
       Old = 'if (!ProtectedUnchanged(before, after)) return false;';
       New = '// mutation: the ledger and the identity may be dropped';
       Test = 'DroppingProtectedSubtreeIsUnsafe' },
    @{ Name = 'pipeline-unpaired-begin-keeps-old-record'; File = 'ProfileWriteProof.cs';
       Old = 'InstallOpaque(t_id);';
       New = '// mutation: the finalizer leaves an unpaired capture as it was';
       Test = 'SkippedStringWriteTurnsRecordOpaqueAndDoesNotLeakSlot' },
    @{ Name = 'pipeline-nested-write-not-poisoned'; File = 'ProfileWriteProof.cs';
       Old = 'if (t_id is not null) { t_poisoned = true; return; }';
       New = 'if (t_id is not null) { return; }';
       Test = 'NestedProfileWriteIsPoisoned' },
    @{ Name = 'pipeline-ram-compared-with-patchable-equality'; File = 'ProfileWriteProof.cs';
       Old = '=> RewardRuntimeFiles.ExactSameProfile(ram, disk)';
       New = '=> RewardRuntimeFiles.SameProfile(ram, disk)';
       Test = 'ForeignSameProfilePostfixDoesNotAffectConfirms' },
    # Pack A Punch side file: a stale one must not be revived, and the carry sets the staged file's time before the move.
    @{ Name = 'pap-freshness-inverted'; File = 'PapSideFileGuard.cs';
       Old = 'return previous - sideTime > FreshWindow ? null : side;';
       New = 'return previous - sideTime > FreshWindow ? side : null;';
       Test = 'AlreadyStaleSideFileIsNotRevived' },
    @{ Name = 'pap-carry-after-move'; File = 'RewardStartupFiles.cs';
       Old = 'if (papSideFile is not null) PapSideFileGuard.Carry(papSideFile, File.GetLastWriteTimeUtc(temporary));';
       New = '// mutation: no carry before the move';
       Old2 = 'File.Move(temporary, path, overwrite: true);';
       New2 = 'File.Move(temporary, path, overwrite: true); if (papSideFile is not null) PapSideFileGuard.Carry(papSideFile, File.GetLastWriteTimeUtc(path));';
       Test = 'SideFileIsAlreadyCarriedWhenTheMoveFails' },
    @{ Name = 'pap-carry-uses-now'; File = 'PapSideFileGuard.cs';
       Old = 'SetWriteTime(sidePath, tempWriteUtc);';
       New = 'SetWriteTime(sidePath, DateTime.UtcNow);';
       Test = 'CarriedSideTimeEqualsNewProfileTime' }
)
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if ($Only.Count -gt 0) { $mutations = @($mutations | Where-Object { $_.Name -in $Only }) }
$originals = @{}
$restoreRequired = $false
Push-Location $repo
try {
    foreach ($case in $mutations) {
        $source = Join-Path $repo ('ArenaMode.Server/Rewards/' + $case.File)
        $originals[$source] = (Get-FileHash -LiteralPath $source).Hash
        $text = [IO.File]::ReadAllText($source)
        if (($text.Split([string[]]@($case.Old), [StringSplitOptions]::None)).Length -ne 2) { throw "Mutation anchor mismatch: $($case.Name)" }
        $mutated = $text.Replace($case.Old, $case.New)
        # An optional second edit of the same file (a move of a statement is two edits).
        if ($case.Old2) {
            if (($mutated.Split([string[]]@($case.Old2), [StringSplitOptions]::None)).Length -ne 2) { throw "Mutation anchor mismatch (second edit): $($case.Name)" }
            $mutated = $mutated.Replace($case.Old2, $case.New2)
        }
        $copy = Join-Path $run ($case.Name + '.cs')
        [IO.File]::WriteAllText($copy, $mutated)
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
