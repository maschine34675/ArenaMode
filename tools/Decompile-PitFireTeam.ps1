#Requires -Version 7.0
<#
.SYNOPSIS
Decompiles the installed PitFireTeam client for tools/PortedCoverChecks.

.DESCRIPTION
Reads <SptRoot>\BepInEx\plugins\pitFireTeam\pitFireTeam.dll read-only (its SHA256 is compared
before and after) and writes an `ilspycmd -p` project into the gitignored artifacts\pft-installed\
of this repository. PortedCoverChecks reads pitTeam.Utils\Covers.cs from there when
ARENA_PFT_INSTALLED_SOURCE is not set. A non-empty output folder is refused, never deleted or
mixed with a new decompile: after a PitFireTeam update remove it yourself, or pass a fresh
-OutputRoot and point ARENA_PFT_INSTALLED_SOURCE at it. Needs ilspycmd on PATH
(dotnet tool install -g ilspycmd). Nothing is built, deployed or written outside the output folder.

.EXAMPLE
pwsh -NoProfile -File tools/Decompile-PitFireTeam.ps1
#>
param(
    [string]$SptRoot = 'D:\SPT41',
    [string]$OutputRoot = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts\pft-installed' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$dll = Join-Path $SptRoot 'BepInEx\plugins\pitFireTeam\pitFireTeam.dll'
if (!(Test-Path -LiteralPath $dll)) { throw "Installed PitFireTeam client missing: $dll" }
if (!(Get-Command ilspycmd -ErrorAction SilentlyContinue)) { throw 'ilspycmd is not on PATH; install it with: dotnet tool install -g ilspycmd' }
if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count) {
    throw "Output folder is not empty: $OutputRoot. Remove it first or pass a fresh -OutputRoot."
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$before = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$version = "$(& ilspycmd --version 2>&1 | Select-Object -First 1)"
# Without the game, BepInEx and SPT references ILSpy cannot resolve the called methods and
# drops named arguments such as `checkCurrent: false`, which the checks read.
$references = @('EscapeFromTarkov_Data\Managed', 'BepInEx\core', 'BepInEx\plugins\spt', 'BepInEx\plugins') |
    ForEach-Object { Join-Path $SptRoot $_ } | Where-Object { Test-Path -LiteralPath $_ }
$referenceArguments = @($references | ForEach-Object { '-r'; $_ })
# A method ILSpy cannot decompile is replaced by an error comment and named in the log;
# the anchors PortedCoverChecks reads still have to be present.
$output = & ilspycmd -p -o $OutputRoot @referenceArguments --ignore-decompilation-errors $dll 2>&1 | Out-String
$code = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $OutputRoot 'decompile.log'), $output)
if ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $before) { throw "Installed PitFireTeam client changed during the decompile: $dll" }
if ($code -ne 0) { throw "ilspycmd failed (exit $code): $output" }
$covers = Join-Path $OutputRoot 'pitTeam.Utils\Covers.cs'
if (!(Test-Path -LiteralPath $covers)) { throw "The decompile lacks the file PortedCoverChecks reads: $covers" }
if (Select-String -LiteralPath $covers -SimpleMatch 'missing references' -Quiet) {
    throw "References were not resolved for $covers (looked in: $($references -join ', ')); its anchors would be missing."
}
[ordered]@{ dll = $dll; sha256 = $before; ilspycmd = $version; created = (Get-Date).ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot 'decompile-receipt.json')
Write-Output "PFT_DECOMPILED $OutputRoot (pitFireTeam.dll SHA256 $before, $version)"
