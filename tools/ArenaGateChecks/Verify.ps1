# Offline checks of the Arena session gate (ArenaMode.Gate) against the real BepInEx core of the
# game, under the Mono that the game runs on.
#
#   pwsh -File tools/ArenaGateChecks/Verify.ps1 [-SptPath D:\SPT41] [-Only <scenario>] [-SkipMutants]
#
# What it does
#   1. builds ArenaMode.Gate with `dotnet build` (never deploying: the NoDeploy.targets override and
#      a SPTUserMods path that does not exist, like every other offline build of this repository),
#   2. compiles the driver (GateChecks.cs, with the shared Session sources linked in) and the
#      dummy preloader patcher template with the Roslyn of the .NET SDK, against the game's own
#      mscorlib,
#   3. runs one scenario per Mono process (the Mono of the matching Unity editor, MONO_PATH = the
#      game's Managed folder) on a fresh fake game folder under artifacts/gate-checks/<run>/,
#   4. builds deliberately broken copies of the gate ("mutants": edits of the gate sources, and of the shared
#      session sources the gate links) and requires that the checks fail for every one of them.
#
# Needs: the game (BepInEx\core, EscapeFromTarkov_Data\Managed, BepInEx\plugins for the real-install
# scenario), the .NET SDK, and the Unity editor 2022.3.x (MonoBleedingEdge). It reads the game,
# never writes to it. If a tool is missing the script says which and stops: it does not skip.
#
# The driver ends with Environment.Exit; under HarmonyX the Mono of the editor can still crash while
# shutting down (exit code -1073741819 after all results are printed). The result line decides.

param(
    [string]$SptPath = '',
    [string]$Only = '',
    [switch]$SkipMutants,
    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $SptPath) { $SptPath = [IO.Path]::GetFullPath((Join-Path $repo '../..')) }
$core = Join-Path $SptPath 'BepInEx\core'
$managed = Join-Path $SptPath 'EscapeFromTarkov_Data\Managed'
foreach ($required in @($core, $managed, (Join-Path $SptPath 'BepInEx\plugins'), (Join-Path $core 'BepInEx.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll'))) {
    if (-not (Test-Path $required)) { throw "Missing $required (use -SptPath for the game folder)." }
}

# ---------------------------------------------------------------------------- tools

function Find-UnityMono {
    if ($env:ARENA_GATE_MONO) {
        if (Test-Path $env:ARENA_GATE_MONO) { return $env:ARENA_GATE_MONO }
        throw "ARENA_GATE_MONO points at a file that does not exist: $env:ARENA_GATE_MONO"
    }
    $hub = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path $hub) {
        # The game runs on 2022.3.43; any other 2022.3 editor is accepted with a warning.
        $editors = Get-ChildItem $hub -Directory | Where-Object { $_.Name -like '2022.3.*' } | Sort-Object { $_.Name -ne '2022.3.43f1' }, Name
        foreach ($editor in $editors) {
            $mono = Join-Path $editor.FullName 'Editor\Data\MonoBleedingEdge\bin\mono.exe'
            if (Test-Path $mono) {
                if ($editor.Name -ne '2022.3.43f1') { Write-Warning "Using the Mono of Unity $($editor.Name); the game runs on 2022.3.43f1." }
                return $mono
            }
        }
    }
    throw 'No Unity 2022.3 editor with MonoBleedingEdge found. Install one or set ARENA_GATE_MONO to its mono.exe.'
}

function Find-Roslyn {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $root = Split-Path $dotnet -Parent
    $sdks = Get-ChildItem (Join-Path $root 'sdk') -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending
    foreach ($sdk in $sdks) {
        $csc = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
        if (Test-Path $csc) { return @{ Csc = $csc; Root = $root } }
    }
    throw 'No Roslyn csc.dll found in the installed .NET SDKs.'
}

# netstandard.dll and the facades next to it (mscorlib, System, ...): BepInEx.dll is built against
# mscorlib 2.0, which only the facade can satisfy.
function Find-NetStandardRef([string]$dotnetRoot) {
    $packs = Join-Path $dotnetRoot 'packs\NETStandard.Library.Ref'
    if (Test-Path $packs) {
        foreach ($version in (Get-ChildItem $packs -Directory | Sort-Object Name -Descending)) {
            $folder = Join-Path $version.FullName 'ref\netstandard2.1'
            if (Test-Path (Join-Path $folder 'netstandard.dll')) { return @(Get-ChildItem $folder -Filter '*.dll' | ForEach-Object { $_.FullName }) }
        }
    }
    throw 'The netstandard2.1 reference assemblies (NETStandard.Library.Ref) are not installed with the .NET SDK.'
}

$mono = Find-UnityMono
$roslyn = Find-Roslyn
$netstandard = Find-NetStandardRef $roslyn.Root
$csc = $roslyn.Csc

# ---------------------------------------------------------------------------- run folder

$runId = [Guid]::NewGuid().ToString('N')
$run = Join-Path $repo ('artifacts/gate-checks/' + $runId)
[IO.Directory]::CreateDirectory($run) | Out-Null
Write-Output "Evidence folder: $run"
Write-Output "Mono: $mono"

function Invoke-Native([string]$Name, [scriptblock]$Command, [string]$Log) {
    & $Command *> $Log
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE), see $Log`n$([IO.File]::ReadAllText($Log))" }
}

# ---------------------------------------------------------------------------- 1. the gate

$gateProject = Join-Path $repo 'ArenaMode.Gate\ArenaMode.Gate.csproj'
$noDeploy = Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets'
$mustNotExist = Join-Path $repo 'artifacts\nodeploy\must-not-exist'
Invoke-Native 'dotnet build ArenaMode.Gate' {
    & dotnet build $gateProject -c Release "-p:CustomAfterMicrosoftCommonTargets=$noDeploy" "-p:SPTUserMods=$mustNotExist" -warnaserror
} (Join-Path $run 'build-gate.log')
$gateDll = Join-Path $repo 'ArenaMode.Gate\bin\Release\maschine-ArenaMode.Gate.dll'
if (-not (Test-Path $gateDll)) { throw "The gate build produced no $gateDll" }
Write-Output "Built the gate: $gateDll"

# ---------------------------------------------------------------------------- 2. driver + template

$sharedSources = @('ArenaMiniJson.cs', 'ArenaWhitelistPolicy.cs', 'ArenaWhitelistDecision.cs', 'ArenaSessionContract.cs') |
    ForEach-Object { Join-Path $repo ('ArenaMode.Shared\Session\' + $_) }
$coreReferences = @('BepInEx.dll', 'BepInEx.Preloader.dll', '0Harmony.dll', 'Mono.Cecil.dll')

function New-DriverFolder([string]$Folder, [string]$Gate) {
    [IO.Directory]::CreateDirectory($Folder) | Out-Null
    foreach ($file in Get-ChildItem $core -Filter '*.dll') {
        if ($file.Name -match '^(BepInEx|Mono|0Harmony|HarmonyX)') { Copy-Item $file.FullName (Join-Path $Folder $file.Name) -Force }
    }
    $gateFolder = Join-Path $Folder 'gate'
    [IO.Directory]::CreateDirectory($gateFolder) | Out-Null
    Copy-Item $Gate (Join-Path $gateFolder 'maschine-ArenaMode.Gate.dll') -Force
}

$driver = Join-Path $run 'driver'
New-DriverFolder $driver $gateDll

$frameworkReferences = @("-r:$managed\mscorlib.dll", "-r:$managed\System.dll", "-r:$managed\System.Core.dll")
$coreArgs = $coreReferences | ForEach-Object { "-r:$driver\$_" }
Invoke-Native 'compile the dummy patcher template' {
    & dotnet $csc -nologo -nostdlib -noconfig -langversion:latest -debug:portable -target:library "-out:$driver\DummyPatcher.template.dll" @frameworkReferences "-r:$driver\Mono.Cecil.dll" (Join-Path $PSScriptRoot 'DummyPatcher.cs')
} (Join-Path $run 'compile-template.log')
Invoke-Native 'compile the driver' {
    & dotnet $csc -nologo -nostdlib -noconfig -langversion:latest -debug:portable -target:exe "-out:$driver\GateChecks.exe" @frameworkReferences @coreArgs (Join-Path $PSScriptRoot 'GateChecks.cs') @sharedSources
} (Join-Path $run 'compile-driver.log')
Write-Output 'Compiled the driver and the dummy patcher template.'

# ---------------------------------------------------------------------------- 3. scenarios

$policy = Join-Path $repo 'assets\arena-whitelist.json'
if (-not (Test-Path $policy)) { throw "Missing the shipped default policy $policy" }

function Invoke-Scenario([string]$DriverFolder, [string]$Scenario, [bool]$Flag, [string]$Evidence) {
    $work = Join-Path $Evidence 'work'
    [IO.Directory]::CreateDirectory($work) | Out-Null
    $log = Join-Path $Evidence ($Scenario + '.log')
    $errLog = Join-Path $Evidence ($Scenario + '.err')
    $arguments = @("`"$DriverFolder\GateChecks.exe`"", $Scenario, "`"$work`"", "`"$managed`"", "`"$policy`"", "`"$SptPath`"")
    if ($Flag) { $arguments += '-arena-session' }

    $env:MONO_PATH = $managed
    Remove-Item Env:MONO_LOG_LEVEL -ErrorAction SilentlyContinue
    Remove-Item Env:MONO_LOG_MASK -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $mono -ArgumentList $arguments -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError $errLog
    if (-not $process.WaitForExit(300000)) {
        $process.Kill()
        return @{ Ok = $false; Reason = 'timed out after 300 s'; Log = $log; Text = '' }
    }
    $text = if (Test-Path $log) { [IO.File]::ReadAllText($log) } else { '' }
    $match = [regex]::Match($text, 'RESULT ' + [regex]::Escape($Scenario) + ' passed=(\d+) failed=(\d+)')
    if (-not $match.Success) {
        return @{ Ok = $false; Reason = "no RESULT line (exit $($process.ExitCode))"; Log = $log; Text = $text }
    }
    $failed = [int]$match.Groups[2].Value
    if ($failed -ne 0) {
        return @{ Ok = $false; Reason = "$failed check(s) failed"; Log = $log; Text = $text; Passed = [int]$match.Groups[1].Value }
    }
    return @{ Ok = $true; Reason = ''; Log = $log; Text = $text; Passed = [int]$match.Groups[1].Value }
}

# name, flag. baseline-real must run before real-install.
$scenarios = @(
    @{ Name = 'inert'; Flag = $false },
    @{ Name = 'full'; Flag = $true },
    @{ Name = 'no-early-patcher'; Flag = $true },
    @{ Name = 'user-file'; Flag = $true },
    @{ Name = 'user-blacklist'; Flag = $true },
    @{ Name = 'default-deny'; Flag = $true },
    @{ Name = 'user-garbage'; Flag = $true },
    @{ Name = 'user-oversize'; Flag = $true },
    @{ Name = 'unidentified-patcher'; Flag = $true },
    @{ Name = 'gate-unlocated'; Flag = $true },
    @{ Name = 'bad-default'; Flag = $true },
    @{ Name = 'missing-default'; Flag = $true },
    @{ Name = 'no-core-module'; Flag = $true },
    @{ Name = 'postfix-failure'; Flag = $true },
    @{ Name = 'control-early-install'; Flag = $false },
    @{ Name = 'baseline-real'; Flag = $false },
    @{ Name = 'real-install'; Flag = $true }
)
if ($Only) {
    $needed = @($Only)
    if ($Only -eq 'real-install') { $needed = @('baseline-real', 'real-install') }
    $scenarios = @($scenarios | Where-Object { $needed -contains $_.Name })
    if ($scenarios.Count -eq 0) { throw "Unknown scenario '$Only'." }
}

$evidence = Join-Path $run 'scenarios'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$totalChecks = 0
$failedScenarios = @()
foreach ($scenario in $scenarios) {
    $result = Invoke-Scenario $driver $scenario.Name $scenario.Flag $evidence
    if ($result.Ok) {
        $totalChecks += $result.Passed
        Write-Output ("  scenario {0,-22} {1} checks passed" -f $scenario.Name, $result.Passed)
        # Informational lines the scenarios print (patcher order, real install contents)
        foreach ($line in ($result.Text -split "`r?`n" | Where-Object { $_ -match '^\s+info:' })) { Write-Output "    $($line.Trim())" }
    }
    else {
        $failedScenarios += $scenario.Name
        Write-Output ("  scenario {0,-22} FAILED: {1}" -f $scenario.Name, $result.Reason)
        foreach ($line in ($result.Text -split "`r?`n" | Where-Object { $_ -match '^\s+FAIL ' })) { Write-Output "    $($line.Trim())" }
        Write-Output "    log: $($result.Log)"
    }
}
if ($failedScenarios.Count -gt 0) { throw "Gate checks failed: $($failedScenarios -join ', ')" }

# ---------------------------------------------------------------------------- 4. mutants

function Edit-Source([string]$Folder, [string]$File, [string]$Find, [string]$Replace) {
    $path = Join-Path $Folder $File
    $text = [IO.File]::ReadAllText($path)
    $count = [regex]::Matches($text, [regex]::Escape($Find)).Count
    if ($count -ne 1) { throw "Mutation anchor '$Find' matches $count times in $File (must be exactly once)." }
    [IO.File]::WriteAllText($path, $text.Replace($Find, $Replace))
}

# Each mutant is a plausible mistake, made in a copy of the gate sources (Edits) or of the shared session
# sources that are compiled into the gate (SharedEdits, file names of ArenaMode.Shared\Session). At least one of
# the scenarios listed must fail for it. The anchors are single lines, so line endings do not matter.
$mutants = @(
    @{ Name = 'install-in-initialize'; Scenarios = @('full'); Edits = @(
        @{ File = 'Patcher.cs'; Find = 'ArenaGateRuntime.Prepare();'; Replace = 'ArenaGateRuntime.Prepare(); ArenaGateRuntime.Arm();' },
        @{ File = 'ArenaGateRuntime.cs'; Find = 'if (!IsAssemblyLoaded(CoreModuleAssemblyName))'; Replace = 'if (false)' }) },
    @{ Name = 'no-flag-check'; Scenarios = @('inert'); Edits = @(
        @{ File = 'Patcher.cs'; Find = 'return ArenaSessionContract.HasSessionFlag(Environment.GetCommandLineArgs());'; Replace = 'return true;' }) },
    @{ Name = 'plugins-not-removed'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGatePluginFilter.cs'; Find = 'if (excluded[i]) slots[i].List.RemoveAt(slots[i].Index);'; Replace = 'if (false) slots[i].List.RemoveAt(slots[i].Index);' }) },
    @{ Name = 'patchers-not-neutralised'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'plugin.TargetDLLs = NoTargets;'; Replace = 'plugin.TargetDLLs = targets;' }) },
    @{ Name = 'blacklist-patchers-neutralised'; Scenarios = @('user-blacklist'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'if (policy.IsAllowedPatcher(file))'; Replace = 'if (policy.FindPatcher(file) != null)' }) },
    @{ Name = 'entry-point-patcher-neutralised'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'if (origin != null && (origin == self || origin == preloader || origin == core)) continue;'; Replace = 'if (origin != null && origin == self) continue;' }) },
    @{ Name = 'every-directory-filtered'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'if (!IsChainloaderDirectory(directory)) return;'; Replace = '' }) },
    @{ Name = 'soft-dependency-pulled-in'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGatePluginFilter.cs'; Find = 'if ((flags & HardDependencyFlag) == 0) continue;'; Replace = '' }) },
    @{ Name = 'postfix-may-throw'; Scenarios = @('postfix-failure'); Edits = @(
        @{ File = 'ArenaGatePluginFilter.cs'; Find = 'catch (Exception ex)'; Replace = 'catch (ArgumentNullException ex)' }) },
    @{ Name = 'patchers-named-at-debug'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'Log(LogLevel.Info, "Arena session: " + DescribeNeutralised());'; Replace = 'Log(LogLevel.Debug, "Arena session: " + DescribeNeutralised());' }) },
    @{ Name = 'stay-off-not-said'; Scenarios = @('postfix-failure'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'if (Neutralised.Count > 0) message +=' ; Replace = 'if (false) message +=' }) },
    # The reason a unit or patcher is off is part of what the gate says: the default list's deny, the user's deny
    # and plain absence from the whitelist are three different causes.
    @{ Name = 'patcher-default-deny-not-named'; Scenarios = @('default-deny'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'if (reason == ArenaWhitelistDecision.ReasonDenied) return "is denied by the default policy list";'; Replace = '' }) },
    @{ Name = 'patcher-user-deny-not-named'; Scenarios = @('user-file', 'user-blacklist'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'if (reason == ArenaWhitelistDecision.ReasonDeniedByUser) return "is denied in the user''s policy file";'; Replace = '' }) },
    @{ Name = 'excluded-debug-line-without-reason'; Scenarios = @('full'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = '+ unit.Reason + ", " + unit.File);'; Replace = '+ unit.File);' }) },
    # The user's own file can turn the whitelist into a blacklist, and the gate says so: the Info summary carries the
    # override suffix, the report carries the flag, and the decision's one warning names the plugins let through.
    @{ Name = 'summary-without-override-suffix'; Scenarios = @('user-blacklist'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = '+ (UnlistedAllowed ? PolicyOverrideSummarySuffix : string.Empty);'; Replace = ';' }) },
    @{ Name = 'override-suffix-in-a-whitelist'; Scenarios = @('full', 'user-file'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = '+ (UnlistedAllowed ? PolicyOverrideSummarySuffix : string.Empty);'; Replace = '+ PolicyOverrideSummarySuffix;' }) },
    @{ Name = 'report-without-unlisted-flag'; Scenarios = @('user-blacklist'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'UnlistedAllowed = policy.AllowsUnlisted,'; Replace = 'UnlistedAllowed = false,' }) },
    @{ Name = 'flag-not-published'; Scenarios = @('user-blacklist'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'root["unlistedAllowed"] = UnlistedAllowed;'; Replace = 'root["unlistedAllowed"] = false;' }) },
    @{ Name = 'override-warning-dropped'; Scenarios = @('user-blacklist'); SharedEdits = @(
        @{ File = 'ArenaWhitelistDecision.cs'; Find = 'if (policyOverride != null) result.Warnings.Add(policyOverride);'; Replace = '' }) },
    @{ Name = 'override-warning-in-a-whitelist'; Scenarios = @('full', 'user-file'); SharedEdits = @(
        @{ File = 'ArenaWhitelistDecision.cs'; Find = 'if (!string.Equals(unit.Reason, ReasonUnlistedAllowed, StringComparison.Ordinal)) continue;'; Replace = '' }) },
    # Patchers that sort before the gate have already run their initializer, which cannot be undone. The report lists
    # exactly those (and not the allowed ones that sort there, nor the denied ones that sort after the gate), always
    # carries the member, keeps it in the inactive reports and counts it in the Info summary.
    @{ Name = 'ran-before-gate-not-collected'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'AddOnce(outcome.RanBeforeGate, file);'; Replace = '' }) },
    @{ Name = 'ran-before-gate-lists-every-neutralised'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'RanBeforeGate.AddRange(patchers.RanBeforeGate);'; Replace = 'RanBeforeGate.AddRange(patchers.Neutralised);' }) },
    @{ Name = 'ran-before-gate-lists-allowed-early-patchers'; Scenarios = @('full', 'no-early-patcher'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'AddOnce(outcome.Allowed, file);'; Replace = 'AddOnce(outcome.Allowed, file); if (plugin.Initializer != null && selfIndex >= 0 && i < selfIndex) AddOnce(outcome.RanBeforeGate, file);' }) },
    @{ Name = 'ran-before-gate-missing-in-final-report'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'ArenaGateReport.FromDecision(_policy, decision, Neutralised, RanBeforeGate, AllowedPatchers);'; Replace = 'ArenaGateReport.FromDecision(_policy, decision, Neutralised, null, AllowedPatchers);' }) },
    @{ Name = 'ran-before-gate-missing-in-inactive-report'; Scenarios = @('full', 'no-core-module', 'postfix-failure'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'report.PatchersRanBeforeGate.AddRange(RanBeforeGate);'; Replace = '' }) },
    @{ Name = 'ran-before-gate-not-published'; Scenarios = @('full', 'postfix-failure'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'root["patchersRanBeforeGate"] = PatchersRanBeforeGate == null ? new List<string>() : PatchersRanBeforeGate;'; Replace = 'root["patchersRanBeforeGate"] = new List<string>();' }) },
    @{ Name = 'ran-before-gate-omitted-when-empty'; Scenarios = @('no-early-patcher', 'bad-default', 'missing-default'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'root["patchersRanBeforeGate"] = PatchersRanBeforeGate == null ? new List<string>() : PatchersRanBeforeGate;'; Replace = 'if (PatchersRanBeforeGate != null && PatchersRanBeforeGate.Count > 0) root["patchersRanBeforeGate"] = PatchersRanBeforeGate;' }) },
    # A user file above 256 KB is not a policy file for the client, so it cannot be one for the gate: the Arena session refuses
    # on the policy hash when the two sides apply different policies to the same file.
    @{ Name = 'user-file-size-unchecked'; Scenarios = @('user-oversize'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'if (total > MaxBytes)'; Replace = 'if (false)' }) },
    @{ Name = 'user-file-read-privately'; Scenarios = @('user-oversize'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'ArenaPolicyFileState userState = ArenaPolicyFiles.TryRead(userPath, out userJson, out detail);'; Replace = 'ArenaPolicyFileState userState = ArenaPolicyFileState.Read; userJson = File.Exists(userPath) ? File.ReadAllText(userPath) : null;' }) },
    # The patchers the gate let run (review 2026-10-23): listed as patchersAllowed, exactly those, in every report, so that the
    # client can judge them again with the policy of a later moment.
    @{ Name = 'allowed-patchers-not-collected'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'AddOnce(outcome.Allowed, file);'; Replace = '' }) },
    @{ Name = 'allowed-patchers-list-the-neutralised'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'AllowedPatchers.AddRange(patchers.Allowed);'; Replace = 'AllowedPatchers.AddRange(patchers.Neutralised);' }) },
    @{ Name = 'allowed-patchers-missing-in-final-report'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'ArenaGateReport.FromDecision(_policy, decision, Neutralised, RanBeforeGate, AllowedPatchers);'; Replace = 'ArenaGateReport.FromDecision(_policy, decision, Neutralised, RanBeforeGate);' }) },
    @{ Name = 'allowed-patchers-missing-in-inactive-report'; Scenarios = @('full', 'no-core-module', 'postfix-failure'); Edits = @(
        @{ File = 'ArenaGateRuntime.cs'; Find = 'report.PatchersAllowed.AddRange(AllowedPatchers);'; Replace = '' }) },
    @{ Name = 'allowed-patchers-not-published'; Scenarios = @('full', 'postfix-failure'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'root["patchersAllowed"] = PatchersAllowed == null ? new List<string>() : PatchersAllowed;'; Replace = 'root["patchersAllowed"] = new List<string>();' }) },
    # Fail closed for patchers the gate cannot place: one without a file is switched off and reported, and when the gate's own
    # position in the patcher list is unknown every neutralised patcher with an initializer counts as one that ran early.
    @{ Name = 'unidentified-patcher-left-active'; Scenarios = @('unidentified-patcher'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'file = UnidentifiedName(plugin, i);'; Replace = 'continue;' }) },
    @{ Name = 'unidentified-patcher-not-reported-as-early'; Scenarios = @('unidentified-patcher'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'bool initializerMayHaveRun = plugin.Initializer != null && (selfIndex < 0 || i < selfIndex);'; Replace = 'bool initializerMayHaveRun = plugin.Initializer != null && identified && (selfIndex < 0 || i < selfIndex);' }) },
    @{ Name = 'unidentified-patcher-without-warning'; Scenarios = @('unidentified-patcher'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = '+ " could not be determined, so it cannot be on the whitelist and was neutralised");'; Replace = '+ " could not be determined");' }) },
    @{ Name = 'unknown-position-lists-nothing'; Scenarios = @('gate-unlocated'); Edits = @(
        @{ File = 'ArenaGatePatchers.cs'; Find = 'bool initializerMayHaveRun = plugin.Initializer != null && (selfIndex < 0 || i < selfIndex);'; Replace = 'bool initializerMayHaveRun = plugin.Initializer != null && selfIndex >= 0 && i < selfIndex;' }) },
    @{ Name = 'summary-without-ran-before-suffix'; Scenarios = @('full', 'user-file', 'user-blacklist', 'default-deny'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'line += " (" + PatchersRanBeforeGate.Count.ToString(CultureInfo.InvariantCulture) + " patcher(s) ran before the gate)";'; Replace = '' }) },
    @{ Name = 'ran-before-suffix-without-patchers'; Scenarios = @('no-early-patcher'); SharedEdits = @(
        @{ File = 'ArenaSessionContract.cs'; Find = 'if (PatchersRanBeforeGate != null && PatchersRanBeforeGate.Count > 0)'; Replace = 'if (PatchersRanBeforeGate != null)' }) }
)

if (-not $SkipMutants -and -not $Only) {
    $gateSources = Join-Path $repo 'ArenaMode.Gate'
    $rejected = 0
    foreach ($mutant in $mutants) {
        $folder = Join-Path $run ('mutants\' + $mutant.Name)
        $src = Join-Path $folder 'src'
        [IO.Directory]::CreateDirectory($src) | Out-Null
        foreach ($file in Get-ChildItem $gateSources -Filter '*.cs') { Copy-Item $file.FullName (Join-Path $src $file.Name) }
        foreach ($edit in $mutant.Edits) {
            Edit-Source $src $edit.File $edit.Find $edit.Replace
        }

        # A mutant of the shared sources compiles a mutated copy of them into the gate instead of the originals.
        $mutantShared = $sharedSources
        if ($mutant.SharedEdits) {
            $sharedCopy = Join-Path $folder 'shared'
            [IO.Directory]::CreateDirectory($sharedCopy) | Out-Null
            foreach ($file in $sharedSources) { Copy-Item $file (Join-Path $sharedCopy (Split-Path $file -Leaf)) }
            foreach ($edit in $mutant.SharedEdits) { Edit-Source $sharedCopy $edit.File $edit.Find $edit.Replace }
            $mutantShared = @(Get-ChildItem $sharedCopy -Filter '*.cs' | ForEach-Object { $_.FullName })
        }

        $gateOut = Join-Path $folder 'build'
        [IO.Directory]::CreateDirectory($gateOut) | Out-Null
        $refs = @($netstandard | ForEach-Object { "-r:$_" }) + ($coreReferences | ForEach-Object { "-r:$core\$_" })
        $sources = @(Get-ChildItem $src -Filter '*.cs' | ForEach-Object { $_.FullName }) + $mutantShared
        Invoke-Native "compile mutant $($mutant.Name)" {
            & dotnet $csc -nologo -nostdlib -noconfig -langversion:latest -debug:portable -target:library "-out:$gateOut\maschine-ArenaMode.Gate.dll" @refs @sources
        } (Join-Path $folder 'compile.log')

        $mutantDriver = Join-Path $folder 'driver'
        New-DriverFolder $mutantDriver (Join-Path $gateOut 'maschine-ArenaMode.Gate.dll')
        Copy-Item (Join-Path $driver 'GateChecks.exe') $mutantDriver -Force
        Copy-Item (Join-Path $driver 'GateChecks.pdb') $mutantDriver -Force -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $driver 'DummyPatcher.template.dll') $mutantDriver -Force

        $caught = @()
        foreach ($name in $mutant.Scenarios) {
            $definition = $scenarios | Where-Object { $_.Name -eq $name }
            $mutantEvidence = Join-Path $folder 'scenarios'
            [IO.Directory]::CreateDirectory($mutantEvidence) | Out-Null
            $result = Invoke-Scenario $mutantDriver $name $definition.Flag $mutantEvidence
            if (-not $result.Ok) { $caught += $name }
        }
        if ($caught.Count -eq 0) { throw "Mutant $($mutant.Name) survived: none of ($($mutant.Scenarios -join ', ')) failed. See $folder" }
        $rejected++
        Write-Output ("  rejected mutant {0,-34} by {1}" -f $mutant.Name, ($caught -join ', '))
    }
    $summary = "ARENA_GATE_CHECKS_PASSED ($($scenarios.Count) scenarios, $totalChecks checks, $rejected mutants rejected); evidence: $run"
}
else {
    $summary = "ARENA_GATE_CHECKS_PASSED ($($scenarios.Count) scenarios, $totalChecks checks, mutants skipped); evidence: $run"
}

# The fake game folders (a copy of UnityEngine.CoreModule each) and the driver copies are bulky and
# only useful when something failed; the logs, the mutated sources and the compile logs stay.
if (-not $KeepWork) {
    Get-ChildItem $run -Recurse -Directory | Where-Object { $_.Name -in @('work', 'driver', 'build') -and $_.FullName -ne $driver } |
        Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { if (Test-Path $_.FullName) { Remove-Item $_.FullName -Recurse -Force } }
    Remove-Item $driver -Recurse -Force
}

Write-Output $summary
