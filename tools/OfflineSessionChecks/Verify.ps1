# Only the three changed client entry failure boundaries. Synthetic linked-source
# harnesses, copied mutations and no-deploy properties; no game/profile access.
param()
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$source = Join-Path $repo 'ArenaMode.Client\Session\ArenaSessionSwitch.cs'
$project = Join-Path $repo 'tools\ArenaSessionClientChecks\ArenaSessionClientChecks.csproj'
$run = Join-Path $repo ('artifacts\offline-client-checks\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
$hash = (Get-FileHash -LiteralPath $source).Hash
$oldSource = $env:ARENA_SESSION_SWITCH_SOURCE
$oldRepo = $env:ARENA_REPO_ROOT
function Invoke-Case([string]$Name, [string]$InputSource) {
    $env:ARENA_SESSION_SWITCH_SOURCE = $InputSource
    $env:ARENA_REPO_ROOT = $repo
    $lines = @(& dotnet run --project $project -c Release --no-launch-profile `
        "-p:ProductionSwitchSource=$InputSource" `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets')" `
        "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" `
        "-p:OutputPath=$(Join-Path $run ($Name + '-bin\'))" `
        "-p:IntermediateOutputPath=$(Join-Path $run ($Name + '-obj\'))" 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $lines | Set-Content -LiteralPath (Join-Path $run ($Name + '.log'))
    return @{ Code = $code; Text = $lines -join "`n" }
}
try {
    $baseline = Invoke-Case 'production' $source
    if ($baseline.Code -ne 0 -or $baseline.Text -notmatch 'ARENA_SESSION_CLIENT_CHECKS_PASSED \(\d+ assertions\)') {
        throw "Client baseline failed: $run`n$($baseline.Text)"
    }
    $count = [regex]::Match($baseline.Text, 'ARENA_SESSION_CLIENT_CHECKS_PASSED \((\d+) assertions\)').Groups[1].Value
    $mutants = @(
        @{ Name = 'lost-entry-reply-downgrades';
           Old = 'if (strictEntry) throw new InvalidOperationException("Arena entry preparation could not be confirmed.");';
           New = '// mutation: transport ambiguity is treated as unsupported';
           Expect = 'Only explicit unavailable entry can use legacy; malformed entry keeps the game open' },
        @{ Name = 'optional-control-hides-accepted-entry';
           Old = 'bool offline = await entry.ConfigureAwait(false);';
           New = 'await Task.WhenAll(control, entry).ConfigureAwait(false); bool offline = entry.Result;';
           Expect = 'Delayed optional consent cannot hide an accepted offline entry or downgrade its schema' },
        @{ Name = 'entry-timeout-downgrades';
           Old = 'if (preparation.Status == TaskStatus.RanToCompletion && preparation.Result != null)';
           New = 'if (true)';
           Expect = 'Unresolved entry preparation timeout leaves the game open without a legacy fallback' }
    )
    foreach ($mutant in $mutants) {
        if (($original.Split([string[]]@($mutant.Old), [StringSplitOptions]::None)).Length -ne 2) {
            throw "Mutation anchor is absent or ambiguous: $($mutant.Name)"
        }
        $copy = Join-Path $run ($mutant.Name + '.cs')
        [IO.File]::WriteAllText($copy, $original.Replace($mutant.Old, $mutant.New), [Text.UTF8Encoding]::new($false))
        $result = Invoke-Case $mutant.Name $copy
        if ($result.Code -eq 0 -or $result.Text -notmatch 'ARENA_SESSION_CLIENT_CHECKS_FAILED' -or
            !$result.Text.Contains($mutant.Expect) -or $result.Text -match 'error CS\d+') {
            throw "Mutation did not fail its behavioral assertion: $($mutant.Name); $run`n$($result.Text)"
        }
        Write-Output "REJECTED $($mutant.Name)"
    }
    if ((Get-FileHash -LiteralPath $source).Hash -ne $hash) { throw 'Production source changed during verification.' }
    Write-Output "OFFLINE_SESSION_CLIENT_VERIFIED baseline=$count mutants=3 evidence=$run"
}
finally {
    $env:ARENA_SESSION_SWITCH_SOURCE = $oldSource
    $env:ARENA_REPO_ROOT = $oldRepo
}
