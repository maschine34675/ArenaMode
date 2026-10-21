$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Arena session spec (docs/ARENA-SESSION-20261021.md, sections 7 and 8): the ping's optional
# arenaInstance/serverMods fields and the bounded ping the side screen asks for when it opens.
# NativeMenuChecks compiles the production ArenaServerClient.cs and executes both
# (CheckSessionPingFields, CheckBoundedPing). Each mutant below is a copy of that source outside the
# repository and must fail the NAMED assertion; the production file is hash-checked afterwards.
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'NativeMenuChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Session\ArenaServerClient.cs'
$source = [System.IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaSessionPingMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null
$env:ARENA_CAPTION_REPO_ROOT = $repoRoot

$mutants = @(
    @{ Name = 'bounded-ping-unbounded'; From = 'if (!request.Wait(milliseconds))'; To = 'if (!request.Wait(60000))';
       Expect = 'a slow server holds the caller for the budget only' },
    @{ Name = 'bounded-ping-applies-late-answer'; From = 'if (!request.Wait(milliseconds))'; To = 'if (!request.Wait(milliseconds) && false)';
       Expect = 'a slow server holds the caller for the budget only' },
    @{ Name = 'bounded-ping-latches-outage'; From = 'Plugin.ModLogger.LogDebug($"Arena server ping not available at screen open';
       To = '_available = false; Plugin.ModLogger.LogDebug($"Arena server ping not available at screen open';
       Expect = 'an unreachable server neither latches an outage' },
    @{ Name = 'session-fields-ignored'; From = 'ReadSessionFields(ping);'; To = '';
       Expect = 'a current normal server reports arenaInstance false and an empty list' },
    @{ Name = 'ping-generation-not-counted'; From = '_pingGeneration++;'; To = '';
       Expect = 'a ping without the session fields reads as an older server' },
    @{ Name = 'malformed-entry-kept'; From = 'if (string.IsNullOrWhiteSpace(guid)) continue;'; To = '';
       Expect = 'arenaInstance true is read and malformed serverMods entries are skipped' },
    @{ Name = 'instance-flag-coerced'; From = 'bool hasInstance = instance?.Type == JTokenType.Boolean;'; To = 'bool hasInstance = instance != null;';
       Expect = 'arenaInstance that is not a boolean' },
    @{ Name = 'dependencies-ignored'; From = 'ReadTextArray(mod["dependencies"])));'; To = 'null));';
       Expect = 'the dependencies of a server mod keep their string entries and drop anything else' },
    @{ Name = 'dependencies-blank-kept'; From = 'if (!string.IsNullOrWhiteSpace(text)) texts.Add(text);'; To = 'texts.Add(text);';
       Expect = 'the dependencies of a server mod keep their string entries and drop anything else' }
)
foreach ($mutant in $mutants) {
    $count = ($source.Split([string[]]@($mutant.From), [System.StringSplitOptions]::None)).Count - 1
    if ($count -lt 1) { throw "Missing mutation anchor: $($mutant.Name)" }
    $copy = Join-Path $scratch ($mutant.Name + '.cs')
    [System.IO.File]::WriteAllText($copy, $source.Replace($mutant.From, $mutant.To))
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet run --project $project -c Release `
            "-p:ProductionServerClientSource=$copy" `
            "-p:OutputPath=$(Join-Path $scratch ($mutant.Name + '-bin\'))" `
            "-p:IntermediateOutputPath=$(Join-Path $scratch ($mutant.Name + '-obj\'))" 2>&1
        $exit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    if ($exit -eq 0 -or $text -match 'error CS' -or $text -notmatch [regex]::Escape($mutant.Expect)) {
        throw "$($mutant.Name) did not fail its intended assertion (exit $exit). Evidence: $scratch`n$text"
    }
    Write-Output "Rejected mutant: $($mutant.Name)"
}

Remove-Item Env:ARENA_CAPTION_REPO_ROOT -ErrorAction SilentlyContinue
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The production native-menu checks failed after mutation verification.'
}
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $sourceHash) {
    throw 'Mutation verification changed ArenaServerClient.cs.'
}
Write-Output "SESSION_PING_VERIFIED ($($mutants.Count) mutants rejected; evidence: $scratch)"
