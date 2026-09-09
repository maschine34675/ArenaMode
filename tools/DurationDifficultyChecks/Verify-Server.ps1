param([string]$SptRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if([string]::IsNullOrWhiteSpace($SptRoot)) { $SptRoot=[IO.Path]::GetFullPath((Join-Path $repo '../..')) }
$stage=Join-Path $repo ('artifacts/duration-balance/server-tests/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$server=Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$before=(Get-FileHash -LiteralPath $server -Algorithm SHA256).Hash
try {
    & dotnet test (Join-Path $repo 'ArenaMode.Tests') -c Release --nologo "-p:SPTUserPath=$stage/install" --logger "trx;LogFileName=results.trx" --results-directory $stage 2>&1 | Tee-Object (Join-Path $stage 'test.log')
    if($LASTEXITCODE -ne 0) { throw 'Server tests failed' }
} finally {
    if((Get-FileHash -LiteralPath $server -Algorithm SHA256).Hash -ne $before) { throw 'Server tests changed installed DLL' }
}
Write-Output 'ARENA_DURATION_SERVER_TESTS_PASSED'
