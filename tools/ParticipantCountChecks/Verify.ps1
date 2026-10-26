$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaArtifacts = Join-Path $arenaRepo 'artifacts/participant-count-checks'
[IO.Directory]::CreateDirectory($arenaArtifacts) | Out-Null
function Run-ParticipantCheck([string]$Project, [string]$Name, [string]$Override = '') {
    $arguments = @('run','--project',(Join-Path $arenaRepo $Project),'-c','Release')
    if ($Override) { $arguments += $Override }
    $output = (& dotnet @arguments 2>&1 | ForEach-Object { "$_" }) -join "`n"
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $arenaArtifacts ($Name + '.log'))
    return [pscustomobject]@{ Code=$code; Output=$output }
}
$arenaChecks = @(
    @('tools/NativeMenuChecks','native','ARENA_MENU_CHECKS_PASSED'),
    @('tools/TeamDeathmatchRuntimeChecks','tdm','ARENA_TDM_RUNTIME_CHECKS_PASSED'))
foreach ($check in $arenaChecks) {
    $result = Run-ParticipantCheck $check[0] $check[1]
    if ($result.Code -ne 0 -or !$result.Output.Contains($check[2])) { throw $result.Output }
    Write-Output ($result.Output -split "`n" | Where-Object { $_ -match 'PASSED' })
}
$arenaFaults = @(
    @('wrong-count-ack','ArenaMode.Client/Session/ArenaServerClient.cs',
        'parsed["participants"].Value<int>() == participants.Value','true',
        'tools/NativeMenuChecks','ProductionServerClientSource','missing, mismatched or coerced total'),
    @('lost-count-freeze','ArenaMode.Client/Session/ArenaLocalRaidStartedPatch.cs',
        'participants, ArenaServerClient.PendingParticipantBosses,','null, false,',
        'tools/NativeMenuChecks','ProductionLocalStartSource','loading edit cannot change frozen'),
    @('live-team-size','ArenaMode.Client/Modes/TeamDeathmatchMode.cs',
        'ArenaSession.Participants.HasValue','false',
        'tools/TeamDeathmatchRuntimeChecks','ModeSource','frozen full-map total overrides'))
try {
    foreach ($fault in $arenaFaults) {
        $source = [IO.File]::ReadAllText((Join-Path $arenaRepo $fault[1]))
        if ($source.Split([string[]]@($fault[2]),[StringSplitOptions]::None).Count -ne 2) {
            throw "Expected one mutation site: $($fault[0])"
        }
        $mutant = Join-Path $arenaArtifacts ($fault[0] + '.cs')
        [IO.File]::WriteAllText($mutant,$source.Replace($fault[2],$fault[3]))
        $result = Run-ParticipantCheck $fault[4] $fault[0] ("-p:" + $fault[5] + '=' + $mutant)
        if ($result.Code -eq 0 -or !$result.Output.Contains($fault[6])) { throw "Fault was not caught: $($fault[0])`n$($result.Output)" }
        Write-Output "NEGATIVE CONTROL PASSED: $($fault[0])"
    }
}
finally {
    foreach ($check in $arenaChecks) {
        & dotnet build (Join-Path $arenaRepo $check[0]) -c Release -t:Rebuild --nologo -v:quiet *> (Join-Path $arenaArtifacts ($check[1] + '-restore.log'))
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $($check[0])" }
        $result = Run-ParticipantCheck $check[0] ($check[1] + '-restored')
        if ($result.Code -ne 0 -or !$result.Output.Contains($check[2])) { throw $result.Output }
    }
}
Write-Output 'PARTICIPANT COUNT CHECKS PASSED'
