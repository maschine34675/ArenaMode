param()
$ErrorActionPreference = 'Stop'
$roleRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$roleProject = Join-Path $PSScriptRoot 'RoleLabelChecks.csproj'
$roleWork = Join-Path $roleRepo ('artifacts\role-label-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $roleWork -Force | Out-Null
$roleSource = [IO.File]::ReadAllText((Join-Path $roleRepo 'ArenaMode.Client\Combat\RoleClassifier.cs'))

function Invoke-Checks([string[]]$Extra = @()) {
    $roleBuild = @(& dotnet build $roleProject -t:Rebuild -c Release --nologo @Extra 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($roleBuild -join [Environment]::NewLine) }
    $roleOutput = @(& dotnet run --project $roleProject -c Release --no-build 2>&1)
    $roleExit = $LASTEXITCODE
    return @{ Exit = $roleExit; Text = ($roleOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine }
}
function Assert-Production {
    $roleResult = Invoke-Checks
    Write-Output $roleResult.Text
    if ($roleResult.Exit -ne 0 -or $roleResult.Text -notmatch 'ARENA_ROLE_LABEL_CONTRACTS_PASSED') {
        throw 'Production-linked role label checks failed'
    }
}
function Reject-Source([string]$Name, [string]$Source, [string]$Assertion) {
    $rolePath = Join-Path $roleWork ($Name + '.cs')
    [IO.File]::WriteAllText($rolePath, $Source)
    $roleResult = Invoke-Checks @("-p:RoleSource=$rolePath")
    [IO.File]::WriteAllText((Join-Path $roleWork ($Name + '.log')), $roleResult.Text)
    if ($roleResult.Exit -eq 0 -or -not $roleResult.Text.Contains('ASSERT: ' + $Assertion + ';')) {
        Write-Output $roleResult.Text
        throw "Negative control must fail its named behavioral assertion: $Name"
    }
    Write-Output "Role label negative control rejected: $Name -> $Assertion"
}
function Test-Mutant([string]$Name, [string]$Before, [string]$After, [string]$Assertion) {
    if (-not $roleSource.Contains($Before)) { throw "Missing exact mutation anchor: $Name" }
    Reject-Source $Name $roleSource.Replace($Before, $After) $Assertion
}

try {
    Assert-Production
    # Compile the actual failing classifier shipped for the GPU acceptance.
    $roleOldLines = @(& git -C $roleRepo show '2327f63:ArenaMode.Client/Combat/RoleClassifier.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read GPU-baseline classifier' }
    Reject-Source 'original-gpu-bug' ($roleOldLines -join [Environment]::NewLine) 'local USEC with native default role label'
    Test-Mutant 'human-score-bucket-omitted' 'if (HumanPmcSide(player).HasValue)' 'if (false)' 'local USEC with native default role bucket'
    Test-Mutant 'ai-faction-overrides-role' 'info == null || player.IsAI' 'info == null' 'AI Usec/assault keeps native role label'
    Test-Mutant 'bear-human-omitted' 'info.Side == EPlayerSide.Usec || info.Side == EPlayerSide.Bear' 'info.Side == EPlayerSide.Usec' 'local BEAR with native default role label'
    Test-Mutant 'human-scav-treated-as-pmc' 'info.Side == EPlayerSide.Usec || info.Side == EPlayerSide.Bear' 'true' 'human Scav stays Scav label'
    Test-Mutant 'faction-labels-swapped' 'humanSide.Value == EPlayerSide.Usec' 'humanSide.Value == EPlayerSide.Bear' 'local USEC with native default role label'
    Test-Mutant 'human-requires-bot-settings' 'info == null || player.IsAI' 'info == null || player.IsAI || info.Settings == null' 'human PMC without bot settings Usec label'
    Test-Mutant 'round-faction-ignored' 'Bots.TeamDeathmatchRelations.Faction(player) ?? ' '' 'selected round side Usec label'
}
finally {
    Assert-Production
}
Write-Output "Role label check evidence: $roleWork"
Write-Output 'ARENA_ROLE_LABEL_CHECKS_PASSED'
