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
    Assert-Production
    # The native session helper also checks IsActive. Removing only the redundant
    # first gate is therefore not a behavioral mutant; bypass the ownership gate.
    Test-Mutant 'scope-ownership-gate-removed' '|| !OwnsCredit(jump, player, credit)' '' 'ordinary raid never receives extra impulse'
    Test-Mutant 'spent-credit-gate-removed' '|| credit.Spent' '' 'spent credit denies repeated input'
    Test-Mutant 'credit-body-identity-removed' '|| !ReferenceEquals(credit.Player, player)' '' 'credit cannot transfer to replacement body sharing state'
    Test-Mutant 'grounded-entry-gate-removed' '&& __instance.MovementContext.IsGrounded' '' 'airborne fresh entry cannot create credit'
    Test-Mutant 'vertical-clock-reset-removed' 'jump._stateTime = jump._liftDelay;' '// missing vertical clock reset' 'extra impulse restarts native vertical clock'
    Test-Mutant 'captured-credit-identity-removed' '|| !ReferenceEquals(credit, expectedCredit)' '' 'same-object exit and entry cannot replace captured credit'
    Test-Mutant 'post-debit-ownership-revalidation-removed' 'if (!OwnsCredit(jump, __state.Player, __state.Credit)' 'if (false' 'body swap during debit rejects impulse'
}
finally { Assert-Production }
Write-Output "Double jump check evidence: $arenaWork"
Write-Output 'ARENA_DOUBLE_JUMP_CHECKS_PASSED'
