$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'ArenaMode.Client/Effects'
$evidence = Join-Path $repo ('artifacts/bot-effects-20261019/outline-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$files = @('ArenaBotEffectLeases.cs','ArenaBotEffectRenderer.cs','ArenaSpawnShieldView.cs','ArenaPowerupIndicator.cs')
$hashes = @{}
foreach ($file in $files) { $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $source $file)).Hash }
function Run-Effect([string]$name, [string]$src, [string]$fault = '') {
    $out = Join-Path $evidence $name
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $build = & dotnet build (Join-Path $PSScriptRoot 'BotEffectOutlineChecks.csproj') -c Release --nologo "-p:EffectSourceRoot=$src" "-p:BaseIntermediateOutputPath=$out/obj/" "-p:OutputPath=$out/out/" 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $out 'build.log'), $build)
    if ($code -ne 0) { throw "Variant $name must compile: $build" }
    $result = & dotnet (Join-Path $out 'out/BotEffectOutlineChecks.dll') 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $out 'result.log'), $result)
    if ($fault) {
        if ($code -eq 0 -or !$result.Contains('ASSERT: ' + $fault)) { throw "Variant $name did not fail intended assertion: $result" }
        Write-Output "Rejected ${name}: $fault"
    } elseif ($code -ne 0 -or !$result.Contains('BOT_EFFECT_OUTLINE_BEHAVIOR_PASSED:')) { throw $result }
    else { Write-Output $result.Trim() }
}
Run-Effect 'production' $source
$faults = @(
    @{Name='body-value-equality'; File='ArenaBotEffectLeases.cs'; Find='ReferenceEquals(candidate.Body, body)'; Replace='Equals(candidate.Body, body)'; Fail='equal profile-like values cannot merge distinct bodies'},
    @{Name='lease-never-stale'; File='ArenaBotEffectLeases.cs'; Find='(long)frame - lease.Frame <= 2'; Replace='true'; Fail='stale presentation heartbeat cannot persist'},
    @{Name='frame-overflow'; File='ArenaBotEffectLeases.cs'; Find='(long)frame - lease.Frame'; Replace='frame - lease.Frame'; Occurrences=2; Fail='wrapped frame cannot borrow future ownership'},
    @{Name='camera-buffer-leak'; File='ArenaBotEffectRenderer.cs'; Find='_camera.RemoveCommandBuffer(RenderEvent, _commands);'; Replace='{}'; Fail='camera switch detaches exact previous buffer'},
    @{Name='stale-commands-on-depth-loss'; File='ArenaBotEffectRenderer.cs'; Find='if (ReferenceEquals(camera, _camera)) _commands?.Clear();'; Replace=''; Fail='lost depth admission clears previous draw commands'},
    @{Name='health-identity-drift'; File='ArenaBotEffectRenderer.cs'; Find='&& ReferenceEquals(body.ActiveHealthController, draw.Health)'; Replace=''; Fail='context body or health drift retires presentation fully'},
    @{Name='temporary-mask-leak'; File='ArenaBotEffectRenderer.cs'; Find='_commands.ReleaseTemporaryRT(MaskId);'; Replace=''; Fail='one bounded mask and composite shared by all effects'},
    @{Name='hidden-native-renderer'; File='ArenaBotEffectRenderer.cs'; Find='|| renderer.forceRenderingOff'; Replace=''; Fail='hidden filtered geometry cannot leave recorded silhouette'},
    @{Name='partial-resource-leak'; File='ArenaBotEffectRenderer.cs'; Find='_failed = true; ReleaseRenderResources();'; Replace='_failed = true; Detach();'; Fail='partial render failure immediately releases buffers materials and bundle'}
)
foreach ($fault in $faults) {
    $copy = Join-Path $evidence ($fault.Name + '/source'); New-Item -ItemType Directory -Path $copy -Force | Out-Null
    foreach ($file in $files) {
        $text = [IO.File]::ReadAllText((Join-Path $source $file))
        if ($file -eq $fault.File) {
            $count = if ($fault.ContainsKey('Occurrences')) { $fault.Occurrences } else { 1 }
            if ($text.Split(@($fault.Find),[StringSplitOptions]::None).Length -ne ($count + 1)) { throw "Ambiguous fault anchor $($fault.Name)" }
            $text = $text.Replace($fault.Find,$fault.Replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $file),$text)
    }
    Run-Effect $fault.Name $copy $fault.Fail
}
Run-Effect 'production-final' $source
foreach ($file in $files) { if ((Get-FileHash -LiteralPath (Join-Path $source $file)).Hash -ne $hashes[$file]) { throw "Source changed during checks: $file" } }
[ordered]@{ sources=$hashes; faults=$faults.Count; evidence=$evidence; limitation='Production C# linked with native ownership/command recording doubles. No pixel rasterization, GPU performance or EFT acceptance claim.' } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json')
Write-Output "BOT_EFFECT_OUTLINE_CHECKS_PASSED: $($faults.Count) compiled faults rejected; $evidence"
