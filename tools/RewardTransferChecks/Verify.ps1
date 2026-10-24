# Synthetic, owned temporary trees only. Never starts EFT/SPT or writes an installed profile.
param()
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'RewardTransferChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Session\RewardTransfer.cs'
$run = Join-Path $repo ('artifacts\reward-transfer-checks\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$noDeploy = Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets'
$text = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
function Invoke-Checks([string]$Name, [string]$ProductionSource) {
    $log = Join-Path $run ($Name + '.log')
    $lines = @(& dotnet run --project $project -c Release --no-launch-profile `
        "-p:RewardTransferSource=$ProductionSource" "-p:CustomAfterMicrosoftCommonTargets=$noDeploy" `
        "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $lines | Set-Content -LiteralPath $log
    return @{ Code = $code; Text = ($lines -join "`n"); Log = $log }
}
$baseline = Invoke-Checks 'production' $source
if ($baseline.Code -ne 0 -or $baseline.Text -notmatch 'REWARD_TRANSFER_CHECKS PASS assertions=\d+ failures=0') {
    throw "Production reward-transfer checks failed: $($baseline.Log)`n$($baseline.Text)"
}
Write-Output (($baseline.Text -split "`n" | Where-Object { $_ -like 'REWARD_TRANSFER_CHECKS *' }) -join "`n")
$mutations = @(
    @{ Name = 'accept-conflicting-result'; Check = 'collision-preserves-both-copies'
        Old = 'if (!Same(bytes, ReadBounded(destination, maximumBytes)))'; New = 'if (false)' },
    @{ Name = 'erase-pending-on-rebuild'; Check = 'rebuild-refuses-before-deleting-old-marker-or-results'
        Old = 'if (PublishedResults(tree).Count != 0)'; New = 'if (false)' }
)
foreach ($mutation in $mutations) {
    if (-not $text.Contains($mutation.Old)) { throw "Mutation anchor missing: $($mutation.Name)" }
    $copy = Join-Path $run ($mutation.Name + '.cs')
    [IO.File]::WriteAllText($copy, $text.Replace($mutation.Old, $mutation.New), [Text.UTF8Encoding]::new($false))
    $result = Invoke-Checks $mutation.Name $copy
    if ($result.Code -eq 0 -or $result.Text -notmatch ('FAIL ' + [regex]::Escape($mutation.Check) + ':') `
        -or $result.Text -notmatch 'REWARD_TRANSFER_CHECKS FAIL') {
        throw "Mutation was not rejected by its behavioral oracle: $($mutation.Name); $($result.Log)`n$($result.Text)"
    }
    Write-Output "REJECTED $($mutation.Name)"
}
# Restore the normal harness binary; callers may run it with --no-build afterward.
$restored = Invoke-Checks 'production-after-mutants' $source
if ($restored.Code -ne 0 -or $restored.Text -notmatch 'REWARD_TRANSFER_CHECKS PASS assertions=\d+ failures=0') {
    throw "Final production recheck failed: $($restored.Log)"
}
Write-Output "Evidence: $run"
Write-Output 'REWARD_TRANSFER_VERIFICATION PASS production=2 mutants=2'
