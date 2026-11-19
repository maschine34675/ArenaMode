#Requires -Version 7.0
<#
Offline checks of tools/Run-ArenaVerification.ps1 itself: the result cache (a step whose inputs are unchanged
since its last PASS is not run again) and what it must never do (reuse a PASS after an input changed, cache a
FAIL, cache a step whose inputs changed while it ran).

Every case runs the real runner and the real input model, copied into a throwaway checkout under %TEMP% with a
toy production tree, toy harness folders, a toy installation, toy siblings (Redeploy, _decompiled, _reference)
and a private cache directory; nothing outside that temp directory is written. A toy harness appends its name
to a counter file each time it really runs, so "ran" and "cached" are counted, not read from the verdict text.

The baseline runs every case, in order, in one sandbox; each fault below is applied to the copied runner or
input model (never to the files in this checkout), and its cases must fail at the named assertion. The baseline
and the faults run side by side as child processes (-Baseline, -Fault), each in a sandbox of its own,
ARENA_MUTANT_PARALLEL faults at a time beside the baseline (default 4). -Case runs the named cases only (after
the two priming cases), for a fix loop.
#>
param([string]$Fault, [string[]]$Case = @(), [switch]$Baseline)
$ErrorActionPreference = 'Stop'
$Case = @($Case | ForEach-Object { $_ -split ',' } | Where-Object { $_ })   # -File passes "a,b" as one string
$here = $PSScriptRoot
$realRepo = [IO.Path]::GetFullPath((Join-Path $here '..\..'))

# Each fault is an edit of the sandbox copy of one file; Find must occur exactly once in the real file.
$faults = @(
    @{ Name = 'tracked-flag-dropped'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'tracking a file reruns the step'; Cases = @('tracked')
        Find = '$lineOf = { param($rel) $e = $snapshot.Files[$rel]; "$rel|$(if ($e.Tracked) { ''T'' } else { ''U'' })|$($e.Hash)" }'
        Replace = '$lineOf = { param($rel) $e = $snapshot.Files[$rel]; "$rel|$($e.Hash)" }' },
    @{ Name = 'shared-files-excluded'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a production file reruns the step'; Cases = @('production')
        Find = '        shared = $Model.SharedHash'; Replace = '        shared = ''''' },
    @{ Name = 'records-never-named'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a named docs record reruns the step'; Cases = @('named-record')
        Find = '            $payloadOnly = $ArenaPayloadOnlyRecordNamers[$record]'; Replace = '            continue' },
    @{ Name = 'folder-mentions-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a referenced harness folder reruns the step'; Cases = @('referenced-folder')
        Find = '        foreach ($m in $fact.Mentions) {'; Replace = '        foreach ($m in @()) {' },
    @{ Name = 'environment-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'an environment variable reruns the step'; Cases = @('environment')
        Find = "        `$_ -match '^(ARENA_|SPT|"; Replace = "        `$_ -match '^(SPT|" },
    @{ Name = 'game-assemblies-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'installed game assemblies rerun the step'; Cases = @('game-assemblies')
        Find = '        $lines.Add("install|$root|managed|$(Get-ArenaStatManifest (Join-Path $root ''EscapeFromTarkov_Data\Managed''))")'; Replace = '' },
    @{ Name = 'small-files-by-time'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a small installed file rewritten with the same bytes keeps the cache'; Cases = @('runtime-state')
        Find = '            if ($HashSmall -and $f.Length -le 262144) {'; Replace = '            if ($false) {' },
    @{ Name = 'install-state-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'installed config reruns a step that names it'; Cases = @('install-state')
        Find = '    foreach ($pattern in $Scope.InstallState) {'; Replace = '    foreach ($pattern in @()) {' },
    @{ Name = 'redeploy-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'the sibling Redeploy checkout reruns the step'; Cases = @('redeploy')
        Find = '    $lines.Add("sibling|Redeploy|$(Get-ArenaGitCheckoutIdentity (Join-Path $parent ''Redeploy''))")'; Replace = '' },
    @{ Name = 'decompiled-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'decompiled sources rerun the step'; Cases = @('decompiled')
        Find = '    foreach ($sibling in ''_decompiled'', ''_reference'') {'; Replace = '    foreach ($sibling in @()) {' },
    @{ Name = 'head-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a commit reruns a step that calls git'; Cases = @('git-head')
        Find = '        head = $(if ($Scope.GitSensitive) { $Model.Snapshot.Head } else { '''' })'; Replace = '        head = ''''' },
    @{ Name = 'failure-cached'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a failure is never cached'; Cases = @('failure')
        Find = 'if ($passed -and $keyInfo -and $keyInfo.Cacheable) {'; Replace = 'if ($keyInfo -and $keyInfo.Cacheable) {' },
    @{ Name = 'no-after-run-check'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a step whose inputs change during the run is not cached'; Cases = @('changed-during-run')
        Find = 'else { $afterRun = Test-ArenaStepStillValid $cacheModel $keyInfo $item $Snapshot $repo $installRoots $runnerArguments }'; Replace = 'else { $afterRun = $null }' },
    # A worker step is checked after its run in its own thread job: its inputs (for the cache) and its worker (for
    # the next step there).
    @{ Name = 'worker-after-run-unchecked'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a step whose inputs change during a worker run is not cached'; Cases = @('parallel-touch')
        Find = '$checked.AfterRun = Test-ArenaStepStillValid $work.Model $work.KeyInfo $work.Item $work.Snapshot $work.RepoRoot $work.InstallRoots $work.RunnerArguments'; Replace = '$checked.AfterRun = $null' },
    @{ Name = 'worker-drift-unchecked'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a worker that a step changed is synced again before its next step'; Cases = @('parallel-touch')
        Find = '$drift = Test-ArenaSnapshotStillValid $work.Snapshot @($work.Snapshot.Files.Keys)'; Replace = '$drift = $null' },
    # The workers build the shared sibling Redeploy checkout each into a folder of their own.
    @{ Name = 'redeploy-props-not-passed'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'each worker builds the Redeploy project into its own folder'; Cases = @('parallel-redeploy')
        Find = "        CustomBeforeDirectoryBuildProps = Join-Path `$full 'tools\ArenaWorkerRedeploy.props'`n"; Replace = '' },
    @{ Name = 'redeploy-exclude-dropped'; File = 'tools/ArenaWorkerRedeploy.props'; Failure = 'each worker builds the Redeploy project into its own folder'; Cases = @('parallel-redeploy')
        Find = '<DefaultItemExcludes>$(DefaultItemExcludes);obj\**;bin\**</DefaultItemExcludes>'; Replace = '' },
    @{ Name = 'redeploy-restore-without-environment'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'each worker builds the Redeploy project into its own folder'; Cases = @('parallel-redeploy')
        Find = "@('restore', `$full, '--nologo', '-v:q') `$Worker `$environment"; Replace = "@('restore', `$full, '--nologo', '-v:q') `$Worker @{}" },
    @{ Name = 'redeploy-build-wiped'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'a worker keeps its Redeploy build between runs'; Cases = @('parallel-redeploy')
        Find = 'Where-Object { $_.Name -ine $keep } | '; Replace = '' },
    @{ Name = 'compile-items-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a linked production file selects its harness'; Cases = @('select')
        Find = '            foreach ($rel in $items) { if (-not $exact.ContainsKey($rel)) { $exact[$rel] = "compiled by $project" } }'; Replace = '' },
    @{ Name = 'named-paths-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a mutant target named in a script selects its harness'; Cases = @('select')
        Find = '        foreach ($p in $fact.ProductionPaths) {'; Replace = '        foreach ($p in @()) {' },
    @{ Name = 'untracked-not-changed'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'an untracked file selects too'; Cases = @('select')
        Find = "        @(Split-ArenaNul (Invoke-ArenaGitLines `$Root @('ls-files', '-z', '--others', '--exclude-standard')))`n    return @(`$changed | Sort-Object -Unique)"
        Replace = "        @()`n    return @(`$changed | Sort-Object -Unique)" },
    # Two layers keep a worker identical: the copy, and the file-by-file comparison after it, which refuses to
    # run on a worker that differs. Each layer is removed alone.
    @{ Name = 'untracked-not-synced'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'steps run in two worker checkouts'; Cases = @('parallel')
        Find = '        elseif (-not $have -or $have.Hash -ne $want.Hash) {'; Replace = '        elseif ($have -and $have.Hash -ne $want.Hash) {' },
    @{ Name = 'deletion-not-synced-unchecked'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'a worker mirrors uncommitted, untracked, deleted and staged changes'; Cases = @('parallel')
        Find = '            if ([IO.File]::Exists($target)) { [IO.File]::Delete($target) }'; Replace = ''
        Find2 = '    if ($diff) { throw "Worker $Worker does not match the checkout under test after the sync: $diff" }'; Replace2 = '' },
    @{ Name = 'stale-write-time'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'a synced file gets a fresh write time'; Cases = @('parallel')
        Find = '            [IO.File]::SetLastWriteTimeUtc($target, $now)'; Replace = '' },
    @{ Name = 'workers-start-workers'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a runner inside a worker runs its steps sequentially'; Cases = @('parallel')
        Find = 'if ($env:ARENA_VERIFICATION_WORKER) { $Parallel = 1 }'; Replace = '' },
    @{ Name = 'deploy-blames-one-step'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a step that ran alongside a deploy fails and both are named'; Cases = @('parallel-guard')
        Find = '                foreach ($other in $running) { $other | Add-Member -NotePropertyName Tainted -NotePropertyValue $installBroken -Force }'; Replace = '' },
    @{ Name = 'mutants-run-from-temp'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'killed mutants pass their harness'; Cases = @('helper')
        Find = '    $mutantBin=Join-Path $projectDir "bin\m-$runId"'; Replace = '    $mutantBin=Join-Path $scratch ''bin''' },
    @{ Name = 'helper-accepts-survivors'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'a survivor mutant fails its harness'; Cases = @('helper')
        Find = '        if($reason) { throw ('; Replace = '        if($false) { throw (' },
    @{ Name = 'fault-environment-dropped'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '    if ($Environment) { foreach ($name in $Environment.Keys) { $info.Environment[$name] = [string]$Environment[$name] } }'; Replace = '' },
    @{ Name = 'fault-edits-not-chained'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '            $text=$text.Replace($find,$replace)'; Replace = '            $text=$source.Replace($find,$replace)' },
    @{ Name = 'prepare-dropped'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '                $build+=$extra'; Replace = '                $null=$extra' },
    @{ Name = 'copy-name-ignored'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '$copy=Join-Path $directory $(if($fault.CopyName) { $fault.CopyName } else { $fault.Name+''.cs'' })'; Replace = '$copy=Join-Path $directory ($fault.Name+''.cs'')' },
    @{ Name = 'failure-prefix-fixed'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '        $expected=$FailurePrefix+$result.Failure'; Replace = '        $expected=''FAIL: ''+$result.Failure' },
    @{ Name = 'whitespace-not-collapsed'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '        if($CollapseWhitespace) {'; Replace = '        if($false) {' },
    @{ Name = 'program-arguments-dropped'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = ' + @($using:programArguments))'; Replace = ')' },
    @{ Name = 'environment-process-wide'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'channel faults pass their harness'; Cases = @('helper-channels')
        Find = '$info.Environment[$name] = [string]$Environment[$name]'; Replace = '[Environment]::SetEnvironmentVariable($name, [string]$Environment[$name])' },
    @{ Name = 'prepare-output-unchecked'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'a leaking Prepare fails its harness'; Cases = @('helper-channels')
        Find = "                if(`$extra -isnot [string] -or -not `$extra.StartsWith('-p:')) {"; Replace = '                if($false) {' },
    @{ Name = 'single-site-unchecked'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'a double-site fault fails its harness'; Cases = @('helper-channels')
        Find = '            if($RequireSingleSite) {'; Replace = '            if($false) {' },
    @{ Name = 'failure-marker-ignored'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'an unmarked fault fails its harness'; Cases = @('helper-channels')
        Find = '            elseif($FailureMarker -and -not $result.Output.Contains($FailureMarker)) { "the output lacks $FailureMarker" }'; Replace = '' },
    @{ Name = 'uncompiled-kills'; File = 'tools/Invoke-ArenaSourceChecks.ps1'; Failure = 'a broken-raw fault fails its harness'; Cases = @('helper-channels')
        Find = "        `$reason=if(-not `$result.Compiled) { 'it did not compile' }"; Replace = "        `$reason=if(`$false) { 'it did not compile' }" },
    @{ Name = 'artifacts-not-mirrored'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'a registered artifact fixture reaches the workers'; Cases = @('parallel-artifacts')
        Find = '    foreach ($prefix in $ArenaArtifactInputs) {'; Replace = '    foreach ($prefix in @()) {' },
    @{ Name = 'register-keyed'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'editing the register keeps every PASS'; Cases = @('register')
        Find = ' -and $_ -notin $ArenaNotStepInputs'; Replace = '' },
    @{ Name = 'runner-inputs-dropped'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'the no-deploy targets the runner passes rerun a program-only step'; Cases = @('runner-inputs')
        Find = '(@($shared) + @($ArenaRunnerInputs | Where-Object { $model.Index.ContainsKey($_.ToLowerInvariant()) }))'; Replace = '@($shared)' },
    @{ Name = 'renames-hidden'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a renamed file selects by its old path too'; Cases = @('select')
        Find = "@('diff', '--name-only', '--no-renames', '-z', `$Since, '--')"; Replace = "@('diff', '--name-only', '-z', `$Since, '--')" },
    @{ Name = 'segments-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a path built from quoted segments selects its harness'; Cases = @('select')
        Find = "            @([regex]::Matches(`$Text, `$ArenaSegmentPathPattern)"; Replace = "            @([regex]::Matches('', `$ArenaSegmentPathPattern)" },
    @{ Name = 'enumerating-helper-trusted'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a step that reaches an enumerating helper reruns on any change'; Cases = @('wide-helper')
        Find = "        if (`$fact.Enumerates -and -not `$payload -and -not `$wide)"; Replace = "        if (`$fact.Enumerates -and -not `$payload -and -not `$wide -and `$rel -notlike 'tools/*-*.ps1')" },
    @{ Name = 'artifact-inputs-ignored'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a changed fixture under artifacts/ reruns the step'; Cases = @('artifact-input')
        Find = '    foreach ($prefix in @($Scope.ArtifactInputs)) {'; Replace = '    foreach ($prefix in @()) {' },
    @{ Name = 'env-path-by-name'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'a changed folder behind a variable reruns the step'; Cases = @('env-path')
        Find = "        `$content = if (`$value -match '^[A-Za-z]:\\' -and -not `$inInstall -and (Test-Path -LiteralPath `$value))"; Replace = "        `$content = if (`$false)" },
    @{ Name = 'env-path-in-install-keyed'; File = 'tools/ArenaVerificationInputs.ps1'; Failure = 'runtime state that mods write keeps the cache'; Cases = @('runtime-state')
        Find = " -and -not `$inInstall -and (Test-Path -LiteralPath `$value)"; Replace = " -and (Test-Path -LiteralPath `$value)" },
    @{ Name = 'no-restore-in-workers'; File = 'tools/ArenaVerificationWorkers.ps1'; Failure = 'a --no-restore step passes in a fresh worker'; Cases = @('parallel-restore')
        Find = '        if ($noRestore) { foreach ($p in @($Model.FolderFiles[$folder] | Where-Object { $_ -like ''*.csproj'' })) { $projects.Add($p) } }'; Replace = '' },
    @{ Name = 'worker-runs-workers'; File = 'tools/Run-ArenaVerification.ps1'; Failure = 'a runner started in a worker runs sequentially and keeps its uncommitted files'; Cases = @('worker-guard')
        Find = "if ((Split-Path -Leaf `$repo) -match ('^' + [regex]::Escape(`$ArenaWorkerPrefix) + '\d+`$')) { `$Parallel = 1 }"; Replace = '' },
    @{ Name = 'no-cache-ignored'; File = 'tools/Run-ArenaVerification.ps1'; Failure = '-NoCache runs the step'; Cases = @('no-cache')
        Find = '    if ($keyInfo -and $keyInfo.Cacheable -and -not $NoCache) {'; Replace = '    if ($keyInfo -and $keyInfo.Cacheable) {' }
)
# Some named assertions are also tripped by a failure unrelated to the fault: "channel faults pass their harness"
# fails just as well when a toy build or a worker sync breaks under load, and the fault would count as killed
# without having been tested. Such a fault must also show its own symptom (compared without ANSI colour codes and
# with line wraps and runs of whitespace collapsed: PowerShell colours and wraps error messages depending on the
# terminal it was started from). Locale-free texts only.
$evidence = @{
    'untracked-not-synced' = 'ArenaMode.Client/Staged.cs failed in'
    'mutants-run-from-temp' = 'repository root not found'
    'fault-environment-dropped' = 'intended assertion: pinned (the program exited 0)'
    'fault-edits-not-chained' = 'Fault changes nothing: two-step'
    'prepare-dropped' = 'intended assertion: prepared (the program exited 0)'
    'copy-name-ignored' = 'intended assertion: prepared (it did not compile)'
    'failure-prefix-fixed' = "the output lacks 'FAIL: compiled answer is 42'"
    'whitespace-not-collapsed' = "lacks 'ASSERT: compiled answer is 42') CHANNEL_TOY_FAILED ASSERT: compiled answer is 42"
    'program-arguments-dropped' = 'CHANNEL_TOY_FAILED ASSERT: program argument arrives'
    'environment-process-wide' = 'CHANNEL_TOY_FAILED ASSERT: pinned text keeps 42'
    'artifacts-not-mirrored' = 'ARTIFACT_TOY_MISSING_FIXTURE'
    'no-restore-in-workers' = 'NETSDK1004'
    'worker-runs-workers' = 'Refusing to sync'
    'worker-after-run-unchecked' = 'TOUCHED_PASS_CACHED'
    'worker-drift-unchecked' = 'ECHO_SAW_A_CHANGED_WORKER'
    'redeploy-props-not-passed' = '\Redeploy\ToyCore\bin\Release\net10.0\ToyCore.dll'
    'redeploy-exclude-dropped' = 'CS0579'
    'redeploy-restore-without-environment' = 'NETSDK1004'
    'redeploy-build-wiped' = 'REDEPLOY_REBUILT'
}
foreach ($name in $evidence.Keys) {
    $definition = $faults | Where-Object { $_.Name -eq $name }
    if (-not $definition) { throw "Evidence names no fault: $name" }
    $definition.Evidence = $evidence[$name]
}
function Get-FlatText([string]$Text) { return ((($Text -replace '\x1b\[[0-9;?]*[A-Za-z]', '') -replace '\r?\n\s*\|?', ' ') -replace '\s+', ' ') }

function Check([bool]$Condition, [string]$Name, [string]$Context = '') {
    if (-not $Condition) { Write-Output "FAIL: $Name"; if ($Context) { Write-Output $Context }; exit 1 }
    Write-Output "ok: $Name"
}

function Write-Text([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory((Split-Path $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}

function Invoke-Git([string]$Root, [string[]]$Arguments) {
    $out = & git -C $Root -c user.name=checks -c user.email=checks@invalid -c core.autocrlf=false @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' '): $out" }
}

# The sandbox: dev/ArenaMode (a git checkout with the real runner files), dev/Redeploy (git), dev/_decompiled,
# dev/_reference, spt/ (a toy installation), cache/, counter.txt.
function New-Sandbox {
    $base = Join-Path ([IO.Path]::GetTempPath()) ('arena-runner-checks-' + [Guid]::NewGuid().ToString('N'))
    $dev = Join-Path $base 'dev'
    $repo = Join-Path $dev 'ArenaMode'
    $spt = Join-Path $base 'spt'
    foreach ($rel in 'tools/Run-ArenaVerification.ps1', 'tools/ArenaVerificationInputs.ps1', 'tools/ArenaVerificationWorkers.ps1',
        'tools/ArenaWorkerRedeploy.props', 'tools/Assert-ArenaTestRun.ps1', 'tools/Read-ArenaInstalledSnapshot.ps1', 'tools/Invoke-ArenaSourceChecks.ps1',
        'tools/AirPitChecks/NoDeploy.targets', '.gitignore') {
        $source = Join-Path $realRepo $rel
        if (Test-Path -LiteralPath $source) { Write-Text (Join-Path $repo $rel) ([IO.File]::ReadAllText($source)) }
    }
    Write-Text (Join-Path $repo 'AGENTS.md') "sandbox`n"
    Write-Text (Join-Path $repo 'ArenaMode.Client/Toy.cs') "namespace Toy { class Toy { } }`n"
    Write-Text (Join-Path $repo 'ArenaMode.Client/Other.cs') "namespace Toy { class Other { } }`n"
    Write-Text (Join-Path $repo 'tools/ReferencedChecks/Data.txt') "data 1`n"
    Write-Text (Join-Path $repo 'docs/reports/2026-11/RECORD.md') "an unnamed record`n"
    Write-Text (Join-Path $repo 'docs/reviews/TOY-REVIEW.md') "a record the toy harness reads`n"
    Write-Text (Join-Path $repo 'docs/README.md') "index`n"
    Write-Text (Join-Path $repo 'docs/licenses/LICENSE.txt') "licence 1`n"
    $toy = @'
param([string]$Stage)
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
Add-Content -LiteralPath $env:RUNNERCHECKS_COUNTER -Value ('ToyChecks' + $(if ($Stage) { ":$Stage" } else { '' }))
$null = Get-Content -Raw (Join-Path $repo 'ArenaMode.Client/Toy.cs')
$null = Get-Content -Raw (Join-Path $PSScriptRoot '../ReferencedChecks/Data.txt')
$null = Get-Content -Raw (Join-Path $repo 'docs/reviews/TOY-REVIEW.md')
if (Test-Path (Join-Path $repo 'ArenaMode.Client/fail.flag')) { 'FAIL: toy asked to fail'; exit 1 }
if ($env:ARENA_RUNNER_CHECKS_TOUCH) { Add-Content (Join-Path $repo 'ArenaMode.Client/Toy.cs') '// touched while running' }
'TOY_CHECKS_PASSED'
'@
    Write-Text (Join-Path $repo 'tools/ToyChecks/Verify.ps1') $toy
    # OtherChecks names the register, as VerificationRunnerChecks does in the real checkout.
    Write-Text (Join-Path $repo 'tools/OtherChecks/Verify.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'OtherChecks'`n`$register = 'tools/verification-steps.txt'`n'OTHER_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/GitToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'GitToyChecks'`n`$null = & git -C (Join-Path `$PSScriptRoot '../..') rev-parse HEAD`n'GIT_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/ConfigToyChecks/Verify.ps1') "param([string]`$SptRoot)`nAdd-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'ConfigToyChecks'`n`$null = Join-Path `$SptRoot 'BepInEx\config'`n'CONFIG_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/Verify-LooseToy.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'tools/Verify-LooseToy.ps1'`n'LOOSE_TOY_PASSED'`n"
    # Selection: a project that links production through a glob (no literal path), a script that names a
    # mutant target, and the registry.
    Write-Text (Join-Path $repo 'tools/LinkToyChecks/LinkToyChecks.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>`n  <ItemGroup><Compile Include=`"../../ArenaMode.Client/To*.cs`" /></ItemGroup>`n</Project>`n"
    Write-Text (Join-Path $repo 'tools/LinkToyChecks/Program.cs') "System.Console.WriteLine(`"LINK_TOY`");`n"
    Write-Text (Join-Path $repo 'tools/LiteralToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'LiteralToyChecks'`n`$target = Join-Path (Join-Path `$PSScriptRoot '../..') 'ArenaMode.Client/Other.cs'`n'LITERAL_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/verification-steps.txt') "# toy registry`nToyChecks`nOtherChecks`nLinkToyChecks`nLiteralToyChecks`nGitToyChecks`nConfigToyChecks`ntools/Verify-LooseToy.ps1`nSegmentToyChecks`n"
    # Parallel workers: a toy that reports what its checkout holds, one that takes a while, one that deploys.
    $echo = @'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Add-Content -LiteralPath $env:RUNNERCHECKS_COUNTER -Value 'EchoToyChecks'
"ROOT=$repo"
"TOY=" + (Get-FileHash -LiteralPath (Join-Path $repo 'ArenaMode.Client/Toy.cs') -Algorithm SHA256).Hash
"NEW=" + (Test-Path -LiteralPath (Join-Path $repo 'ArenaMode.Client/New.cs'))
"OTHER=" + (Test-Path -LiteralPath (Join-Path $repo 'ArenaMode.Client/Other.cs'))
$null = & git -C $repo ls-files --error-unmatch ArenaMode.Client/Staged.cs 2>$null
"STAGED=$LASTEXITCODE"
'ECHO_TOY_PASSED'
'@
    Write-Text (Join-Path $repo 'tools/EchoToyChecks/Verify.ps1') ($echo + "`nif (`$env:ARENA_RUNNER_CHECKS_ECHO_MARK) { Set-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_ECHO_MARK 'echo ran' }`n")
    # Two stages that build a project of the sibling Redeploy checkout at the same time (each waits up to 60 s
    # for the other to start), with --no-restore like the harnesses that rely on the workers' restore.
    Write-Text (Join-Path $repo 'tools/RedeployToyChecks/RedeployToyChecks.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>`n  <ItemGroup><ProjectReference Include=`"..\..\..\Redeploy\ToyCore\ToyCore.csproj`" /></ItemGroup>`n</Project>`n"
    Write-Text (Join-Path $repo 'tools/RedeployToyChecks/Program.cs') "System.Console.WriteLine(ToyCore.Core.Value);`n"
    Write-Text (Join-Path $repo 'tools/RedeployToyChecks/Verify.ps1') @'
param([string]$Stage)
Add-Content -LiteralPath $env:RUNNERCHECKS_COUNTER -Value "RedeployToyChecks:$Stage"
Set-Content -LiteralPath (Join-Path $env:ARENA_RUNNER_CHECKS_REDEPLOY_MARKS "$Stage.started") $Stage
$other = Join-Path $env:ARENA_RUNNER_CHECKS_REDEPLOY_MARKS "$(if ($Stage -eq 'A') { 'B' } else { 'A' }).started"
$deadline = (Get-Date).AddSeconds(60); while (-not (Test-Path -LiteralPath $other) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
"OVERLAP=$(Test-Path -LiteralPath $other)"
& dotnet build (Join-Path $PSScriptRoot 'RedeployToyChecks.csproj') -c Release --no-restore -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'REDEPLOY_TOY_PASSED'
'@
    # SleepToyChecks can also undo a deploy before it ends (ARENA_RUNNER_CHECKS_RESTORE), as a restoring
    # harness or a person would: the change must still fail it, because it ran while the install was changed.
    # It then waits for that deploy (at most 60 s) and runs 8 s past it, so the overlap does not depend on how
    # fast two workers start on a loaded machine.
    Write-Text (Join-Path $repo 'tools/SleepToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'SleepToyChecks'`nif (`$env:ARENA_RUNNER_CHECKS_RESTORE) { `$deadline = (Get-Date).AddSeconds(60); while (-not (Test-Path -LiteralPath `$env:ARENA_RUNNER_CHECKS_RESTORE) -and (Get-Date) -lt `$deadline) { Start-Sleep -Milliseconds 200 } }`nStart-Sleep -Seconds 8`nif (`$env:ARENA_RUNNER_CHECKS_RESTORE) { Remove-Item -LiteralPath `$env:ARENA_RUNNER_CHECKS_RESTORE -ErrorAction SilentlyContinue }`n'SLEEP_TOY_PASSED'`n"
    # A loose helper that enumerates the checkout, a fixture under the ignored artifacts/ (a registered input
    # prefix), a project built with --no-restore, and a program that reads production through path segments.
    Write-Text (Join-Path $repo 'tools/Scan-Toy.ps1') "`$root = Join-Path `$PSScriptRoot '..'`n`$null = & git -C `$root ls-files`n"
    Write-Text (Join-Path $repo 'tools/ScanToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'ScanToyChecks'`n& (Join-Path `$PSScriptRoot '../Scan-Toy.ps1')`n'SCAN_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'artifacts/pft-installed/Fixture.cs') "fixture 1`n"
    Write-Text (Join-Path $repo 'tools/ArtifactToyChecks/Verify.ps1') "`$ErrorActionPreference = 'Stop'`nAdd-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'ArtifactToyChecks'`n`$fixture = Join-Path `$PSScriptRoot '../../artifacts/pft-installed/Fixture.cs'`nif (-not (Test-Path -LiteralPath `$fixture)) { 'ARTIFACT_TOY_MISSING_FIXTURE'; exit 1 }`n`$null = Get-Content -Raw `$fixture`n'ARTIFACT_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/NoRestoreToyChecks/NoRestoreToyChecks.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>`n</Project>`n"
    Write-Text (Join-Path $repo 'tools/NoRestoreToyChecks/Program.cs') "System.Console.WriteLine(`"NO_RESTORE_TOY_PASSED`");`n"
    Write-Text (Join-Path $repo 'tools/NoRestoreToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'NoRestoreToyChecks'`n& dotnet run --project (Join-Path `$PSScriptRoot 'NoRestoreToyChecks.csproj') -c Release --no-restore`nexit `$LASTEXITCODE`n"
    Write-Text (Join-Path $repo 'tools/SegmentToyChecks/Program.cs') "var text = System.IO.File.ReadAllText(System.IO.Path.Combine(`"..`", `"..`", `"ArenaMode.Client`", `"Answer.cs`"));`n"
    # The shared mutant helper: a production file, a program that pins it, and faults through the real helper
    # (ARENA_RUNNER_CHECKS_BADFAULT adds one that must make the harness fail).
    Write-Text (Join-Path $repo 'ArenaMode.Client/Answer.cs') "public static class Answer { public static int Value() => 42; }`n"
    Write-Text (Join-Path $repo 'tools/MutantToyChecks/MutantToyChecks.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AnswerSource Condition=`"'`$(AnswerSource)' == ''`">../../ArenaMode.Client/Answer.cs</AnswerSource></PropertyGroup>`n  <ItemGroup><Compile Include=`"`$(AnswerSource)`" /></ItemGroup>`n</Project>`n"
    # Like CustomKitChecks, the toy program finds the repository by walking up from its own directory.
    Write-Text (Join-Path $repo 'tools/MutantToyChecks/Program.cs') "var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);`nwhile (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, `"AGENTS.md`"))) d = d.Parent;`nif (d == null) { System.Console.WriteLine(`"repository root not found`"); return 2; }`nif (Answer.Value() != 42) { System.Console.WriteLine(`"FAIL: toy answers 42`"); return 1; }`nif (Answer.Value() < 0) { System.Console.WriteLine(`"FAIL: toy answer is positive`"); return 1; }`nSystem.Console.WriteLine(`"MUTANT_TOY_PASSED`");`nreturn 0;`n"
    $mutantToy = @'
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$answer = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Answer.cs')).Path
Add-Content -LiteralPath $env:RUNNERCHECKS_COUNTER -Value 'MutantToyChecks'
$faults = @(
    @{ Name = 'off-by-one'; Find = '=> 42;'; Replace = '=> 41;'; Failure = 'toy answers 42' },
    @{ Name = 'negative'; Find = '=> 42;'; Replace = '=> -42;'; Failure = 'toy answers 42' }
)
switch ($env:ARENA_RUNNER_CHECKS_BADFAULT) {
    'survivor' { $faults += @{ Name = 'survivor'; Find = '=> 42;'; Replace = '=> 42 ;'; Failure = 'toy answers 42' } }
    'broken' { $faults += @{ Name = 'broken'; Find = '=> 42;'; Replace = '=> 42 +;'; Failure = 'toy answers 42' } }
    'wrong-assertion' { $faults += @{ Name = 'wrong-assertion'; Find = '=> 42;'; Replace = '=> 41;'; Failure = 'toy answer is positive' } }
}
foreach ($fault in $faults) { $fault.Path = $answer; $fault.Property = 'AnswerSource' }
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'MutantToyChecks.csproj') 'MUTANT_TOY_PASSED' @() $faults
'@
    Write-Text (Join-Path $repo 'tools/MutantToyChecks/Verify.ps1') $mutantToy
    # The helper's other channels (environment variable, chained edits, Prepare with CopyName, prefix, marker,
    # single site, whitespace, program arguments) through a second toy; ARENA_RUNNER_CHECKS_BADFAULT adds a
    # fault the helper must refuse.
    Write-Text (Join-Path $repo 'ArenaMode.Client/Channel.cs') "public static class Channel { public static int Value() => 42; } // 42 is the answer`n"
    Write-Text (Join-Path $repo 'tools/MutantChannelToyChecks/MutantChannelToyChecks.csproj') @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ChannelSource Condition="'$(ChannelSource)' == ''">../../ArenaMode.Client/Channel.cs</ChannelSource>
    <ChannelSource Condition="'$(ChannelRoot)' != ''">$(ChannelRoot)/Channel.cs</ChannelSource>
  </PropertyGroup>
  <ItemGroup><Compile Include="$(ChannelSource)" /></ItemGroup>
</Project>
'@
    Write-Text (Join-Path $repo 'tools/MutantChannelToyChecks/Program.cs') @'
var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "AGENTS.md"))) d = d.Parent;
if (d == null) { System.Console.WriteLine("repository root not found"); return 2; }
if (args.Length != 1 || args[0] != "go") { System.Console.WriteLine("CHANNEL_TOY_FAILED ASSERT: program argument arrives"); return 1; }
int value = Channel.Value();
if (value == 7) { System.Console.WriteLine("ASSERT: answer is never seven"); return 3; }
if (value != 42) { System.Console.WriteLine("CHANNEL_TOY_FAILED ASSERT: compiled answer\n   is  42"); return 1; }
var pinned = System.Environment.GetEnvironmentVariable("TOY_PINNED_SOURCE") ?? System.IO.Path.Combine(d.FullName, "ArenaMode.Client", "Channel.cs");
if (!System.IO.File.ReadAllText(pinned).Contains("=> 42;")) { System.Console.WriteLine("CHANNEL_TOY_FAILED ASSERT: pinned text keeps 42"); return 1; }
System.Console.WriteLine("CHANNEL_TOY_PASSED");
return 0;
'@
    Write-Text (Join-Path $repo 'tools/MutantChannelToyChecks/Verify.ps1') @'
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$channel = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Channel.cs')).Path
$project = Join-Path $PSScriptRoot 'MutantChannelToyChecks.csproj'
Add-Content -LiteralPath $env:RUNNERCHECKS_COUNTER -Value 'MutantChannelToyChecks'
$faults = @(
    @{ Name = 'compiled'; Property = 'ChannelSource'; Find = '=> 42;'; Replace = '=> 41;'; Failure = 'compiled answer is 42' },
    @{ Name = 'pinned'; Environment = 'TOY_PINNED_SOURCE'; Find = '=> 42;'; Replace = '=> 42 ;'; Failure = 'pinned text keeps 42' },
    @{ Name = 'two-step'; Property = 'ChannelSource'; Find = @('=> 42;', '40 + 2'); Replace = @('=> 40 + 2;', '40 + 1'); Failure = 'compiled answer is 42' },
    @{ Name = 'prepared'; CopyName = 'Channel.cs'; Prepare = { param($Copy, $Directory) "-p:ChannelRoot=$(Split-Path -Parent $Copy)" }; Find = '=> 42;'; Replace = '=> 43;'; Failure = 'compiled answer is 42' }
)
$raw = @(@{ Name = 'raw'; Property = 'ChannelSource'; Find = '=> 42;'; Replace = '=> 40;'; Failure = 'compiled answer' })
switch ($env:ARENA_RUNNER_CHECKS_BADFAULT) {
    'double-site' { $faults += @{ Name = 'double-site'; Property = 'ChannelSource'; Find = '42'; Replace = '41'; Failure = 'compiled answer is 42' } }
    'unmarked' { $faults += @{ Name = 'unmarked'; Property = 'ChannelSource'; Find = '=> 42;'; Replace = '=> 7;'; Failure = 'answer is never seven' } }
    'prepare-leak' { $faults += @{ Name = 'prepare-leak'; CopyName = 'Channel.cs'; Prepare = { param($Copy, $Directory) 'stray'; "-p:ChannelRoot=$Directory" }; Find = '=> 42;'; Replace = '=> 44;'; Failure = 'compiled answer is 42' } }
    'broken-raw' { $raw += @{ Name = 'broken-raw'; Property = 'ChannelSource'; Find = '=> 42;'; Replace = '=> 42'; Failure = '; expected' } }
}
foreach ($fault in $faults + $raw) { $fault.Path = $channel }
Test-ArenaSourceFaults $project 'CHANNEL_TOY_PASSED' @() $faults -FailurePrefix 'ASSERT: ' -FailureMarker 'CHANNEL_TOY_FAILED' -RequireSingleSite -CollapseWhitespace -Arguments 'go'
Test-ArenaSourceFaults $project 'CHANNEL_TOY_PASSED' @() $raw -FailurePrefix '' -Arguments 'go'
'@
    Write-Text (Join-Path $repo 'tools/DeployToyChecks/Verify.ps1') "param([string]`$SptRoot)`nAdd-Content -LiteralPath `$env:RUNNERCHECKS_COUNTER -Value 'DeployToyChecks'`n`$null = New-Item -ItemType Directory -Force (Join-Path `$SptRoot 'BepInEx/plugins/maschine-ArenaMode')`nSet-Content -LiteralPath (Join-Path `$SptRoot 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll') 'deployed'`n'DEPLOY_TOY_PASSED'`n"
    # The runner's own git calls use the machine's configuration; with core.autocrlf=true a worker would check out
    # CRLF where the sandbox holds LF (a faulted file, a toy written here), and the sync would copy that file
    # again on every run with a fresh write time, which rebuilds every project importing it.
    Invoke-Git $repo @('init', '-q', '-b', 'main'); Invoke-Git $repo @('config', 'core.autocrlf', 'false')
    Invoke-Git $repo @('add', '-A'); Invoke-Git $repo @('commit', '-q', '-m', 'sandbox')
    $redeploy = Join-Path $dev 'Redeploy'
    Write-Text (Join-Path $redeploy 'Redeploy.Core/Core.cs') "core 1`n"
    Write-Text (Join-Path $redeploy '.gitignore') "bin/`nobj/`n"
    Write-Text (Join-Path $redeploy 'ToyCore/ToyCore.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>`n</Project>`n"
    Write-Text (Join-Path $redeploy 'ToyCore/Core.cs') "namespace ToyCore { public static class Core { public static string Value => `"TOY_CORE`"; } }`n"
    Invoke-Git $redeploy @('init', '-q', '-b', 'main'); Invoke-Git $redeploy @('add', '-A'); Invoke-Git $redeploy @('commit', '-q', '-m', 'core')
    Write-Text (Join-Path $dev '_decompiled/EFT/Player.cs') "player 1`n"
    Write-Text (Join-Path $dev '_reference/Mod/Mod.cs') "mod 1`n"
    Write-Text (Join-Path $spt 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll') 'game 1'
    Write-Text (Join-Path $spt 'BepInEx/core/BepInEx.dll') 'bepinex 1'
    Write-Text (Join-Path $spt 'BepInEx/plugins/Other.dll') 'plugin 1'
    Write-Text (Join-Path $spt 'BepInEx/config/other.cfg') 'config 1'
    Write-Text (Join-Path $spt 'SPT_Runtime/SPT_Data/database/items.json') '{"a":1}'
    Write-Text (Join-Path $spt 'SPT_Runtime/user/mods/Other/mod.dll') 'server mod 1'
    Write-Text (Join-Path $spt 'SPT_Runtime/user/profiles/p.json') '{"p":1}'
    return [pscustomobject]@{ Base = $base; Dev = $dev; Repo = $repo; Spt = $spt; Cache = (Join-Path $base 'cache'); Counter = (Join-Path $base 'counter.txt') }
}

function Set-Fault($Sandbox, $Definition) {
    $path = Join-Path $Sandbox.Repo $Definition.File
    # Anchors compare with LF line endings on both sides (a core.autocrlf checkout has CRLF files).
    $text = [IO.File]::ReadAllText((Join-Path $realRepo $Definition.File)).Replace("`r`n", "`n")
    foreach ($edit in @(@($Definition.Find, $Definition.Replace), @($Definition.Find2, $Definition.Replace2))) {
        if (-not $edit[0]) { continue }
        $find = ([string]$edit[0]).Replace("`r`n", "`n")
        $count = ($text.Split([string[]]@($find), [StringSplitOptions]::None)).Count - 1
        if ($count -ne 1) { throw "Fault anchor for $($Definition.Name) occurs $count times in $($Definition.File)" }
        $text = $text.Replace($find, ([string]$edit[1]).Replace("`r`n", "`n"))
    }
    [IO.File]::WriteAllText($path, $text)
    Invoke-Git $Sandbox.Repo @('add', '-A'); Invoke-Git $Sandbox.Repo @('commit', '-q', '-m', 'fault')
}

# One runner invocation; returns the verdict of $Item, how often each toy really ran, the output and exit code.
function Invoke-Runner($Sandbox, [string[]]$Items, [switch]$NoCache, [hashtable]$Environment = @{}, [string[]]$Extra = @(), [string]$Root = $Sandbox.Repo) {
    $before = @(if (Test-Path -LiteralPath $Sandbox.Counter) { Get-Content -LiteralPath $Sandbox.Counter })
    # These checks may themselves run as a step inside a worker of a real run: the switches that run sets
    # for its steps must not reach the runner under test unless a case sets them.
    $Environment = @{} + $Environment
    foreach ($inherited in 'ARENA_VERIFICATION_WORKER', 'ARENA_MUTANT_PARALLEL', 'CustomBeforeDirectoryBuildProps', 'ARENA_VERIFICATION_REDEPLOY_CHECKOUT', 'ARENA_VERIFICATION_REDEPLOY_BUILD') {
        if (-not $Environment.ContainsKey($inherited)) { $Environment[$inherited] = $null }
    }
    $saved = @{}
    $env:ARENA_VERIFICATION_CACHE = $Sandbox.Cache
    $env:ARENA_VERIFICATION_INSTALL_ROOTS = $Sandbox.Spt
    $env:RUNNERCHECKS_COUNTER = $Sandbox.Counter
    foreach ($k in $Environment.Keys) {
        $saved[$k] = [Environment]::GetEnvironmentVariable($k)
        if ($null -eq $Environment[$k]) { Remove-Item -LiteralPath "Env:$k" -ErrorAction SilentlyContinue } else { [Environment]::SetEnvironmentVariable($k, $Environment[$k]) }
    }
    try {
        $arguments = @('-NoProfile', '-NonInteractive', '-File', (Join-Path $Root 'tools/Run-ArenaVerification.ps1'), '-SkipUnitTests', '-SptRoot', $Sandbox.Spt)
        if ($Items.Count -and $Items[0]) { $arguments += @('-Harness', ($Items -join ',')) }
        if ($NoCache) { $arguments += '-NoCache' }
        $arguments += $Extra
        $output = (& pwsh @arguments 2>&1 | ForEach-Object { $_.ToString() })
        $code = $LASTEXITCODE
    }
    finally {
        # PowerShell hands $null to a .NET string parameter as "", which leaves an empty variable behind.
        foreach ($k in $Environment.Keys) { if ($null -eq $saved[$k]) { Remove-Item -LiteralPath "Env:$k" -ErrorAction SilentlyContinue } else { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
    }
    $after = @(if (Test-Path -LiteralPath $Sandbox.Counter) { Get-Content -LiteralPath $Sandbox.Counter })
    $ran = @{}
    foreach ($line in $after[$before.Count..($after.Count)]) { if ($line) { $ran[$line] = 1 + [int]$ran[$line] } }
    if ($env:ARENA_RUNNER_CHECKS_DEBUG) { Write-Host "--- runner $($Items -join ','):`n$($output -join "`n")" }
    $verdicts = @{}
    foreach ($line in $output) {
        if ($line -match '^(PASS|FAIL) (\S+)\s+\d+s\s+(.*)$') { $verdicts[$Matches[2]] = [pscustomobject]@{ Passed = $Matches[1] -eq 'PASS'; Detail = $Matches[3]; Cached = $Matches[3].StartsWith('cached') } }
    }
    return [pscustomobject]@{ Output = ($output -join "`n"); Exit = $code; Verdicts = $verdicts; Ran = $ran }
}

# A folder's state in one line: file count, total size, newest write time (absent: '<absent>').
function Get-TreeState([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '<absent>' }
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force)
    return '{0} files, {1} bytes, newest {2:o}' -f $files.Count, ($files | Measure-Object Length -Sum).Sum, ($files | Measure-Object LastWriteTimeUtc -Maximum).Maximum
}

# dotnet with the worker build variables removed: these checks may run as a step inside a real worker, whose
# variables must not reach a build of the sandbox.
function Invoke-CleanDotnet([string[]]$Arguments) {
    $psi = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    foreach ($name in 'CustomBeforeDirectoryBuildProps', 'ARENA_VERIFICATION_REDEPLOY_CHECKOUT', 'ARENA_VERIFICATION_REDEPLOY_BUILD') { [void]$psi.Environment.Remove($name) }
    $process = [Diagnostics.Process]::Start($psi)
    $errors = $process.StandardError.ReadToEndAsync(); $output = $process.StandardOutput.ReadToEnd(); $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "dotnet $($Arguments -join ' ') failed: $output $($errors.Result)" }
}

function Edit-File($Sandbox, [string]$Rel, [string]$Append) { Add-Content -LiteralPath (Join-Path $Sandbox.Repo $Rel) -Value $Append }
function Edit-External([string]$Path, [string]$Append) { Add-Content -LiteralPath $Path -Value $Append }

# Expect one run of $Item: whether it really ran, and that it passed.
function Assert-Run($Result, [string]$Item, [bool]$Ran, [string]$Name, [bool]$Passed = $true, [switch]$CounterFree) {
    $verdict = $Result.Verdicts[$Item]
    # A program-only toy cannot count itself; whether it ran is then read from the verdict alone.
    $runs = if ($CounterFree) { if ($verdict -and -not $verdict.Cached) { 1 } else { 0 } } else { [int]$Result.Ran[$Item] }
    $ok = $null -ne $verdict -and $verdict.Passed -eq $Passed -and ($(if ($Ran) { $runs -eq 1 -and -not $verdict.Cached } else { $runs -eq 0 -and $verdict.Cached }))
    Check $ok $Name "ran=$runs verdict=$($verdict | ConvertTo-Json -Compress)`n$($Result.Output)"
}

$cases = [ordered]@{
    'first' = { param($s) Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'the first run executes the step' }
    'cached' = { param($s)
        $r = Invoke-Runner $s 'ToyChecks'
        Assert-Run $r 'ToyChecks' $false 'unchanged inputs are cached'
        Check ($r.Verdicts['ToyChecks'].Detail -match 'inputs unchanged since') 'the cached verdict says since when' $r.Output
        $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/ToyChecks.log')
        Check ($log -match 'cached' -and $log -match 'TOY_CHECKS_PASSED') 'a cached step still writes its log with the original output' $log
        Check ($r.Output -match 'ARENA_VERIFICATION_SUMMARY passed=1 failed=0 seconds=\d+ cached=1' -and $r.Exit -eq 0) 'the summary counts cached steps and the exit code stays 0' $r.Output }
    'unnamed-record' = { param($s) Edit-File $s 'docs/reports/2026-11/RECORD.md' 'more'; Edit-File $s 'docs/README.md' 'new index line'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'an unnamed docs record and the docs index keep the cache' }
    'other-folder' = { param($s) Edit-File $s 'tools/OtherChecks/Verify.ps1' '# edited'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'an unrelated harness folder keeps the cache' }
    'loose-entry' = { param($s)
        Assert-Run (Invoke-Runner $s 'tools/Verify-LooseToy.ps1') 'tools/Verify-LooseToy.ps1' $true 'a loose entry script runs as its own step'
        Assert-Run (Invoke-Runner $s 'tools/Verify-LooseToy.ps1') 'tools/Verify-LooseToy.ps1' $false 'a loose entry script is cached like a folder'
        Edit-File $s 'tools/Verify-LooseToy.ps1' '# edited'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'a loose entry script is not in other steps'' keys'
        Assert-Run (Invoke-Runner $s 'tools/Verify-LooseToy.ps1') 'tools/Verify-LooseToy.ps1' $true 'an edited loose entry script reruns itself' }
    'production' = { param($s) Edit-File $s 'ArenaMode.Client/Toy.cs' '// edited'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a production file reruns the step'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'the new state is cached after its PASS' }
    'untracked' = { param($s) Write-Text (Join-Path $s.Repo 'ArenaMode.Client/New.cs') "class New { }`n"
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'an untracked production file reruns the step' }
    'tracked' = { param($s) Write-Text (Join-Path $s.Repo 'ArenaMode.Client/Tracked.cs') "class Tracked { }`n"
        $null = Invoke-Runner $s 'ToyChecks'
        Invoke-Git $s.Repo @('add', 'ArenaMode.Client/Tracked.cs')
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'tracking a file reruns the step' }
    'referenced-folder' = { param($s) Edit-File $s 'tools/ReferencedChecks/Data.txt' 'data 2'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a referenced harness folder reruns the step' }
    'named-record' = { param($s) Edit-File $s 'docs/reviews/TOY-REVIEW.md' 'edited'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a named docs record reruns the step' }
    'licence' = { param($s) Edit-File $s 'docs/licenses/LICENSE.txt' 'licence 2'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a licence file reruns the step' }
    'shared-helper' = { param($s) Edit-File $s 'tools/Invoke-ArenaSourceChecks.ps1' '# edited'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a shared helper reruns the step' }
    'environment' = { param($s)
        Assert-Run (Invoke-Runner $s 'ToyChecks' -Environment @{ ARENA_TOY_SWITCH = '1' }) 'ToyChecks' $true 'an environment variable reruns the step'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'the earlier environment finds its earlier PASS' }
    'game-assemblies' = { param($s) Edit-External (Join-Path $s.Spt 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll') 'game 2'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'installed game assemblies rerun the step' }
    'database' = { param($s) Edit-External (Join-Path $s.Spt 'SPT_Runtime/SPT_Data/database/items.json') ' '
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'the SPT database reruns the step' }
    'runtime-state' = { param($s)
        Write-Text (Join-Path $s.Spt 'SPT_Runtime/user/mods/Other/logs/server.log') ("written while the server runs " + [Guid]::NewGuid())
        Write-Text (Join-Path $s.Spt 'BepInEx/plugins/SAIN/EditorDefaults.json') ('{"x":"' + [Guid]::NewGuid() + '"}')
        Write-Text (Join-Path $s.Spt 'SPT_Runtime/user/profiles/p.json') ('{"played":"' + [Guid]::NewGuid() + '"}')   # under SPT_RUNTIME, which the runner sets
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'runtime state that mods write keeps the cache'
        $settings = Join-Path $s.Spt 'SPT_Runtime/user/mods/Other/mod.dll'
        [IO.File]::WriteAllText($settings, [IO.File]::ReadAllText($settings)); (Get-Item -LiteralPath $settings).LastWriteTimeUtc = [DateTime]::UtcNow.AddMinutes(1)
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'a small installed file rewritten with the same bytes keeps the cache'
        Write-Text (Join-Path $s.Spt 'BepInEx/plugins/New.dll') 'a new plugin'
        $r = Invoke-Runner $s 'ToyChecks'
        Assert-Run $r 'ToyChecks' $true 'a new plugin assembly reruns the step'
        Check ($r.Verdicts['ToyChecks'].Detail -match 'BepInEx\\plugins') 'the verdict names what moved outside the checkout' $r.Output }
    'install-state' = { param($s)
        $null = Invoke-Runner $s 'ConfigToyChecks'
        Edit-External (Join-Path $s.Spt 'BepInEx/config/other.cfg') 'config 2'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'installed config does not rerun a step that does not name it'
        Assert-Run (Invoke-Runner $s 'ConfigToyChecks') 'ConfigToyChecks' $true 'installed config reruns a step that names it' }
    'redeploy' = { param($s) Edit-External (Join-Path $s.Dev 'Redeploy/Redeploy.Core/Core.cs') 'core 2'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'the sibling Redeploy checkout reruns the step' }
    'decompiled' = { param($s) Edit-External (Join-Path $s.Dev '_decompiled/EFT/Player.cs') 'player 2'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'decompiled sources rerun the step' }
    'git-head' = { param($s)
        $null = Invoke-Runner $s 'GitToyChecks,ToyChecks'
        Edit-File $s 'docs/reports/2026-11/RECORD.md' 'committed record'
        Invoke-Git $s.Repo @('commit', '-q', '-am', 'docs only')
        $r = Invoke-Runner $s 'GitToyChecks,ToyChecks'
        Assert-Run $r 'ToyChecks' $false 'a docs-only commit keeps a step that does not call git'
        Assert-Run $r 'GitToyChecks' $true 'a commit reruns a step that calls git' }
    'failure' = { param($s)
        Write-Text (Join-Path $s.Repo 'ArenaMode.Client/fail.flag') 'fail'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a failing step runs' $false
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $true 'a failure is never cached' $false
        Remove-Item -LiteralPath (Join-Path $s.Repo 'ArenaMode.Client/fail.flag')
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'a reverted change finds the earlier PASS' }
    'no-cache' = { param($s) Assert-Run (Invoke-Runner $s 'ToyChecks' -NoCache) 'ToyChecks' $true '-NoCache runs the step' }
    'stage' = { param($s) Assert-Run (Invoke-Runner $s 'ToyChecks:Fast') 'ToyChecks:Fast' $true 'a stage is a step of its own' }
    'select' = { param($s)
        $selected = { param($r, $step) [regex]::Match($r.Output, '(?m)^SELECT ' + [regex]::Escape($step) + '\s+(.*)$') }
        Edit-File $s 'ArenaMode.Client/Toy.cs' '// selection'
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD', '-ListOnly')
        Check ((& $selected $r 'LinkToyChecks').Groups[1].Value -match 'ArenaMode.Client/Toy.cs \(compiled by tools/LinkToyChecks/LinkToyChecks.csproj\)') 'a linked production file selects its harness' $r.Output
        Check ((& $selected $r 'ToyChecks').Groups[1].Value -match 'ArenaMode.Client/Toy.cs \(named in tools/ToyChecks/Verify.ps1\)') 'a mutant target named in a script selects its harness' $r.Output
        Check (-not (& $selected $r 'OtherChecks').Success -and -not (& $selected $r 'LiteralToyChecks').Success) 'a harness that neither compiles nor names the file is not selected' $r.Output
        Check ($r.Exit -eq 0 -and $r.Ran.Count -eq 0 -and $r.Output -match 'ARENA_VERIFICATION_SELECTION selected=2 of 8 registered') '-ListOnly runs nothing and counts the selection' $r.Output
        Invoke-Git $s.Repo @('commit', '-q', '-am', 'toy edit')
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD~1', '-ListOnly')
        Check ((& $selected $r 'LinkToyChecks').Success) 'a committed change selects like an uncommitted one' $r.Output
        Write-Text (Join-Path $s.Repo 'ArenaMode.Client/Topping.cs') "class Topping { }`n"
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD', '-ListOnly')
        Check ((& $selected $r 'LinkToyChecks').Groups[1].Value -match 'Topping.cs') 'an untracked file selects too' $r.Output
        Remove-Item -LiteralPath (Join-Path $s.Repo 'ArenaMode.Client/Topping.cs')
        Edit-File $s 'ArenaMode.Client/Other.cs' '// selection'
        Write-Text (Join-Path $s.Repo 'tools/NewChecks/Verify.ps1') "'NEW'`n"
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD', '-ListOnly')
        Check ((& $selected $r 'LiteralToyChecks').Groups[1].Value -match 'ArenaMode.Client/Other.cs \(named in tools/LiteralToyChecks/Verify.ps1\)') 'a mutant target named in a script selects its harness' $r.Output
        Check (-not (& $selected $r 'LinkToyChecks').Success) 'a file outside the glob does not select the project' $r.Output
        Check ($r.Output -match 'UNREGISTERED tools/NewChecks') 'an unregistered harness folder is reported' $r.Output
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD')
        Check ([int]$r.Ran['LiteralToyChecks'] -eq 1 -and $r.Verdicts['LiteralToyChecks'].Passed) 'a selection without -ListOnly runs the selected steps' $r.Output
        Remove-Item -LiteralPath (Join-Path $s.Repo 'tools/NewChecks') -Recurse -Force
        Invoke-Git $s.Repo @('add', '-A'); Invoke-Git $s.Repo @('commit', '-q', '-m', 'selection state')
        Invoke-Git $s.Repo @('mv', 'ArenaMode.Client/Other.cs', 'ArenaMode.Client/Renamed.cs'); Invoke-Git $s.Repo @('commit', '-q', '-m', 'rename')
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD~1', '-ListOnly')
        Check ((& $selected $r 'LiteralToyChecks').Groups[1].Value -match 'ArenaMode.Client/Other.cs') 'a renamed file selects by its old path too' $r.Output
        Invoke-Git $s.Repo @('mv', 'ArenaMode.Client/Renamed.cs', 'ArenaMode.Client/Other.cs'); Invoke-Git $s.Repo @('commit', '-q', '-m', 'rename back')
        Edit-File $s 'ArenaMode.Client/Answer.cs' '// selection'
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD', '-ListOnly')
        Check ((& $selected $r 'SegmentToyChecks').Groups[1].Value -match 'ArenaMode.Client/Answer.cs') 'a path built from quoted segments selects its harness' $r.Output
        Write-Text (Join-Path $s.Repo 'tools/Verify-NewLoose.ps1') "'NEW LOOSE'`n"
        $r = Invoke-Runner $s @() -Extra @('-ChangedSince', 'HEAD', '-ListOnly')
        Check ($r.Output -match 'UNREGISTERED tools/Verify-NewLoose.ps1') 'an unregistered loose script is reported' $r.Output }
    'runner-inputs' = { param($s)
        Assert-Run (Invoke-Runner $s 'LinkToyChecks') 'LinkToyChecks' $true 'a program-only step runs' -CounterFree
        Assert-Run (Invoke-Runner $s 'LinkToyChecks') 'LinkToyChecks' $false 'a program-only step is cached' -CounterFree
        Edit-File $s 'tools/AirPitChecks/NoDeploy.targets' '<!-- edited -->'
        Assert-Run (Invoke-Runner $s 'LinkToyChecks') 'LinkToyChecks' $true 'the no-deploy targets the runner passes rerun a program-only step' -CounterFree }
    'wide-helper' = { param($s)
        Assert-Run (Invoke-Runner $s 'ScanToyChecks') 'ScanToyChecks' $true 'a step that reaches an enumerating helper runs'
        Edit-File $s 'tools/OtherChecks/Verify.ps1' '# edited'
        Assert-Run (Invoke-Runner $s 'ScanToyChecks') 'ScanToyChecks' $true 'a step that reaches an enumerating helper reruns on any change' }
    'artifact-input' = { param($s)
        Assert-Run (Invoke-Runner $s 'ArtifactToyChecks') 'ArtifactToyChecks' $true 'a step with an artifact fixture runs'
        Assert-Run (Invoke-Runner $s 'ArtifactToyChecks') 'ArtifactToyChecks' $false 'a step with an unchanged artifact fixture is cached'
        Add-Content -LiteralPath (Join-Path $s.Repo 'artifacts/pft-installed/Fixture.cs') 'fixture 2'
        Assert-Run (Invoke-Runner $s 'ArtifactToyChecks') 'ArtifactToyChecks' $true 'a changed fixture under artifacts/ reruns the step' }
    'env-path' = { param($s)
        $folder = Join-Path $s.Base 'apbs-root'; Write-Text (Join-Path $folder 'config.json') '{"v":1}'
        Assert-Run (Invoke-Runner $s 'ToyChecks' -Environment @{ ARENA_TOY_ROOT = $folder }) 'ToyChecks' $true 'a step with a path variable runs'
        Assert-Run (Invoke-Runner $s 'ToyChecks' -Environment @{ ARENA_TOY_ROOT = $folder }) 'ToyChecks' $false 'an unchanged folder behind a variable keeps the cache'
        Write-Text (Join-Path $folder 'config.json') '{"v":2}'
        Assert-Run (Invoke-Runner $s 'ToyChecks' -Environment @{ ARENA_TOY_ROOT = $folder }) 'ToyChecks' $true 'a changed folder behind a variable reruns the step' }
    'helper' = { param($s)
        $r = Invoke-Runner $s 'MutantToyChecks' -NoCache
        $toyLog = Join-Path $s.Repo 'artifacts/run-arena-verification/MutantToyChecks.log'
        $log = if (Test-Path -LiteralPath $toyLog) { Get-Content -Raw -LiteralPath $toyLog } else { '' }
        Check ($r.Verdicts['MutantToyChecks'].Passed -and $r.Verdicts['MutantToyChecks'].Detail -match 'Source faults rejected: 2') 'killed mutants pass their harness' "$($r.Output)`n$log"
        foreach ($bad in 'survivor', 'broken', 'wrong-assertion') {
            $r = Invoke-Runner $s 'MutantToyChecks' -NoCache -Environment @{ ARENA_RUNNER_CHECKS_BADFAULT = $bad; ARENA_MUTANT_PARALLEL = '3' }
            $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/MutantToyChecks.log')
            Check (-not $r.Verdicts['MutantToyChecks'].Passed -and $log -match "Fault was not killed by intended assertion: $bad") "a $bad mutant fails its harness" $log
        } }
    'helper-channels' = { param($s)
        $r = Invoke-Runner $s 'MutantChannelToyChecks' -NoCache
        $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/MutantChannelToyChecks.log')
        Check ($r.Verdicts['MutantChannelToyChecks'].Passed -and $log -match 'Source faults rejected: 4' -and $log -match 'Source faults rejected: 1') 'channel faults pass their harness' "$($r.Output)`n$log"
        foreach ($bad in @(@('double-site', 'Fault anchor matches 2 sites \(expected exactly one\): double-site', 'a double-site fault fails its harness'),
                @('unmarked', 'Fault was not killed by intended assertion: unmarked \(the output lacks CHANNEL_TOY_FAILED\)', 'an unmarked fault fails its harness'),
                @('broken-raw', 'Fault was not killed by intended assertion: broken-raw \(it did not compile\)', 'a broken-raw fault fails its harness'),
                @('prepare-leak', "Prepare of fault prepare-leak returned 'stray'", 'a leaking Prepare fails its harness'))) {
            $r = Invoke-Runner $s 'MutantChannelToyChecks' -NoCache -Environment @{ ARENA_RUNNER_CHECKS_BADFAULT = $bad[0]; ARENA_MUTANT_PARALLEL = '3' }
            $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/MutantChannelToyChecks.log')
            Check (-not $r.Verdicts['MutantChannelToyChecks'].Passed -and $log -match $bad[1]) $bad[2] $log
        } }
    'parallel' = { param($s)
        Edit-File $s 'ArenaMode.Client/Toy.cs' '// parallel'
        (Get-Item -LiteralPath (Join-Path $s.Repo 'ArenaMode.Client/Toy.cs')).LastWriteTimeUtc = [datetime]'2020-01-01'   # older than any build output
        Write-Text (Join-Path $s.Repo 'ArenaMode.Client/New.cs') "class New { }`n"
        Remove-Item -LiteralPath (Join-Path $s.Repo 'ArenaMode.Client/Other.cs')
        Write-Text (Join-Path $s.Repo 'ArenaMode.Client/Staged.cs') "class Staged { }`n"
        Invoke-Git $s.Repo @('add', 'ArenaMode.Client/Staged.cs')
        $statusBefore = (& git -C $s.Repo status --porcelain) -join "`n"
        $r = Invoke-Runner $s 'EchoToyChecks,SleepToyChecks,ToyChecks' -Extra @('-Parallel', '2')
        Check ($r.Verdicts['EchoToyChecks'].Detail -match 'worker vw[12]' -and $r.Output -match 'ARENA_VERIFICATION_WORKERS 2') 'steps run in two worker checkouts' $r.Output
        $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/EchoToyChecks.log')
        $root = [regex]::Match($log, 'ROOT=(.*)').Groups[1].Value.Trim()
        Check ($root -like (Join-Path $s.Dev 'ArenaMode-vw*')) 'a worker is a sibling of the checkout under test' $log
        $toyHash = (Get-FileHash -LiteralPath (Join-Path $s.Repo 'ArenaMode.Client/Toy.cs') -Algorithm SHA256).Hash
        Check ($log -match "TOY=$toyHash" -and $log -match 'NEW=True' -and $log -match 'OTHER=False' -and $log -match 'STAGED=0') 'a worker mirrors uncommitted, untracked, deleted and staged changes' $log
        Check ((Get-Item -LiteralPath (Join-Path $root 'ArenaMode.Client/Toy.cs')).LastWriteTimeUtc -gt [datetime]'2026-01-01') 'a synced file gets a fresh write time' $log
        Check ($r.Exit -eq 0 -and $r.Verdicts['SleepToyChecks'].Passed -and $r.Verdicts['ToyChecks'].Passed -and [int]$r.Ran['SleepToyChecks'] -eq 1) 'every step passes in the workers' $r.Output
        Check (((& git -C $s.Repo status --porcelain) -join "`n") -eq $statusBefore) 'the checkout under test is left as it was' $statusBefore
        $again = Invoke-Runner $s 'EchoToyChecks,SleepToyChecks,ToyChecks' -Extra @('-Parallel', '2')
        Check ($again.Verdicts['EchoToyChecks'].Cached -and $again.Verdicts['SleepToyChecks'].Cached -and $again.Verdicts['ToyChecks'].Cached) 'steps that passed in workers are cached' $again.Output
        $nested = Invoke-Runner $s 'EchoToyChecks,SleepToyChecks' -NoCache -Environment @{ ARENA_VERIFICATION_WORKER = '1' } -Extra @('-Parallel', '2')
        Check ($nested.Output -notmatch 'ARENA_VERIFICATION_WORKERS' -and $nested.Verdicts['EchoToyChecks'].Detail -notmatch 'worker vw') 'a runner inside a worker runs its steps sequentially' $nested.Output }
    'parallel-restore' = { param($s)
        $r = Invoke-Runner $s 'NoRestoreToyChecks,SleepToyChecks' -NoCache -Extra @('-Parallel', '2')
        Check ($r.Verdicts['NoRestoreToyChecks'].Passed -and $r.Verdicts['NoRestoreToyChecks'].Detail -match 'worker vw') 'a --no-restore step passes in a fresh worker' $r.Output }
    'parallel-redeploy' = { param($s)
        # The shared toy Redeploy project was built in place before, as a build outside the runner leaves it: its
        # obj/ holds generated sources that a redirected build must not compile again.
        $toyCore = Join-Path $s.Dev 'Redeploy/ToyCore'
        Invoke-CleanDotnet @('build', (Join-Path $toyCore 'ToyCore.csproj'), '-c', 'Release', '-nologo', '-v:q')
        $shared = { "obj: $(Get-TreeState (Join-Path $toyCore 'obj')); bin: $(Get-TreeState (Join-Path $toyCore 'bin'))" }
        $sharedBefore = & $shared
        $marks = Join-Path $s.Base 'redeploy-marks'; [IO.Directory]::CreateDirectory($marks) | Out-Null
        $r = Invoke-Runner $s 'RedeployToyChecks:A,RedeployToyChecks:B' -NoCache -Extra @('-Parallel', '2') -Environment @{ ARENA_RUNNER_CHECKS_REDEPLOY_MARKS = $marks }
        $sharedAfter = & $shared
        $logs = (@('RedeployToyChecks_A', 'RedeployToyChecks_B') | ForEach-Object { $p = Join-Path $s.Repo "artifacts/run-arena-verification/$_.log"; if (Test-Path -LiteralPath $p) { Get-Content -Raw -LiteralPath $p } }) -join "`n"
        $built = @(1, 2 | ForEach-Object { Join-Path $s.Dev "ArenaMode-vw$_/artifacts/redeploy-build/ToyCore/bin/Release/net10.0/ToyCore.dll" })
        $context = "shared before: $sharedBefore`nshared after: $sharedAfter$(if ($sharedAfter -ne $sharedBefore) { "`nSHARED_REDEPLOY_WRITTEN" })`n$($r.Output)`n$logs"
        Check (@($built | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 2) 'each worker builds the Redeploy project into its own folder' $context
        Check ($sharedAfter -eq $sharedBefore) 'the shared Redeploy obj and bin stay untouched' $context
        Check ($r.Verdicts['RedeployToyChecks:A'].Passed -and $r.Verdicts['RedeployToyChecks:B'].Passed -and ([regex]::Matches($logs, 'OVERLAP=True')).Count -eq 2) 'two workers build the Redeploy project side by side' $context
        # The sync keeps the worker's Redeploy build, so the next run builds it incrementally.
        $stamps = @($built | ForEach-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc })
        Get-ChildItem -LiteralPath $marks -File | Remove-Item -Force
        $again = Invoke-Runner $s 'RedeployToyChecks:A,RedeployToyChecks:B' -NoCache -Extra @('-Parallel', '2') -Environment @{ ARENA_RUNNER_CHECKS_REDEPLOY_MARKS = $marks }
        $restamps = @($built | ForEach-Object { if (Test-Path -LiteralPath $_) { (Get-Item -LiteralPath $_).LastWriteTimeUtc } })
        $kept = $restamps.Count -eq 2 -and $restamps[0] -eq $stamps[0] -and $restamps[1] -eq $stamps[1]
        Check ($again.Exit -eq 0 -and $kept) 'a worker keeps its Redeploy build between runs' "$stamps -> $restamps$(if ($again.Exit -eq 0 -and -not $kept) { "`nREDEPLOY_REBUILT" })`n$($again.Output)" }
    'parallel-touch' = { param($s)
        # The queue starts the longest last PASS first: ToyChecks (it changes Toy.cs in its worker) and
        # SleepToyChecks, which then waits until EchoToyChecks has run, so EchoToyChecks must take ToyChecks' worker.
        foreach ($last in @(@('ToyChecks', 300), @('SleepToyChecks', 200), @('EchoToyChecks', 1))) {
            Write-Text (Join-Path $s.Cache "last/$($last[0]).json") "{`"Seconds`": $($last[1])}"
        }
        $mark = Join-Path $s.Base 'echo-ran'
        $r = Invoke-Runner $s 'EchoToyChecks,SleepToyChecks,ToyChecks' -NoCache -Extra @('-Parallel', '2') -Environment @{ ARENA_RUNNER_CHECKS_TOUCH = '1'; ARENA_RUNNER_CHECKS_RESTORE = $mark; ARENA_RUNNER_CHECKS_ECHO_MARK = $mark }
        $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/EchoToyChecks.log')
        $toyHash = (Get-FileHash -LiteralPath (Join-Path $s.Repo 'ArenaMode.Client/Toy.cs') -Algorithm SHA256).Hash
        $toy = $r.Verdicts['ToyChecks']
        $cachedTouch = if ($toy -and $toy.Passed -and $toy.Detail -match 'worker vw' -and $toy.Detail -notmatch 'not cached') { "`nTOUCHED_PASS_CACHED" } else { '' }
        Check ($toy.Passed -and $toy.Detail -match 'worker vw' -and $toy.Detail -match 'not cached: ArenaMode.Client/Toy.cs changed') 'a step whose inputs change during a worker run is not cached' "$($r.Output)$cachedTouch"
        $sawChange = if ($r.Verdicts['EchoToyChecks'].Passed -and $log -match 'TOY=' -and $log -notmatch "TOY=$toyHash") { "`nECHO_SAW_A_CHANGED_WORKER" } else { '' }
        Check ($r.Output -match 'ARENA_VERIFICATION_WORKER vw\d drifted \(ArenaMode.Client/Toy.cs changed\); syncing again' -and $r.Verdicts['EchoToyChecks'].Passed -and $log -match "TOY=$toyHash") 'a worker that a step changed is synced again before its next step' "$($r.Output)`n$log$sawChange"
        Check ($r.Exit -eq 0 -and $r.Verdicts['SleepToyChecks'].Passed) 'every step passes beside a touching step' $r.Output }
    'parallel-artifacts' = { param($s)
        $r = Invoke-Runner $s 'ArtifactToyChecks,SleepToyChecks' -NoCache -Extra @('-Parallel', '2')
        Check ($r.Verdicts['ArtifactToyChecks'].Passed -and $r.Verdicts['ArtifactToyChecks'].Detail -match 'worker vw') 'a registered artifact fixture reaches the workers' $r.Output }
    'register' = { param($s)
        Edit-File $s 'tools/verification-steps.txt' 'OtherChecks'
        Assert-Run (Invoke-Runner $s 'ToyChecks') 'ToyChecks' $false 'editing the register keeps every PASS' }
    'worker-guard' = { param($s)
        $null = Invoke-Runner $s 'EchoToyChecks,SleepToyChecks' -NoCache -Extra @('-Parallel', '2')
        $worker = Join-Path $s.Dev 'ArenaMode-vw1'
        Write-Text (Join-Path $worker 'ArenaMode.Client/Precious.cs') "uncommitted work in a worker`n"
        $r = Invoke-Runner $s 'EchoToyChecks,OtherChecks' -NoCache -Extra @('-Parallel', '2') -Root $worker
        Check ((Test-Path -LiteralPath (Join-Path $worker 'ArenaMode.Client/Precious.cs')) -and $r.Verdicts['EchoToyChecks'].Passed -and $r.Verdicts['OtherChecks'].Passed -and
            $r.Output -notmatch 'ARENA_VERIFICATION_WORKERS \d') 'a runner started in a worker runs sequentially and keeps its uncommitted files' $r.Output }
    'parallel-guard' = { param($s)
        $deployed = Join-Path $s.Spt 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll'
        $r = Invoke-Runner $s 'DeployToyChecks,SleepToyChecks' -NoCache -Extra @('-Parallel', '2') -Environment @{ ARENA_RUNNER_CHECKS_RESTORE = $deployed }
        Remove-Item -LiteralPath $deployed -ErrorAction SilentlyContinue
        Check (-not $r.Verdicts['DeployToyChecks'].Passed -and $r.Verdicts['DeployToyChecks'].Detail -match 'changed an Arena installation file') 'a deploying step fails in a worker' $r.Output
        Check (-not $r.Verdicts['SleepToyChecks'].Passed -and $r.Verdicts['SleepToyChecks'].Detail -match 'while .*DeployToyChecks.*SleepToyChecks|while .*SleepToyChecks.*DeployToyChecks') 'a step that ran alongside a deploy fails and both are named' $r.Output }
    'changed-during-run' = { param($s)
        $r = Invoke-Runner $s 'ToyChecks' -Environment @{ ARENA_RUNNER_CHECKS_TOUCH = '1' }
        Assert-Run $r 'ToyChecks' $true 'a touching step runs'
        Check ($r.Verdicts['ToyChecks'].Detail -match 'not cached: ArenaMode.Client/Toy.cs changed') 'a step whose inputs change during the run is not cached' $r.Output
        $again = Invoke-Runner $s 'ToyChecks' -Environment @{ ARENA_RUNNER_CHECKS_TOUCH = '1' }
        Check ([int]$again.Ran['ToyChecks'] -eq 1) 'a step whose inputs change during the run is not cached' $again.Output }
}

function Invoke-Cases([string[]]$Names, $Definition) {
    $sandbox = New-Sandbox
    try {
        if ($Definition) { Set-Fault $sandbox $Definition }
        foreach ($name in $Names) { & $cases[$name] $sandbox }
    }
    finally {
        if ($env:ARENA_RUNNER_CHECKS_KEEP) { Write-Host "sandbox kept: $($sandbox.Base)" }
        else { Remove-Item -LiteralPath $sandbox.Base -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

if ($Fault) {
    $definition = $faults | Where-Object Name -eq $Fault
    if (-not $definition) { throw "No such fault: $Fault" }
    Invoke-Cases (@('first', 'cached') + $definition.Cases) $definition
    Write-Output "FAULT_NOT_KILLED $Fault"
    exit 0
}
if ($Case.Count) {
    Invoke-Cases (@('first', 'cached') + $Case) $null
    Write-Output "RUNNER_CASES_PASSED $($Case -join ',')"
    exit 0
}
if ($Baseline) {
    Invoke-Cases @($cases.Keys) $null
    Write-Output "RUNNER_BASELINE_PASSED cases=$($cases.Count)"
    exit 0
}

# The full run. The baseline and every fault are child processes with sandboxes of their own (own checkout,
# cache, counter file and worker checkouts), so they share nothing and run side by side: the baseline from the
# start, beside it ARENA_MUTANT_PARALLEL faults at a time (Run-ArenaVerification.ps1 sets it for the steps of
# its workers). A fault proves something only against a passing baseline, so a red baseline fails the run
# whatever the faults did, and every fault must report exactly once.
$throttle = if ($env:ARENA_MUTANT_PARALLEL -as [int]) { [Math]::Max(1, [int]$env:ARENA_MUTANT_PARALLEL) } else { 4 }
$jobs = @(@{ Name = ''; Arguments = @('-Baseline') }) + @($faults | ForEach-Object { @{ Name = $_.Name; Arguments = @('-Fault', $_.Name) } })
$verifyScript = $PSCommandPath
$results = @($jobs | ForEach-Object -ThrottleLimit ($throttle + 1) -Parallel {
    $job = $_
    $watch = [Diagnostics.Stopwatch]::StartNew()
    # Runspaces are reused: a child that does not start must not inherit the exit code of the one before.
    $global:LASTEXITCODE = -1
    $output = (& pwsh -NoProfile -NonInteractive -File $using:verifyScript @($job.Arguments) 2>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    [pscustomobject]@{ Name = $job.Name; Exit = $LASTEXITCODE; Output = $output; Seconds = [int]$watch.Elapsed.TotalSeconds }
})

$baselineResults = @($results | Where-Object { $_.Name -eq '' })
if ($baselineResults.Count -ne 1) { Write-Output "Baseline reported $($baselineResults.Count) results"; exit 1 }
Write-Output $baselineResults[0].Output
if ($baselineResults[0].Exit -ne 0 -or -not $baselineResults[0].Output.Contains("RUNNER_BASELINE_PASSED cases=$($cases.Count)")) {
    Write-Output "Baseline cases failed (exit $($baselineResults[0].Exit)); without them the faults prove nothing"
    exit 1
}
$survivors = @()
foreach ($definition in $faults) {
    $result = @($results | Where-Object { $_.Name -eq $definition.Name })
    if ($result.Count -ne 1) { $survivors += $definition.Name; Write-Output "Fault reported $($result.Count) results: $($definition.Name)"; continue }
    $missing = if ($result[0].Exit -eq 0 -or -not $result[0].Output.Contains('FAIL: ' + $definition.Failure)) { '' }
        elseif ($definition.Evidence -and -not (Get-FlatText $result[0].Output).Contains((Get-FlatText $definition.Evidence))) { " (its own symptom '$($definition.Evidence)' is missing)" }
        else { $null }
    if ($null -ne $missing) {
        $survivors += $definition.Name
        Write-Output "Fault was not killed by intended assertion: $($definition.Name)$missing`n$($result[0].Output)"
        continue
    }
    Write-Output "fault rejected: $($definition.Name) -> $($definition.Failure)"
}
$slowest = @($results | Sort-Object Seconds -Descending | Select-Object -First 3 | ForEach-Object { "$(if ($_.Name) { $_.Name } else { 'baseline' }) $($_.Seconds) s" })
Write-Output "side by side: baseline + $throttle faults at a time; slowest: $($slowest -join ', ')"
if ($survivors.Count) { Write-Output "Faults not killed: $($survivors.Count) of $($faults.Count) ($($survivors -join ', '))"; exit 1 }
Write-Output "VERIFICATION_RUNNER_CHECKS_PASSED cases=$($cases.Count) faults=$($faults.Count)"
