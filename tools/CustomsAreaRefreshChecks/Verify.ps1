param([string]$SptRoot='D:\SPT41')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run=Join-Path $repo ('artifacts/customs-refresh-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$project=Join-Path $PSScriptRoot 'CustomsAreaRefreshChecks.csproj'
$fixtures=Join-Path $PSScriptRoot 'Fixtures'
$database=Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database/locations/bigmap/base.json'
function Build([string]$name,[string]$override='') {
 $args=@('build',$project,'-c','Release','-v:q',"-p:OutputPath=$run/$name/out/","-p:BaseIntermediateOutputPath=$run/$name/obj/")
 if($override){$args+="-p:AreaCatalogSource=$override"}
 & dotnet @args > (Join-Path $run ($name+'-build.log')) 2>&1
 if($LASTEXITCODE -ne 0){throw "Compile failed: $name"}
 return Join-Path $run "$name/out/CustomsAreaRefreshChecks.dll"
}
$dll=Build 'baseline'
& dotnet $dll $fixtures $database
if($LASTEXITCODE -ne 0){throw 'Customs production checks failed'}
$source=Get-Content -LiteralPath (Join-Path $repo 'ArenaMode.Shared/ArenaAreaCatalog.cs') -Raw
foreach($fault in @(
 @('center-drift','new ArenaAreaVertex(480.774139f, -20.654253f)','new ArenaAreaVertex(481.774139f, -20.654253f)','EXACT-CONTOUR'),
 @('yard-drift','new ArenaAreaVertex(-157.857864f, -33.0622368f)','new ArenaAreaVertex(-158.857864f, -33.0622368f)','EXACT-CONTOUR'),
 @('old-wire','var candidate = id == CustomsFactoryCenterTeamsId ? CustomsFactoryCenterTeams','var candidate = id == CustomsFactoryCenterPolygonId ? CustomsFactoryCenterPolygon : id == CustomsFactoryCenterTeamsId ? CustomsFactoryCenterTeams','RETIRED-WIRE'),
 @('opposing-overlap','Position = new ArenaAreaPoint(source.X + offsetX, source.Y, source.Z + offsetZ);','Position = new ArenaAreaPoint(-204.84f + offsetX, source.Y, -133.96f + offsetZ);','COMPACT-GROUND-CANDIDATE')
)) {
 if(-not $source.Contains($fault[1])){throw "Missing fault site $($fault[0])"}
 $file=Join-Path $run ($fault[0]+'.cs');[IO.File]::WriteAllText($file,$source.Replace($fault[1],$fault[2]))
 $mutant=Build $fault[0] $file
 $output=& dotnet $mutant $fixtures $database 2>&1; $code=$LASTEXITCODE
 $output | Set-Content (Join-Path $run ($fault[0]+'.log'))
 if($code -eq 0 -or ($output -join "`n") -notmatch ('CUSTOMS AREA REFRESH REJECTED: '+$fault[3])){throw "Uncaught fault $($fault[0]): $output"}
 Write-Output "Negative control passed: $($fault[0])"
}
& pwsh -NoProfile -File (Join-Path $repo 'tools/CustomsEastAreaChecks/Verify.ps1') -SptRoot $SptRoot
if($LASTEXITCODE -ne 0){throw 'Side geometry/spawn regression failed'}
Write-Output "Evidence: $run"
Write-Output 'CUSTOMS AREA REFRESH CHECKS PASSED'
