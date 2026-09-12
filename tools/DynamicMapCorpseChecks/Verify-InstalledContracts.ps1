#requires -Version 7.0
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$mapPath = Join-Path $SptPath 'BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll'
$mapAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($mapPath)
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
function Assert-Calls($Type, [string]$Method, [string[]]$Calls) {
    $found = @($Type.Methods | Where-Object Name -eq $Method)
    if ($found.Count -ne 1) { throw "Ambiguous method $($Type.FullName).$Method" }
    $body = $found[0].Body.Instructions.Operand | Out-String
    foreach ($call in $Calls) { if (!$body.Contains($call)) { throw "Missing reviewed call in ${Method}: $call" } }
}
try {
    $identity = @($mapAssembly.MainModule.GetType('DynamicMaps.Plugin').CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' })
    if ($identity.Count -ne 1 -or $identity[0].ConstructorArguments[0].Value -ne 'com.mpstark.dynamicmaps' -or
        $identity[0].ConstructorArguments[2].Value -ne '1.2.1') { throw 'DynamicMaps installed identity/version differs from reviewed 1.2.1.' }
    $provider = $mapAssembly.MainModule.GetType('DynamicMaps.DynamicMarkers.CorpseMarkerProvider')
    $markers = @($provider.Fields | Where-Object Name -eq '_corpseMarkers')
    if ($markers.Count -ne 1 -or !$markers[0].IsPrivate -or $markers[0].IsStatic -or
        $markers[0].FieldType.FullName -ne 'System.Collections.Generic.Dictionary`2<EFT.Player,DynamicMaps.UI.Components.MapMarker>') { throw 'DynamicMaps marker identity table drift.' }
    foreach ($name in @('TryAddMarker', 'TryRemoveMarker')) {
        $method = @($provider.Methods | Where-Object Name -eq $name)
        if ($method.Count -ne 1 -or !$method[0].IsPrivate -or $method[0].IsStatic -or $method[0].ReturnType.FullName -ne 'System.Void' -or
            $method[0].Parameters.Count -ne 1 -or $method[0].Parameters[0].ParameterType.FullName -ne 'EFT.Player') { throw "DynamicMaps method drift: $name" }
    }
    $player = $gameAssembly.MainModule.GetType('EFT.Player')
    $corpse = @($player.Fields | Where-Object Name -eq 'Corpse')
    if ($corpse.Count -ne 1 -or !$corpse[0].IsFamily -or $corpse[0].IsStatic -or $corpse[0].FieldType.FullName -ne 'EFT.Interactive.Corpse') { throw 'Native exact Player.Corpse identity drift.' }
    $addCall = 'CorpseMarkerProvider::TryAddMarker(EFT.Player)'
    foreach ($method in @('TryAddMarkers', 'RefreshMarkers', 'OnMapChanged', 'OnUnregisterPlayer')) { Assert-Calls $provider $method @($addCall) }
    Assert-Calls $provider 'OnShowInRaid' @('CorpseMarkerProvider::TryAddMarkers()')
    Assert-Calls $provider 'HandleSetBoolOption' @('CorpseMarkerProvider::TryAddMarkers()')
    Assert-Calls $provider 'TryAddMarkers' @('EFT.GameWorld::get_AllPlayersEverExisted()')
    Assert-Calls $provider 'TryRemoveMarker' @('MapView::RemoveMapMarker(', '::Remove(!0)')
    $mapView = $mapAssembly.MainModule.GetType('DynamicMaps.UI.Components.MapView')
    Assert-Calls $mapView 'RemoveMapMarker' @('MapMarker::remove_OnPositionChanged', 'UnityEngine.GameObject::SetActive', 'UnityEngine.Object::Destroy')
    $removeBodies = @(
        ($provider.Methods | Where-Object Name -eq 'TryRemoveMarker').Body.Instructions.Operand
        ($mapView.Methods | Where-Object Name -eq 'RemoveMapMarker').Body.Instructions.Operand
    ) | Out-String
    foreach ($forbidden in @('EFT.GameWorld::', 'EFT.Player::', 'EFT.Interactive.Corpse::', 'EFT.BotSpawner::')) {
        if ($removeBodies.Contains($forbidden)) { throw "Reviewed UI removal now reaches native lifetime owner: $forbidden" }
    }
    Write-Output ('DynamicMaps 1.2.1 SHA256 ' + (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash)
    Write-Output 'ARENA_DYNAMICMAP_CORPSE_INSTALLED_CONTRACTS_PASSED'
}
finally { $mapAssembly.Dispose(); $gameAssembly.Dispose() }
