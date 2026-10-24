param([string]$ReleaseRoot = '', [string]$LegacyServer = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ReleaseRoot) { $ReleaseRoot = Join-Path $repo 'artifacts/pft-100-contracts/release' }
if (-not (Test-Path -LiteralPath $ReleaseRoot)) { throw 'Download the exact PFT 1.0.0 release into artifacts/pft-100-contracts/release first, or pass -ReleaseRoot.' }
dotnet run --project (Join-Path $PSScriptRoot 'PitFireTeam100Checks.csproj') --configuration Release -- $ReleaseRoot $LegacyServer
if ($LASTEXITCODE -ne 0) { throw "PFT checks failed: $LASTEXITCODE" }
if (-not $LegacyServer) {
    dotnet run --project (Join-Path $PSScriptRoot 'PitFireTeam100Checks.csproj') --configuration Release --no-build -- $ReleaseRoot --absent
    if ($LASTEXITCODE -ne 0) { throw "Absent-PFT checks failed: $LASTEXITCODE" }
    foreach ($version in @('0.10.0', '0.10.3')) {
        $legacy = Join-Path $repo "artifacts/pft-100-contracts/legacy-$version/SPT_Runtime/user/mods/pitFireTeam-ServerMod/pitFireTeam.Server.dll"
        if (-not (Test-Path -LiteralPath $legacy)) { throw "Missing exact legacy fixture: $legacy" }
        dotnet run --project (Join-Path $PSScriptRoot 'PitFireTeam100Checks.csproj') --configuration Release --no-build -- $ReleaseRoot $legacy
        if ($LASTEXITCODE -ne 0) { throw "Legacy $version checks failed: $LASTEXITCODE" }
    }
}
