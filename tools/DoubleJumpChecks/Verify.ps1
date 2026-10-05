param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject = Join-Path $PSScriptRoot 'DoubleJumpChecks.csproj'
$arenaWork = Join-Path $arenaRepo ('artifacts\double-jump-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaSource = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaDoubleJump.cs'))

function Invoke-Checks([string[]]$Extra = @()) {
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaBuild = @(& dotnet build $arenaProject -t:Rebuild -c Release --nologo @Extra 2>&1)
        $arenaBuildExit = $LASTEXITCODE
        if ($arenaBuildExit -ne 0) { throw ($arenaBuild -join [Environment]::NewLine) }
        $arenaLines = @(& dotnet run --project $arenaProject -c Release --no-build 2>&1)
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    return @{ Exit = $arenaExit; Text = (($arenaLines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine) }
}
function Assert-Production {
    $arenaResult = Invoke-Checks
    Write-Output $arenaResult.Text
    if ($arenaResult.Exit -ne 0 -or $arenaResult.Text -notmatch 'ARENA_DOUBLE_JUMP_CONTRACTS_PASSED') {
        throw 'Production-linked double jump checks failed'
    }
    [IO.File]::WriteAllText((Join-Path $arenaWork 'production.log'), $arenaResult.Text)
}
function Test-Mutant([string]$Name, [string]$Before, [string]$After, [string]$Assertion) {
    if (-not $arenaSource.Contains($Before)) { throw "Missing exact mutation anchor: $Name" }
    if (($arenaSource.Length - $arenaSource.Replace($Before, '').Length) / $Before.Length -ne 1) {
        throw "Mutation anchor is not unique: $Name"
    }
    $arenaPath = Join-Path $arenaWork ($Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaSource.Replace($Before, $After))
    $arenaResult = Invoke-Checks @("-p:DoubleJumpSource=$arenaPath")
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name + '.log')), $arenaResult.Text)
    if ($arenaResult.Exit -eq 0 -or -not $arenaResult.Text.Contains('ASSERT: ' + $Assertion)) {
        Write-Output $arenaResult.Text
        throw "Mutant must fail its named behavioral assertion: $Name"
    }
    Write-Output "Double jump negative control rejected: $Name -> $Assertion"
}
try {
    # Ground-hook wiring only; native Harmony ordering/trajectory still need a raid.
    $arenaGround = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaMovementPatches.cs'))
    $arenaGround = $arenaGround.Substring($arenaGround.IndexOf('private sealed class JumpHeightPatch'))
    $arenaGround = $arenaGround.Substring(0, $arenaGround.IndexOf('private sealed class VaultSpeedPatch'))
    if ($arenaGround -notmatch 'if \(mult > 1f && IsArenaMovement\(__instance.MovementContext\)\)\s+__instance\._liftForce \*= Mathf.Sqrt\(mult\);\s+//[^\r\n]*\r?\n\s+ArenaJumpBoots.ApplyEntry\(__instance\);') {
        throw 'Ground jump must apply boots after base height without the base-height setting gate.'
    }
    Assert-Production
    Test-Mutant 'boots-captured-twice' 'credit.NativeLift = ArenaJumpBoots.WithoutEntryBonus(__instance);' 'credit.NativeLift = __instance._liftForce;' 'boots affect double jump exactly once'
    Test-Mutant 'boots-stale-after-debit' 'if (!TryGetImpulse(jump, __state.Credit, out lift, out velocity)) return;' ';' 'debit callback clears boots before impulse without double cost'
    # Both observation and ownership use the exact admission helper. Remove that
    # whole admission condition so the mutant is behavioral despite redundant IsActive.
    $arenaScopeStart = $arenaSource.IndexOf('        if (!ArenaSession.IsActive || !_enabled || context == null')
    $arenaScopeEnd = $arenaSource.IndexOf('            return false;', $arenaScopeStart)
    $arenaScopeGate = $arenaSource.Substring($arenaScopeStart, $arenaScopeEnd - $arenaScopeStart)
    $arenaNoAdmission = $arenaScopeGate.Replace('!ArenaSession.IsActive || ', '').Replace('!ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)', 'false')
    Test-Mutant 'exact-arena-admission-removed' $arenaScopeGate $arenaNoAdmission 'ordinary raid never receives extra impulse'
    Test-Mutant 'spent-credit-gate-removed' 'credit.Spent || ' '' 'spent credit denies repeated input'
    Test-Mutant 'falling-state-excluded' ' || state.GetType() == typeof(FallDown)' '' 'walking off edge receives one deliberate impulse and debit'
    Test-Mutant 'fresh-falling-body-denied' 'SetCredit(new Credit { Player = player, Context = context, World = world, Game = game });' 'SetCredit(new Credit { Player = player, Context = context, World = world, Game = game, Spent = !context.IsGrounded });' 'fresh falling body needs no preceding observed ground jump'
    Test-Mutant 'state-transition-refills-credit' 'credit.StateRevision++;' 'credit.StateRevision++; credit.Spent = false;' 'Jump FallDown transitions cannot replenish airborne credit'
    Test-Mutant 'physical-ground-contact-removed' '&& (credit.Player.CharacterController.isGrounded || ArenaBunnyhop.HasRecentPhysicalContact(__instance)) && landedState' '&& landedState' 'ground proximity ray alone cannot replenish credit'
    Test-Mutant 'upward-contact-refills-credit' '&& jump._currentLiftforce.y <= 0f' '' 'upward native controller contact cannot replenish credit'
    Test-Mutant 'genuine-landing-reset-removed' 'if (credit.Airborne && __instance.IsGrounded' 'if (false && credit.Airborne && __instance.IsGrounded' 'genuine landing restores the reused jump credit'
    Test-Mutant 'ground-toggle-always-spent' 'credit.Spent = credit.Airborne || !credit.Context.IsGrounded' 'credit.Spent = true || credit.Airborne || !credit.Context.IsGrounded' 'grounded setting toggle preserves next airborne credit'
    Test-Mutant 'vertical-clock-reset-removed' 'jump._stateTime = jump._liftDelay;' '// missing vertical clock reset' 'extra impulse restarts native vertical clock'
    Test-Mutant 'captured-credit-identity-removed' '|| !ReferenceEquals(_credit, expectedCredit)' '' 'landing replacement invalidates captured credit'
    Test-Mutant 'post-debit-ownership-revalidation-removed' 'if (!OwnsCredit(jump, player, __state.Credit)' 'if (false' 'body swap during debit rejects impulse'
    Test-Mutant 'pending-mantle-guard-removed' ' && !ArenaAirVault.HasPendingRequest(player)' '' 'pending native mantle takes precedence'
    Test-Mutant 'native-edge-request-removed' 'state.MovementContext.TryJump();' ';' 'centimetre edge press requests native transition without premature debit'
    Test-Mutant 'native-edge-double-debit' 'request.Credit.NativeRequest = null;' 'request.Credit.NativeRequest = null; request.Player.Physical.OnJump(true);' 'successful native ledge entry owns one debit and one noise'
    Test-Mutant 'native-transition-expiry-removed' '&& Time.realtimeSinceStartup <= request.ExpiresAt' '' 'expired request denied even before landing observer ticks'
    Test-Mutant 'setting-invalidation-removed' 'private static void ClearCredits(object sender, EventArgs args) => InvalidateCredit();' 'private static void ClearCredits(object sender, EventArgs args) { }' 'settings toggle invalidates outstanding unspent credit'
    Test-Mutant 'falling-arena-height-removed' 'if (firstHeight > 1f) lift *= Mathf.Sqrt(firstHeight);' '// missing existing Arena height' 'falling lift retains native injured weight skill and Arena height factors'
    Test-Mutant 'falling-owned-signal-release-removed' 'if (jump.GetType() == typeof(FallDown)) ReleaseNativeSignal(request.NativeReservation);' ';' 'FallDown completion releases only the owned native jump signal'
    Test-Mutant 'reused-jump-lift-compounds' 'lift = credit.NativeLift;' 'lift = jump._liftForce;' 'reused native jump state cannot compound height across landings'
    Test-Mutant 'signal-cleanup-loses-locomotion-owner' '(IsGroundLocomotion(player.CurrentManagedState)' '(ReferenceEquals(player.CurrentManagedState, request.State)' 'owned pending signal expires after Idle to Move Sprint transition'
    $arenaGroundHandoff = "if (state?.MovementContext.IsGrounded == true && _credit != null"
    Test-Mutant 'ground-command-signal-handoff-removed' $arenaGroundHandoff "if (false && state?.MovementContext.IsGrounded == true && _credit != null" 'grounded native command takes signal ownership before delayed landing cleanup'
}
finally { Assert-Production }
Write-Output "Double jump check evidence: $arenaWork"
Write-Output 'ARENA_DOUBLE_JUMP_CHECKS_PASSED'
