$ErrorActionPreference = 'Stop'
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$scratch = Join-Path $PSScriptRoot ('obj\frame-helper-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$video = Join-Path $scratch 'synthetic-parser-fixture.mkv'
$output = Join-Path $scratch 'decoded'
# Explicit synthetic test data for the extraction helper, never GPU evidence.
& $ffmpeg -v error -n -f lavfi -i 'testsrc2=size=64x64:rate=30' -t 0.4 -c:v ffv1 -an $video
if ($LASTEXITCODE -ne 0) { throw 'Frame helper fixture generation failed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'ReviewFrames.ps1') -VideoPath $video -StartSeconds 0.1 -FrameCount 6 -OutputDirectory $output
if ($LASTEXITCODE -ne 0) { throw 'Frame helper did not extract fixture frames.' }
$manifest = Get-Content -LiteralPath (Join-Path $output 'frames.json') -Raw | ConvertFrom-Json
if ($manifest.frames.Count -ne 6 -or $manifest.frames[0].source_frame_index -ne 3 -or $manifest.frames[5].source_frame_index -ne 8) {
    throw 'Extractor returned the wrong source frame interval.'
}
if ($manifest.frames[0].presentation_timestamp_seconds -ne '0.100000' -or $manifest.frames[5].presentation_timestamp_seconds -ne '0.267000') {
    throw 'Extractor did not preserve the fixture presentation timestamps.'
}
if ($manifest.width -ne 64 -or $manifest.height -ne 64 -or $manifest.source_sha256 -ne (Get-FileHash -LiteralPath $video -Algorithm SHA256).Hash) {
    throw 'Extractor dimensions or input provenance differ.'
}
foreach ($frame in $manifest.frames) {
    if ($frame.sha256 -ne (Get-FileHash -LiteralPath $frame.image -Algorithm SHA256).Hash) { throw 'Extracted frame hash differs.' }
}
$failureOutput = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'ReviewFrames.ps1') -VideoPath $video -StartSeconds 5 -OutputDirectory (Join-Path $scratch 'past-end') 2>&1
if ($LASTEXITCODE -eq 0 -or ($failureOutput -join "`n") -notmatch 'does not contain the requested consecutive frames') {
    throw 'A missing source frame range was not rejected for the expected reason.'
}
$failureOutput = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'ReviewFrames.ps1') -VideoPath $video -OutputDirectory $output 2>&1
if ($LASTEXITCODE -eq 0 -or ($failureOutput -join "`n") -notmatch 'existing evidence is preserved') {
    throw 'An existing evidence output was not protected.'
}
Write-Output 'PREVIEW_FRAME_HELPER_PASSED: synthetic parser fixture only, no gameplay claim.'
