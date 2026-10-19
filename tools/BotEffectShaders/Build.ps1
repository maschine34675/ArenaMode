param([string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo 'artifacts/bot-effects-20261019/shader-build'
$asset = Join-Path $repo 'ArenaMode.Client/Assets/BotEffects'
New-Item -ItemType Directory -Path $output,$asset -Force | Out-Null
$old = $env:ARENA_EFFECT_SHADER_OUTPUT
try {
    $env:ARENA_EFFECT_SHADER_OUTPUT = $output
    $args = @('-batchmode','-nographics','-projectPath',('"'+$PSScriptRoot+'"'),'-executeMethod','BuildBotEffects.Run','-logFile',('"'+(Join-Path $output 'unity.log')+'"'))
    $process = Start-Process -FilePath $Unity -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Shader build exited $($process.ExitCode); inspect $output/unity.log" }
    if (!(Select-String -LiteralPath (Join-Path $output 'unity.log') -Pattern 'ARENA_BOT_EFFECT_SHADER_BUILD_PASSED' -Quiet)) { throw 'Missing shader build success token' }
    Copy-Item -LiteralPath (Join-Path $output 'bot-effect-outlines.bundle') -Destination $asset
    $sourcePaths = @('tools/BotEffectShaders/Assets/ArenaBotEffects.shader',
        'tools/BotEffectShaders/Assets/Editor/BuildBotEffects.cs',
        'tools/BotEffectShaders/ProjectSettings/ProjectVersion.txt', 'tools/BotEffectShaders/Build.ps1')
    $bundlePath = 'ArenaMode.Client/Assets/BotEffects/bot-effect-outlines.bundle'
    $manifest = [ordered]@{
        schema=1; provenance='Independently authored ArenaMode shader and editor build script; no third-party source or binary assets copied.'
        unityVersion='2022.3.43f1'; target='StandaloneWindows64'
        sourceFiles=@($sourcePaths | ForEach-Object { [ordered]@{path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash} })
        bundle=[ordered]@{path=$bundlePath; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $bundlePath)).Hash; bytes=(Get-Item -LiteralPath (Join-Path $repo $bundlePath)).Length}
        buildEvidence='artifacts/bot-effects-20261019/shader-build/unity.log'
        nativeAcceptance='Not claimed by shader compilation; see GPU test report.'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $asset 'build-manifest.json')
    Get-FileHash -LiteralPath (Join-Path $asset 'bot-effect-outlines.bundle') -Algorithm SHA256
    Write-Output 'ARENA_BOT_EFFECT_SHADER_BUILD_PASSED'
} finally { $env:ARENA_EFFECT_SHADER_OUTPUT = $old }
