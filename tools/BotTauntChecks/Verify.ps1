$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$tauntRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tauntSourcePath = Join-Path $tauntRepo 'ArenaMode.Client/Bots/ArenaBotTaunts.cs'
$tauntSource = [IO.File]::ReadAllText($tauntSourcePath).Replace("`r`n", "`n")
$tauntEvidence = Join-Path $PSScriptRoot ('obj/check-evidence/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tauntEvidence)
$tauntInputs = @{}
$tauntPaths = @($tauntSourcePath, (Join-Path $tauntRepo 'ArenaMode.Client/Bots/ArenaSainVersion.cs'))
$tauntPaths += @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object FullName)
foreach ($path in $tauntPaths) { $tauntInputs[$path] = (Get-FileHash -LiteralPath $path).Hash }
function Run([string]$Name, [string]$Override = '') {
    $arguments = @('build', (Join-Path $PSScriptRoot 'BotTauntChecks.csproj'), '-t:Rebuild', '-c', 'Release', '--nologo')
    if ($Override) { $arguments += "-p:TauntSource=$Override" }
    $build = @(& dotnet @arguments 2>&1)
    $build | Set-Content -LiteralPath (Join-Path $tauntEvidence ($Name + '-build.log'))
    if ($LASTEXITCODE -ne 0) { throw ($build -join "`n") }
    $result = @(& dotnet run --project (Join-Path $PSScriptRoot 'BotTauntChecks.csproj') -c Release --no-build 2>&1)
    $exit = $LASTEXITCODE; $message = ($result | ForEach-Object ToString) -join "`n"
    [IO.File]::WriteAllText((Join-Path $tauntEvidence ($Name + '.log')), $message)
    return @{ Exit = $exit; Text = $message }
}
function Positive([string]$Name) {
    $result = Run $Name
    if ($result.Exit -ne 0 -or !$result.Text.Contains('BOT_TAUNT_BEHAVIOR_PASSED')) { throw $result.Text }
    Write-Output $result.Text
}
$faults = @(
    @('wrong-enum-assembly', 'var personality = _personality.PropertyType;', 'var personality = assembly.GetType("SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality", true);', 'only combat update patched'),
    @('ordinary-raid', '!ArenaSession.IsActive', 'false', 'ordinary raid unchanged'),
    @('retired-body', '!ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)', 'false', 'retired body unchanged'),
    @('foreign-component', '!ReferenceEquals(owner.GetComponent(_botType), bot)', 'false', 'foreign component unchanged'),
    @('pft-roster', 'PitFireTeamCompat.IsTeamPlayer(player)', 'false', 'PFT roster unchanged'),
    @('normal-only', '!_normal.Equals(_personality.GetValue(info))', 'false', 'Chad unchanged'),
    @('native-mind', '|| !allowed) return native;', ') return native;', 'Mind.BotTaunts false retained'),
    @('missing-substitution', ('Observe(world, source, player);' + "`n" + '            return true;'), ('Observe(world, source, player);' + "`n" + '            return native;'), 'live Normal eligible')
)
$rejected = 0
try {
    Positive 'production-before'
    foreach ($fault in $faults) {
        if ($tauntSource.Split([string[]]@($fault[1]), [StringSplitOptions]::None).Count -ne 2) { throw "Ambiguous mutation anchor $($fault[0])" }
        $mutant = Join-Path $tauntEvidence ($fault[0] + '.cs')
        [IO.File]::WriteAllText($mutant, $tauntSource.Replace($fault[1], $fault[2]))
        $result = Run $fault[0] $mutant
        if ($result.Exit -eq 0 -or !$result.Text.Contains('ASSERT: ' + $fault[3])) { throw "Fault not rejected at intended assertion $($fault[0]): $($result.Text)" }
        $rejected++; Write-Output "Rejected taunt fault: $($fault[0])"
    }
} finally {
    Positive 'production-after'
    foreach ($path in $tauntInputs.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $tauntInputs[$path]) { throw "Input changed during verification: $path" }
    }
}
@{ InputsSHA256 = $tauntInputs; RejectedFaults = $rejected; CheckedUtc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $tauntEvidence 'evidence.json')
Write-Output "BOT_TAUNT_CHECKS_PASSED; $rejected compiled negative controls; evidence: $tauntEvidence"
