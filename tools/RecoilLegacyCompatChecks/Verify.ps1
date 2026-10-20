param(
    [string]$PublishedDll = '',
    [string]$EvidenceDirectory = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (!$PublishedDll) {
    $PublishedDll = Join-Path $taskRoot 'artifacts\compat-first-20261020\recoil\release\BepInEx\plugins\SPTRecoilReworkLegacy\SPTRecoilReworkLegacyClient.dll'
}
if (!(Test-Path -LiteralPath $PublishedDll -PathType Leaf)) { throw 'Published 2.0.0 client DLL required for binary checks.' }
if (!$EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $taskRoot ('artifacts\compat-first-20261020\recoil-impl\checks-' + [guid]::NewGuid().ToString('N'))
}
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith((Join-Path $taskRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Evidence must stay within this repository artifacts directory.'
}
New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
$taskProject = Join-Path $PSScriptRoot 'RecoilLegacyCompatChecks.csproj'
$taskProduction = Join-Path $taskRoot 'ArenaMode.Client\Compat'
$taskNames = @('ArenaRecoilLegacyCompat.cs','ArenaRecoilLegacyContract.cs','ArenaRecoilLegacyScope.cs')
$taskHashes = @{}
foreach ($taskName in $taskNames) { $taskHashes[$taskName] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $taskProduction $taskName)).Hash }
$taskRows = [Collections.Generic.List[object]]::new()
function Invoke-Check([string]$Name, [string]$Source, [string]$Expected) {
    $taskLog = Join-Path $taskEvidence ($Name + '.log')
    & dotnet run --project $taskProject -c Release "-p:ProductionRoot=$Source" -- $PublishedDll *> $taskLog
    $taskExit = $LASTEXITCODE
    $taskText = Get-Content -Raw -LiteralPath $taskLog
    if ($Expected) {
        if ($taskExit -eq 0 -or $taskText -notmatch [regex]::Escape('ASSERT: ' + $Expected)) {
            throw "Mutant $Name was not killed by the expected behavioral assertion; see $taskLog"
        }
    } elseif ($taskExit -ne 0 -or $taskText -notmatch 'checks passed') {
        throw "Baseline failed; see $taskLog"
    }
    $taskRows.Add([pscustomobject]@{name=$Name;exit=$taskExit;expected=$Expected;log=$taskLog})
}
Invoke-Check 'baseline' $taskProduction ''
$taskMutations = @(
    @{name='destructive-singletons';file='ArenaRecoilLegacyCompat.cs';from='_contract.RecoilInstance.SetValue(null, null); _contract.CameraInstance.SetValue(null, null);';to='/* negative control: singleton handoff missing */';expected='replacement initialized without singleton destruction 0'},
    @{name='retired-updates';file='ArenaRecoilLegacyCompat.cs';from='return lease.Committed && Current(lease);';to='return true;';expected='retired body cannot rotate 0'},
    @{name='previous-controller';file='ArenaRecoilLegacyCompat.cs';from='_contract.Cache[0].SetValue(null, __3);';to='/* negative control: previous controller retained */';expected='recalculation uses incoming weapon 0'},
    @{name='lost-rollback';file='ArenaRecoilLegacyCompat.cs';from='if (restore)';to='if (false && restore)';expected='old singleton pair restored'}
)
foreach ($taskMutation in $taskMutations) {
    $taskCopy = Join-Path $taskEvidence $taskMutation.name
    New-Item -ItemType Directory -Path $taskCopy -Force | Out-Null
    foreach ($taskName in $taskNames) { Copy-Item -LiteralPath (Join-Path $taskProduction $taskName) -Destination (Join-Path $taskCopy $taskName) }
    $taskFile = Join-Path $taskCopy $taskMutation.file
    $taskSource = Get-Content -Raw -LiteralPath $taskFile
    if ([regex]::Matches($taskSource,[regex]::Escape($taskMutation.from)).Count -ne 1) { throw "Mutation seam drifted: $($taskMutation.name)" }
    Set-Content -LiteralPath $taskFile -Value $taskSource.Replace($taskMutation.from,$taskMutation.to)
    Invoke-Check $taskMutation.name $taskCopy $taskMutation.expected
}
Invoke-Check 'final-baseline' $taskProduction ''
foreach ($taskName in $taskNames) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $taskProduction $taskName)).Hash -ne $taskHashes[$taskName]) {
        throw 'Production sources changed during verification; rerun against a stable revision.'
    }
}
$taskHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEvidence 'production-sha256.json')
$taskRows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskEvidence 'results.json')
Write-Output ("Recoil Legacy baseline and four behavioral mutants verified: " + $taskEvidence)
