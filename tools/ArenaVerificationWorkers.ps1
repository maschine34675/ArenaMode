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
# the run stops. The worker shares nothing in the checkout with any other worker; only the sibling Redeploy
# checkout stays shared, which a build of ArenaMode.Client or ArenaMode.sln with project references writes
# (Redeploy.Core's obj/ and bin/); those steps take a machine-wide mutex.

$ArenaWorkerPrefix = 'ArenaMode-vw'
$ArenaRedeployMutexName = 'Global\ArenaVerification-RedeployCore'

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
        # incremental builds.
        $evidence = Join-Path $Worker 'artifacts'
        if (Test-Path -LiteralPath $evidence) { Remove-Item -LiteralPath $evidence -Recurse -Force -ErrorAction SilentlyContinue }
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
    # Steps that build with --no-restore rely on a restore that ran earlier; the obj/ of a worker is its own and
    # starts empty, so restore those projects when their assets are missing or older than the project file.
    foreach ($project in $RestoreProjects) {
        $full = Join-Path $Worker $project
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $assets = Join-Path (Split-Path $full) 'obj\project.assets.json'
        if ((Test-Path -LiteralPath $assets) -and (Get-Item -LiteralPath $assets).LastWriteTimeUtc -ge (Get-Item -LiteralPath $full).LastWriteTimeUtc) { continue }
        $output = & dotnet restore $full --nologo -v:q 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore $project failed in ${Worker}: $output" }
    }
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

# Whether a step writes the shared Redeploy checkout: it builds ArenaMode.Client or ArenaMode.sln (project
# references build Redeploy.Core into ../Redeploy). Conservative: a staged build that passes
# BuildProjectReferences=false is counted too.
function Test-ArenaStepBuildsRedeploy($Model, $Scope) {
    foreach ($rel in $Scope.Active) {
        $fact = $Model.Facts[$rel]
        if (-not $fact -or ($rel -in $ArenaKeyInfrastructureFiles)) { continue }
        foreach ($b in @($fact.ProductionBuilds)) { if ($b -in 'sln', 'ArenaMode.Client') { return $true } }
    }
    return $false
}
