# Offline checks of the Arena session helper's socket and mutex code on the runtime it ships on.
#
#   pwsh -File tools/ArenaSessionHelperNet472Checks/Verify.ps1 [-Configuration Debug|Release] [-SkipMutants] [-KeepWork]
#
# Why this exists: ArenaMode.Tests runs the helper's sources on net10.0, where `new TcpClient()` is a dual-stack socket. The
# helper itself is a .NET Framework 4.7.2 exe, where the same call is an IPv4-only socket, so a probe of an IPv6 literal such as
# `::1` fails there and passes in the unit tests. Windows PowerShell 5.1 runs on the CLR of .NET Framework 4.x, so
# Check-HelperNet472.ps1 loads the built exe in it and calls HelperProcesses.CanConnect / GetListenerProcessIds by reflection.
#
# What it does
#   1. copies the helper sources (and the shared session sources it links) to artifacts/helper-net472-checks/<run>/,
#      builds that copy with `dotnet build` (never deploying: the NoDeploy.targets override and a SPTUserMods path that does not
#      exist, like every other offline build of this repository) and requires every check to pass,
#   2. builds deliberately broken copies ("mutants": one text edit of a helper source each) and requires that the check
#      named for the edit fails. A mutant that does not compile, or fails on another check only, is itself a failure.
# Nothing under the game folders is read or written; the production sources are only copied.

param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$SkipMutants,
    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$noDeploy = Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets'
$mustNotExist = Join-Path $repo 'artifacts\nodeploy\must-not-exist'
$check = Join-Path $PSScriptRoot 'Check-HelperNet472.ps1'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path $windowsPowerShell)) { throw "Windows PowerShell 5.1 not found at $windowsPowerShell" }
$run = Join-Path $repo ('artifacts\helper-net472-checks\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($run) | Out-Null
Write-Output "Evidence folder: $run"

# One text edit of a helper source (HelperProcesses.cs unless File names another; LF-normalised) and the check that has to
# fail for it.
$mutants = @(
    @{ Name = 'probe-without-address-family'; Expect = 'ipv6-literal-connect'
       Old = 'return IPAddress.TryParse(host, out literal) ? new TcpClient(literal.AddressFamily) : new TcpClient();'
       New = 'return new TcpClient();' },
    @{ Name = 'wildcard-rows-not-matched'; Expect = 'dual-stack-wildcard-owner'
       Old = 'if (!ListenerServes(local, serves)) continue;'
       New = 'if (!local.Equals(serves)) continue;' },
    @{ Name = 'ipv4-wildcard-serves-nothing'; Expect = 'ipv4-wildcard-owner'
       Old = 'if (listening.Equals(IPAddress.Any)) return connected.AddressFamily == AddressFamily.InterNetwork;'
       New = 'if (listening.Equals(IPAddress.Any)) return false;' },
    # The helper mutex (review 2026-10-23): a Local name is one per logon session; the default access list of a Global object
    # does not name the user, so another logon session of the same user could not open it.
    @{ Name = 'mutex-per-logon-session'; Expect = 'mutex-name-global-for-user'; File = 'ArenaMode.Shared\Session\ArenaSessionContract.cs'
       Old = 'public const string HelperMutexNamePrefix = "Global\\maschine-ArenaMode.Session.";'
       New = 'public const string HelperMutexNamePrefix = "Local\\maschine-ArenaMode.Session.";' },
    @{ Name = 'mutex-without-user-rule'; Expect = 'mutex-access-list'; File = 'ArenaMode.Session\HelperHost.cs'
       Old = 'if (user != null) security.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));'
       New = '' },
    @{ Name = 'mutex-default-access-list'; Expect = 'mutex-access-list'; File = 'ArenaMode.Session\HelperHost.cs'
       Old = 'return new Mutex(false, name, out createdNew, security);'
       New = 'return new Mutex(false, name, out createdNew);' }
)

function New-SourceCopy([string]$Name) {
    $root = Join-Path $run $Name
    $session = Join-Path $root 'ArenaMode.Session'
    $shared = Join-Path $root 'ArenaMode.Shared\Session'
    [IO.Directory]::CreateDirectory($session) | Out-Null
    [IO.Directory]::CreateDirectory($shared) | Out-Null
    Get-ChildItem (Join-Path $repo 'ArenaMode.Session') -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.manifest' } |
        ForEach-Object { Copy-Item $_.FullName $session }
    Get-ChildItem (Join-Path $repo 'ArenaMode.Shared\Session') -File -Filter '*.cs' | ForEach-Object { Copy-Item $_.FullName $shared }
    return $root
}

function Build-Copy([string]$Root, [string]$Name) {
    $log = Join-Path $run ($Name + '-build.log')
    & dotnet build (Join-Path $Root 'ArenaMode.Session\ArenaMode.Session.csproj') -c $Configuration --nologo `
        "-p:CustomAfterMicrosoftCommonTargets=$noDeploy" "-p:SPTUserMods=$mustNotExist" *> $log
    if ($LASTEXITCODE -ne 0) { throw "dotnet build of '$Name' failed (exit $LASTEXITCODE), see $log`n$([IO.File]::ReadAllText($log))" }
    $exe = Join-Path $Root "ArenaMode.Session\bin\$Configuration\maschine-ArenaMode.Session.exe"
    if (-not (Test-Path $exe)) { throw "The build of '$Name' produced no $exe" }
    return $exe
}

function Invoke-Check([string]$Exe) {
    $lines = & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $check -Exe $Exe 2>&1 | ForEach-Object { "$_" }
    return @{ Exit = $LASTEXITCODE; Lines = @($lines) }
}

$failures = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------------------- 1. production sources
$root = New-SourceCopy 'production'
$result = Invoke-Check (Build-Copy $root 'production')
$result.Lines | ForEach-Object { Write-Output "  $_" }
if ($result.Exit -ne 0) { $failures.Add('the production sources fail the checks') }
$passed = @($result.Lines | Where-Object { $_ -like 'PASS *' }).Count
Write-Output "Production sources: $passed checks passed"

# ---------------------------------------------------------------------------- 2. mutants
$rejected = 0
if (-not $SkipMutants -and $failures.Count -eq 0) {
    foreach ($mutant in $mutants) {
        $root = New-SourceCopy ('mutant-' + $mutant.Name)
        $file = if ($mutant.File) { $mutant.File } else { 'ArenaMode.Session\HelperProcesses.cs' }
        $path = Join-Path $root $file
        $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        if (([regex]::Matches($text, [regex]::Escape($mutant.Old))).Count -ne 1) { throw "Mutant '$($mutant.Name)': the text to edit is not there exactly once." }
        [IO.File]::WriteAllText($path, $text.Replace($mutant.Old, $mutant.New))
        $result = Invoke-Check (Build-Copy $root ('mutant-' + $mutant.Name))
        $failedChecks = @($result.Lines | Where-Object { $_ -like 'FAIL *' } | ForEach-Object { ($_ -replace '^FAIL ', '') -replace ':.*$', '' })
        if ($result.Exit -ne 1 -or $failedChecks -notcontains $mutant.Expect) {
            $failures.Add("mutant '$($mutant.Name)' was not rejected on '$($mutant.Expect)' (exit $($result.Exit), failed: $($failedChecks -join ', '))")
        } else {
            $rejected++
            Write-Output "Mutant '$($mutant.Name)' rejected on '$($mutant.Expect)' (all failed: $($failedChecks -join ', '))"
        }
    }
}

if (-not $KeepWork -and $failures.Count -eq 0) { Remove-Item $run -Recurse -Force }
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output "FAILED: $_" }
    exit 1
}
Write-Output "ArenaSessionHelperNet472Checks: $passed checks, $rejected of $($mutants.Count) mutants rejected"
