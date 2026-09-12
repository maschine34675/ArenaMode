param(
    [Parameter(Mandatory = $true)][string]$VideoPath,
    [double]$StartSeconds = 2.0,
    [ValidateRange(2, 12)][int]$FrameCount = 6,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$sourceVideo = (Resolve-Path -LiteralPath $VideoPath).Path
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source
if ($StartSeconds -lt 0 -or [double]::IsNaN($StartSeconds) -or [double]::IsInfinity($StartSeconds)) {
    throw 'StartSeconds must be a finite nonnegative presentation time.'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $sourceVideo) ('preview-frames-' + [guid]::NewGuid().ToString('N'))
}
$frameOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $frameOutput) { throw 'Use a new output directory so existing evidence is preserved.' }
$probeJson = & $ffprobe -v error -select_streams v:0 -show_frames -show_streams -show_entries 'frame=best_effort_timestamp_time:stream=width,height,r_frame_rate' -of json $sourceVideo
if ($LASTEXITCODE -ne 0) { throw 'The recording could not be inspected.' }
$probe = ($probeJson -join "`n") | ConvertFrom-Json
$frames = @($probe.frames)
$startFrame = -1
for ($i = 0; $i -lt $frames.Count; ++$i) {
    $timestamp = [double]::Parse([string]$frames[$i].best_effort_timestamp_time, [Globalization.CultureInfo]::InvariantCulture)
    if ($timestamp -ge $StartSeconds) { $startFrame = $i; break }
}
if ($startFrame -lt 0 -or $startFrame + $FrameCount -gt $frames.Count) {
    throw 'The recording does not contain the requested consecutive frames.'
}
$lastFrame = $startFrame + $FrameCount - 1
[IO.Directory]::CreateDirectory($frameOutput) | Out-Null
$outputPattern = Join-Path $frameOutput 'frame-%03d.png'
# Frame indices and timestamps come from this recording. No interpolation,
# scaling, synthetic frames, optical flow or guessed frame-rate timestamps.
& $ffmpeg -v error -n -i $sourceVideo -map 0:v:0 -vf "select=between(n\,$startFrame\,$lastFrame)" -fps_mode passthrough -frames:v $FrameCount $outputPattern
if ($LASTEXITCODE -ne 0) { throw 'Frame extraction failed; preserve the partial directory for diagnosis.' }
$entries = @()
for ($i = 0; $i -lt $FrameCount; ++$i) {
    $imagePath = Join-Path $frameOutput ('frame-{0:D3}.png' -f ($i + 1))
    if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) { throw 'A requested frame is missing.' }
    $entries += [ordered]@{
        source_frame_index = $startFrame + $i
        presentation_timestamp_seconds = [string]$frames[$startFrame + $i].best_effort_timestamp_time
        image = $imagePath
        sha256 = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash
    }
}
$manifest = [ordered]@{
    source_video = $sourceVideo
    source_sha256 = (Get-FileHash -LiteralPath $sourceVideo -Algorithm SHA256).Hash
    extracted_at_utc = [DateTime]::UtcNow.ToString('o')
    width = $probe.streams[0].width
    height = $probe.streams[0].height
    reported_frame_rate = $probe.streams[0].r_frame_rate
    note = 'Consecutive decoded source frames; timestamps read from ffprobe. This file does not certify that the recording contains gameplay or a particular DLL.'
    frames = $entries
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $frameOutput 'frames.json') -Encoding utf8
Write-Output ("EXTRACTED_REAL_SOURCE_FRAMES: $FrameCount; $frameOutput")
