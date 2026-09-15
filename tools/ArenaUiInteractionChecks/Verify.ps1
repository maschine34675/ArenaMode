$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'ArenaUiInteractionChecks.csproj'
$sourcePath = Join-Path $arenaRepo 'ArenaMode.Client/Session/ArenaRaidExit.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$sourceHash = (Get-FileHash -LiteralPath $sourcePath).Hash
$evidence = Join-Path $arenaRepo ('artifacts/ui-interaction-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
function Invoke-Case([string]$name, [string]$path) {
    $result = & dotnet run --project $project -c $name "-p:ExitSource=$path" -- $arenaRepo 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence ($name + '.log')), $result)
    return @{ Code=$code; Text=$result }
}
$baseline = Invoke-Case 'Release' $sourcePath
if ($baseline.Code -ne 0 -or !$baseline.Text.Contains('ARENA_EXIT_CHECKS_PASSED')) { throw $baseline.Text }
$baseline.Text.Trim() | Write-Output
$mutations = @(
    @{ Name='PipelineBypass'; From='if (RespawnEngineState.IsPipelineRunning)'; To='if (false)'; Failure='unsafe exit untouched 0' },
    @{ Name='PausedExit'; From='if (Time.timeScale <= 0f)'; To='if (false)'; Failure='unsafe exit untouched 1' },
    @{ Name='ForeignOwner'; From='!ReferenceEquals(owner.Player, local)'; To='false'; Failure='unsafe exit untouched 3' },
    @{ Name='MissingExactRaid'; From='&& ArenaSession.IsExactActiveRaid(game, world)'; To=''; Failure='ordinary or invalid request stays native' },
    @{ Name='ReleaseHoldOnFailure'; From='if (!committed) ArenaRespawnDriver.StandDown = standDown;'; To='if (!committed) ArenaRespawnDriver.StandDown = false;'; Failure='precommit restores prior hold' },
    @{ Name='ThrowingNotification'; From='try { NotificationManager.DisplayMessageNotification(message); }'; To='try { NotificationManager.DisplayMessageNotification(message); }'; Failure='notification failure cannot discard pending exit'; Custom=$true }
)
foreach ($mutation in $mutations) {
    if ($mutation.Custom) {
        $from = 'catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena leave notification unavailable: {ex.Message}"); }'
        $to = 'catch (Exception) { throw; }'
    } else { $from=$mutation.From; $to=$mutation.To }
    if (!$source.Contains($from) -or $source.IndexOf($from) -ne $source.LastIndexOf($from)) { throw "Invalid mutation anchor: $($mutation.Name)" }
    $path = Join-Path $evidence ($mutation.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($from, $to))
    $result = Invoke-Case $mutation.Name $path
    if ($result.Code -eq 0 -or $result.Text.Contains('error CS') -or !$result.Text.Contains($mutation.Failure)) { throw "Wrong defect result $($mutation.Name): $($result.Text)" }
    Write-Output "Rejected compiled defect: $($mutation.Name)"
}
if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne $sourceHash) { throw 'Exit source changed during verification.' }
$sourceHash | Set-Content -LiteralPath (Join-Path $evidence 'exit-source-sha256.txt')
& dotnet run --project (Join-Path $arenaRepo 'tools/NativeMenuChecks/NativeMenuChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native menu regression failed.' }
& pwsh -NoProfile -File (Join-Path $arenaRepo 'tools/ArenaCharacterPreviewChecks/Verify.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Preview regression failed.' }
Write-Output "Evidence: $evidence"
Write-Output 'ARENA UI INTERACTION CHECKS PASSED'
