param()
$ErrorActionPreference = 'Stop'
$previewRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$previewProject = Join-Path $PSScriptRoot 'AreaMapPreviewChecks.csproj'
$previewRun = Join-Path $PSScriptRoot ('artifacts\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $previewRun -Force | Out-Null
$previewBoundFiles = @(
    'ArenaMode.Client\Session\Maps\ArenaAreaMapProjection.cs',
    'ArenaMode.Client\Session\Maps\ArenaAreaMapGraphic.cs',
    'ArenaMode.Client\Session\Maps\ArenaAreaMapPreview.cs',
    'ArenaMode.Client\Session\ArenaCompactMapInfo.cs',
    'ArenaMode.Client\Session\ArenaMapPanelLayout.cs',
    'ArenaMode.Client\Session\ArenaAreaSelectionState.cs',
    'ArenaMode.Shared\ArenaAreaCatalog.cs',
    'ArenaMode.Client\Assets\MapPreviews\customs-ground.png',
    'tools\AreaMapPreviewChecks\AreaMapPreviewChecks.csproj',
    'tools\AreaMapPreviewChecks\Program.cs',
    'tools\AreaMapPreviewChecks\UnityDoubles.cs',
    'tools\AreaMapPreviewChecks\Verify.ps1'
)
$previewBefore = @{}
foreach ($previewFile in $previewBoundFiles) {
    $previewBefore[$previewFile] = (Get-FileHash -LiteralPath (Join-Path $previewRepo $previewFile) -Algorithm SHA256).Hash
}
$previewBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $previewRun 'sources-before.json')
function Build-PreviewHarness([string]$Name,[string]$Property,[string]$Source) {
    $previewDestination = Join-Path $previewRun $Name
    $previewArgs = @('build',$previewProject,'-c','Release','--nologo','-v:q',
        "-p:OutputPath=$previewDestination\out\","-p:BaseIntermediateOutputPath=$previewDestination\obj\")
    if ($Property) { $previewArgs += "-p:$Property=$Source" }
    & dotnet @previewArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Standalone harness did not compile: $Name" }
    return Join-Path $previewDestination 'out\AreaMapPreviewChecks.dll'
}
Start-Transcript -Path (Join-Path $previewRun 'verification.log') | Out-Null
try {
    # No project references or SPT SDK package: only the executable's own
    # output/intermediates and compiler-included plain production sources.
    $previewBaseline = Build-PreviewHarness 'baseline' '' ''
    & dotnet $previewBaseline
    if ($LASTEXITCODE -ne 0) { throw 'Clean production preview checks failed.' }
    $previewFaults = @(
        @{ Name='horizontal-flip'; File='ArenaAreaMapProjection.cs'; Property='ProjectionSource'; Old='(WorldMaxX - x)'; New='(x - WorldMinX)'; Marker='ORIENTATION' },
        @{ Name='texture-y-flip'; File='ArenaAreaMapGraphic.cs'; Property='GraphicSource'; Old='new Vector2(uv.X, 1f - uv.Y)'; New='new Vector2(uv.X, uv.Y)'; Marker='TEXTURE-UV' },
        @{ Name='native-alpha-reset'; File='ArenaAreaMapPreview.cs'; Property='PreviewSource'; Old='_infoGroup.alpha = _infoAlpha;'; New='_infoGroup.alpha = 1f;'; Marker='NATIVE-RESTORE' },
        @{ Name='retired-callback'; File='ArenaAreaMapPreview.cs'; Property='PreviewSource'; Old='if (!ReferenceEquals(owner, _root) || owner == null || !owner.activeInHierarchy || _area == null) return;'; New='if (_area == null) return;'; Marker='RETIRED-CALLBACK' },
        @{ Name='deferred-native-group'; File='ArenaAreaMapPreview.cs'; Property='PreviewSource'; Old='UnityEngine.Object.DestroyImmediate(_infoGroup)'; New='UnityEngine.Object.Destroy(_infoGroup)'; Marker='SAME-FRAME-GROUP' }
    )
    foreach ($previewFault in $previewFaults) {
        $previewOriginal = Join-Path $previewRepo ('ArenaMode.Client\Session\Maps\' + $previewFault.File)
        $previewText = Get-Content -LiteralPath $previewOriginal -Raw
        if ([regex]::Matches($previewText,[regex]::Escape($previewFault.Old)).Count -ne 1) {
            throw "Mutation anchor must match once: $($previewFault.Name)"
        }
        $previewMutation = Join-Path $previewRun ($previewFault.Name + '.cs')
        [IO.File]::WriteAllText($previewMutation,$previewText.Replace($previewFault.Old,$previewFault.New))
        $previewDll = Build-PreviewHarness $previewFault.Name $previewFault.Property $previewMutation
        $previewOutput = & dotnet $previewDll 2>&1
        $previewExit = $LASTEXITCODE
        $previewOutput | Set-Content -LiteralPath (Join-Path $previewRun ($previewFault.Name + '.log'))
        if ($previewExit -eq 0 -or ($previewOutput -join "`n") -notmatch ('AREA MAP PREVIEW REJECTED: ' + [regex]::Escape($previewFault.Marker) + ':')) {
            throw "Source fault did not fail for its designated behavior: $($previewFault.Name)"
        }
        Write-Output "Negative control rejected as intended: $($previewFault.Name) [$($previewFault.Marker)]"
    }
    & dotnet $previewBaseline
    if ($LASTEXITCODE -ne 0) { throw 'Final clean production preview verification failed.' }
    $previewAfter = @{}
    foreach ($previewFile in $previewBoundFiles) {
        $previewAfter[$previewFile] = (Get-FileHash -LiteralPath (Join-Path $previewRepo $previewFile) -Algorithm SHA256).Hash
        if ($previewAfter[$previewFile] -ne $previewBefore[$previewFile]) {
            throw "A bound source changed during verification; rerun against the final source: $previewFile"
        }
    }
    $previewAfter | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $previewRun 'sources-after.json')
    Write-Output "Evidence: $previewRun"
    Write-Output 'AREA MAP PREVIEW CHECKS PASSED'
}
finally { Stop-Transcript | Out-Null }
