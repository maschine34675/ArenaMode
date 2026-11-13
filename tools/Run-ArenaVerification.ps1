#Requires -Version 7.0
<#
.SYNOPSIS
One entry point for the offline verification: the xunit suite once, without deploying, then any number
of harness scripts under tools/, with one verdict line each and a summary at the end.

.DESCRIPTION
The suite (ArenaMode.Tests, about 3 900 tests, about 100 s plus the build) runs exactly once with the
no-deploy targets and a sentinel that must not exist, so neither the server DLL nor the client payload
reaches the live install (a bare `dotnet test ArenaMode.Tests` would deploy the server DLL). Its TRX is
judged by tools/Assert-ArenaTestRun.ps1: every result must pass, only the known environment-gated skips
are tolerated. The live install (client, companion, server DLL, gate patcher, session helper) is hashed
before the run and after every step; a step that changed any of them fails, whatever it printed. A
failing step does not stop the rest; the summary names every verdict and the exit code is the number of
failures (1 when nothing ran at all). Logs: artifacts/run-arena-verification/<step>.log (xunit.log).

Parallel steps (-Parallel, default 4): parallel dotnet builds in one checkout collide, so the steps run in
worker checkouts, persistent sibling worktrees ArenaMode-vw<k> that the runner makes byte-identical to the
checkout under test (HEAD plus every uncommitted, untracked and deleted file) before it starts, and checks
file by file (tools/ArenaVerificationWorkers.ps1). The longest steps start first; steps that build
ArenaMode.Client or the solution (which writes the sibling Redeploy checkout) take a machine-wide mutex. A
change of the live install fails every step that ran while it happened and names them. -Parallel 1 runs
the steps one after another in this checkout, as before. Inside a worker, a nested runner runs sequentially.

Result cache: a step whose inputs are unchanged since its last PASS is not run again; its verdict line
reads "PASS <step> 0s cached: inputs unchanged since <time> (ran <n>s) ..." and its log is the log of that
PASS. The inputs are every file of the checkout the step can reach (shared files, its harness folder and
the folders it names, the docs it names), the installed game/BepInEx/SPT state, the sibling checkouts, the
tool versions, the relevant environment variables and, for steps that call git, HEAD; see
tools/ArenaVerificationInputs.ps1 for the exact rules. Only a PASS is kept, and only when the step's inputs
are still the same after it ran. Entries live under %LOCALAPPDATA%\ArenaMode\verification-cache (or
$env:ARENA_VERIFICATION_CACHE), shared by every checkout on the machine. -NoCache runs every step and
refreshes the entries; a release gate runs with -NoCache.

Several older wrappers run the whole suite themselves (`git grep -n 'dotnet test' tools`; the ones
without --filter); naming them here runs the suite twice. Running every Verify.ps1 under tools/ takes
hours: pick the harnesses whose production code the change touched (-ChangedSince), and run the full set
only when a pass ends.

.PARAMETER Harness
Harness folder names (tools/<name>/Verify.ps1; a folder without one but with tools/<name>/<name>.csproj is
run with `dotnet run`, no program arguments and the no-deploy properties), optionally with a stage for the scripts that need one
(<name>:<Stage>, forwarded as -Stage), or script paths relative to the repository root. Several names
go comma-separated without spaces (-Harness A,B:Stage) or as repeated arguments.

.PARAMETER SkipUnitTests
Leave the suite out (for example when a named wrapper runs it anyway).

.PARAMETER SptRoot
The installed game, for the environment variables the data-bound tests read and for the live-install
hashes. When it is named here, it is also forwarded as -SptRoot to every harness script whose param
block declares one, so the harnesses read the same installation the runner hashes; left out, each
script keeps its own default.

.PARAMETER NoCache
Run every step even when an earlier PASS with the same inputs exists (the PASS entries are refreshed).

.PARAMETER ChangedSince
A git revision. Adds every step registered in tools/verification-steps.txt that checks a file changed since that
revision (committed since, staged, unstaged or untracked): a file its projects compile, a production path its
scripts name (a mutant target), a file of its harness folder, a shared helper it runs, a record it names, a file
compiled by a production project it builds or tests. One SELECT line per step names the reasons; a changed
harness folder that is not registered is reported as UNREGISTERED. The selection is advisory (as a hand-made one
is); the cache stays coarse.

.PARAMETER All
Adds every registered step.

.PARAMETER ListOnly
Prints the selection and the named steps, runs nothing, exits 0.

.PARAMETER Parallel
How many steps run at the same time, each in its own worker checkout (default 4; 1 = sequentially in this
checkout). Measured on 12 cores/26 GB: 4 to 6 workers ran 3.3 to 3.4 times faster than one, with about 8 GB
left free; above 6, memory and the longest step bound the gain.

.EXAMPLE
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness PoolPrewarmChecks,ArenaAreaChecks:Server

.EXAMPLE
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -ChangedSince master -ListOnly
#>
param(
    [string[]]$Harness = @(),
    [switch]$SkipUnitTests,
    [string]$SptRoot = 'D:\SPT41',
    [switch]$NoCache,
    [string]$ChangedSince,
    [switch]$All,
    [switch]$ListOnly,
    [ValidateRange(1, 8)][int]$Parallel = 4
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Assert-ArenaTestRun.ps1')
. (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot 'ArenaVerificationInputs.ps1')
. (Join-Path $PSScriptRoot 'ArenaVerificationWorkers.ps1')
$results = Join-Path $repo 'artifacts\run-arena-verification'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$noDeploy = Join-Path $results 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $noDeploy) { throw 'The no-deploy sentinel must not exist.' }
# "A,B:Stage" and repeated arguments both work; a stage after ':' is forwarded to the script.
$Harness = @($Harness | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$verdicts = New-Object System.Collections.Generic.List[object]
# Only a value named on this command line is forwarded: the scripts' own defaults are not all
# D:\SPT41 (ApbsCompatibilityChecks derives its root from the checkout), and they stay as they are.
$forwardSptRoot = $PSBoundParameters.ContainsKey('SptRoot')
# The installations whose state is part of every key: the one named here and the default every harness
# script without -SptRoot keeps (ARENA_VERIFICATION_INSTALL_ROOTS replaces both, for the runner's own checks).
$installRoots = if ($env:ARENA_VERIFICATION_INSTALL_ROOTS) { @($env:ARENA_VERIFICATION_INSTALL_ROOTS -split ';') } else { @($SptRoot, 'D:\SPT41') }
# A runner started by a step inside a worker runs sequentially there; workers never start workers.
if ($env:ARENA_VERIFICATION_WORKER) { $Parallel = 1 }
$runtime = Join-Path $SptRoot 'SPT_Runtime'
$xunitName = 'ArenaMode.Tests (xunit, once)'
function Test-ScriptDeclaresSptRoot([string]$Path) {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$null, [ref]$null)
    return $null -ne $ast.ParamBlock -and
        @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'SptRoot' }).Count -gt 0
}

function Add-Verdict([string]$Name, [bool]$Passed, [double]$Seconds, [string]$Detail, [bool]$Cached = $false) {
    $verdicts.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Seconds = [math]::Round($Seconds); Detail = $Detail; Cached = $Cached })
    Write-Output ("{0} {1,-48} {2,5}s  {3}" -f ($(if ($Passed) { 'PASS' } else { 'FAIL' })), $Name, [math]::Round($Seconds), $Detail)
}

# The installed snapshot covers the client and companion DLLs; the server DLL, the gate patcher and the
# session helper are hashed on top, so a deploy from any step is seen as a changed file, not inferred.
$liveFiles = @(
    (Join-Path $SptRoot 'SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll'),
    (Join-Path $SptRoot 'BepInEx\patchers\maschine-ArenaMode\maschine-ArenaMode.Gate.dll'),
    (Join-Path $SptRoot 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Session.exe'),
    (Join-Path $SptRoot 'BepInEx\plugins\maschine-ArenaMode\arena-whitelist.json'))
function Get-LiveHashes {
    $hashes = @{}
    foreach ($path in $liveFiles) {
        $hashes[$path] = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { '<absent>' }
    }
    return $hashes
}
function Test-LiveUnchanged([hashtable]$Snapshot, [hashtable]$Hashes) {
    try { Assert-ArenaInstalledSnapshotUnchanged $Snapshot $SptRoot } catch { return $_.Exception.Message }
    $now = Get-LiveHashes
    foreach ($path in $liveFiles) { if ($now[$path] -ne $Hashes[$path]) { return "live install changed: $path" } }
    return $null
}

# --- Steps -------------------------------------------------------------------------------------------------

# One step: what runs, its log, its key. The command is built for a checkout root, so the same step runs in
# this checkout or in a worker.
function New-Step([string]$Item) {
    $step = [pscustomobject]@{ Item = $Item; Name = $Item; Stage = $null; Kind = 'missing'; Rel = $null; Log = $null; KeyInfo = $null; KeyNote = ''; RedeployBuild = $false; Expected = 120 }
    if ($Item -eq 'xunit') {
        $step.Name = $xunitName; $step.Kind = 'xunit'; $step.Log = Join-Path $results 'xunit.log'; $step.Expected = 110
    }
    else {
        $name, $stage = $Item -split ':', 2
        $step.Stage = $stage
        $step.Log = Join-Path $results (($Item -replace '[^A-Za-z0-9]', '_') + '.log')
        $script = if ($name -like '*.ps1') { ($name -replace '\\', '/') -replace '^\./', '' } else { "tools/$name/Verify.ps1" }
        if (Test-Path -LiteralPath (Join-Path $repo $script)) { $step.Kind = 'script'; $step.Rel = $script }
        elseif ($name -notlike '*.ps1' -and (Test-Path -LiteralPath (Join-Path $repo "tools/$name/$name.csproj"))) { $step.Kind = 'project'; $step.Rel = "tools/$name/$name.csproj" }
        else { $step.Rel = $script }
    }
    if ($cacheModel -and $step.Kind -ne 'missing') {
        try {
            $homeFolder = $null; $extra = @()
            if ($step.Kind -ne 'xunit') { if ($step.Rel -match '^tools/([^/]+)/') { $homeFolder = $Matches[1] } else { $extra = @($step.Rel) } }
            $scope = Get-ArenaStepScope $cacheModel $homeFolder $extra
            $info = Get-ArenaStepKey $cacheModel $scope $Item $runnerArguments $script:environmentHash $script:externalFingerprint $installRoots
            $info | Add-Member -NotePropertyName Scope -NotePropertyValue $scope
            $step.KeyInfo = $info
            $step.RedeployBuild = Test-ArenaStepBuildsRedeploy $cacheModel $scope
            $last = Get-ArenaLastPassPath $Item
            if (Test-Path -LiteralPath $last) { try { $step.Expected = [double](Get-Content -Raw -LiteralPath $last | ConvertFrom-Json).Seconds } catch { } }
        }
        catch { $step.KeyNote = "; not cached: key failed: $($_.Exception.Message)" }
    }
    elseif ($step.Kind -eq 'script' -or $step.Kind -eq 'project') { $step.RedeployBuild = $true }   # no model: assume the worst
    return $step
}

# The command of a step in the checkout $Root: @{ Exe; Arguments }.
function Get-StepCommand($Step, [string]$Root) {
    $targets = Join-Path $Root 'tools\AirPitChecks\NoDeploy.targets'
    switch ($Step.Kind) {
        'xunit' {
            return @{ Exe = 'dotnet'; Arguments = @('test', (Join-Path $Root 'ArenaMode.Tests\ArenaMode.Tests.csproj'), '-c', 'Release', '--nologo', '-v:minimal',
                "-p:CustomAfterMicrosoftCommonTargets=$targets", "-p:SPTUserMods=$noDeploy", "-p:SPTPath=$SptRoot", "-p:SPTUserPath=$runtime",
                '--logger', 'trx;LogFileName=run-arena-verification.trx', '--results-directory', $results) }
        }
        'script' {
            $script = Join-Path $Root $Step.Rel
            $arguments = @('-NonInteractive', '-NoLogo', '-NoProfile', '-File', $script)
            if ($Step.Stage) { $arguments += @('-Stage', $Step.Stage) }
            if ($forwardSptRoot -and (Test-ScriptDeclaresSptRoot $script)) { $arguments += @('-SptRoot', $SptRoot) }
            return @{ Exe = 'pwsh'; Arguments = $arguments }
        }
        'project' {
            # A harness without a Verify.ps1: its program is the whole check (no mutants). There is no shared
            # positional-argument contract: MolotovPresentationChecks treats a path as an audio export
            # destination, NativeStopChecks as a repo root. Use their no-argument defaults; SPT paths are
            # supplied via properties/env.
            return @{ Exe = 'dotnet'; Arguments = @('run', '--project', (Join-Path $Root $Step.Rel), '-c', 'Release', "-p:SPTPath=$SptRoot",
                "-p:CustomAfterMicrosoftCommonTargets=$targets", "-p:SPTUserMods=$noDeploy") }
        }
    }
}

# The verdict of a finished step from its exit code and log (xunit: its TRX).
function Get-StepVerdict($Step, [int]$Code) {
    if ($Step.Kind -eq 'xunit') {
        $trxPath = Join-Path $results 'run-arena-verification.trx'
        $detail = "dotnet test exit $Code"
        $passed = $Code -eq 0 -and (Test-Path -LiteralPath $trxPath)
        if ($passed) {
            try {
                $run = Assert-ArenaTestRun -TrxPath $trxPath
                $detail = "total=$($run.Total) passed=$($run.Passed) skipped=$($run.Skipped)" + $(if ($run.Skipped) { " ($($run.SkippedNames -join ', '))" } else { '' })
            }
            catch { $passed = $false; $detail = $_.Exception.Message }
        }
        elseif ($Code -eq 0) { $detail = 'dotnet test wrote no TRX' }
        return @{ Passed = $passed; Detail = $detail }
    }
    $tail = (Get-Content -LiteralPath $Step.Log -Tail 3) -join ' | '
    return @{ Passed = $Code -eq 0; Detail = "exit $Code; " + $tail.Substring(0, [Math]::Min(160, $tail.Length)) }
}

# --- Cache -------------------------------------------------------------------------------------------------
# Any error in the input model switches the cache off for this run, never the verification.
$cacheModel = $null
$cacheNote = $null
$runnerArguments = "sptRoot=$SptRoot|forward=$forwardSptRoot"
# After a step ran: the same files (in the checkout it ran in), environment and installation, or the PASS is
# not kept.
function Test-ArenaStepStillValid($KeyInfo, $Scope, [string]$Item, $Snapshot) {
    $changed = Test-ArenaSnapshotStillValid $Snapshot (Get-ArenaScopeFiles $cacheModel $Scope)
    if ($changed) { return $changed }
    Reset-ArenaStatManifestMemo
    $script:externalFingerprint = Get-ArenaExternalFingerprint $repo $installRoots
    $script:environmentHash = Get-ArenaEnvironmentFingerprint
    $again = Get-ArenaStepKey $cacheModel $Scope $Item $runnerArguments $script:environmentHash $script:externalFingerprint $installRoots
    if ($again.Key -ne $KeyInfo.Key) {
        $moved = @(foreach ($p in $again.Parts.GetEnumerator()) { if ([string]$KeyInfo.Parts[$p.Key] -ne [string]$p.Value) { $p.Key } })
        $what = Compare-ArenaExternalDigest $KeyInfo.ExternalDigest $again.ExternalDigest
        return 'inputs moved during the run: ' + ($moved -join ', ') + $(if ($what) { " ($what)" } else { '' })
    }
    return $null
}
function Write-CachedLog([string]$Log, $Entry) {
    $header = "cached: inputs unchanged since $(([datetime]$Entry.Finished).ToString('yyyy-MM-dd HH:mm:ss')) (key $($Entry.Key.Substring(0, 12)), checkout $($Entry.Checkout), HEAD $($Entry.Head)); original log follows"
    $original = if ($Entry.Log -and (Test-Path -LiteralPath $Entry.Log)) { Get-Content -LiteralPath $Entry.Log } else { @('<original log no longer in the cache>') }
    Set-Content -LiteralPath $Log -Value (@($header) + @($original))
}
# One step under the cache: on a hit, write the step's log from the cached PASS and return the verdict
# detail; $null means the caller runs the step.
function Get-CachedVerdict($KeyInfo, [string]$Log) {
    if ($keyInfo -and $keyInfo.Cacheable -and -not $NoCache) {
        $entry = Get-ArenaCachedPass $KeyInfo.Key
        if (-not $entry) { return $null }
        Write-CachedLog $Log $entry
        $detail = "cached: inputs unchanged since $(([datetime]$entry.Finished).ToString('yyyy-MM-dd HH:mm')) (ran $($entry.Seconds)s); $($entry.Detail)"
        return $detail.Substring(0, [Math]::Min(300, $detail.Length)) + "; log $Log"
    }
    return $null
}
function Complete-Step([string]$Item, $KeyInfo, [bool]$Passed, [double]$Seconds, [string]$Detail, [string]$Log, $Snapshot, [string]$Checkout) {
    if ($passed -and $keyInfo -and $keyInfo.Cacheable) {
        $afterRun = Test-ArenaStepStillValid $keyInfo $keyInfo.Scope $item $Snapshot
        if ($afterRun) { return "; not cached: $afterRun" }
        Save-ArenaCachedPass $KeyInfo $Item $Seconds $Log $Checkout $cacheModel.Snapshot.Head $Detail
        return ''
    }
    if ($KeyInfo -and -not $KeyInfo.Cacheable) { return "; not cacheable: $($KeyInfo.Why)" }
    return ''
}

# --- Run ---------------------------------------------------------------------------------------------------
$previousDatabase = $env:SPT_DATABASE
$previousRuntime = $env:SPT_RUNTIME
$previousWorker = $env:ARENA_VERIFICATION_WORKER
$previousMutantParallel = $env:ARENA_MUTANT_PARALLEL
$slots = @()
$redeployMutex = $null
Push-Location $repo
try {
    $env:SPT_DATABASE = Join-Path $runtime 'SPT_Data\database'
    $env:SPT_RUNTIME = $runtime
    $liveSnapshot = Read-ArenaInstalledSnapshot $SptRoot
    $liveHashes = Get-LiveHashes
    try {
        $cacheModel = Get-ArenaInputModel $repo
        $script:externalFingerprint = Get-ArenaExternalFingerprint $repo $installRoots
        $script:environmentHash = Get-ArenaEnvironmentFingerprint
    }
    catch { $cacheModel = $null; $cacheNote = "result cache off: $($_.Exception.Message)"; Write-Output $cacheNote }

    if ($All -or $ChangedSince) {
        if (-not $cacheModel) { throw "The selection needs the input model ($cacheNote)." }
        $registered = Get-ArenaRegisteredSteps $repo
        if ($All) { $picked = @($registered | ForEach-Object { [pscustomobject]@{ Step = $_; Reasons = @('registered (-All)') } }) }
        else {
            $changedFiles = @(Get-ArenaChangedFiles $repo $ChangedSince)
            Write-Output "ARENA_VERIFICATION_CHANGED $($changedFiles.Count) files since $ChangedSince (committed, staged, unstaged, untracked)"
            $picked = @(Select-ArenaSteps $cacheModel $registered $changedFiles)
        }
        foreach ($p in $picked) {
            $more = if ($p.Reasons.Count -gt 3) { "; +$($p.Reasons.Count - 3) more" } else { '' }
            Write-Output ("SELECT {0,-48} {1}{2}" -f $p.Step, (($p.Reasons | Select-Object -First 3) -join '; '), $more)
        }
        if ($ChangedSince) {
            $registeredFolders = @($registered | ForEach-Object { $n = ($_ -split ':', 2)[0]; if ($n -match '^tools/([^/]+)/') { $Matches[1] } elseif ($n -notlike '*.ps1') { $n } })
            $unregistered = @($changedFiles | ForEach-Object { Get-ArenaHarnessFolder $_ } | Where-Object { $_ -and $_ -notin $registeredFolders } | Sort-Object -Unique)
            foreach ($f in $unregistered) { Write-Output "UNREGISTERED tools/$f changed and is not in tools/verification-steps.txt" }
        }
        Write-Output "ARENA_VERIFICATION_SELECTION selected=$($picked.Count) of $($registered.Count) registered"
        $Harness = @(@($Harness) + @($picked | ForEach-Object Step) | Where-Object { $_ } | Select-Object -Unique)
    }
    if ($ListOnly) {
        foreach ($h in $Harness) { Write-Output "STEP $h" }
        if (-not $SkipUnitTests) { Write-Output "STEP $xunitName" }
        exit 0
    }

    # Every step's key now, against one snapshot; cached steps are reported at once.
    $steps = @(@(if (-not $SkipUnitTests) { 'xunit' }) + @($Harness) | ForEach-Object { New-Step $_ })
    $pending = [Collections.Generic.List[object]]::new()
    foreach ($step in $steps) {
        if ($step.Kind -eq 'missing') { Add-Verdict $step.Name $false 0 "no such script or project: $(Join-Path $repo $step.Rel)"; continue }
        $cachedDetail = Get-CachedVerdict $step.KeyInfo $step.Log
        if ($null -ne $cachedDetail) { Add-Verdict $step.Name $true 0 $cachedDetail $true; continue }
        if ($step.KeyInfo -and -not $NoCache) { $step.KeyNote += "; ran: $(Get-ArenaMissReason $step.KeyInfo $step.Item)" }
        $pending.Add($step)
    }

    if ($Parallel -le 1 -or $pending.Count -le 1) {
        # Sequentially, in this checkout, in the order given.
        foreach ($step in $pending) {
            $t0 = Get-Date
            $command = Get-StepCommand $step $repo
            if ($step.Kind -eq 'xunit') { $trx = Join-Path $results 'run-arena-verification.trx'; if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx -Force } }   # never judge a stale run
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                if ($step.Kind -eq 'xunit') {
                    & $command.Exe @($command.Arguments) 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $step.Log | Where-Object { $_ -match 'error|Fehler|Bestanden|Passed!|Failed!' }
                }
                else { & $command.Exe @($command.Arguments) 2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $step.Log }
            }
            finally { $ErrorActionPreference = $previousPreference }
            $verdict = Get-StepVerdict $step $LASTEXITCODE
            $passed = $verdict.Passed; $detail = $verdict.Detail
            $changed = Test-LiveUnchanged $liveSnapshot $liveHashes
            if ($changed) { $passed = $false; $detail += "; $changed" }
            $seconds = ((Get-Date) - $t0).TotalSeconds
            $detail += $step.KeyNote + (Complete-Step $step.Item $step.KeyInfo $passed $seconds $detail $step.Log $cacheModel.Snapshot $repo)
            Add-Verdict $step.Name $passed $seconds $(if ($step.Kind -eq 'xunit') { $detail } else { "$detail; log $($step.Log)" })
        }
    }
    else {
        if (-not $cacheModel) { throw "Parallel steps need the input model to sync the workers ($cacheNote); run with -Parallel 1." }
        $slots = @(Get-ArenaWorkerSlots $repo ([Math]::Min($Parallel, $pending.Count)))
        if (-not $slots.Count) { throw 'No worker slot is free (other runs hold all eight); run with -Parallel 1 or wait.' }
        $t0 = Get-Date
        $sourceSnapshot = $cacheModel.Snapshot
        $syncs = $slots | ForEach-Object -ThrottleLimit $slots.Count -Parallel {
            $slot = $_
            $repoPath = $using:repo
            . (Join-Path $repoPath 'tools\ArenaVerificationInputs.ps1')
            . (Join-Path $repoPath 'tools\ArenaVerificationWorkers.ps1')
            try { [pscustomobject]@{ Index = $slot.Index; Snapshot = (Sync-ArenaWorker $repoPath $using:sourceSnapshot $slot.Path); Error = $null } }
            catch { [pscustomobject]@{ Index = $slot.Index; Snapshot = $null; Error = $_.Exception.Message } }
        }
        $failedSync = @($syncs | Where-Object Error)
        if ($failedSync.Count) { throw "Worker sync failed: $(($failedSync | ForEach-Object { "vw$($_.Index): $($_.Error)" }) -join '; ')" }
        foreach ($slot in $slots) { $slot | Add-Member -NotePropertyName Snapshot -NotePropertyValue (($syncs | Where-Object Index -eq $slot.Index).Snapshot) -Force }
        Write-Output ("ARENA_VERIFICATION_WORKERS {0} in {1:N0}s: {2}" -f $slots.Count, ((Get-Date) - $t0).TotalSeconds, (($slots | ForEach-Object Path) -join ', '))

        $env:ARENA_VERIFICATION_WORKER = '1'
        # Mutants inside a step build side by side too (Invoke-ArenaSourceChecks.ps1); share the cores between
        # the workers unless the caller chose a number.
        if (-not $env:ARENA_MUTANT_PARALLEL) { $env:ARENA_MUTANT_PARALLEL = [string][Math]::Max(2, [Math]::Floor([Environment]::ProcessorCount / ($slots.Count * 2))) }
        $redeployMutex = [Threading.Mutex]::new($false, $ArenaRedeployMutexName)
        $redeployHeld = $false
        $queue = [Collections.Generic.List[object]]::new([object[]]@($pending | Sort-Object Expected -Descending))
        $free = [Collections.Generic.Queue[object]]::new([object[]]$slots)
        $running = [Collections.Generic.List[object]]::new()
        $done = [Collections.Generic.List[object]]::new()   # finished since the last clean live-install check
        $installBroken = $null
        while ($queue.Count -or $running.Count) {
            # Start what fits: a free worker, and the Redeploy mutex for a step that builds the client.
            while ($free.Count -and $queue.Count) {
                $index = -1
                for ($i = 0; $i -lt $queue.Count; $i++) {
                    if (-not $queue[$i].RedeployBuild) { $index = $i; break }
                    if (-not $redeployHeld) {
                        $taken = $false
                        try { $taken = $redeployMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $taken = $true }
                        if ($taken) { $redeployHeld = $true; $index = $i; break }
                    }
                }
                if ($index -lt 0) { break }
                $step = $queue[$index]; $queue.RemoveAt($index)
                $slot = $free.Dequeue()
                # A step must not leave a non-ignored change behind; if one did, the next step in that worker
                # would test a tree that is not the one under test. Check and sync again before reuse.
                if ($slot.PSObject.Properties['Used']) {
                    $drift = Test-ArenaSnapshotStillValid $slot.Snapshot @($slot.Snapshot.Files.Keys)
                    if ($drift) { Write-Output "ARENA_VERIFICATION_WORKER vw$($slot.Index) drifted ($drift); syncing again"; $slot.Snapshot = Sync-ArenaWorker $repo $cacheModel.Snapshot $slot.Path }
                }
                $slot | Add-Member -NotePropertyName Used -NotePropertyValue $true -Force
                $command = Get-StepCommand $step $slot.Path
                if ($step.Kind -eq 'xunit') { $trx = Join-Path $results 'run-arena-verification.trx'; if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx -Force } }
                $job = Start-ThreadJob -ArgumentList $slot.Path, $command.Exe, $command.Arguments, $step.Log -ScriptBlock {
                    param($root, $exe, $arguments, $log)
                    Set-Location -LiteralPath $root
                    $ErrorActionPreference = 'Continue'
                    & $exe @arguments 2>&1 | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log
                    $LASTEXITCODE
                }
                $running.Add([pscustomobject]@{ Step = $step; Slot = $slot; Job = $job; Start = Get-Date })
            }
            if (-not $running.Count) {
                # Only client builds are left and another run holds the Redeploy mutex: wait for it.
                $taken = $false
                try { $taken = $redeployMutex.WaitOne(5000) } catch [Threading.AbandonedMutexException] { $taken = $true }
                if ($taken) { $redeployMutex.ReleaseMutex() }
                continue
            }
            $finished = Wait-Job -Job @($running | ForEach-Object Job) -Any -Timeout 2
            if (-not $finished) { continue }
            $entry = $running | Where-Object { $_.Job.Id -eq $finished.Id } | Select-Object -First 1
            $running.Remove($entry) | Out-Null
            $code = @(Receive-Job -Job $entry.Job -ErrorAction SilentlyContinue) | Select-Object -Last 1
            Remove-Job -Job $entry.Job -Force
            if ($entry.Step.RedeployBuild -and $redeployHeld) { $redeployMutex.ReleaseMutex(); $redeployHeld = $false }
            $seconds = ((Get-Date) - $entry.Start).TotalSeconds
            $verdict = Get-StepVerdict $entry.Step ([int]$code)
            $passed = $verdict.Passed; $detail = $verdict.Detail
            # The live install is shared: a change fails every step that ran since the last clean check, and
            # the steps still running when it was seen.
            $done.Add($entry)
            $changed = Test-LiveUnchanged $liveSnapshot $liveHashes
            if ($changed -and -not $installBroken) {
                $installBroken = "$changed (while $((@($done) + @($running) | ForEach-Object { $_.Step.Name } | Sort-Object -Unique) -join ', ') ran)"
                foreach ($other in $running) { $other | Add-Member -NotePropertyName Tainted -NotePropertyValue $installBroken -Force }
            }
            if ($changed) { $passed = $false; $detail += "; $installBroken" }
            elseif ($entry.PSObject.Properties['Tainted']) { $passed = $false; $detail += "; $($entry.Tainted)" }
            else { $done.Clear() }
            $detail += $entry.Step.KeyNote + (Complete-Step $entry.Step.Item $entry.Step.KeyInfo $passed $seconds $detail $entry.Step.Log $entry.Slot.Snapshot $entry.Slot.Path)
            Add-Verdict $entry.Step.Name $passed $seconds $(if ($entry.Step.Kind -eq 'xunit') { $detail } else { "$detail; worker vw$($entry.Slot.Index); log $($entry.Step.Log)" })
            $free.Enqueue($entry.Slot)
        }
    }
}
finally {
    if ($redeployMutex) { try { if ($redeployHeld) { $redeployMutex.ReleaseMutex() } } catch { }; $redeployMutex.Dispose() }
    if ($slots.Count) { Remove-ArenaWorkerSlots $slots }
    Pop-Location
    $env:SPT_DATABASE = $previousDatabase
    $env:SPT_RUNTIME = $previousRuntime
    if ($null -eq $previousWorker) { Remove-Item Env:ARENA_VERIFICATION_WORKER -ErrorAction SilentlyContinue } else { $env:ARENA_VERIFICATION_WORKER = $previousWorker }
    if ($slots.Count) { if ($null -eq $previousMutantParallel) { Remove-Item Env:ARENA_MUTANT_PARALLEL -ErrorAction SilentlyContinue } else { $env:ARENA_MUTANT_PARALLEL = $previousMutantParallel } }
}

if ($verdicts.Count -eq 0) { Write-Output 'ARENA_VERIFICATION_SUMMARY nothing ran (no harness named and the suite skipped)'; exit 1 }
$failed = @($verdicts | Where-Object { -not $_.Passed })
Write-Output ("ARENA_VERIFICATION_SUMMARY passed={0} failed={1} seconds={2} cached={3}" -f (@($verdicts | Where-Object Passed).Count), $failed.Count, ($verdicts | Measure-Object Seconds -Sum).Sum, @($verdicts | Where-Object Cached).Count)
exit $failed.Count
