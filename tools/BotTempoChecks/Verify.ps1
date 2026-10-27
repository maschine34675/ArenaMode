param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$tempoRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tempoSourcePath = Join-Path $tempoRepo 'ArenaMode.Client/Bots/ArenaBotTempo.cs'
$tempoSource = [IO.File]::ReadAllText($tempoSourcePath).Replace("`r`n", "`n")
$tempoEvidence = Join-Path $tempoRepo ('artifacts/bot-tempo-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempoEvidence)
$tempoInputs = @{}
$tempoPaths = @($tempoSourcePath, (Join-Path $tempoRepo 'ArenaMode.Client/Movement/ArenaBotMovement.cs'))
$tempoPaths += @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object FullName)
foreach ($path in $tempoPaths) { $tempoInputs[$path] = (Get-FileHash -LiteralPath $path).Hash }
function Run([string]$Name, [string]$Override = '') {
    $arguments = @('build', (Join-Path $PSScriptRoot 'BotTempoChecks.csproj'), '-t:Rebuild', '-c', 'Release', '--nologo')
    if ($Override) { $arguments += "-p:TempoSource=$Override" }
    $build = @(& dotnet @arguments 2>&1)
    $build | Set-Content -LiteralPath (Join-Path $tempoEvidence ($Name + '-build.log'))
    if ($LASTEXITCODE -ne 0) { throw ($build -join "`n") }
    $result = @(& dotnet run --project (Join-Path $PSScriptRoot 'BotTempoChecks.csproj') -c Release --no-build 2>&1)
    $exit = $LASTEXITCODE; $message = ($result | ForEach-Object ToString) -join "`n"
    [IO.File]::WriteAllText((Join-Path $tempoEvidence ($Name + '.log')), $message)
    return @{ Exit = $exit; Text = $message }
}
function Positive([string]$Name) {
    $r = Run $Name
    if ($r.Exit -ne 0 -or !$r.Text.Contains('BOT_TEMPO_BEHAVIOR_PASSED')) { throw $r.Text }
    Write-Output $r.Text
}
$faults = @(
    @('live-guard', 'if (!ArenaBotMovement.IsLiveBot(owner)) return false;', 'if (owner == null) return false;', 'normal raid unchanged wait'),
    @('reload-guard', 'owner.WeaponManager?.Reload?.Reloading == true', 'false', 'reload active wait'),
    @('short-route', 'remaining >= 8f', 'remaining >= 0f', 'short path unchanged'),
    @('strafe-missing', 'return 0.85f;', 'return native;', 'supported live mode RoundsMode'),
    @('old-strafe-floor', 'return 0.85f;', 'return 0.7f;', 'supported live mode RoundsMode'),
    @('old-strafe-threshold', 'native >= 0.85f', 'native >= 0.7f', 'subfloor strafe raised across old boundary'),
    @('wait-too-early', 'if (now - observation.SafeSince < 0.5f) return;', 'if (false) return;', 'safe tail not released early'),
    @('foreign-pause', '(float)_unpauseTimer.GetValue(path) != deadline', 'false', 'foreign pause preserved'),
    @('restore-missing', '_waitTimer.SetValue(lease.Search, lease.Original);', '_waitTimer.SetValue(lease.Search, lease.Written);', 'exception restores owned wait'),
    @('mutative-flash', 'owner.FlashGrenade?._isFlashed', 'owner.FlashGrenade?.IsFlashed', 'supported live mode RoundsMode')
)
$rejected = 0
try {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Installed.ps1') -SptPath $SptPath 2>&1 | Tee-Object (Join-Path $tempoEvidence 'installed.log')
    if ($LASTEXITCODE -ne 0) { throw 'Installed SAIN contract failed' }
    Positive 'production-before'
    foreach ($fault in $faults) {
        if ($tempoSource.Split([string[]]@($fault[1]), [StringSplitOptions]::None).Count -ne 2) { throw "Ambiguous mutation anchor $($fault[0])" }
        $mutant = Join-Path $tempoEvidence ($fault[0] + '.cs')
        [IO.File]::WriteAllText($mutant, $tempoSource.Replace($fault[1], $fault[2]))
        $r = Run $fault[0] $mutant
        if ($r.Exit -eq 0 -or !$r.Text.Contains('ASSERT: ' + $fault[3])) { throw "Fault not rejected at intended assertion $($fault[0]): $($r.Text)" }
        $rejected++; Write-Output "Rejected tempo fault: $($fault[0])"
    }
} finally {
    Positive 'production-after'
    foreach ($path in $tempoInputs.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $tempoInputs[$path]) { throw "Input changed during verification: $path" }
    }
}
@{ InputsSHA256 = $tempoInputs; RejectedFaults = $rejected; CheckedUtc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $tempoEvidence 'evidence.json')
Write-Output "BOT TEMPO CHECKS PASSED; $rejected compiled negative controls; evidence: $tempoEvidence"
