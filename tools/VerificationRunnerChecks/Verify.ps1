#Requires -Version 7.0
<#
Offline checks of tools/Run-ArenaVerification.ps1 itself: the result cache (a step whose inputs are unchanged
since its last PASS is not run again) and what it must never do (reuse a PASS after an input changed, cache a
FAIL, cache a step whose inputs changed while it ran).

Every case runs the real runner and the real input model, copied into a throwaway checkout under %TEMP% with a
toy production tree, toy harness folders, a toy installation, toy siblings (Redeploy, _decompiled, _reference)
and a private cache directory; nothing outside that temp directory is written. A toy harness appends its name
to a counter file each time it really runs, so "ran" and "cached" are counted, not read from the verdict text.

The baseline runs every case; then each fault below is applied to the copied runner or input model (never to
the files in this checkout), and its cases must fail at the named assertion. -Case runs the named cases only
(after the two priming cases), for a fix loop.
#>
param([string]$Fault, [string[]]$Case = @())
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
        Find = '    $lines = @($names | ForEach-Object { "$_=$([Environment]::GetEnvironmentVariable($_))" }) + @("COMPUTERNAME=$env:COMPUTERNAME")'
        Replace = '    $lines = @("COMPUTERNAME=$env:COMPUTERNAME")' },
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
        Find = '$afterRun = Test-ArenaStepStillValid $keyInfo $keyInfo.Scope $item $Snapshot'; Replace = '$afterRun = $null' },
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
        Find = "        if(`$result.Code -eq 0 -or -not `$result.Output.Contains('FAIL: '+`$result.Failure)) {"; Replace = '        if($false) {' },
    @{ Name = 'no-cache-ignored'; File = 'tools/Run-ArenaVerification.ps1'; Failure = '-NoCache runs the step'; Cases = @('no-cache')
        Find = '    if ($keyInfo -and $keyInfo.Cacheable -and -not $NoCache) {'; Replace = '    if ($keyInfo -and $keyInfo.Cacheable) {' }
)

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
        'tools/Assert-ArenaTestRun.ps1', 'tools/Read-ArenaInstalledSnapshot.ps1', 'tools/Invoke-ArenaSourceChecks.ps1',
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
Add-Content -LiteralPath $env:ARENA_RUNNER_CHECKS_COUNTER -Value ('ToyChecks' + $(if ($Stage) { ":$Stage" } else { '' }))
$null = Get-Content -Raw (Join-Path $repo 'ArenaMode.Client/Toy.cs')
$null = Get-Content -Raw (Join-Path $PSScriptRoot '../ReferencedChecks/Data.txt')
$null = Get-Content -Raw (Join-Path $repo 'docs/reviews/TOY-REVIEW.md')
if (Test-Path (Join-Path $repo 'ArenaMode.Client/fail.flag')) { 'FAIL: toy asked to fail'; exit 1 }
if ($env:ARENA_RUNNER_CHECKS_TOUCH) { Add-Content (Join-Path $repo 'ArenaMode.Client/Toy.cs') '// touched while running' }
'TOY_CHECKS_PASSED'
'@
    Write-Text (Join-Path $repo 'tools/ToyChecks/Verify.ps1') $toy
    Write-Text (Join-Path $repo 'tools/OtherChecks/Verify.ps1') "Add-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'OtherChecks'`n'OTHER_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/GitToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'GitToyChecks'`n`$null = & git -C (Join-Path `$PSScriptRoot '../..') rev-parse HEAD`n'GIT_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/ConfigToyChecks/Verify.ps1') "param([string]`$SptRoot)`nAdd-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'ConfigToyChecks'`n`$null = Join-Path `$SptRoot 'BepInEx\config'`n'CONFIG_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/Verify-LooseToy.ps1') "Add-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'tools/Verify-LooseToy.ps1'`n'LOOSE_TOY_PASSED'`n"
    # Selection: a project that links production through a glob (no literal path), a script that names a
    # mutant target, and the registry.
    Write-Text (Join-Path $repo 'tools/LinkToyChecks/LinkToyChecks.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>`n  <ItemGroup><Compile Include=`"../../ArenaMode.Client/To*.cs`" /></ItemGroup>`n</Project>`n"
    Write-Text (Join-Path $repo 'tools/LinkToyChecks/Program.cs') "System.Console.WriteLine(`"LINK_TOY`");`n"
    Write-Text (Join-Path $repo 'tools/LiteralToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'LiteralToyChecks'`n`$target = Join-Path (Join-Path `$PSScriptRoot '../..') 'ArenaMode.Client/Other.cs'`n'LITERAL_TOY_PASSED'`n"
    Write-Text (Join-Path $repo 'tools/verification-steps.txt') "# toy registry`nToyChecks`nOtherChecks`nLinkToyChecks`nLiteralToyChecks`nGitToyChecks`nConfigToyChecks`ntools/Verify-LooseToy.ps1`n"
    # Parallel workers: a toy that reports what its checkout holds, one that takes a while, one that deploys.
    $echo = @'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Add-Content -LiteralPath $env:ARENA_RUNNER_CHECKS_COUNTER -Value 'EchoToyChecks'
"ROOT=$repo"
"TOY=" + (Get-FileHash -LiteralPath (Join-Path $repo 'ArenaMode.Client/Toy.cs') -Algorithm SHA256).Hash
"NEW=" + (Test-Path -LiteralPath (Join-Path $repo 'ArenaMode.Client/New.cs'))
"OTHER=" + (Test-Path -LiteralPath (Join-Path $repo 'ArenaMode.Client/Other.cs'))
$null = & git -C $repo ls-files --error-unmatch ArenaMode.Client/Staged.cs 2>$null
"STAGED=$LASTEXITCODE"
'ECHO_TOY_PASSED'
'@
    Write-Text (Join-Path $repo 'tools/EchoToyChecks/Verify.ps1') $echo
    # SleepToyChecks can also undo a deploy before it ends (ARENA_RUNNER_CHECKS_RESTORE), as a restoring
    # harness or a person would: the change must still fail it, because it ran while the install was changed.
    Write-Text (Join-Path $repo 'tools/SleepToyChecks/Verify.ps1') "Add-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'SleepToyChecks'`nStart-Sleep -Seconds 8`nif (`$env:ARENA_RUNNER_CHECKS_RESTORE) { Remove-Item -LiteralPath `$env:ARENA_RUNNER_CHECKS_RESTORE -ErrorAction SilentlyContinue }`n'SLEEP_TOY_PASSED'`n"
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
Add-Content -LiteralPath $env:ARENA_RUNNER_CHECKS_COUNTER -Value 'MutantToyChecks'
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
    Write-Text (Join-Path $repo 'tools/DeployToyChecks/Verify.ps1') "param([string]`$SptRoot)`nAdd-Content -LiteralPath `$env:ARENA_RUNNER_CHECKS_COUNTER -Value 'DeployToyChecks'`n`$null = New-Item -ItemType Directory -Force (Join-Path `$SptRoot 'BepInEx/plugins/maschine-ArenaMode')`nSet-Content -LiteralPath (Join-Path `$SptRoot 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll') 'deployed'`n'DEPLOY_TOY_PASSED'`n"
    Invoke-Git $repo @('init', '-q', '-b', 'main'); Invoke-Git $repo @('add', '-A'); Invoke-Git $repo @('commit', '-q', '-m', 'sandbox')
    $redeploy = Join-Path $dev 'Redeploy'
    Write-Text (Join-Path $redeploy 'Redeploy.Core/Core.cs') "core 1`n"
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
function Invoke-Runner($Sandbox, [string[]]$Items, [switch]$NoCache, [hashtable]$Environment = @{}, [string[]]$Extra = @()) {
    $before = @(if (Test-Path -LiteralPath $Sandbox.Counter) { Get-Content -LiteralPath $Sandbox.Counter })
    $saved = @{}
    $env:ARENA_VERIFICATION_CACHE = $Sandbox.Cache
    $env:ARENA_VERIFICATION_INSTALL_ROOTS = $Sandbox.Spt
    $env:ARENA_RUNNER_CHECKS_COUNTER = $Sandbox.Counter
    foreach ($k in $Environment.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $Environment[$k]) }
    try {
        $arguments = @('-NoProfile', '-NonInteractive', '-File', (Join-Path $Sandbox.Repo 'tools/Run-ArenaVerification.ps1'), '-SkipUnitTests', '-SptRoot', $Sandbox.Spt)
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

function Edit-File($Sandbox, [string]$Rel, [string]$Append) { Add-Content -LiteralPath (Join-Path $Sandbox.Repo $Rel) -Value $Append }
function Edit-External([string]$Path, [string]$Append) { Add-Content -LiteralPath $Path -Value $Append }

# Expect one run of $Item: whether it really ran, and that it passed.
function Assert-Run($Result, [string]$Item, [bool]$Ran, [string]$Name, [bool]$Passed = $true) {
    $verdict = $Result.Verdicts[$Item]
    $runs = [int]$Result.Ran[$Item]
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
        Check ($r.Exit -eq 0 -and $r.Ran.Count -eq 0 -and $r.Output -match 'ARENA_VERIFICATION_SELECTION selected=2 of 7 registered') '-ListOnly runs nothing and counts the selection' $r.Output
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
        Check ([int]$r.Ran['LiteralToyChecks'] -eq 1 -and $r.Verdicts['LiteralToyChecks'].Passed) 'a selection without -ListOnly runs the selected steps' $r.Output }
    'helper' = { param($s)
        $r = Invoke-Runner $s 'MutantToyChecks' -NoCache
        Check ($r.Verdicts['MutantToyChecks'].Passed -and $r.Verdicts['MutantToyChecks'].Detail -match 'Source faults rejected: 2') 'killed mutants pass their harness' $r.Output
        foreach ($bad in 'survivor', 'broken', 'wrong-assertion') {
            $r = Invoke-Runner $s 'MutantToyChecks' -NoCache -Environment @{ ARENA_RUNNER_CHECKS_BADFAULT = $bad; ARENA_MUTANT_PARALLEL = '3' }
            $log = Get-Content -Raw -LiteralPath (Join-Path $s.Repo 'artifacts/run-arena-verification/MutantToyChecks.log')
            Check (-not $r.Verdicts['MutantToyChecks'].Passed -and $log -match "Fault was not killed by intended assertion: $bad") "a $bad mutant fails its harness" $log
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
$names = if ($Case.Count) { @('first', 'cached') + $Case } else { @($cases.Keys) }
Invoke-Cases $names $null
if ($Case.Count) { Write-Output "RUNNER_CASES_PASSED $($Case -join ',')"; exit 0 }
foreach ($definition in $faults) {
    $output = (& pwsh -NoProfile -NonInteractive -File $PSCommandPath -Fault $definition.Name 2>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains('FAIL: ' + $definition.Failure)) {
        Write-Output "Fault was not killed by intended assertion: $($definition.Name)`n$output"
        exit 1
    }
    Write-Output "fault rejected: $($definition.Name) -> $($definition.Failure)"
}
Write-Output "VERIFICATION_RUNNER_CHECKS_PASSED cases=$($cases.Count) faults=$($faults.Count)"
