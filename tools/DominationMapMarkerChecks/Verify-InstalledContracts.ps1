#requires -Version 7.0
param([string]$SptPath = 'D:/SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$path = Join-Path $SptPath 'BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    $module = $assembly.MainModule
    $view = $module.GetType('DynamicMaps.UI.Components.MapView')
    $marker = $module.GetType('DynamicMaps.UI.Components.MapMarker')
    $def = $module.GetType('DynamicMaps.Data.MapMarkerDef')
    foreach ($contract in @(
        @('LoadMap', 'System.Void', 'DynamicMaps.Data.MapDef,UnityEngine.RectTransform'),
        @('UnloadMap', 'System.Void', ''),
        @('AddMapMarker', 'DynamicMaps.UI.Components.MapMarker', 'DynamicMaps.Data.MapMarkerDef'),
        @('RemoveMapMarker', 'System.Void', 'DynamicMaps.UI.Components.MapMarker'))) {
        $methods = @($view.Methods | Where-Object { $_.Name -eq $contract[0] -and $_.IsPublic -and !$_.IsStatic -and
            $_.ReturnType.FullName -eq $contract[1] -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $contract[2] })
        if ($methods.Count -ne 1) { throw "Installed DynamicMaps method mismatch: $($contract[0])" }
    }
    foreach ($contract in @(
        @($view, 'CurrentMapDef', 'DynamicMaps.Data.MapDef', $false),
        @($module.GetType('DynamicMaps.Data.MapDef'), 'MapInternalNames', 'System.Collections.Generic.List`1<System.String>', $false),
        @($def, 'Text', 'System.String', $true), @($def, 'Category', 'System.String', $true), @($def, 'ImagePath', 'System.String', $true),
        @($def, 'Position', 'UnityEngine.Vector3', $true), @($marker, 'Position', 'UnityEngine.Vector3', $true),
        @($marker, 'Color', 'UnityEngine.Color', $true), @($marker, 'Size', 'UnityEngine.Vector2', $true),
        @($marker, 'Label', 'TMPro.TextMeshProUGUI', $false),
        @($marker, 'LabelAlphaLayerStatus', 'System.Collections.Generic.Dictionary`2<DynamicMaps.UI.Components.LayerStatus,System.Single>', $false))) {
        $p = @($contract[0].Properties | Where-Object Name -eq $contract[1])
        if ($p.Count -ne 1 -or $p[0].PropertyType.FullName -ne $contract[2] -or !$p[0].GetMethod.IsPublic -or
            ($contract[3] -and !$p[0].SetMethod.IsPublic)) { throw "Installed DynamicMaps property mismatch: $($contract[1])" }
    }
    $remove = ($view.Methods | Where-Object Name -eq 'RemoveMapMarker').Body.Instructions.Operand | Out-String
    foreach ($call in @('MapMarker::remove_OnPositionChanged', 'UnityEngine.GameObject::SetActive', 'UnityEngine.Object::Destroy')) {
        if (!$remove.Contains($call)) { throw "Native removal contract changed: $call" }
    }
    if (!(Test-Path -LiteralPath (Join-Path $SptPath 'BepInEx/plugins/mpstark-dynamicmaps/Markers/dot.png'))) { throw 'Native dot icon absent.' }
    Write-Output ('DynamicMaps installed SHA256 ' + (Get-FileHash -LiteralPath $path).Hash)
    Write-Output 'DOMINATION_MAP_INSTALLED_CONTRACTS_PASSED'
} finally { $assembly.Dispose() }
