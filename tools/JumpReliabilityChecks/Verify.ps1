param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaWork = Join-Path $arenaRepo ('artifacts\movement-transitions-20261115\jump\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
Start-Transcript -LiteralPath (Join-Path $arenaWork 'verification.log') | Out-Null
try {
    . (Join-Path $arenaRepo 'tools\Invoke-ArenaSourceChecks.ps1')
    $arenaSource = Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaJumpReliability.cs'
    $arenaDouble = Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaDoubleJump.cs'
    $arenaFaults = @(
        @{ Name='stale-timing-repair-removed'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='VaultingTiming(player) = 0f;'; Replace=';'; Failure='fresh tap repairs stale unheld vault timing' },
        @{ Name='held-vault-intent-ignored'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='|| VaultingPressed(player)'; Replace=''; Failure='held vault and excluded owners retain native timing' },
        @{ Name='coyote-window-unbounded'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='Time.time - contact.GroundAt > CoyoteWindow'; Replace='false'; Failure='expired nonfinite or backwards coyote time is denied' },
        @{ Name='physical-evidence-removed'; Path=$arenaSource; Property='JumpReliabilitySource'; Find=@('if (player.CharacterController.isGrounded || pulse)', 'player.CharacterController.isGrounded ? Time.time : _contact.PulseAt'); Replace=@('if (true)', 'Time.time'); Failure='ray proximity alone never creates coyote grace' },
        @{ Name='foreign-controller-contact'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='ReferenceEquals(_sample.Owner.Player.CharacterController, __instance)'; Replace='true'; Failure='only successful exact controller contact creates coyote grace' },
        @{ Name='failed-motion-accepted'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='!__runOriginal || '; Replace=''; Failure='only successful exact controller contact creates coyote grace' },
        @{ Name='observer-refreshes-contact'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='player.CharacterController.isGrounded ? Time.time : _contact.PulseAt'; Replace='Time.time'; Failure='delayed contact observer cannot extend physical window' },
        @{ Name='ground-jump-keeps-coyote'; Path=$arenaSource; Property='JumpReliabilitySource'; Find='{ _contact = null; _sample = null; }'; Replace='{ }'; Failure='ordinary ground jump entry disarms coyote grace' },
        @{ Name='coyote-spends-extra-credit'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='if (!__state.Coyote) __state.Credit.Spent = true; // Before callbacks, including failure and re-entry.'; Replace='__state.Credit.Spent = true; // Deliberate fault'; Failure='coyote preserves exactly one separate double jump credit' },
        @{ Name='coyote-uses-double-height'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='float height = coyote ? 1f : Settings.DoubleJumpHeightMult.Value;'; Replace='float height = Settings.DoubleJumpHeightMult.Value;'; Failure='coyote uses first jump strength independent of double toggle' },
        @{ Name='reentrant-credit-unlocked'; Path=$arenaDouble; Property='DoubleJumpSource'; Find=@('credit.ApplyingImpulse || ', '_command != null || '); Replace=@('', ''); Failure='reentrant debit cannot steal double jump command' },
        @{ Name='pending-request-does-not-own-command'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='credit.NativeRequest != null || '; Replace=''; Failure='native coyote entry callback cannot steal credit' },
        @{ Name='pending-scope-validation-removed'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='return TryGetLocalPlayer(credit.Context, out var player)'; Replace='return true || TryGetLocalPlayer(credit.Context, out var player)'; Failure='ineligible pending coyote releases signal without movement observer' },
        @{ Name='translator-lifetime-unlocked'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='_command != null || '; Replace=''; Failure='translator lifetime permits only one captured command' },
        @{ Name='postfix-releases-command-too-early'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='if (__state.Credit != null) __state.Credit.ApplyingImpulse = false;'; Replace='if (__state.Credit != null) __state.Credit.ApplyingImpulse = false; ReleaseCommand(__state);'; Failure='later Harmony postfix cannot claim a second jump command' },
        @{ Name='input-owner-reset-removed'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='if (__instance?.Player == null) return;'; Replace='return;'; Failure='ineligible pending coyote releases signal without movement observer' },
        @{ Name='landing-slide-queue-never-expires'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='|| !Finite(Time.realtimeSinceStartup) || Time.realtimeSinceStartup > request.ExpiresAt'; Replace='|| !Finite(Time.realtimeSinceStartup)'; Failure='repeated landing input cannot extend queue timeout' },
        @{ Name='landing-slide-adds-debit'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='DispatchNativeRequest(request, () => player.Jump());'; Replace='DispatchNativeRequest(request, () => { player.Jump(); player.Physical.OnJump(true); });'; Failure='landing slide queue dispatches one native ground jump' },
        @{ Name='landing-slide-spends-extra-credit'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='request.State = player.CurrentManagedState;'; Replace='request.Credit.Spent = true; request.State = player.CurrentManagedState;'; Failure='buffered slide jump preserves separate extra jump credit' },
        @{ Name='sprint-exit-window-truncated'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='ExpiresAt = Time.realtimeSinceStartup + bufferWindow'; Replace='ExpiresAt = Time.realtimeSinceStartup + .5f'; Failure='bound sprint exit queue survives beyond half second and dispatches once' },
        @{ Name='queue-clock-rollback-accepted'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='|| Time.realtimeSinceStartup < request.CreatedAt'; Replace=''; Failure='queue rejects clock rollback against actual creation timestamp' },
        @{ Name='queue-window-unbounded'; Path=$arenaDouble; Property='DoubleJumpSource'; Find='&& Finite(bufferWindow) && bufferWindow > 0f && bufferWindow <= 1f'; Replace=''; Failure='buffer window must be finite positive and at most one second' },
        @{ Name='queued-stale-timer-repair-removed'; Path=$arenaDouble; Property='DoubleJumpSource'; Find="ArenaJumpReliability.PrepareJump(player);`n                if (!OwnsSource(request)"; Replace='if (!OwnsSource(request)'; Failure='queued jump repairs unheld stale vault timing after native standing' }
    )
    Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'JumpReliabilityChecks.csproj') -Token 'JUMP_RELIABILITY_CONTRACTS_PASSED' -Faults $arenaFaults -FailurePrefix 'ASSERT: ' -RequireSingleSite
    & (Join-Path $arenaRepo 'tools\DoubleJumpChecks\Verify.ps1') -EvidenceDirectory (Join-Path $arenaWork 'double-jump')
    Get-FileHash -LiteralPath $arenaSource,$arenaDouble -Algorithm SHA256 | Format-Table -AutoSize
    Write-Output 'JUMP_RELIABILITY_VERIFIED'
}
finally { Stop-Transcript | Out-Null }
