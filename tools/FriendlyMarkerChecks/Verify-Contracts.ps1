#requires -Version 7.0
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$managed = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
$unity = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.CoreModule.dll'))
$physics = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'UnityEngine.PhysicsModule.dll'))
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
    foreach ($name in @('Exist', 'Instance', 'Camera')) {
        if (@($manager.Properties | Where-Object Name -eq $name).Count -ne 1) { throw "Native CameraManager.$name missing" }
    }
    $camera = $unity.MainModule.GetType('UnityEngine.Camera')
    if (@($camera.Fields | Where-Object { $_.Name -eq 'onPreCull' -and $_.IsStatic -and $_.FieldType.FullName -eq 'UnityEngine.Camera/CameraCallback' }).Count -ne 1) { throw 'Native exact-camera pre-cull callback drifted.' }
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
finally { $game.Dispose(); $unity.Dispose(); $physics.Dispose() }
