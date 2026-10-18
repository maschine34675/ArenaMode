$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifact=Join-Path $root ('artifacts/pft-tactical-entry-checks/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($artifact)|Out-Null
$entry=Join-Path $root 'ArenaMode.Client/Kits/TacticalTeamKitEntry.cs'
$preload=Join-Path $root 'ArenaMode.Client/Kits/KitBundlePreloader.RequiredEntry.cs'
$wrapper=Join-Path $root 'ArenaMode.Client/Session/ArenaLocalRaidStartedPatch.cs'
function Run([string]$name,[string]$project,[string[]]$properties,[string]$token,[bool]$pass) {
    $result=& dotnet run --project $project -c Release --nologo @properties 2>&1
    $code=$LASTEXITCODE; $result|Set-Content -LiteralPath (Join-Path $artifact ($name+'.log'))
    $text=$result -join "`n"
    if($pass) {
        if($code -ne 0 -or $text -notmatch $token){throw "$name failed: $text"}
        Write-Output ($text -split "`n" | Where-Object { $_ -match $token })
    } else {
        if($code -eq 0 -or $text -match '(error CS\d+|error MSB\d+)' -or $text -notmatch 'Unhandled exception') {throw "$name failed to reject behavior: $text"}
        Write-Output "FAULT_REJECTED $name"
    }
}
$project=Join-Path $PSScriptRoot 'PftTacticalKitChecks.csproj'
$native=Join-Path $root 'tools/NativeMenuChecks/NativeMenuChecks.csproj'
Run 'entry-baseline' $project @() 'PFT_TACTICAL_KIT_CHECKS_OK' $true
Run 'native-baseline' $native @() 'ARENA_MENU_CHECKS_PASSED' $true
$faults=@(
    @{Name='all-solo-kits';Kind='entry';Old='_request = protectedTeam && !ownGear';New='_request = !ownGear'},
    @{Name='live-body-allowed';Kind='entry';Old='&& request.World.MainPlayer == null';New=''},
    @{Name='unawaited-assets';Kind='entry';Old='await KitBundlePreloader.PreloadEntryEquipment';New='_ = KitBundlePreloader.PreloadEntryEquipment'},
    @{Name='missing-issued-proof';Kind='entry';Old='if (request == null) throw new InvalidOperationException("Tactical team has no issued kit request.");';New='return;'},
    @{Name='inactive-optic-guard';Kind='preload';Old='loadingScreen: true';New='loadingScreen: false'},
    @{Name='unawaited-native-entry';Kind='wrapper';Old='await Kits.TacticalTeamKitEntry.IssueAsync';New='_ = Kits.TacticalTeamKitEntry.IssueAsync'},
    @{Name='stale-failure-cleanup';Kind='wrapper';Old='if (OwnsStart())';New='if (ArenaSession.ArmEpoch == armEpoch)'},
    @{Name='stale-response-bind';Kind='wrapper';Old='if (!OwnsStart())';New='if (false)'},
    @{Name='cancel-before-http-settled';Kind='wrapper';Old='catch (Exception ex) { captureFailure = ex; }';New='catch (Exception) { throw; }'},
    @{Name='wrong-native-map';Kind='entry';Old='&& string.Equals(request.RequestedMap, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase)';New=''}
)
foreach($fault in $faults){
    $source=switch($fault.Kind){entry{$entry} preload{$preload} wrapper{$wrapper}}
    $content=[IO.File]::ReadAllText($source)
    if(-not $content.Contains($fault.Old)){throw "Missing mutation anchor $($fault.Name)"}
    $path=Join-Path $artifact ($fault.Name+'.cs')
    [IO.File]::WriteAllText($path,$content.Replace($fault.Old,$fault.New))
    $property=switch($fault.Kind){entry{'EntrySource'} preload{'PreloadSource'} wrapper{'ProductionLocalStartSource'}}
    $target=if($fault.Kind -eq 'wrapper'){$native}else{$project}
    Run $fault.Name $target @("-p:${property}=$path") '' $false
}
Run 'entry-restored' $project @() 'PFT_TACTICAL_KIT_CHECKS_OK' $true
Run 'native-restored' $native @() 'ARENA_MENU_CHECKS_PASSED' $true
Write-Output "PFT_TACTICAL_ENTRY_FAULTS_OK $($faults.Count) compiled behavioral faults rejected"
