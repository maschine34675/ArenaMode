#requires -Version 7.0
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$brightPath = Join-Path $SptPath 'BepInEx/plugins/acidphantasm-brightlasers/acidphantasm-brightlasers.dll'
try {
    $laser = $game.MainModule.GetType('LaserBeam')
    $weapon = $game.MainModule.GetType('WeaponPrefab')
    $tactical = $game.MainModule.GetType('TacticalComboVisualController')
    foreach ($spec in @(
        @($laser, '_beamBlock', 'UnityEngine.MaterialPropertyBlock'),
        @($laser, '_pointBlock', 'UnityEngine.MaterialPropertyBlock'),
        @($laser, '_light', 'UnityEngine.Light'),
        @($laser, 'BeamMaterial', 'UnityEngine.Material'),
        @($laser, 'PointMaterial', 'UnityEngine.Material'),
        @($laser, 'IntensityFactor', 'System.Single'),
        @($laser, 'LightIntensity', 'System.Single'),
        @($weapon, '_player', 'EFT.IPlayer'),
        @($tactical, 'LightMod', 'EFT.InventoryLogic.LightComponent')
    )) {
        $field = @($spec[0].Fields | Where-Object Name -eq $spec[1])
        if ($field.Count -ne 1 -or $field[0].FieldType.FullName -ne $spec[2]) { throw "Native laser field drift: $($spec[1])" }
    }
    $late = @($laser.Methods | Where-Object { $_.Name -eq 'LateUpdate' -and $_.Parameters.Count -eq 0 })
    $instructions = $late[0].Body.Instructions
    $draws = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('UnityEngine.Graphics::DrawMesh(') })
    if ($late.Count -ne 1 -or $draws.Count -ne 2 -or @($draws | Where-Object { -not $_.Operand.ToString().Contains('UnityEngine.MaterialPropertyBlock') }).Count) {
        throw 'Native laser no longer draws exactly beam and point with property blocks.'
    }
    $sets = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('UnityEngine.MaterialPropertyBlock::SetFloat(') })
    $colors = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('::SetColor(') })
    if ($sets.Count -ne 7 -or $colors.Count -ne 0) { throw 'Native property-block writes differ from reviewed distance/intensity/size contract.' }
    $nativeBody = $instructions.Operand | Out-String
    if (!$nativeBody.Contains('LaserBeam::IntensityFactor') -or !$nativeBody.Contains('LaserBeam::LightIntensity') -or
        !$nativeBody.Contains('UnityEngine.Light::set_intensity')) { throw 'Native laser intensity contract drifted.' }
    if (@($instructions | Where-Object { $_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -in @('IntensityFactor','LightIntensity') }).Count) {
        throw 'Native LateUpdate now changes persistent intensity scalars; restoration requires review.'
    }
    $awake = $laser.Methods | Where-Object Name -eq 'Awake'
    if (@($awake.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('LaserBeam::CreateLight()') }).Count -ne 1) {
        throw 'Awake no longer precedes exactly one native CreateLight call.'
    }
    $create = $laser.Methods | Where-Object Name -eq 'CreateLight'
    if (-not (($create.Body.Instructions.Operand | Out-String).Contains('UnityEngine.Light::set_color'))) { throw 'Native point light no longer receives its material color during creation.' }
    if (Test-Path -LiteralPath $brightPath) {
        $bright = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($brightPath)
        try {
            $postfix = $bright.MainModule.GetType('BrightLasers.Patches.LaserBeamLateUpdatePatch').Methods | Where-Object Name -eq 'Postfix'
            $body = $postfix.Body.Instructions.Operand | Out-String
            if (!$body.Contains('UnityEngine.Material::SetColor') -or $body.Contains('MaterialPropertyBlock')) { throw 'Installed BrightLasers draw contract differs; review compatibility before updating baseline.' }
            if (!$body.Contains('BrightLasersConfig::OverrideLaserColor') -or !$body.Contains('BrightLasersConfig::RGBCycle')) {
                throw 'Installed BrightLasers no longer gates its material recoloring on override/RGB.'
            }
            $creation = $bright.MainModule.GetType('BrightLasers.Patches.LaserBeamCreateLightPatch').Methods | Where-Object Name -eq 'Postfix'
            $creationBody = $creation.Body.Instructions.Operand | Out-String
            foreach ($required in @('UnityEngine.Object::Instantiate<UnityEngine.Material>', 'UnityEngine.Material::SetFloat', 'LaserBeam::IntensityFactor', 'LaserBeam::LightIntensity', 'BrightLasersConfig::IntensityMultiplier', 'BrightLasersConfig::FactorMultiplier')) {
                if (!$creationBody.Contains($required)) { throw "Installed BrightLasers creation contract drift: $required" }
            }
            if ($creationBody.Contains('MaterialPropertyBlock')) { throw 'BrightLasers now writes property blocks during creation.' }
            $beamAwake = $bright.MainModule.GetType('BrightLasers.Patches.LaserBeamAwakePatch').Methods | Where-Object Name -eq 'Prefix'
            $stores = @($beamAwake.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Stfld' })
            if ($stores.Count -ne 1 -or $stores[0].Operand.Name -ne 'BeamSize') { throw 'BrightLasers now changes more than beam width before native Awake.' }
            $plugin = $bright.MainModule.Types | Where-Object { @($_.CustomAttributes | Where-Object AttributeType -match 'BepInPlugin').Count -gt 0 }
            $identity = $plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }
            if ($identity.ConstructorArguments[2].Value -ne '3.0.0') { throw 'BrightLasers installed version differs from reviewed 3.0.0.' }
            Write-Output ('BrightLasers SHA256 ' + (Get-FileHash -LiteralPath $brightPath -Algorithm SHA256).Hash)
        }
        finally { $bright.Dispose() }
    }
    & python (Join-Path $PSScriptRoot 'Verify-Bundles.py') $SptPath
    if ($LASTEXITCODE -ne 0) { throw 'Native visible/IR shader/material metadata failed.' }
    Write-Output 'ARENA_TEAM_LASER_CONTRACTS_PASSED'
}
finally { $game.Dispose() }
