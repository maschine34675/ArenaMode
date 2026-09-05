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
    $create = $laser.Methods | Where-Object Name -eq 'CreateLight'
    if (-not (($create.Body.Instructions.Operand | Out-String).Contains('UnityEngine.Light::set_color'))) { throw 'Native point light no longer receives its material color during creation.' }
    if (Test-Path -LiteralPath $brightPath) {
        $bright = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($brightPath)
        try {
            $postfix = $bright.MainModule.GetType('BrightLasers.Patches.LaserBeamLateUpdatePatch').Methods | Where-Object Name -eq 'Postfix'
            $body = $postfix.Body.Instructions.Operand | Out-String
            if (!$body.Contains('UnityEngine.Material::SetColor') -or $body.Contains('MaterialPropertyBlock')) { throw 'Installed BrightLasers draw contract differs; review compatibility before updating baseline.' }
            Write-Output ('BrightLasers SHA256 ' + (Get-FileHash -LiteralPath $brightPath -Algorithm SHA256).Hash)
        }
        finally { $bright.Dispose() }
    }
    & python (Join-Path $PSScriptRoot 'Verify-Bundles.py') $SptPath
    if ($LASTEXITCODE -ne 0) { throw 'Native visible/IR shader/material metadata failed.' }
    Write-Output 'ARENA_TEAM_LASER_CONTRACTS_PASSED'
}
finally { $game.Dispose() }
