param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$agilityRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$agilitySourcePath = Join-Path $agilityRepo 'ArenaMode.Client/Movement/ArenaBotAgility.cs'
$agilitySource = [IO.File]::ReadAllText($agilitySourcePath).Replace("`r`n", "`n")
$agilityEvidence = Join-Path $agilityRepo ('artifacts/bot-agility-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($agilityEvidence)
$agilityInputs = @{}
$agilityPaths = @($agilitySourcePath, (Join-Path $agilityRepo 'ArenaMode.Client/Movement/ArenaBotMovement.cs'))
$agilityPaths += @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object FullName)
foreach ($path in $agilityPaths) { $agilityInputs[$path] = (Get-FileHash -LiteralPath $path).Hash }
function Run([string]$Name, [string]$Override = '') {
    $arguments = @('build', (Join-Path $PSScriptRoot 'BotAgilityChecks.csproj'), '-t:Rebuild', '-c', 'Release', '--nologo')
    if ($Override) { $arguments += "-p:AgilitySource=$Override" }
    $build = @(& dotnet @arguments 2>&1)
    $build | Set-Content -LiteralPath (Join-Path $agilityEvidence ($Name + '-build.log'))
    if ($LASTEXITCODE -ne 0) { throw ($build -join "`n") }
    $result = @(& dotnet run --project (Join-Path $PSScriptRoot 'BotAgilityChecks.csproj') -c Release --no-build 2>&1)
    $exit = $LASTEXITCODE; $message = ($result | ForEach-Object ToString) -join "`n"
    [IO.File]::WriteAllText((Join-Path $agilityEvidence ($Name + '.log')), $message)
    return @{ Exit = $exit; Text = $message }
}
function Positive([string]$Name) {
    $r = Run $Name
    if ($r.Exit -ne 0 -or !$r.Text.Contains('BOT_AGILITY_BEHAVIOR_PASSED')) { throw $r.Text }
    Write-Output $r.Text
}
$faults = @(
    @('wrong-dispatch', 'new Site(Get(nameof(MovementContext.WalkInertia)), ValueKind.Walk, 2)', 'new Site(Get(nameof(MovementContext.WalkInertia)), ValueKind.Acceleration, 2)', 'each native read dispatches correct policy'),
    @('inertia-unchanged', 'adjusted = native * 0.5f;', 'adjusted = native;', 'remaining inertia halved'),
    @('walk-blend-missing', 'Lighter(native, inertia.WalkInertia.x, false) * 0.6f', 'Lighter(native, inertia.WalkInertia.x, false)', 'walk weight and blend benefits compose'),
    @('wrong-sprint-baseline', 'Mathf.InverseLerp(inertia.SprintAccelerationLimits.x, inertia.SprintAccelerationLimits.y, 0f)', 'inertia.SprintAccelerationLimits.y', 'sprint uses inverse-lerp zero-weight baseline'),
    @('slower-side-curve', 'if (higherIsBetter ? baseline <= native : baseline >= native) return native;', 'if (false) return native;', 'beneficial native side curves preserved'),
    @('nested-barrier-missing', '_scope = default;', '', 'ineligible nested call is barrier'),
    @('scope-restore-missing', 'if (__state.Entered) _scope = __state.Previous;', 'if (__state.Entered) _scope = default;', 'outer scope restored'),
    @('skipped-prefix-unsafe', 'if (__state.Entered) _scope = __state.Previous;', '_scope = __state.Previous;', 'skipped prefix does not clear outer scope'),
    @('physical-identity-missing', '!ReferenceEquals(_scope.Body.Physical, _scope.Physical)', 'false', 'mid-call scope change rejected'),
    @('receiver-identity-missing', '!ReferenceEquals(receiver, _scope.Receiver)', 'false', 'foreign receiver cannot borrow scope')
)
$rejected = 0
try {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Installed.ps1') -SptPath $SptPath 2>&1 | Tee-Object (Join-Path $agilityEvidence 'installed.log')
    if ($LASTEXITCODE -ne 0) { throw 'Installed native agility contract failed' }
    Positive 'production-before'
    foreach ($fault in $faults) {
        if (!$agilitySource.Contains($fault[1])) { throw "Missing mutation anchor $($fault[0])" }
        $mutant = Join-Path $agilityEvidence ($fault[0] + '.cs')
        [IO.File]::WriteAllText($mutant, $agilitySource.Replace($fault[1], $fault[2]))
        $r = Run $fault[0] $mutant
        if ($r.Exit -eq 0 -or !$r.Text.Contains('ASSERT: ' + $fault[3])) { throw "Fault not rejected at intended assertion $($fault[0]): $($r.Text)" }
        $rejected++; Write-Output "Rejected agility fault: $($fault[0])"
    }
} finally {
    Positive 'production-after'
    foreach ($path in $agilityInputs.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $agilityInputs[$path]) { throw "Input changed during verification: $path" }
    }
}
@{ InputsSHA256 = $agilityInputs; RejectedFaults = $rejected; CheckedUtc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $agilityEvidence 'evidence.json')
Write-Output "BOT AGILITY CHECKS PASSED; $rejected compiled negative controls; evidence: $agilityEvidence"
