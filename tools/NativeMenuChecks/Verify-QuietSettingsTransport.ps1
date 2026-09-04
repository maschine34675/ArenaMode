$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'NativeMenuChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Session\ArenaServerClient.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaQuietSettingsMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

$quietPattern = 'byte\[\]\s+payload\s*=\s*Task\.Run\(\s*\(\)\s*=>\s*RequestHandler\.HttpClient\.GetAsync\(Protocol\.ClientSettingsRoute\)\)\.Result;\s*var\s+json\s*=\s*Encoding\.UTF8\.GetString\(payload\);'
$matches = [regex]::Matches($source, $quietPattern)
if ($matches.Count -ne 1) {
    throw "Expected exactly one quiet client-settings transport in production source; found $($matches.Count)."
}
$mutant = [regex]::Replace(
    $source,
    $quietPattern,
    'var json = RequestHandler.GetJson(Protocol.ClientSettingsRoute);')

$mutantPath = Join-Path $scratch 'ArenaServerClient-noisy.cs'
[System.IO.File]::WriteAllText($mutantPath, $mutant)

$previousErrorPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $output = & dotnet run --project $project -c Release `
        "-p:ProductionServerClientSource=$mutantPath" `
        "-p:OutputPath=$(Join-Path $scratch 'bin\')" `
        "-p:IntermediateOutputPath=$(Join-Path $scratch 'obj\')" 2>&1
    $exit = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorPreference
}

$text = ($output | ForEach-Object { [string]$_ }) -join "`n"
if ($exit -eq 0 -or $text -notmatch 'Periodic client-settings reads bypass the noisy RequestHandler wrapper') {
    throw "Noisy transport mutant did not fail its intended assertion (exit $exit). Evidence: $scratch"
}

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The production native-menu checks failed after mutation verification.'
}

$afterHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
if ($afterHash -ne $sourceHash) {
    throw 'Mutation verification changed ArenaServerClient.cs.'
}

Write-Output "QUIET_SETTINGS_TRANSPORT_VERIFIED (noisy wrapper mutant rejected; evidence: $scratch)"
