#requires -Version 7.0
param([string]$SptRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$SptRoot) { $SptRoot = [IO.Path]::GetFullPath((Join-Path $repo '../..')) }
$stage = Join-Path $repo ('artifacts/apbs-compat/faults/' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $stage 'source'
[IO.Directory]::CreateDirectory($source) | Out-Null
# Copy only source inputs; tests cannot deploy into either SPT installation.
$files = & git -C $repo ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source inputs' }
foreach ($relative in $files) {
    if ($relative -notmatch '^(ArenaMode\.(Server|Tests|Shared|Client)/|Shared\.props$|AGENTS\.md$)') { continue }
    $destination = Join-Path $source $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $destination
}
$ownership = Join-Path $source 'ArenaMode.Server/Services/ArenaBotTierOwnership.cs'
$original = [IO.File]::ReadAllText($ownership)
$liveHash = (Get-FileHash -LiteralPath (Join-Path $repo 'ArenaMode.Server/Services/ArenaBotTierOwnership.cs')).Hash
$oldRoot = $env:ARENA_APBS_TEST_ROOT
try {
    $env:ARENA_APBS_TEST_ROOT = $SptRoot
    & dotnet test (Join-Path $source 'ArenaMode.Tests') -c Release --nologo "-p:SPTUserPath=$stage/install" --filter 'FullyQualifiedName~ApbsRejectsStaleForeignAndChangedOwnership' *> (Join-Path $stage 'positive-control.log')
    if ($LASTEXITCODE -ne 0) { throw 'Pristine copied-source positive control failed' }
    $needle = '&& Sessions.IsCurrentBotTierIdentity(Identity)'
    if (($original.Split($needle).Length - 1) -ne 1) { throw 'Fault anchor changed' }
    [IO.File]::WriteAllText($ownership, $original.Replace($needle, ''))
    & dotnet test (Join-Path $source 'ArenaMode.Tests') -c Release --nologo "-p:SPTUserPath=$stage/install" --filter 'FullyQualifiedName~ApbsRejectsStaleForeignAndChangedOwnership' --logger 'trx;LogFileName=identity-fault.trx' --results-directory $stage *> (Join-Path $stage 'identity-fault.log')
    if ($LASTEXITCODE -eq 0) { throw 'Removed session identity validation survived tests' }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $stage 'identity-fault.trx') -Raw
    $failed = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' -and $_.testName -like '*sameTokenRearm*' })
    if ($failed.Count -ne 1) { throw 'Fault failed for an unrelated reason rather than stale identity acceptance' }
} finally {
    $env:ARENA_APBS_TEST_ROOT = $oldRoot
    if ((Get-FileHash -LiteralPath (Join-Path $repo 'ArenaMode.Server/Services/ArenaBotTierOwnership.cs')).Hash -ne $liveHash) {
        throw 'Production source changed during copied-source fault check'
    }
}
Write-Output "Fault evidence: $stage"
Write-Output 'ARENA_APBS_FAULT_CHECKS_PASSED'
