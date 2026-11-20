#requires -Version 7.0
param([string]$SptPath = 'D:/SPT41', [string]$DynamicMapsPath)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$path = if ($DynamicMapsPath) { [IO.Path]::GetFullPath($DynamicMapsPath) } else { Join-Path $SptPath 'BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll' }
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    $module = $assembly.MainModule
    $view = $module.GetType('DynamicMaps.UI.Components.MapView')
    $marker = $module.GetType('DynamicMaps.UI.Components.MapMarker')
    $def = $module.GetType('DynamicMaps.Data.MapMarkerDef')
    # Mirror the production shape selection, then validate the entire chosen API.
    # The version identifies the evidence; it never substitutes for a member check.
    $modern = @($view.Methods | Where-Object { $_.Name -eq 'LoadMap' -and $_.IsPublic -and !$_.IsStatic -and
        (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq 'DynamicMaps.Data.MapDef' }).Count -ne 0
    $loadParameters = if ($modern) { 'DynamicMaps.Data.MapDef' } else { 'DynamicMaps.Data.MapDef,UnityEngine.RectTransform' }
    $visualAccess = if ($modern) { 'Private' } else { 'Public' }
    $alphaAccess = if ($modern) { 'Family' } else { 'Public' }
    foreach ($contract in @(
        @('LoadMap', 'System.Void', $loadParameters),
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
        @($marker, 'Label', 'TMPro.TextMeshProUGUI', $false, $visualAccess),
        @($marker, 'Image', 'UnityEngine.UI.Image', $false, $visualAccess),
        @($marker, 'LabelAlphaLayerStatus', 'System.Collections.Generic.Dictionary`2<DynamicMaps.UI.Components.LayerStatus,System.Single>', $false, $alphaAccess))) {
        $p = @($contract[0].Properties | Where-Object Name -eq $contract[1])
        $access = if ($contract.Count -gt 4) { $contract[4] } else { 'Public' }
        if ($p.Count -ne 1 -or $p[0].PropertyType.FullName -ne $contract[2] -or $p[0].Parameters.Count -ne 0 -or
            !$p[0].GetMethod -or $p[0].GetMethod.IsStatic -or
            ($p[0].GetMethod.Attributes -band [Mono.Cecil.MethodAttributes]::MemberAccessMask).ToString() -ne $access -or
            ($contract[3] -and !$p[0].SetMethod.IsPublic)) { throw "Installed DynamicMaps property mismatch: $($contract[1])" }
    }
    $remove = ($view.Methods | Where-Object Name -eq 'RemoveMapMarker').Body.Instructions.Operand | Out-String
    foreach ($call in @('MapMarker::remove_OnPositionChanged', 'UnityEngine.GameObject::SetActive', 'UnityEngine.Object::Destroy')) {
        if (!$remove.Contains($call)) { throw "Native removal contract changed: $call" }
    }
    if (!(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $path) 'Markers/dot.png'))) { throw 'Native dot icon absent.' }
    Write-Output ('DynamicMaps ' + $assembly.Name.Version + ' SHA256 ' + (Get-FileHash -LiteralPath $path).Hash)
    Write-Output 'DOMINATION_MAP_INSTALLED_CONTRACTS_PASSED'
} finally { $assembly.Dispose() }
