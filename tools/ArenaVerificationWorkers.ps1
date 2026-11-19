# Worker checkouts for tools/Run-ArenaVerification.ps1 -Parallel. Steps cannot run side by side in one checkout:
# about 30 harnesses run `dotnet test` on ArenaMode.Tests, 13 build ArenaMode.sln, 9 build ArenaMode.Client through
# DoubleJumpChecks/Build-Staged.ps1, all into the production projects' own obj/ and bin/, and several write fixed
# artifacts/ paths. A worker is a persistent git worktree of the same repository, a SIBLING of the checkout under
# test (D:\SPT41\Development\ArenaMode-vw<k>): 31 harness paths leave the repository relatively
# (..\..\..\..\BepInEx, ..\_decompiled, ..\Redeploy), so a worker must sit at the same depth. Its ignored build
# output stays between runs, so builds stay incremental.
#
# Before a run every worker is made byte-identical to the snapshot the step keys were computed from: HEAD checked
# out, then every file whose bytes, presence or tracked flag differ copied (with a fresh write time: a copied file
# keeps the source's older time otherwise, and MSBuild would take its stale obj/ output for current) or deleted,
# and the worker's index aligned; the worker's own snapshot must then equal the source snapshot file by file, or
# the run stops. The worker shares nothing in the checkout with any other worker. The sibling Redeploy checkout is
# shared by all of them: a build of ArenaMode.Client or ArenaMode.sln builds Redeploy.Core through the project
# reference, and would write its obj/ and bin/ there. Every process of a worker step therefore gets
# tools/ArenaWorkerRedeploy.props through its own environment (Get-ArenaWorkerEnvironment), which moves the obj/
# and bin/ of every project in that checkout into the worker's artifacts\redeploy-build; the sources stay shared
# and are only read.

$ArenaWorkerPrefix = 'ArenaMode-vw'
# Inside a worker; kept by the sync like obj/ and bin/, so Redeploy.Core builds incrementally.
$ArenaWorkerRedeployBuild = 'artifacts\redeploy-build'

# The environment variables of every process of a step (and of the restore) in $Worker. They must be set per
# process: the steps of all workers run in thread jobs and runspaces of ONE runner process, and a variable set
# with $env: there would reach the steps of the other workers.
function Get-ArenaWorkerEnvironment([string]$Worker) {
    $full = [IO.Path]::GetFullPath($Worker).TrimEnd('\')
    return @{
        CustomBeforeDirectoryBuildProps = Join-Path $full 'tools\ArenaWorkerRedeploy.props'
        ARENA_VERIFICATION_REDEPLOY_CHECKOUT = Join-Path (Split-Path -Parent $full) 'Redeploy'
        ARENA_VERIFICATION_REDEPLOY_BUILD = Join-Path $full $ArenaWorkerRedeployBuild
    }
}

# Runs one program with $Environment added to its own environment only. Standard output and standard error are
# merged in the order their lines arrive (as `2>&1` does) into $Log, or returned as Output when $Log is empty.
# ExitCode is the program's own exit code, -1 when it did not start. $Encoding decodes the output; the runner
# passes the console encoding it read at its start (a runspace that runs git may switch the process-wide one).
function Invoke-ArenaWorkerProcess([string]$Exe, [string[]]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment = @{}, [string]$Log = '', [Text.Encoding]$Encoding = [Console]::OutputEncoding) {
    $resolved = Get-Command $Exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $psi = [Diagnostics.ProcessStartInfo]::new($(if ($resolved) { $resolved.Source } else { $Exe }))
    foreach ($argument in $Arguments) { $psi.ArgumentList.Add([string]$argument) }
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $Encoding; $psi.StandardErrorEncoding = $Encoding
    foreach ($name in $Environment.Keys) { $psi.Environment[$name] = [string]$Environment[$name] }
    $collected = [Text.StringBuilder]::new()
    $writer = if ($Log) { [IO.StreamWriter]::new($Log, $false, [Text.UTF8Encoding]::new($false)) } else { $null }
    try {
        try { $process = [Diagnostics.Process]::Start($psi) }
        catch {
            $message = "runner: the step did not run: $($_.Exception.Message)"
            if ($writer) { $writer.WriteLine($message) } else { [void]$collected.AppendLine($message) }
            return [pscustomobject]@{ ExitCode = -1; Output = $collected.ToString() }
        }
        try {
            $out = $process.StandardOutput; $err = $process.StandardError
            $readOut = $out.ReadLineAsync(); $readErr = $err.ReadLineAsync()
            while ($readOut -or $readErr) {
                $open = [Threading.Tasks.Task[]]@(@($readOut, $readErr) | Where-Object { $_ })
                $ready = $open[[Threading.Tasks.Task]::WaitAny($open)]
                $line = $ready.Result
                if ([object]::ReferenceEquals($ready, $readOut)) { $readOut = if ($null -eq $line) { $null } else { $out.ReadLineAsync() } }
                else { $readErr = if ($null -eq $line) { $null } else { $err.ReadLineAsync() } }
                if ($null -ne $line) { if ($writer) { $writer.WriteLine($line) } else { [void]$collected.AppendLine($line) } }
            }
            $process.WaitForExit()
            return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $collected.ToString() }
        }
        finally { $process.Dispose() }
    }
    finally { if ($writer) { $writer.Dispose() } }
}

function Get-ArenaRepoIdentity([string]$Root) {
    $common = (Invoke-ArenaGitLines $Root @('rev-parse', '--path-format=absolute', '--git-common-dir') | Select-Object -First 1)
    return (Get-ArenaStringSha ([IO.Path]::GetFullPath($common).ToLowerInvariant())).Substring(0, 16)
}

# Takes up to $Count worker slots (a named mutex each, so two runs never share a worker). Returns
# @{ Index; Path; Mutex } for every slot taken; the caller releases the mutexes on the same thread.
function Get-ArenaWorkerSlots([string]$Source, [int]$Count, [int]$MaxSlots = 8) {
    $identity = Get-ArenaRepoIdentity $Source
    $parent = Split-Path -Parent $Source
    $slots = [Collections.Generic.List[object]]::new()
    for ($k = 1; $k -le $MaxSlots -and $slots.Count -lt $Count; $k++) {
        $mutex = [Threading.Mutex]::new($false, "Global\ArenaVerificationWorker-$identity-$k")
        $taken = $false
        try { $taken = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $taken = $true }
        if ($taken) { $slots.Add([pscustomobject]@{ Index = $k; Path = (Join-Path $parent "$ArenaWorkerPrefix$k"); Mutex = $mutex }) }
        else { $mutex.Dispose() }
    }
    return @($slots)
}

function Remove-ArenaWorkerSlots([object[]]$Slots) {
    foreach ($slot in $Slots) { try { $slot.Mutex.ReleaseMutex() } catch { }; $slot.Mutex.Dispose() }
}

# Makes one worker byte-identical to $Snapshot (see the header). Returns the worker's own snapshot (its write
# times are its own), or throws.
function Sync-ArenaWorker([string]$Source, $Snapshot, [string]$Worker, [string[]]$RestoreProjects = @()) {
    if ([IO.Path]::GetFullPath($Worker).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Source).TrimEnd('\')) { throw "Refusing to sync $Worker onto itself." }
    $sourceCommon = Get-ArenaRepoIdentity $Source
    if (Test-Path -LiteralPath $Worker) {
        $isOurs = $false
        try { $isOurs = (Get-ArenaRepoIdentity $Worker) -eq $sourceCommon -and (Test-Path -LiteralPath (Join-Path $Worker '.git') -PathType Leaf) } catch { }
        if (-not $isOurs) { throw "$Worker exists and is not a worktree of this repository; refusing to touch it." }
        $null = Invoke-ArenaGitLines $Worker @('checkout', '-q', '--detach', '--force', $Snapshot.Head)
        $null = Invoke-ArenaGitLines $Worker @('clean', '-fdq')
        # The harness evidence of the previous run in this worker (artifacts/ is ignored, so clean keeps it);
        # without this a worker grows by every run, and Syncthing mirrors it. bin/ and obj/ stay for
        # incremental builds, and so does the worker's Redeploy build folder.
        $evidence = Join-Path $Worker 'artifacts'
        $keep = Split-Path -Leaf $ArenaWorkerRedeployBuild
        if (Test-Path -LiteralPath $evidence) {
            Get-ChildItem -LiteralPath $evidence -Force | Where-Object { $_.Name -ine $keep } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        # A worker folder deleted by hand stays registered and blocks `worktree add` until pruned.
        $null = Invoke-ArenaGitLines $Source @('worktree', 'prune')
        $null = Invoke-ArenaGitLines $Source @('worktree', 'add', '-q', '--detach', $Worker, $Snapshot.Head)
    }
    $current = Get-ArenaRepoSnapshot $Worker   # not $worker: PowerShell names are case-insensitive, that is the [string] parameter
    $now = [DateTime]::UtcNow
    $toAdd = [Collections.Generic.List[string]]::new(); $toUntrack = [Collections.Generic.List[string]]::new()
    foreach ($rel in $Snapshot.Files.Keys) {
        $want = $Snapshot.Files[$rel]
        $have = $current.Files[$rel]
        $target = Join-Path $Worker $rel
        if ($want.Hash -eq '<deleted>') {
            if ([IO.File]::Exists($target)) { [IO.File]::Delete($target) }
        }
        elseif (-not $have -or $have.Hash -ne $want.Hash) {
            [IO.Directory]::CreateDirectory((Split-Path $target)) | Out-Null
            [IO.File]::Copy((Join-Path $Source $rel), $target, $true)
            [IO.File]::SetLastWriteTimeUtc($target, $now)
            if ((Get-ArenaFileSha $target) -ne $want.Hash) { throw "$rel changed in $Source while the workers were synced; run again." }
        }
        if ($want.Tracked -and (-not $have -or -not $have.Tracked)) { $toAdd.Add($rel) }
        if (-not $want.Tracked -and $have -and $have.Tracked) { $toUntrack.Add($rel) }
    }
    foreach ($rel in $current.Files.Keys) {
        if (-not $Snapshot.Files.ContainsKey($rel)) { $target = Join-Path $Worker $rel; if ([IO.File]::Exists($target)) { [IO.File]::Delete($target) } }
    }
    # The index decides what Write-ArenaAssets copies and what a harness's git calls see.
    foreach ($batch in @($toAdd | Group-Object { [int]($toAdd.IndexOf($_) / 200) })) { $null = Invoke-ArenaGitLines $Worker (@('add', '-f', '--') + @($batch.Group)) }
    foreach ($batch in @($toUntrack | Group-Object { [int]($toUntrack.IndexOf($_) / 200) })) { $null = Invoke-ArenaGitLines $Worker (@('rm', '-q', '--cached', '--') + @($batch.Group)) }
    $result = Get-ArenaRepoSnapshot $Worker
    $diff = Compare-ArenaSnapshots $Snapshot $result
    if ($diff) { throw "Worker $Worker does not match the checkout under test after the sync: $diff" }
    # The registered fixtures under the ignored artifacts/ (a downloaded release, the decompiled installation)
    # are inputs, so a worker gets the checkout's copy; the key reads them in the checkout under test.
    foreach ($prefix in $ArenaArtifactInputs) {
        $from = Join-Path $Source ($prefix -replace '/', '\')
        $to = Join-Path $Worker ($prefix -replace '/', '\')
        if (Test-Path -LiteralPath $to) { Remove-Item -LiteralPath $to -Recurse -Force }
        if (Test-Path -LiteralPath $from) {
            [IO.Directory]::CreateDirectory((Split-Path $to)) | Out-Null
            Copy-Item -LiteralPath $from -Destination $to -Recurse -Force
            Get-ChildItem -LiteralPath $to -Recurse -File | ForEach-Object { $_.LastWriteTimeUtc = $now }
        }
    }
    # Steps that build with --no-restore rely on a restore that ran earlier; the obj/ of a worker is its own and
    # starts empty, so restore those projects when their assets are missing or older than the project file. The
    # restore runs with the worker's environment, so a Redeploy project it reaches gets its assets in the worker's
    # Redeploy build folder, where the build looks for them; while that folder does not exist yet, every project
    # is restored once.
    $environment = Get-ArenaWorkerEnvironment $Worker
    $redeployBuild = $environment.ARENA_VERIFICATION_REDEPLOY_BUILD
    $redeployRestored = Test-Path -LiteralPath $redeployBuild
    foreach ($project in $RestoreProjects) {
        $full = Join-Path $Worker $project
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $assets = Join-Path (Split-Path $full) 'obj\project.assets.json'
        if ($redeployRestored -and (Test-Path -LiteralPath $assets) -and (Get-Item -LiteralPath $assets).LastWriteTimeUtc -ge (Get-Item -LiteralPath $full).LastWriteTimeUtc) { continue }
        $restore = Invoke-ArenaWorkerProcess 'dotnet' @('restore', $full, '--nologo', '-v:q') $Worker $environment
        if ($restore.ExitCode -ne 0) { throw "dotnet restore $project failed in ${Worker}: $($restore.Output)" }
    }
    [IO.Directory]::CreateDirectory($redeployBuild) | Out-Null
    return $result
}

# The projects a worker must have restored: the test project (several harnesses run `dotnet test --no-restore`
# on it) and the projects of every harness folder whose scripts build with --no-restore.
function Get-ArenaNoRestoreProjects($Model) {
    $projects = [Collections.Generic.List[string]]::new()
    $projects.Add('ArenaMode.Tests/ArenaMode.Tests.csproj')
    foreach ($folder in $Model.FolderNames) {
        $scripts = @($Model.FolderFiles[$folder] | Where-Object { $_ -like '*.ps1' })
        $noRestore = $false
        foreach ($rel in $scripts) { if ([IO.File]::ReadAllText((Join-Path $Model.Snapshot.Root $rel)).Contains('--no-restore')) { $noRestore = $true; break } }
        if ($noRestore) { foreach ($p in @($Model.FolderFiles[$folder] | Where-Object { $_ -like '*.csproj' })) { $projects.Add($p) } }
    }
    return @($projects | Sort-Object -Unique)
}

function Compare-ArenaSnapshots($Expected, $Actual) {
    if ($Expected.Head -ne $Actual.Head) { return "HEAD $($Actual.Head) instead of $($Expected.Head)" }
    foreach ($rel in $Expected.Files.Keys) {
        $e = $Expected.Files[$rel]; $a = $Actual.Files[$rel]
        if ($e.Hash -eq '<deleted>') { if ($a -and $a.Hash -ne '<deleted>') { return "$rel exists" }; continue }
        if (-not $a) { return "$rel is missing" }
        if ($a.Hash -ne $e.Hash) { return "$rel differs" }
        if ($a.Tracked -ne $e.Tracked) { return "$rel tracked=$($a.Tracked)" }
    }
    foreach ($rel in $Actual.Files.Keys) { if (-not $Expected.Files.ContainsKey($rel) -and $Actual.Files[$rel].Hash -ne '<deleted>') { return "$rel is extra" } }
    return $null
}
