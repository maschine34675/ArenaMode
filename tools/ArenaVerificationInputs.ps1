# The input model of tools/Run-ArenaVerification.ps1: which files, installed state, tools and environment a
# verification step can read, reduced to one content key per step. A step whose key equals the key of an
# earlier PASS is not run again (the result cache).
#
# The key is deliberately coarse where a miss would cost a stale PASS. Every file of the checkout that git
# tracks or would track (untracked, not ignored) is in it with its bytes and its tracked flag (Write-ArenaAssets
# copies only tracked assets), EXCEPT two kinds of file that the rules below show irrelevant to one step:
#   * another harness folder (tools/<Folder>/) that is not reached: a step reaches its own folder, every folder
#     the shared files name, and every folder that a reached file names, transitively. A bare folder word
#     names the whole folder (all its files may run); a folder followed by a file path names that file.
#   * a record under docs/ (everything there except docs/licenses/) that no reached or shared file names by
#     a docs/ path (a directory names everything below it), by its unique file name, or that a reached file
#     could build from a quoted "docs" segment.
# What counts as a mention: the whole text of scripts and data files (full-line PowerShell comments and XML
# comments stripped), and only the string literals of C# files, so a comment like "pinned by KitPickerChecks"
# in production code is no dependency. A reached file that enumerates directories (Get-ChildItem -Recurse,
# EnumerateFiles, git ls-files, ...) may read anything: its step keeps every folder and every record. Ignored
# files (bin/, obj/, build/, artifacts/, .unlazy/) are build output or regenerated per run (AreaTeamRuntimeChecks
# writes its artifacts/ source from production every time) and are not inputs, except the fixtures registered
# in $ArenaArtifactInputs (downloaded releases, a decompiled installation), keyed for the steps that name them.
# Outside the checkout the key holds the installed game, BepInEx and SPT state that a harness compiles against
# or reads (stat manifests: path, size, write time), the sibling Redeploy checkout (HEAD, status, dirty bytes),
# ../_decompiled and ../_reference, the tool versions (dotnet SDKs and runtimes, git, PowerShell), the
# environment variables a harness or MSBuild reads, external paths a reached file names, and HEAD for steps
# whose reached files call git. The installed parts that change on every game start (BepInEx/config, logs,
# profiles) count only for steps whose reached files name them.
# docs/TRAPS.md ("Verification runner") lists the argued exceptions; tools/VerificationRunnerChecks proves that
# each kind of change invalidates the key and that the excluded kinds do not.

$ArenaVerificationKeyVersion = 'arena-verification-key-v1'
# The schema of the cached per-file facts (independent of the key: a new fact does not void stored PASSes).
$ArenaFactsVersion = 'facts-4'

# Reviewed infrastructure that builds PAYLOAD paths: its "docs" segments and docs/ tokens name the payload's
# docs folder (notices under docs/licenses), and its enumerations walk the payload, the assets or, for the
# runner and these modules, the checkout to build this key. A bare "docs" token or a quoted "docs" segment in
# these files names no record, and their enumerations make no step wide. Any other verification file that
# enumerates and names docs or tools makes every step keep every folder and every record.
$ArenaPayloadLayoutFiles = @(
    'tools/ArenaVerificationInputs.ps1', 'tools/ArenaVerificationWorkers.ps1', 'tools/Run-ArenaVerification.ps1',
    'tools/Write-ArenaAssets.ps1', 'tools/Arena-NoticeLayout.ps1', 'tools/Deploy-ArenaClient.ps1', 'tools/Deploy-ArenaSession.ps1',
    'tools/Package-TestRelease.ps1', 'tools/Verify-TestRelease.py')
# Fixtures a harness reads from the ignored artifacts/ (downloaded releases, a decompiled installation). Ignored
# files are otherwise output and not in the key; a step whose reached files name one of these prefixes keys
# its content. Register a prefix here when a harness starts reading a fixture from artifacts/.
$ArenaArtifactInputs = @(
    'artifacts/pft-100-contracts',                          # PitFireTeam100Checks: PFT 1.0.0 release, legacy builds
    'artifacts/pft-installed',                              # PortedCoverChecks: Decompile-PitFireTeam.ps1 output
    'artifacts/ammo-raid-health-compat-20261019/releases',  # SvmCompatChecks: downloaded SVM release
    'artifacts/compat-seven-20261025/items')                # UseItemsAnywhereChecks: downloaded UseItemsAnywhere
# Production code runs in the game, or inside a harness against paths the harness hands it; it never
# addresses the checkout, so its enumerations and path literals are no inputs of a step. Path literals in the
# xunit sources are test data for path-handling code (the tests that read installed data get it through
# SPT_DATABASE/SPT_RUNTIME, which are in the key); they are no inputs either.
$ArenaProductionPrefixes = @('ArenaMode.Client/', 'ArenaMode.Server/', 'ArenaMode.Shared/', 'ArenaMode.Gate/', 'ArenaMode.Session/', 'ArenaMode.BigBrainCompat/')
# Records that some files name only as a PAYLOAD path: Write-ArenaAssets copies the root README.md to the
# payload's docs/README.md and DeployLayoutChecks asserts that payload file. The repository's docs/README.md
# (the documentation index, changed by every new document) is read by neither; a mention by any other file
# makes it an input again.
# The runner and this model name docs paths only to describe the rules above; they read no record.
$ArenaKeyInfrastructureFiles = @('tools/ArenaVerificationInputs.ps1', 'tools/ArenaVerificationWorkers.ps1', 'tools/Run-ArenaVerification.ps1')
$ArenaPayloadOnlyRecordNamers = @{
    'docs/README.md' = @('tools/Write-ArenaAssets.ps1', 'tools/DeployLayoutChecks/Verify.ps1')
}
# Installed subtrees outside the shared fingerprint: they change with every game start, so they count only for
# a step whose reached files name them ('dir|filter' limits a directory to matching top-level files).
$ArenaInstallStateSubtrees = [ordered]@{
    'BepInEx(?:[\\/]+|["'']\s*,\s*["''])config' = @('BepInEx\config')
    'BepInEx(?:[\\/]+|["'']\s*,\s*["''])cache' = @('BepInEx\cache')
    'LogOutput' = @('BepInEx|*.log', 'BepInEx\logs')
    'user(?:[\\/]+|["'']\s*,\s*["''])profiles' = @('SPT_Runtime\user\profiles')
    'user(?:[\\/]+|["'']\s*,\s*["''])logs' = @('SPT_Runtime\user\logs')
    'user(?:[\\/]+|["'']\s*,\s*["''])cache' = @('SPT_Runtime\user\cache')
    'sptappdata' = @('SPT_Runtime\user\sptappdata')
}
$ArenaUncoveredInstallDirs = @('BepInEx\config', 'BepInEx\cache', 'BepInEx\logs', 'SPT_Runtime\user\profiles',
    'SPT_Runtime\user\logs', 'SPT_Runtime\user\cache', 'SPT_Runtime\user\sptappdata')
$ArenaScannedExtensions = @('.ps1', '.psm1', '.cs', '.csproj', '.props', '.targets', '.json', '.py', '.txt', '.xml',
    '.sln', '.config', '.cmd', '.bat', '.sh', '.yml', '.yaml', '.ini', '.cfg', '.html', '.razor', '.css', '.js')
$ArenaEnumerationPattern = '(?i)Get-ChildItem[^\r\n|;]*-Recurse|\bgci\b[^\r\n|;]*-r|EnumerateFiles|EnumerateDirectories|EnumerateFileSystemEntries|GetFiles\(|GetDirectories\(|GetFileSystemEntries|Directory\.Get|ls-files|git\s+grep|Resolve-Path[^\r\n|;]*\*|Get-Item[^\r\n|;]*\*|Select-String[^\r\n|;]*-Path[^\r\n|;]*\*'
$ArenaGitPattern = '(?i)(?:^|[\s&(;|{])git(?:\.exe)?["'']?\s+(?:-C\s|-c\s|rev-parse|status|diff|log|ls-files|grep|show|describe|cat-file|rev-list|hash-object|branch|stash|worktree)|["'']git["'']'
# A quoted "docs" segment builds a docs path from parts (Path.Combine(root, "docs", ...)): any record may be read.
$ArenaDocsQuotedPattern = '(?i)["'']docs[\\/]?["'']'
$ArenaDocsOrToolsPattern = '(?i)(?<![A-Za-z0-9_])(?:docs|tools)(?![A-Za-z0-9_])'
$ArenaAbsolutePathPattern = '(?i)(?<![A-Za-z0-9])[A-Z]:\\[^\s''"<>|*?;,()\[\]{}`$]+'
$ArenaQuotedAbsolutePathPattern = '(?i)(?<=["''])[A-Z]:\\[^''"<>|*?\r\n]+(?=["''])'
$ArenaArtifactPathPattern = '(?i)(?<![A-Za-z0-9_])artifacts[\\/][A-Za-z0-9_.\-\\/]*'
# A path built from quoted segments ("ArenaMode.Client", "Spectator", "X.cs"): selection joins them.
$ArenaSegmentPathPattern = '(?i)["''](ArenaMode\.(?:Client|Server|Shared|Gate|Session|BigBrainCompat|Tests)|assets)["'']((?:\s*,\s*["''][A-Za-z0-9_.\-]+["''])+)'
$ArenaEscapePathPattern = '(?:\.\.[\\/])+[A-Za-z0-9_.\-]+(?:[\\/][A-Za-z0-9_.\-]+)*'
$ArenaDocsPathPattern = '(?i)(?<![A-Za-z0-9_])docs[\\/][A-Za-z0-9_.\-\\/]*'
$ArenaMarkdownNamePattern = '(?i)[A-Za-z0-9_.\-]+\.md(?![A-Za-z0-9_])'
$ArenaCSharpStringPattern = '@"(?:[^"]|"")*"|"(?:[^"\\\r\n]|\\.)*"'
# Selection only (-ChangedSince): production paths a script or program names (mutant targets, linked sources),
# and builds or test runs of a production project, which reach every production file.
$ArenaProductionPathPattern = '(?i)(?<![A-Za-z0-9_.\-])(?:ArenaMode\.(?:Client|Server|Shared|Gate|Session|BigBrainCompat|Tests)|assets)[\\/][A-Za-z0-9_.\-\\/]*'
$ArenaProductionProjects = [ordered]@{
    'ArenaMode.Client' = 'ArenaMode.Client/ArenaMode.Client.csproj'; 'ArenaMode.Server' = 'ArenaMode.Server/ArenaMode.Server.csproj'
    'ArenaMode.Gate' = 'ArenaMode.Gate/ArenaMode.Gate.csproj'; 'ArenaMode.Session' = 'ArenaMode.Session/ArenaMode.Session.csproj'
    'ArenaMode.BigBrainCompat' = 'ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj'; 'ArenaMode.Tests' = 'ArenaMode.Tests/ArenaMode.Tests.csproj'
}
function Get-ArenaProductionBuilds([string]$Text) {
    if ($Text -match '(?i)ArenaMode\.sln') { 'sln' }
    foreach ($name in $ArenaProductionProjects.Keys) { if ($Text -match ('(?i)' + [regex]::Escape($name) + '\.csproj')) { $name } }
    if ($Text -match '(?i)dotnet[''"\s]+test\b') { 'ArenaMode.Tests' }   # a filtered run of the suite
}

function Get-ArenaVerificationCacheRoot {
    if ($env:ARENA_VERIFICATION_CACHE) { return [IO.Path]::GetFullPath($env:ARENA_VERIFICATION_CACHE) }
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ArenaMode\verification-cache'
}

function Get-ArenaStringSha([string]$Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text)))
}

function Get-ArenaFileSha([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
}

function Invoke-ArenaGitLines([string]$Root, [string[]]$Arguments) {
    $previous = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    try { $all = @(& git -C $Root -c core.quotepath=false @Arguments 2>&1) }
    finally { [Console]::OutputEncoding = $previous }
    $errors = @($all | Where-Object { $_ -is [Management.Automation.ErrorRecord] } | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed in ${Root}: $($errors -join ' ')" }
    return @($all | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] })
}

function Split-ArenaNul([object[]]$Lines) {
    return @((($Lines | ForEach-Object { [string]$_ }) -join "`n") -split "`0" | ForEach-Object { $_.Trim("`n") } | Where-Object { $_ })
}

function Get-ArenaHarnessFolder([string]$Rel) {
    if ($Rel -match '^tools/([^/]+)/') { return $Matches[1] }
    return $null
}

function Test-ArenaRecord([string]$Rel) { return $Rel.StartsWith('docs/') -and -not $Rel.StartsWith('docs/licenses/') }

# Every file git tracks or would track, from disk: path (forward slashes) -> tracked flag, hash, size, time.
# A tracked file missing on disk is recorded as deleted.
function Get-ArenaRepoSnapshot([string]$Root) {
    $root = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $tracked = Split-ArenaNul (Invoke-ArenaGitLines $root @('ls-files', '-z', '--cached'))
    $untracked = Split-ArenaNul (Invoke-ArenaGitLines $root @('ls-files', '-z', '--others', '--exclude-standard'))
    $files = [Collections.Generic.SortedDictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($group in @(@{ Paths = $tracked; Tracked = $true }, @{ Paths = $untracked; Tracked = $false })) {
        foreach ($rel in $group.Paths) {
            $full = Join-Path $root $rel
            $info = [IO.FileInfo]::new($full)
            $files[$rel] = if ($info.Exists) {
                [pscustomobject]@{ Tracked = $group.Tracked; Hash = (Get-ArenaFileSha $full); Length = $info.Length; Ticks = $info.LastWriteTimeUtc.Ticks }
            } else { [pscustomobject]@{ Tracked = $group.Tracked; Hash = '<deleted>'; Length = -1; Ticks = 0 } }
        }
    }
    $head = (Invoke-ArenaGitLines $root @('rev-parse', 'HEAD') | Select-Object -First 1)
    return [pscustomobject]@{ Root = $root; Head = [string]$head; Files = $files }
}

# The part of a file's text whose words count as mentions (see the header).
function Get-ArenaMentionText([string]$Text, [string]$Extension) {
    switch ($Extension) {
        '.cs' { return (([regex]::Matches($Text, $ArenaCSharpStringPattern) | ForEach-Object { $_.Value }) -join "`n") }
        { $_ -in '.csproj', '.props', '.targets', '.xml' } { return [regex]::Replace($Text, '(?s)<!--.*?-->', '') }
        { $_ -in '.ps1', '.psm1' } { return [regex]::Replace([regex]::Replace($Text, '(?s)<#.*?#>', ''), '(?m)^\s*#.*$', '') }
        default { return $Text }
    }
}

# What one text file names and does. A pure function of the text, the extension and the folder-name set,
# so the result is cached by content hash.
function Get-ArenaTextFacts([string]$Text, [string]$Extension, [regex]$FolderRegex, [regex]$LooseRegex) {
    $words = Get-ArenaMentionText $Text $Extension
    $mentions = @(if ($FolderRegex) {
        $FolderRegex.Matches($words) | ForEach-Object {
            $rest = $_.Groups['rest'].Value -replace '\\', '/'
            if ($rest -match '^/[A-Za-z0-9_.\-/]*[A-Za-z0-9_\-]\.[A-Za-z0-9]{1,12}$') { $_.Groups['folder'].Value + $rest } else { $_.Groups['folder'].Value }
        } | Sort-Object -Unique
    })
    return [pscustomobject]@{
        Mentions = $mentions
        Loose = @(if ($LooseRegex) { $LooseRegex.Matches($words) | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique })
        DocPaths = @([regex]::Matches($words, $ArenaDocsPathPattern) | ForEach-Object { ($_.Value -replace '\\', '/').TrimEnd('/', '.') } | Sort-Object -Unique)
        MdNames = @([regex]::Matches($words, $ArenaMarkdownNamePattern) | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique)
        DocsQuoted = $words -match $ArenaDocsQuotedPattern
        NamesDocsOrTools = $words -match $ArenaDocsOrToolsPattern
        Enumerates = $Text -match $ArenaEnumerationPattern
        Git = $Text -match $ArenaGitPattern
        # The whole text: a C# path built as "BepInEx", "config" spans two literals.
        Install = @(foreach ($pattern in $ArenaInstallStateSubtrees.Keys) { if ($Text -match "(?i)$pattern") { $pattern } })
        ProductionPaths = @(@([regex]::Matches($words, $ArenaProductionPathPattern) | ForEach-Object { ($_.Value -replace '\\', '/').TrimEnd('/', '.') }) +
            @([regex]::Matches($Text, $ArenaSegmentPathPattern) | ForEach-Object { $_.Groups[1].Value + '/' + (([regex]::Matches($_.Groups[2].Value, '[A-Za-z0-9_.\-]+') | ForEach-Object Value) -join '/') }) | Sort-Object -Unique)
        ArtifactPaths = @([regex]::Matches($words, $ArenaArtifactPathPattern) | ForEach-Object { ($_.Value -replace '\\', '/').TrimEnd('/', '.').ToLowerInvariant() } | Sort-Object -Unique)
        ProductionBuilds = @(Get-ArenaProductionBuilds $Text)
        Externals = @(@([regex]::Matches($words, $ArenaAbsolutePathPattern) | ForEach-Object { 'abs|' + $_.Value.TrimEnd('.', '\') }) +
            @([regex]::Matches($words, $ArenaQuotedAbsolutePathPattern) | ForEach-Object { 'abs|' + $_.Value.TrimEnd('.', '\', ' ') }) +
            @([regex]::Matches($words, $ArenaEscapePathPattern) | ForEach-Object { 'rel|' + $_.Value.TrimEnd('.') }) | Sort-Object -Unique)
    }
}

# Stat manifest of a file or a directory tree (relative path, size, write time), memoised per run. With
# -HashSmall a file up to 256 KB counts by its bytes instead of its write time: mods and the server rewrite
# their small settings files with the same content on every start (PitFireTeam's settings.json, BepInEx
# configs), which voided every key without changing anything a harness reads. The byte hashes are kept
# across runs of the memo by path, size and write time.
$script:ArenaManifestMemo = @{}
$script:ArenaSmallHashMemo = @{}
function Get-ArenaStatManifest([string]$Path, [string]$Filter = '*', [int]$Cap = 50000, [string[]]$SkipExtensions = @(), [string]$SkipPattern = '', [switch]$HashSmall) {
    $memoKey = "$Path|$Filter|$Cap|$($SkipExtensions -join ',')|$SkipPattern|$HashSmall"
    if ($script:ArenaManifestMemo.ContainsKey($memoKey)) { return $script:ArenaManifestMemo[$memoKey] }
    $result = if ([IO.File]::Exists($Path)) { $i = [IO.FileInfo]::new($Path); "file|$($i.Length)|$($i.LastWriteTimeUtc.Ticks)" }
    elseif (-not [IO.Directory]::Exists($Path)) { '<absent>' }
    else {
        $lines = [Collections.Generic.List[string]]::new()
        $options = [IO.EnumerationOptions]::new(); $options.RecurseSubdirectories = $Filter -eq '*'; $options.IgnoreInaccessible = $true; $options.AttributesToSkip = 0
        $over = $false
        foreach ($f in [IO.DirectoryInfo]::new($Path).EnumerateFiles($Filter, $options)) {
            if ($SkipExtensions -contains $f.Extension.ToLowerInvariant()) { continue }
            $rel = $f.FullName.Substring($Path.Length)
            if ($SkipPattern -and $rel -match $SkipPattern) { continue }
            if ($HashSmall -and $f.Length -le 262144) {
                $stamp = "$($f.Length)|$($f.LastWriteTimeUtc.Ticks)"
                $known = $script:ArenaSmallHashMemo[$f.FullName]
                if (-not $known -or $known.Stamp -ne $stamp) {
                    $hash = try { Get-ArenaFileSha $f.FullName } catch { "<unreadable $stamp>" }
                    $known = @{ Stamp = $stamp; Hash = $hash }; $script:ArenaSmallHashMemo[$f.FullName] = $known
                }
                $lines.Add("$rel|$($f.Length)|#$($known.Hash)")
            }
            else { $lines.Add("$rel|$($f.Length)|$($f.LastWriteTimeUtc.Ticks)") }
            if ($lines.Count -gt $Cap) { $over = $true; break }
        }
        if ($over) { "<over $Cap files>" } else { $lines.Sort([StringComparer]::Ordinal); Get-ArenaStringSha ($lines -join "`n") }
    }
    $script:ArenaManifestMemo[$memoKey] = $result
    return $result
}
function Reset-ArenaStatManifestMemo { $script:ArenaManifestMemo = @{} }

# The model of one checkout: snapshot, folders, per-file facts, the records each text names, and the shared
# reach that every step starts from. Built once per run.
function Get-ArenaInputModel([string]$Root, [string[]]$InstallRoots = @()) {
    $snapshot = Get-ArenaRepoSnapshot $Root
    $folderFiles = @{}
    $shared = [Collections.Generic.List[string]]::new()
    $records = [Collections.Generic.List[string]]::new()
    $byBase = @{}; $baseCount = @{}; $index = @{}
    foreach ($rel in $snapshot.Files.Keys) {
        $index[$rel.ToLowerInvariant()] = $rel
        $b = [IO.Path]::GetFileName($rel).ToLowerInvariant(); $baseCount[$b] = 1 + [int]$baseCount[$b]
        $f = Get-ArenaHarnessFolder $rel
        if ($f) { if (-not $folderFiles.ContainsKey($f)) { $folderFiles[$f] = [Collections.Generic.List[string]]::new() }; $folderFiles[$f].Add($rel) }
        elseif (Test-ArenaRecord $rel) { $records.Add($rel); $byBase[$b] = $rel }
        else { $shared.Add($rel) }
    }
    $folderNames = @($folderFiles.Keys | Sort-Object)
    $loose = @($shared | Where-Object { $_ -match '^tools/[^/]+$' })
    $looseRegex = if ($loose.Count) {
        [regex]::new('(?i)(?<![A-Za-z0-9_.\-])(?:' + (($loose | ForEach-Object { [regex]::Escape([IO.Path]::GetFileName($_)) }) -join '|') + ')(?![A-Za-z0-9_])', 'Compiled')
    } else { $null }
    $folderRegex = if ($folderNames.Count) {
        [regex]::new('(?<![A-Za-z0-9_])(?<folder>' + (($folderNames | Sort-Object Length -Descending | ForEach-Object { [regex]::Escape($_) }) -join '|') +
            ')(?![A-Za-z0-9_])(?<rest>[\\/][A-Za-z0-9_.\-\\/]+)?', 'Compiled, IgnoreCase')
    } else { $null }
    $nameSetHash = Get-ArenaStringSha (($folderNames -join '|') + '|' + ($loose -join '|') + '|' + $ArenaVerificationKeyVersion + '|' + $ArenaFactsVersion)
    $scanPath = Join-Path (Get-ArenaVerificationCacheRoot) "scan\$nameSetHash.json"
    $scanCache = @{}
    if (Test-Path -LiteralPath $scanPath) {
        try { foreach ($e in (Get-Content -Raw -LiteralPath $scanPath | ConvertFrom-Json -AsHashtable).GetEnumerator()) { $scanCache[$e.Key] = [pscustomobject]$e.Value } } catch { $scanCache = @{} }
    }
    $facts = @{}
    $dirty = $false
    foreach ($rel in $snapshot.Files.Keys) {
        $entry = $snapshot.Files[$rel]
        $ext = [IO.Path]::GetExtension($rel).ToLowerInvariant()
        if ($entry.Hash -eq '<deleted>' -or $ext -eq '.md' -or $ext -notin $ArenaScannedExtensions) { continue }
        $scanKey = "$($entry.Hash)$ext"
        if ($scanCache.ContainsKey($scanKey)) { $facts[$rel] = $scanCache[$scanKey]; continue }
        $fact = Get-ArenaTextFacts ([IO.File]::ReadAllText((Join-Path $snapshot.Root $rel))) $ext $folderRegex $looseRegex
        $scanCache[$scanKey] = $fact; $facts[$rel] = $fact; $dirty = $true
    }
    if ($dirty) {
        try {
            $tmp = "$scanPath.$PID.tmp"
            [IO.Directory]::CreateDirectory((Split-Path $scanPath)) | Out-Null
            $scanCache | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $tmp -Encoding utf8NoBOM
            Move-Item -LiteralPath $tmp -Destination $scanPath -Force
        } catch { }   # the scan cache only saves time
    }
    # The records each text names: a docs/ path (a directory names everything below it) or the unique file
    # name of a markdown record.
    $recordsLower = @($records | ForEach-Object { $_.ToLowerInvariant() })
    $named = @{}
    foreach ($rel in $facts.Keys) {
        $fact = $facts[$rel]
        if ($rel -in $ArenaKeyInfrastructureFiles) { continue }
        if (-not $fact.DocPaths.Count -and -not $fact.MdNames.Count) { continue }
        $set = [Collections.Generic.List[string]]::new()
        foreach ($p in $fact.DocPaths) {
            # A token names every record it is a prefix of: a directory names what is below it, a partial
            # name ('docs/RELEASE-' + version) every file it could become.
            $pl = $p.ToLowerInvariant()
            if ($pl -eq 'docs' -and $rel -in $ArenaPayloadLayoutFiles) { continue }
            for ($i = 0; $i -lt $records.Count; $i++) { if ($recordsLower[$i].StartsWith($pl)) { $set.Add($records[$i]) } }
        }
        foreach ($m in $fact.MdNames) { if ($baseCount[$m] -eq 1 -and $byBase.ContainsKey($m)) { $set.Add($byBase[$m]) } }
        if ($set.Count) { $named[$rel] = @($set | Sort-Object -Unique) }
    }
    # Content hash per folder and per shared/record file, so a step key is a few joins.
    $lineOf = { param($rel) $e = $snapshot.Files[$rel]; "$rel|$(if ($e.Tracked) { 'T' } else { 'U' })|$($e.Hash)" }
    $folderHash = @{}
    foreach ($f in $folderNames) { $folderHash[$f] = Get-ArenaStringSha (($folderFiles[$f] | ForEach-Object { & $lineOf $_ }) -join "`n") }
    $model = [pscustomobject]@{
        Snapshot = $snapshot; FolderNames = $folderNames; FolderFiles = $folderFiles; FolderHash = $folderHash
        FolderCanon = (& { $c = @{}; foreach ($f in $folderNames) { $c[$f.ToLowerInvariant()] = $f }; $c })
        SharedFiles = $null; SharedHash = $null; LooseFiles = $loose; LooseEntries = @()
        Records = @($records); Facts = $facts; NamedRecords = $named; Index = $index
        SharedReach = $null; SharedWide = $false; SharedWideReason = $null
        SharedRecordNamers = @{}; SharedExternals = @(); ExternalMemo = @{}
    }
    # A loose tools/* file is shared infrastructure when something outside tools/ or any harness file reaches
    # it (a dot-sourced helper, Write-ArenaAssets from the client project). The others are entry scripts:
    # steps of their own, in their own key and in the key of whatever names them, not in every key.
    $nonLoose = @(@($shared | Where-Object { $_ -notin $loose }) + @($folderFiles.Values | ForEach-Object { $_ }))
    $fromAll = Get-ArenaReach $model $nonLoose @()
    $model.LooseEntries = @($loose | Where-Object { -not $fromAll.Active.Contains($_) })
    $shared = @($shared | Where-Object { $_ -notin $model.LooseEntries })
    $model.SharedFiles = $shared
    $model.SharedHash = Get-ArenaStringSha (($shared | ForEach-Object { & $lineOf $_ }) -join "`n")
    foreach ($rel in $shared) {
        $fact = $facts[$rel]
        # Shared helper scripts make a step wide only when the step reaches them (Get-ArenaStepScope); the xunit
        # sources run inside every step that runs the suite.
        if (-not $fact -or -not $rel.StartsWith('ArenaMode.Tests/') -or $rel -in $ArenaPayloadLayoutFiles) { continue }
        if ($fact.Enumerates -and $fact.NamesDocsOrTools) { $model.SharedWide = $true; $model.SharedWideReason = "$rel enumerates and names docs or tools"; break }
    }
    # The folders the shared files reach are in every key; the reached files name records and externals for
    # every step. Their own enumeration and git calls do not make every step wide or HEAD-bound: shared code
    # runs the same for every step, and the reviewed payload files above build payloads.
    $model.SharedReach = Get-ArenaReach $model @($shared) @()
    $sharedTexts = @(@($shared) + @($model.SharedReach.Active) | Sort-Object -Unique)
    foreach ($rel in $sharedTexts) {
        foreach ($record in @($named[$rel])) { if ($record) { if (-not $model.SharedRecordNamers.ContainsKey($record)) { $model.SharedRecordNamers[$record] = [Collections.Generic.List[string]]::new() }; $model.SharedRecordNamers[$record].Add($rel) } }
    }
    $model.SharedExternals = @($sharedTexts | ForEach-Object { Resolve-ArenaExternals $model $_ } | Sort-Object -Unique)
    return $model
}

function Test-ArenaProductionFile([string]$Rel) {
    foreach ($p in $ArenaProductionPrefixes) { if ($Rel.StartsWith($p)) { return $true } }
    return $false
}

function Resolve-ArenaExternals($Model, [string]$Rel) {
    if ($Model.ExternalMemo.ContainsKey($Rel)) { return $Model.ExternalMemo[$Rel] }
    $fact = $Model.Facts[$Rel]
    $ext = [IO.Path]::GetExtension($Rel).ToLowerInvariant()
    $counts = $Rel.StartsWith('tools/') -or $ext -in '.csproj', '.props', '.targets', '.sln'
    $paths = @(if ($fact -and $counts) {
        $dir = Split-Path -Parent (Join-Path $Model.Snapshot.Root $Rel)
        foreach ($token in $fact.Externals) {
            $kind, $value = $token -split '\|', 2
            try { if ($kind -eq 'abs') { [IO.Path]::GetFullPath($value) } else { [IO.Path]::GetFullPath((Join-Path $dir $value)) } } catch { }
        }
    })
    $Model.ExternalMemo[$Rel] = $paths
    return $paths
}

# Breadth-first over mentions. Active files may run or be read; their mentions are followed and their facts
# count. A folder named without a file activates all its files; a named file activates itself (or, when it
# is not in the checkout, its whole folder). Every touched folder is content.
function Get-ArenaReach($Model, [string[]]$StartFiles, [string[]]$StartFolders) {
    $active = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $content = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $queue = [Collections.Generic.Queue[string]]::new()
    foreach ($rel in $StartFiles) { if ($active.Add($rel)) { $queue.Enqueue($rel) } }
    foreach ($f in $StartFolders) { [void]$content.Add($f); foreach ($rel in @($Model.FolderFiles[$f])) { if ($active.Add($rel)) { $queue.Enqueue($rel) } } }
    while ($queue.Count) {
        $next = $queue.Dequeue()
        if ($next -in $ArenaKeyInfrastructureFiles) { continue }   # they name files only to state these rules
        $fact = $Model.Facts[$next]
        if (-not $fact) { continue }
        foreach ($name in $fact.Loose) { $l = $Model.Index["tools/$name"]; if ($l -and $active.Add($l)) { $queue.Enqueue($l) } }
        foreach ($m in $fact.Mentions) {
            $slash = $m.IndexOf('/')
            $folder = if ($slash -lt 0) { $m } else { $m.Substring(0, $slash) }
            $folder = $Model.FolderCanon[$folder.ToLowerInvariant()]   # the folder's own spelling (NTFS ignores case)
            if (-not $folder) { continue }
            if (-not $Model.FolderFiles.ContainsKey($folder)) { continue }
            [void]$content.Add($folder)
            $exact = if ($slash -ge 0) { $Model.Index[("tools/$folder" + $m.Substring($slash)).ToLowerInvariant()] } else { $null }
            if ($exact) { if ($active.Add($exact)) { $queue.Enqueue($exact) } }
            else { foreach ($rel in $Model.FolderFiles[$folder]) { if ($active.Add($rel)) { $queue.Enqueue($rel) } } }
        }
    }
    return [pscustomobject]@{ Active = $active; Content = $content }
}

# What a step's key covers. $HomeFolder is the step's tools/<Folder> (empty for the xunit suite and loose
# tools/*.ps1 scripts); $ExtraFiles are loose scripts the step runs directly.
function Get-ArenaStepScope($Model, [string]$HomeFolder, [string[]]$ExtraFiles = @()) {
    $reach = Get-ArenaReach $Model @($ExtraFiles) @(if ($HomeFolder) { $HomeFolder })
    $wide = $Model.SharedWide; $wideReason = $Model.SharedWideReason
    $git = $false; $docsAll = $false
    $install = [Collections.Generic.HashSet[string]]::new()
    $namers = @{}
    foreach ($e in $Model.SharedRecordNamers.GetEnumerator()) { $namers[$e.Key] = [Collections.Generic.List[string]]::new($e.Value) }
    $externals = [Collections.Generic.HashSet[string]]::new([string[]]$Model.SharedExternals, [StringComparer]::OrdinalIgnoreCase)
    $artifacts = [Collections.Generic.HashSet[string]]::new()
    foreach ($rel in $reach.Active) {
        $fact = $Model.Facts[$rel]
        if (-not $fact) { continue }
        $payload = $rel -in $ArenaPayloadLayoutFiles
        if ($fact.Enumerates -and -not $payload -and -not $wide) { $wide = $true; $wideReason = "$rel enumerates directories" }
        if ($fact.Git) { $git = $true }
        if ($fact.DocsQuoted -and -not $payload) { $docsAll = $true }
        foreach ($i in $fact.Install) { [void]$install.Add($i) }
        foreach ($record in @($Model.NamedRecords[$rel])) { if ($record) { if (-not $namers.ContainsKey($record)) { $namers[$record] = [Collections.Generic.List[string]]::new() }; $namers[$record].Add($rel) } }
        foreach ($p in (Resolve-ArenaExternals $Model $rel)) { [void]$externals.Add($p) }
        foreach ($token in @($fact.ArtifactPaths)) {
            foreach ($prefix in $ArenaArtifactInputs) { if ($token.StartsWith($prefix) -or $prefix.StartsWith($token)) { [void]$artifacts.Add($prefix) } }
        }
    }
    $folders = if ($wide) { @($Model.FolderNames) } else { @(@($reach.Content) + @($Model.SharedReach.Content) | Sort-Object -Unique) }
    $recordSet = if ($wide -or $docsAll) { @($Model.Records) } else {
        @(foreach ($record in $namers.Keys) {
            $payloadOnly = $ArenaPayloadOnlyRecordNamers[$record]
            if ($payloadOnly -and @($namers[$record] | Where-Object { $_ -notin $payloadOnly }).Count -eq 0) { continue }
            $record
        }) | Sort-Object
    }
    return [pscustomobject]@{
        Folders = @($folders); OwnFolders = @($reach.Content | Sort-Object); Records = @($recordSet); Wide = $wide; WideReason = $wideReason
        LooseFiles = @($reach.Active | Where-Object { $_ -in $Model.LooseEntries } | Sort-Object)
        GitSensitive = $git; InstallState = @($install | Sort-Object); ExternalPaths = @($externals | Sort-Object); Active = @($reach.Active)
        ArtifactInputs = @($artifacts | Sort-Object)
    }
}

# The repository files a scope covers (for the after-run check and the worker sync).
function Get-ArenaScopeFiles($Model, $Scope) {
    $files = [Collections.Generic.List[string]]::new([string[]]$Model.SharedFiles)
    foreach ($f in $Scope.Folders) { $files.AddRange([string[]]$Model.FolderFiles[$f]) }
    $files.AddRange([string[]]$Scope.Records)
    $files.AddRange([string[]]$Scope.LooseFiles)
    return $files
}

function Get-ArenaGitCheckoutIdentity([string]$Root) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) { return "<no git: $(Get-ArenaStatManifest $Root)>" }
    $head = Invoke-ArenaGitLines $Root @('rev-parse', 'HEAD')
    $status = Invoke-ArenaGitLines $Root @('status', '--porcelain=v1', '--untracked-files=all')
    $dirty = foreach ($line in $status) {
        $rel = ([string]$line).Substring(3) -replace '^.* -> ', '' -replace '^"|"$', ''
        $full = Join-Path $Root $rel
        if ([IO.File]::Exists($full)) { "$rel|$(Get-ArenaFileSha $full)" } else { "$rel|<absent>" }
    }
    return Get-ArenaStringSha ((@($head) + @($status) + @($dirty)) -join "`n")
}

function Get-ArenaGitCheckoutIdentityMemo([string]$Root) {
    $memoKey = "git|$Root"
    if (-not $script:ArenaManifestMemo.ContainsKey($memoKey)) { $script:ArenaManifestMemo[$memoKey] = Get-ArenaGitCheckoutIdentity $Root }
    return $script:ArenaManifestMemo[$memoKey]
}

function Get-ArenaInstallRoots([string[]]$Roots) {
    return @($Roots | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') } | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)
}

# Runtime state that mods and the server write into their installed folders while the game or the server runs
# (here or on a GPU host, mirrored by Syncthing): logs, the APBS web root, Redeploy's per-profile data, the
# PitFireTeam teammate databases, SAIN's editor defaults. No offline harness reads them (checked with
# `git grep -n -i -E 'teammates|EditorDefaults|wwwroot|pmc-scav|Redeploy.data' tools ArenaMode.Tests`: only words
# in assertions), and they change every few minutes during a test day, so they are not in the fingerprint.
$ArenaInstallRuntimeStatePattern = '(?i)(^|\\)logs(\\|$)|\.log$|(^|\\)wwwroot\\|^\\?Redeploy\\data\\|\\Resources\\teammates[^\\]*\\[^\\]+\.db$|\\EditorDefaults\.json$'

# The byte hashes of small installed files survive the process (by path, size and write time), so a run does
# not hash thousands of ArenaMapPort textures again.
$script:ArenaSmallHashLoaded = $false
function Sync-ArenaSmallHashStore([switch]$Save) {
    $path = Join-Path (Get-ArenaVerificationCacheRoot) 'small-file-hashes.json'
    if (-not $Save) {
        if ($script:ArenaSmallHashLoaded) { return }
        $script:ArenaSmallHashLoaded = $true
        if (Test-Path -LiteralPath $path) {
            try { foreach ($e in (Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable).GetEnumerator()) { if (-not $script:ArenaSmallHashMemo.ContainsKey($e.Key)) { $script:ArenaSmallHashMemo[$e.Key] = @{ Stamp = $e.Value.Stamp; Hash = $e.Value.Hash } } } } catch { }
        }
        $script:ArenaSmallHashCount = $script:ArenaSmallHashMemo.Count
        return
    }
    if ($script:ArenaSmallHashMemo.Count -eq $script:ArenaSmallHashCount) { return }
    try { Write-ArenaAtomic $path ($script:ArenaSmallHashMemo | ConvertTo-Json -Depth 3 -Compress); $script:ArenaSmallHashCount = $script:ArenaSmallHashMemo.Count } catch { }
}

# Everything outside the checkout that every step may read, one "label|identity" line each (the labels name
# what moved in the miss report).
function Get-ArenaExternalFingerprint([string]$RepoRoot, [string[]]$InstallRoots) {
    Sync-ArenaSmallHashStore
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($root in (Get-ArenaInstallRoots $InstallRoots)) {
        $lines.Add("install|$root|managed|$(Get-ArenaStatManifest (Join-Path $root 'EscapeFromTarkov_Data\Managed'))")
        foreach ($sub in 'BepInEx\core', 'BepInEx\plugins', 'BepInEx\patchers', 'SPT_Runtime\SPT_Data', 'SPT_Runtime\user\mods') {
            $lines.Add("install|$root|$sub|$(Get-ArenaStatManifest (Join-Path $root $sub) -SkipPattern $ArenaInstallRuntimeStatePattern -HashSmall)")
        }
        foreach ($filter in '*.dll', '*.exe', '*.json') { $lines.Add("install|$root|SPT_Runtime\$filter|$(Get-ArenaStatManifest (Join-Path $root 'SPT_Runtime') -Filter $filter)") }
    }
    $parent = Split-Path -Parent $RepoRoot
    $lines.Add("sibling|Redeploy|$(Get-ArenaGitCheckoutIdentity (Join-Path $parent 'Redeploy'))")
    foreach ($sibling in '_decompiled', '_reference') { $lines.Add("sibling|$sibling|$(Get-ArenaStatManifest (Join-Path $parent $sibling))") }
    $lines.Add("dotnet|$((& dotnet --version 2>$null) -join ';')|$((& dotnet --list-sdks 2>$null) -join ';')|$((& dotnet --list-runtimes 2>$null) -join ';')")
    $lines.Add("git|$((& git --version 2>$null) -join ';')")
    $lines.Add("pwsh|$($PSVersionTable.PSVersion)|os|$([Environment]::OSVersion.VersionString)")
    # Other tools some harnesses run (generators, audio, decompiler), by the file they resolve to.
    foreach ($tool in 'python', 'py', 'node', 'ffmpeg', 'ffprobe', 'ilspycmd', 'mono') {
        $command = Get-Command $tool -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        $lines.Add("tool|$tool|$(if ($command) { "$($command.Source)|$(Get-ArenaStatManifest $command.Source)" } else { '<none>' })")
    }
    # MSBuild and NuGet read these above the checkout and per user.
    $dir = Split-Path -Parent $RepoRoot
    while ($dir) {
        foreach ($name in 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config', 'nuget.config', 'global.json') {
            $candidate = Join-Path $dir $name
            if (Test-Path -LiteralPath $candidate) { $lines.Add("config|$candidate|$(Get-ArenaFileSha $candidate)") }
        }
        $dir = Split-Path -Parent $dir
    }
    $userNuGet = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'NuGet\NuGet.Config'
    if (Test-Path -LiteralPath $userNuGet) { $lines.Add("config|user NuGet|$(Get-ArenaFileSha $userNuGet)") }
    Sync-ArenaSmallHashStore -Save
    return [pscustomobject]@{ Hash = Get-ArenaStringSha ($lines -join "`n"); Lines = @($lines) }
}

# Environment variables a harness, the SDK or MSBuild reads (MSBuild turns every variable into a property,
# so the ones that matter are named by prefix), plus the machine name; the runner's own switches excluded.
# An empty variable counts as absent: MSBuild reads it as an undefined property and `if ($env:X)` as false.
function Get-ArenaEnvironmentFingerprint([string[]]$InstallRoots = @()) {
    # A variable naming a path inside an installation (SPT_DATABASE, SPT_RUNTIME, set by the runner) is the
    # installed fingerprint's business, with its runtime-state rules; keying its whole content here would pull
    # the profiles and logs back into every key.
    $roots = @(Get-ArenaInstallRoots $InstallRoots)
    $names = [Environment]::GetEnvironmentVariables().Keys | Where-Object {
        $_ -match '^(ARENA_|SPT|MONO_|DOTNET_|MSBUILD|NUGET_|BEPINEX|COMPLUS_|CORECLR_|UNITY)' -and $_ -notmatch '^(ARENA_VERIFICATION_|ARENA_MUTANT_PARALLEL$)' -and
            [Environment]::GetEnvironmentVariable($_)
    } | Sort-Object
    $lines = @($names | ForEach-Object {
        $value = [Environment]::GetEnvironmentVariable($_)
        # A variable that names a file or folder (ARENA_APBS_TEST_ROOT, SPT_REWARD_RECOVERY_BASELINE) keys its content.
        $inInstall = $false
        if ($value -match '^[A-Za-z]:\\') { $full = [IO.Path]::GetFullPath($value).TrimEnd('\'); foreach ($r in $roots) { if ($full -ieq $r -or $full.StartsWith($r + '\', [StringComparison]::OrdinalIgnoreCase)) { $inInstall = $true } } }
        $content = if ($value -match '^[A-Za-z]:\\' -and -not $inInstall -and (Test-Path -LiteralPath $value)) { '|' + (Get-ArenaStatManifest $value -Cap 50000 -HashSmall) } else { '' }
        "$_=$value$content"
    }) + @("COMPUTERNAME=$env:COMPUTERNAME")
    return Get-ArenaStringSha ($lines -join "`n")
}

# Installed state and external paths that only this step names.
function Get-ArenaScopeExternalLines($Scope, [string]$RepoRoot, [string[]]$InstallRoots) {
    $roots = Get-ArenaInstallRoots $InstallRoots
    $parent = Split-Path -Parent $RepoRoot
    $covered = @($roots) + @((Join-Path $parent 'Redeploy'), (Join-Path $parent '_decompiled'), (Join-Path $parent '_reference'))
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($pattern in $Scope.InstallState) {
        foreach ($root in $roots) {
            foreach ($sub in $ArenaInstallStateSubtrees[$pattern]) {
                $dir, $filter = $sub -split '\|', 2
                $lines.Add("state|$root|$sub|$(Get-ArenaStatManifest (Join-Path $root $dir) -Filter $(if ($filter) { $filter } else { '*' }) -HashSmall)")
            }
        }
    }
    foreach ($prefix in @($Scope.ArtifactInputs)) {
        $lines.Add("artifact|$prefix|$(Get-ArenaStatManifest (Join-Path $RepoRoot ($prefix -replace '/', '\')) -HashSmall)")
    }
    foreach ($path in $Scope.ExternalPaths) {
        $full = $path.TrimEnd('\')
        if ($full -ieq $RepoRoot -or $full.StartsWith($RepoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { continue }
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $isCovered = $false
        foreach ($c in $covered) {
            # An install root or one of its parents named as a default (D:\SPT41) is the fingerprint's business.
            if ($c -ieq $full -or $c.StartsWith($full + '\', [StringComparison]::OrdinalIgnoreCase)) { $isCovered = $true; break }
            if ($full.StartsWith($c + '\', [StringComparison]::OrdinalIgnoreCase)) {
                $isCovered = $true
                foreach ($u in $ArenaUncoveredInstallDirs) { $ud = Join-Path $c $u; if ($full -ieq $ud -or $full.StartsWith($ud + '\', [StringComparison]::OrdinalIgnoreCase)) { $isCovered = $false } }
                break
            }
        }
        if ($isCovered) { continue }
        # Another checkout (D:\ArenaMapPort) is fingerprinted like the Redeploy sibling: HEAD, status, dirty bytes.
        $identity = if ([IO.Directory]::Exists($full) -and (Test-Path -LiteralPath (Join-Path $full '.git'))) { 'git|' + (Get-ArenaGitCheckoutIdentityMemo $full) } else { Get-ArenaStatManifest $full -Cap 20000 }
        $lines.Add("external|$full|$identity")
    }
    return @($lines)
}

# The key of one step: its parts (for the miss report), the key, and whether the step is cacheable at all.
function Get-ArenaStepKey($Model, $Scope, [string]$StepId, [string]$RunnerArguments, [string]$EnvironmentHash, $External, [string[]]$InstallRoots) {
    $files = $Model.Snapshot.Files
    $folderPart = ($Scope.Folders | ForEach-Object { "$_|$($Model.FolderHash[$_])" }) -join "`n"
    $recordPart = (@($Scope.Records) + @($Scope.LooseFiles) | ForEach-Object { $e = $files[$_]; "$_|$(if ($e.Tracked) { 'T' } else { 'U' })|$($e.Hash)" }) -join "`n"
    $scopeExternal = Get-ArenaScopeExternalLines $Scope $Model.Snapshot.Root $InstallRoots
    $uncacheable = @(@($scopeExternal) + @($External.Lines) | Where-Object { $_ -match '<over \d+ files>' })
    $parts = [ordered]@{
        version = $ArenaVerificationKeyVersion
        step = $StepId
        arguments = $RunnerArguments
        shared = $Model.SharedHash
        folders = Get-ArenaStringSha $folderPart
        records = Get-ArenaStringSha $recordPart
        environment = $EnvironmentHash
        externals = $External.Hash
        stepExternals = Get-ArenaStringSha ($scopeExternal -join "`n")
        head = $(if ($Scope.GitSensitive) { $Model.Snapshot.Head } else { '' })
    }
    $key = Get-ArenaStringSha ((@($parts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })) -join "`n")
    # Per-line digests of the external parts, labelled by what they describe, so a miss can say what moved.
    $digest = @(foreach ($line in @($External.Lines) + @($scopeExternal)) {
        $fields = $line -split '\|'
        $label = if ($fields[0] -in 'install', 'state', 'external', 'sibling', 'tool', 'config', 'artifact') { ($fields | Select-Object -First 3) -join '|' } else { $fields[0] }
        $label + '#' + (Get-ArenaStringSha $line).Substring(0, 16)
    })
    return [pscustomobject]@{
        Key = $key; Parts = $parts; Cacheable = $uncacheable.Count -eq 0; ExternalDigest = $digest
        Why = $(if ($uncacheable.Count) { 'external input too large to fingerprint: ' + ($uncacheable[0] -replace '^external\|', '' -replace '\|<over.*$', '') } else { $null })
    }
}

# A changed or vanished file after a step means its key no longer describes what the step saw.
function Test-ArenaSnapshotStillValid($Snapshot, [string[]]$Paths) {
    foreach ($rel in $Paths) {
        $e = $Snapshot.Files[$rel]
        $full = Join-Path $Snapshot.Root $rel
        $info = [IO.FileInfo]::new($full)
        if ($e.Hash -eq '<deleted>') { if ($info.Exists) { return "$rel appeared" } else { continue } }
        if (-not $info.Exists) { return "$rel vanished" }
        if ($info.Length -ne $e.Length -or $info.LastWriteTimeUtc.Ticks -ne $e.Ticks) {
            if ((Get-ArenaFileSha $full) -ne $e.Hash) { return "$rel changed" }
        }
    }
    foreach ($rel in (Split-ArenaNul (Invoke-ArenaGitLines $Snapshot.Root @('ls-files', '-z', '--others', '--exclude-standard')))) {
        if (-not $Snapshot.Files.ContainsKey($rel)) { return "$rel appeared" }
    }
    foreach ($rel in (Split-ArenaNul (Invoke-ArenaGitLines $Snapshot.Root @('ls-files', '-z', '--cached')))) {
        if (-not $Snapshot.Files.ContainsKey($rel) -or -not $Snapshot.Files[$rel].Tracked) { return "$rel became tracked" }
    }
    if ((Invoke-ArenaGitLines $Snapshot.Root @('rev-parse', 'HEAD') | Select-Object -First 1) -ne $Snapshot.Head) { return 'HEAD moved' }
    return $null
}

function Get-ArenaCacheEntryPath([string]$Key) { return Join-Path (Get-ArenaVerificationCacheRoot) "entries\$($Key.Substring(0, 2))\$Key.json" }
function Get-ArenaLastPassPath([string]$StepId) { return Join-Path (Get-ArenaVerificationCacheRoot) ('last\' + ($StepId -replace '[^A-Za-z0-9]', '_') + '.json') }

function Get-ArenaCachedPass([string]$Key) {
    $path = Get-ArenaCacheEntryPath $Key
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { $entry = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json } catch { return $null }
    if ($entry.Key -ne $Key -or $entry.Verdict -ne 'PASS') { return $null }
    return $entry
}

function Write-ArenaAtomic([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory((Split-Path $Path)) | Out-Null
    $tmp = "$Path.$PID.$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($tmp, $Text, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}

function Save-ArenaCachedPass($KeyInfo, [string]$StepId, [double]$Seconds, [string]$LogPath, [string]$Checkout, [string]$Head, [string]$Detail = '') {
    $entryPath = Get-ArenaCacheEntryPath $KeyInfo.Key
    $logCopy = [IO.Path]::ChangeExtension($entryPath, '.log')
    [IO.Directory]::CreateDirectory((Split-Path $entryPath)) | Out-Null
    if ($LogPath -and (Test-Path -LiteralPath $LogPath)) { Copy-Item -LiteralPath $LogPath -Destination $logCopy -Force }
    $json = [ordered]@{ Key = $KeyInfo.Key; Step = $StepId; Verdict = 'PASS'; Finished = (Get-Date).ToString('o'); Seconds = [math]::Round($Seconds)
        Checkout = $Checkout; Head = $Head; Log = $logCopy; Detail = $Detail; Parts = $KeyInfo.Parts; Externals = @($KeyInfo.ExternalDigest) } | ConvertTo-Json -Depth 4
    Write-ArenaAtomic $entryPath $json
    Write-ArenaAtomic (Get-ArenaLastPassPath $StepId) $json
}

# Which parts of the key moved since the step's last PASS, for the verdict line of a step that runs again.
function Get-ArenaMissReason($KeyInfo, [string]$StepId) {
    $path = Get-ArenaLastPassPath $StepId
    if (-not (Test-Path -LiteralPath $path)) { return 'no earlier PASS' }
    try { $last = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json } catch { return 'no earlier PASS' }
    $changed = @(foreach ($p in $KeyInfo.Parts.GetEnumerator()) { if ([string]$last.Parts.($p.Key) -ne [string]$p.Value) { $p.Key } })
    if (-not $changed.Count) { return 'no earlier PASS' }
    $moved = Compare-ArenaExternalDigest @($last.Externals) $KeyInfo.ExternalDigest
    return 'changed since last PASS: ' + ($changed -join ', ') + $(if ($moved) { " ($moved)" } else { '' })
}

# The labels of external lines whose identity differs between two digests.
function Compare-ArenaExternalDigest([string[]]$Before, [string[]]$After) {
    if (-not @($Before | Where-Object { $_ }).Count) { return '' }
    $old = @{}
    foreach ($d in $Before) { if ($d) { $label, $hash = $d -split '#', 2; $old[$label] = $hash } }
    $labels = foreach ($d in $After) {
        $label, $hash = $d -split '#', 2
        if (-not $old.ContainsKey($label) -or $old[$label] -ne $hash) { $label }
        $old.Remove($label)
    }
    return (@($labels) + @($old.Keys) | Sort-Object -Unique) -join '; '
}

# ---------------------------------------------------------------------------------------------------------
# Selection (-ChangedSince). Finer than the key and only advisory: it answers "which registered steps check
# the files that changed", using what each step compiles (the evaluated MSBuild items of every project in its
# folders), the production paths its scripts name (mutant targets), its harness folders, the shared helpers it
# reaches and the records it names. A step that builds or tests a production project is selected by any
# production change. A miss here costs what a hand-made selection costs; it never makes a PASS stale, because
# the cache key above stays coarse.

function Get-ArenaRegisteredSteps([string]$Root) {
    $path = Join-Path $Root 'tools\verification-steps.txt'
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | Select-Object -Unique)
}

# Files that differ from $Since: committed since, staged, unstaged and untracked (forward slashes).
function Get-ArenaChangedFiles([string]$Root, [string]$Since) {
    $null = Invoke-ArenaGitLines $Root @('rev-parse', '--verify', "$Since^{commit}")
    # --no-renames: a renamed file is its old path (which a mutant anchor may still name) and its new one.
    $changed = @(Split-ArenaNul (Invoke-ArenaGitLines $Root @('diff', '--name-only', '--no-renames', '-z', $Since, '--'))) +
        @(Split-ArenaNul (Invoke-ArenaGitLines $Root @('ls-files', '-z', '--others', '--exclude-standard')))
    return @($changed | Sort-Object -Unique)
}

# The evaluated items of one project, as repository paths. Cached by the project's folder content, the list of
# files in the checkout (globs), the .props/.targets files and the SDK. The evaluation itself is a script block
# so the prefetch can run it in parallel runspaces.
$ArenaEvaluateProject = {
    param([string]$Full, [string]$Root, [string]$CachePath)
    $json = & dotnet msbuild $Full -getItem:Compile -getItem:None -getItem:Content -getItem:EmbeddedResource -getItem:ProjectReference -getItem:Reference -p:Configuration=Release -nologo 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { return $null }
    try { $o = $json | ConvertFrom-Json } catch { return $null }
    $prefix = $Root + '\'
    $found = foreach ($kind in 'Compile', 'None', 'Content', 'EmbeddedResource', 'ProjectReference', 'Reference') {
        foreach ($item in @($o.Items.$kind)) {
            $candidate = if ($kind -eq 'Reference') { if ($item.HintPath) { [IO.Path]::GetFullPath((Join-Path (Split-Path $Full) $item.HintPath)) } } else { $item.FullPath }
            if ($candidate -and $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $candidate.Substring($prefix.Length) -replace '\\', '/' }
        }
    }
    $items = @($found | Sort-Object -Unique)
    try {
        [IO.Directory]::CreateDirectory((Split-Path $CachePath)) | Out-Null
        $tmp = "$CachePath.$PID.$([Guid]::NewGuid().ToString('N')).tmp"
        [IO.File]::WriteAllText($tmp, (ConvertTo-Json -InputObject @($items) -Compress))
        Move-Item -LiteralPath $tmp -Destination $CachePath -Force
    } catch { }
    return $items
}

function Get-ArenaProjectItemsPath($Model, [string]$Project, [string]$SdkVersion) {
    if (-not $Model.PSObject.Properties['GlobHash']) {
        $Model | Add-Member -NotePropertyName GlobHash -NotePropertyValue (Get-ArenaStringSha ((@($Model.Snapshot.Files.Keys) -join "`n")))
        $Model | Add-Member -NotePropertyName PropsHash -NotePropertyValue (Get-ArenaStringSha ((@($Model.Snapshot.Files.Keys | Where-Object { $_ -match '\.(props|targets)$' } | ForEach-Object { "$_|$($Model.Snapshot.Files[$_].Hash)" })) -join "`n"))
    }
    $folder = Get-ArenaHarnessFolder $Project
    $content = if ($folder) { $Model.FolderHash[$folder] } else {
        # A production project: the contents of its own directory.
        $dir = ((Split-Path $Project -Parent) -replace '\\', '/') + '/'
        Get-ArenaStringSha ((@($Model.Snapshot.Files.Keys | Where-Object { $_.StartsWith($dir) } | ForEach-Object { "$_|$($Model.Snapshot.Files[$_].Hash)" })) -join "`n")
    }
    $key = Get-ArenaStringSha "$Project|$content|$($Model.GlobHash)|$($Model.PropsHash)|$SdkVersion"
    return Join-Path (Get-ArenaVerificationCacheRoot) "msbuild\$key.json"
}

function Get-ArenaProjectItems($Model, [string]$Project, [string]$SdkVersion) {
    if (-not $Model.PSObject.Properties['ProjectItemsMemo']) { $Model | Add-Member -NotePropertyName ProjectItemsMemo -NotePropertyValue @{} }
    if ($Model.ProjectItemsMemo.ContainsKey($Project)) { return $Model.ProjectItemsMemo[$Project] }
    $path = Get-ArenaProjectItemsPath $Model $Project $SdkVersion
    $items = $null
    if (Test-Path -LiteralPath $path) { try { $items = @(Get-Content -Raw -LiteralPath $path | ConvertFrom-Json) } catch { } }
    if ($null -eq $items) { $items = & $ArenaEvaluateProject (Join-Path $Model.Snapshot.Root $Project) $Model.Snapshot.Root $path }
    $Model.ProjectItemsMemo[$Project] = $items
    return $items
}

# Evaluates every project of the given folders that is not cached yet, eight at a time.
function Initialize-ArenaProjectItems($Model, [string[]]$Folders, [string]$SdkVersion) {
    $todo = @(foreach ($f in ($Folders | Sort-Object -Unique)) {
        foreach ($project in @($Model.FolderFiles[$f] | Where-Object { $_ -like '*.csproj' })) {
            $path = Get-ArenaProjectItemsPath $Model $project $SdkVersion
            if (-not (Test-Path -LiteralPath $path)) { [pscustomobject]@{ Full = (Join-Path $Model.Snapshot.Root $project); Path = $path } }
        }
    })
    if (-not $todo.Count) { return }
    $evaluate = $ArenaEvaluateProject.ToString()
    $root = $Model.Snapshot.Root
    $todo | ForEach-Object -ThrottleLimit 8 -Parallel { $null = & ([scriptblock]::Create($using:evaluate)) $_.Full $using:root $_.Path }
}

# What a step checks, for selection: exact paths and path prefixes with the kind of link, and whether any
# production change selects it.
function Get-ArenaSelectionInputs($Model, $Scope, [string]$SdkVersion) {
    $exact = @{}; $prefixes = [Collections.Generic.List[object]]::new(); $wideProduction = $false
    # Only what the step's own files reach: its folders, the helpers and scripts they run, the records they
    # name. What every step shares (production through the shared files, the no-deploy targets) selects
    # through the compile items and named paths below, not wholesale.
    foreach ($f in $Scope.OwnFolders) { foreach ($rel in $Model.FolderFiles[$f]) { if (-not $exact.ContainsKey($rel)) { $exact[$rel] = "harness folder $f" } } }
    foreach ($rel in $Scope.Active) {
        if (-not (Get-ArenaHarnessFolder $rel) -and -not $exact.ContainsKey($rel)) { $exact[$rel] = $(if ($rel -in $Scope.LooseFiles) { 'script it runs' } else { 'shared helper it runs' }) }
        foreach ($record in @($Model.NamedRecords[$rel])) { if ($record -and -not $exact.ContainsKey($record)) { $exact[$record] = "record named in $rel" } }
    }
    foreach ($f in $Scope.OwnFolders) {
        foreach ($project in @($Model.FolderFiles[$f] | Where-Object { $_ -like '*.csproj' })) {
            $items = Get-ArenaProjectItems $Model $project $SdkVersion
            if ($null -eq $items) { $wideProduction = $true; continue }
            foreach ($rel in $items) { if (-not $exact.ContainsKey($rel)) { $exact[$rel] = "compiled by $project" } }
        }
    }
    # A production project the step builds or tests: what that project compiles, plus its own directory.
    $built = [Collections.Generic.HashSet[string]]::new()
    foreach ($rel in $Scope.Active) {
        $fact = $Model.Facts[$rel]
        if (-not $fact -or (-not (Get-ArenaHarnessFolder $rel) -and $rel -notin $Scope.LooseFiles)) { continue }
        foreach ($b in @($fact.ProductionBuilds)) { [void]$built.Add($b) }
    }
    foreach ($b in $built) {
        $projects = if ($b -eq 'sln') { @($ArenaProductionProjects.Values) } elseif ($b -eq 'ArenaMode.Tests') { @($ArenaProductionProjects['ArenaMode.Tests'], $ArenaProductionProjects['ArenaMode.Server']) } else { @($ArenaProductionProjects[$b]) }
        foreach ($project in $projects) {
            $prefixes.Add([pscustomobject]@{ Prefix = ((Split-Path $project -Parent) -replace '\\', '/').ToLowerInvariant(); Kind = "builds $project" })
            $items = Get-ArenaProjectItems $Model $project $SdkVersion
            if ($null -eq $items) { $wideProduction = $true; continue }
            foreach ($rel in $items) { if (-not $exact.ContainsKey($rel)) { $exact[$rel] = "compiled by $project, which it builds" } }
        }
    }
    foreach ($rel in $Scope.Active) {
        $fact = $Model.Facts[$rel]
        if (-not $fact) { continue }
        if (-not (Get-ArenaHarnessFolder $rel) -and $rel -notin $Scope.LooseFiles) { continue }   # shared code names paths for every step
        foreach ($p in $fact.ProductionPaths) { $prefixes.Add([pscustomobject]@{ Prefix = $p.ToLowerInvariant(); Kind = "named in $rel" }) }
    }
    return [pscustomobject]@{ Exact = $exact; Prefixes = $prefixes; WideProduction = $wideProduction }
}

function Test-ArenaProductionPath([string]$Rel) { return (Test-ArenaProductionFile $Rel) -or $Rel.StartsWith('ArenaMode.Tests/') -or $Rel.StartsWith('assets/') }

# The registered steps that check at least one changed file, each with its first reasons.
function Select-ArenaSteps($Model, [string[]]$Steps, [string[]]$Changed) {
    $sdk = (& dotnet --version 2>$null) -join ''
    $plans = foreach ($step in $Steps) {
        $name = ($step -split ':', 2)[0]
        $homeFolder = $null; $extra = @()
        if ($name -like '*.ps1') { $rel = ($name -replace '\\', '/') -replace '^\./', ''; if ($rel -match '^tools/([^/]+)/') { $homeFolder = $Matches[1] } else { $extra = @($rel) } }
        else { $homeFolder = $name }
        [pscustomobject]@{ Step = $step; Scope = (Get-ArenaStepScope $Model $homeFolder $extra) }
    }
    Initialize-ArenaProjectItems $Model @($plans | ForEach-Object { $_.Scope.OwnFolders }) $sdk
    foreach ($project in $ArenaProductionProjects.Values) { $null = Get-ArenaProjectItems $Model $project $sdk }
    foreach ($plan in $plans) {
        $step = $plan.Step; $scope = $plan.Scope
        $inputs = Get-ArenaSelectionInputs $Model $scope $sdk
        $reasons = [Collections.Generic.List[string]]::new()
        foreach ($c in $Changed) {
            $why = $inputs.Exact[$c]
            if (-not $why) { $cl = $c.ToLowerInvariant(); foreach ($p in $inputs.Prefixes) { if ($cl -eq $p.Prefix -or $cl.StartsWith($p.Prefix + '/')) { $why = $p.Kind; break } } }
            if (-not $why -and $inputs.WideProduction -and (Test-ArenaProductionPath $c)) { $why = 'a project of its could not be evaluated' }
            if ($why) { $reasons.Add("$c ($why)") }
        }
        if ($reasons.Count) { [pscustomobject]@{ Step = $step; Reasons = @($reasons) } }
    }
}
