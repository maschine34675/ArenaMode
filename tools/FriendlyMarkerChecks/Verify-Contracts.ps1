#requires -Version 7.0
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$managed = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
$unity = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.CoreModule.dll'))
$physics = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.PhysicsModule.dll'))
$uiModule = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.UIModule.dll'))
$ui = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.UI.dll'))
try {
    foreach ($spec in @(
        @('PlayerBones', 'Head', 'EFT.BifacialTransform'),
        @('EFT.BifacialTransform', 'Original', 'UnityEngine.Transform'),
        @('EFT.GameWorld', 'AllAlivePlayersList', 'System.Collections.Generic.List`1<EFT.Player>'),
        @('LayersMaskController', 'HighPolyWithTerrainMask', 'UnityEngine.LayerMask'),
        @('LayersMaskController', 'LowPolyColliderLayerMask', 'UnityEngine.LayerMask'),
        @('LayersMaskController', 'PlayerStaticDoorMask', 'UnityEngine.LayerMask'),
        @('LayersMaskController', 'TransparentLayerMask', 'UnityEngine.LayerMask')
    )) {
        $type = $game.MainModule.GetType($spec[0]); $field = @($type.Fields | Where-Object Name -eq $spec[1])
        if ($field.Count -ne 1 -or $field[0].FieldType.FullName -ne $spec[2]) { throw "Native marker field drift: $($spec[0]).$($spec[1])" }
    }
    $manager = $game.MainModule.GetType('EFT.CameraControl.CameraManager')
    $bone = $game.MainModule.GetType('EFT.BifacialTransform')
    $pose = @($bone.Methods | Where-Object Name -eq 'get_position')
    if ($pose.Count -ne 1 -or $pose[0].ReturnType.FullName -ne 'UnityEngine.Vector3') { throw 'Native head pose getter changed.' }
    $poseReads = $pose[0].Body.Instructions.Operand | Out-String
    foreach ($field in @('_accumulatePositionAndRotation', '_accumulatedPosition', '_useImitation', '_positionImitator')) {
        if (!$poseReads.Contains($field)) { throw "Native virtualized head pose path missing: $field" }
    }
    foreach ($name in @('Exist', 'Instance', 'Camera')) {
        if (@($manager.Properties | Where-Object Name -eq $name).Count -ne 1) { throw "Native CameraManager.$name missing" }
    }
    $camera = $unity.MainModule.GetType('UnityEngine.Camera')
    $canvas = $uiModule.MainModule.GetType('UnityEngine.Canvas')
    if (@($canvas.Events | Where-Object Name -eq 'preWillRenderCanvases').Count -ne 1) { throw 'Native pre-rebuild canvas callback missing.' }
    $force = ($canvas.Methods | Where-Object Name -eq 'ForceUpdateCanvases').Body.Instructions.Operand | Out-String
    if ($force.IndexOf('SendPreWillRenderCanvases') -lt 0 -or $force.IndexOf('SendWillRenderCanvases') -le $force.IndexOf('SendPreWillRenderCanvases')) { throw 'Native canvas callback order changed.' }
    $registry = $ui.MainModule.GetType('UnityEngine.UI.CanvasUpdateRegistry')
    $ctor = ($registry.Methods | Where-Object Name -eq '.ctor').Body.Instructions.Operand | Out-String
    if (!$ctor.Contains('add_willRenderCanvases') -or !$ctor.Contains('PerformUpdate')) { throw 'Native UI rebuild callback changed.' }
    $graphic = $ui.MainModule.GetType('UnityEngine.UI.Graphic')
    $disable = ($graphic.Methods | Where-Object Name -eq 'OnDisable').Body.Instructions.Operand | Out-String
    $enable = ($graphic.Methods | Where-Object Name -eq 'OnEnable').Body.Instructions.Operand | Out-String
    if (!$disable.Contains('CanvasRenderer::Clear') -or !$enable.Contains('SetAllDirty')) { throw 'Native UI geometry invalidation changed.' }
    foreach ($method in @('WorldToViewportPoint', 'WorldToScreenPoint')) {
        if (@($camera.Methods | Where-Object { $_.Name -eq $method -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Vector3' }).Count -ne 1) { throw "Camera.$method(Vector3) unavailable" }
    }
    $linecast = @($physics.MainModule.GetType('UnityEngine.Physics').Methods | Where-Object {
        $_.Name -eq 'Linecast' -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq 'UnityEngine.Vector3,UnityEngine.Vector3,System.Int32,UnityEngine.QueryTriggerInteraction'
    })
    if ($linecast.Count -ne 1 -or $linecast[0].ReturnType.FullName -ne 'System.Boolean') { throw 'Native occlusion query contract unavailable.' }
    $layerCtor = $game.MainModule.GetType('LayersMaskController').Methods | Where-Object Name -eq '.cctor'
    $layerData = $layerCtor.Body.Instructions.Operand | Out-String
    foreach ($layerName in @('Terrain', 'HighPolyCollider', 'DoorLowPolyCollider', 'LowPolyCollider', 'TransparentCollider')) {
        if (!$layerData.Contains($layerName)) { throw "Native occlusion layer missing: $layerName" }
    }
    $world = $game.MainModule.GetType('EFT.GameWorld')
    foreach ($method in @('GetAlivePlayerByProfileID', 'GetEverExistedPlayerByID')) {
        $found = @($world.Methods | Where-Object { $_.Name -eq $method -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.String' })
        if ($found.Count -ne 1 -or $found[0].ReturnType.FullName -ne 'EFT.Player') { throw "Native current body lookup differs: $method" }
    }
    Write-Output 'ARENA_FRIENDLY_MARKER_CONTRACTS_PASSED'
}
finally { $game.Dispose(); $unity.Dispose(); $physics.Dispose(); $uiModule.Dispose(); $ui.Dispose() }
