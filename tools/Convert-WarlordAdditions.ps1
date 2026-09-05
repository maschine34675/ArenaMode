param([string]$SourceDirectory = 'C:\Users\Administrator\Downloads\announcer_pack_-_warlord')
$ErrorActionPreference = 'Stop'
$assetRoot = Join-Path $PSScriptRoot '..\ArenaMode.Client\Assets\ArenaMode'
$sounds = Join-Path $assetRoot 'sounds'
$sourceUrl = 'https://opengameart.org/content/warlord-video-game-announcer'
$licenseUrl = 'https://creativecommons.org/licenses/by-sa/4.0/'
$map = [ordered]@{
    '08._first_blood.wav' = 'first_blood'
    '09._headshot.wav' = 'headshot'
    '06._unstoppable.wav' = 'unstoppable'
    '15._last_man_standing.wav' = 'last_man_standing'
    '22._mission_accomplished.wav' = 'match_won'
    '29._game_over.wav' = 'match_lost'
    '25._payback.wav' = 'payback'
}
$records = foreach ($entry in $map.GetEnumerator()) {
    $inputFile = Join-Path $SourceDirectory $entry.Key
    $outputFile = Join-Path $sounds ($entry.Value + '.ogg')
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing source $inputFile" }
    & ffmpeg -hide_banner -loglevel error -y -i $inputFile -map 0:a:0 -vn `
        -af 'loudnorm=I=-16:TP=-2:LRA=11' -ar 44100 -ac 2 -c:a libvorbis -q:a 5 `
        -metadata 'artist=VoiceBosch' -metadata "title=$($entry.Value)" `
        -metadata "copyright=VoiceBosch; CC BY-SA 4.0; $licenseUrl" `
        -metadata "license=$licenseUrl" -metadata "source=$sourceUrl" `
        -metadata 'comment=Converted and loudness-normalized for ArenaMode; -16 LUFS target, -2 dBTP encoder target; CC BY-SA 4.0.' $outputFile
    if ($LASTEXITCODE -ne 0) { throw "Conversion failed: $inputFile" }
    $probe = & ffprobe -v error -show_entries 'stream=codec_name,sample_rate,channels:format=duration' -of json $outputFile | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Decode metadata failed: $outputFile" }
    $measurementText = (& ffmpeg -hide_banner -i $outputFile -af 'loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json' -f null NUL 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Output measurement failed: $outputFile" }
    $measurement = [regex]::Match($measurementText, '(?s)\{\s*"input_i".*?\}').Value | ConvertFrom-Json
    $peak = [double]::Parse($measurement.input_tp, [cultureinfo]::InvariantCulture)
    if ($peak -gt -1.5) { throw "Encoded true peak exceeds -1.5 dBTP: $outputFile ($peak)" }
    [ordered]@{
        original = $entry.Key; sourceSha256 = (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash
        bundled = 'sounds/' + $entry.Value + '.ogg'; sha256 = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash
        seconds = $probe.format.duration; rate = $probe.streams[0].sample_rate; channels = $probe.streams[0].channels
        measuredLufs = $measurement.input_i; measuredTruePeakDb = $measurement.input_tp
    }
}
$manifest = [ordered]@{
    creator = 'VoiceBosch'; source = $sourceUrl; license = 'CC BY-SA 4.0'; licenseUrl = $licenseUrl
    conversion = 'FFmpeg libvorbis q5; 44100 Hz stereo; loudnorm I=-16 TP=-2 LRA=11; no trimming/pitch/splicing.'
    encoder = (& ffmpeg -version | Select-Object -First 1)
    files = @($records)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $assetRoot 'WARLORD-ADDITIONS.json') -Encoding utf8
Write-Output "WARLORD_ADDITIONS_VERIFIED ($($records.Count) files; Vorbis, 44.1 kHz stereo; true peaks <= -1.5 dBTP)"
