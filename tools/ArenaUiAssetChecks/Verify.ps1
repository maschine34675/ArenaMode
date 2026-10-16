param([string]$ClientAssembly='')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run=Join-Path $repo ('artifacts/ui-asset-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
& python (Join-Path $PSScriptRoot 'Verify-Artwork.py') --repo $repo --report (Join-Path $run 'real-pixels.json')
if($LASTEXITCODE -ne 0){throw 'Real PNG/provenance validation failed'}
& python (Join-Path $PSScriptRoot 'Test-ArtworkValidator.py') --output (Join-Path $run 'invalid-artwork')
if($LASTEXITCODE -ne 0){throw 'Artwork validator negative control failed'}
$project=Join-Path $PSScriptRoot 'ArenaUiAssetChecks.csproj'
function Run([string]$name,[string]$property='',[string]$source='') {
 $args=@('run','--project',$project,'-c','Release')
 if($property){$args+="-p:$property=$source"}
 $runtimeArgs=@((Join-Path $run 'scoreboard.svg'))
 if($ClientAssembly){$runtimeArgs+=$ClientAssembly}
 $out=& dotnet @args -- @runtimeArgs 2>&1
 $code=$LASTEXITCODE;$out | Set-Content -LiteralPath (Join-Path $run ($name+'.log'))
 return @{Code=$code;Text=($out -join "`n")}
}
$baseline=Run 'baseline'
if($baseline.Code -ne 0 -or $baseline.Text -notmatch 'ARENA UI ASSET CONTRACTS PASSED'){throw $baseline.Text}
Write-Output $baseline.Text
foreach($fault in @(
 @('failure-retry','ArenaUiAssets.cs','AssetSource','_textures[icon] = texture;','if (texture != null) _textures[icon] = texture;','failure cached once'),
 @('leaked-textures','ArenaUiAssets.cs','AssetSource','foreach (var texture in _textures.Values) if (texture != null) Object.Destroy(texture);','// omitted disposal','disposal releases sprites and textures'),
 @('unnecessary-pixel-read','ArenaUiAssets.cs','AssetSource','texture.name = "Arena UI " + spec.File;','texture.GetPixels32(); texture.name = "Arena UI " + spec.File;','neutral source art requires no pixel rewrite'),
 @('bleed-stale-rifle','KillfeedIcon.cs','IconSource','case "LightBleeding": case "HeavyBleeding": return ArenaUiIcon.Bleeding;','case "LightBleeding": case "HeavyBleeding": return ArenaUiIcon.Sniper;','final cause takes precedence'),
 @('stale-headshot','KillfeedView.cs','FeedSource','line.Headshot.enabled = line.Headshot.sprite != null;','line.Headshot.enabled = true;','pooled headshot is cleared'),
 @('bleed-retained-headshot','KillfeedView.cs','FeedSource','headshot && !hazard','headshot','bleed death suppresses retained headshot'),
 @('bleed-tint-leak','KillfeedView.cs','FeedSource','line.Method.color = hazard ? ArenaUiTheme.Danger : Color.white;','if (hazard) line.Method.color = ArenaUiTheme.Danger;','pooled bleed resets to normal headshot'),
 @('barbed-stale-rifle','KillfeedIcon.cs','IconSource','case "Barbed": return ArenaUiIcon.BarbedWire;','case "Barbed": return ArenaUiIcon.Sniper;','final cause takes precedence Barbed'),
 @('barbed-stale-headshot','KillfeedView.cs','FeedSource','method == ArenaUiIcon.Bleeding || method == ArenaUiIcon.BarbedWire','method == ArenaUiIcon.Bleeding','wire death uses red categorical wire icon'),
 @('lost-text-width','KillfeedView.cs','FeedSource','FeedWidth - IconRail - 2f * HorizontalPadding','FeedWidth - 2f * HorizontalPadding','icon rail preserves original text width'),
 @('oversized-hazard','KillfeedView.cs','FeedSource','_ => new Vector2(44f, 22f)','_ => new Vector2(64f, 32f)','fallback keeps original symbol dimensions after weapon reuse'),
 @('wrong-assist-count','ArenaScoreboardPresentation.cs','TableSource','row.Assists.ToString()','row.Deaths.ToString()','K D A HS score order retained'),
 @('global-tint-leak','ArenaScoreboardPresentation.cs','TableSource','finally { GUI.color = previous; }','finally { }','global tint restored'),
 @('skull-on-other-kill','HitmarkerElement.cs','HitmarkerSource','|| !kill.KilledByLocal || kill.LocalIsVictim','|| kill.LocalIsVictim','nonlocal self or ineligible kill does not refresh skull'),
 @('skull-on-damage','HitmarkerElement.cs','HitmarkerSource','_shownAt = Time.unscaledTime;','_shownAt = Time.unscaledTime; _killShownAt = Time.unscaledTime;','damage callback cannot invent a confirmed skull'),
 @('skull-never-expires','HitmarkerElement.cs','HitmarkerSource','KillSeconds = 0.8f','KillSeconds = 8f','skull expires independently')
)) {
 $text=[IO.File]::ReadAllText((Join-Path $repo ('ArenaMode.Client/Hud/'+$fault[1])))
 if(!$text.Contains($fault[3])){throw ('Missing fault anchor '+$fault[0])}
 $path=Join-Path $run ($fault[0]+'.cs');[IO.File]::WriteAllText($path,$text.Replace($fault[3],$fault[4]))
 $result=Run $fault[0] $fault[2] $path
 if($result.Code -eq 0 -or $result.Text.Contains('error CS') -or !$result.Text.Contains('CHECK: '+$fault[5])){throw ($fault[0]+': '+$result.Text)}
 Write-Output ('Negative control rejected: '+$fault[0])
}
$restored=Run 'restored';if($restored.Code -ne 0){throw $restored.Text}
Write-Output "Evidence: $run"
Write-Output 'ARENA UI ASSET CHECKS PASSED'
