param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$sain = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.dll'))
$shared = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.Preset.Shared.dll'))
try {
    if ($sain.Name.Name -ne 'SAIN' -or $sain.Name.Version.ToString() -ne '4.5.1.0') { throw 'SAIN version not reviewed' }
    $info = $sain.MainModule.GetType('SAIN.SAINComponent.Classes.Info.SAINBotInfoClass')
    $enumName = 'SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality'
    $settingsName = 'SAIN.Preset.Shared.Personalities.BasePersonality.PersonalitySettingsClass'
    $select = @($info.Methods | Where-Object Name -eq 'GetPersonality')
    $setter = @($info.Methods | Where-Object Name -eq 'SetPersonality')
    if ($select.Count -ne 1 -or !$select[0].IsPublic -or $select[0].ReturnType.FullName -ne $enumName -or
        $select[0].Parameters.Count -ne 1 -or !$select[0].Parameters[0].IsOut -or
        $select[0].Parameters[0].Name -ne 'settings' -or $select[0].Parameters[0].ParameterType.FullName -ne ($settingsName + '&')) { throw 'SAIN selection signature drift' }
    if ($setter.Count -ne 1 -or !$setter[0].IsPublic -or $setter[0].ReturnType.FullName -ne 'System.Void' -or
        $setter[0].Parameters.Count -ne 1 -or $setter[0].Parameters[0].Name -ne 'personality' -or
        $setter[0].Parameters[0].ParameterType.FullName -ne $enumName) { throw 'SAIN setter signature drift' }
    foreach ($value in @('Rat', 'Timmy', 'Coward', 'Normal')) {
        if (@($shared.MainModule.GetType($enumName).Fields | Where-Object Name -eq $value).Count -ne 1) { throw "Enum missing: $value" }
    }
    $ownerBase = $sain.MainModule.GetType('SAIN.SAINComponent.BotBase')
    foreach ($pair in @(@('BotOwner','EFT.BotOwner'), @('Player','EFT.Player'))) {
        $property = @($ownerBase.Properties | Where-Object Name -eq $pair[0])
        if ($property.Count -ne 1 -or $property[0].PropertyType.FullName -ne $pair[1] -or !$property[0].GetMethod.IsPublic) { throw "SAIN owner contract drift: $($pair[0])" }
    }
    $preset = @($sain.MainModule.GetType('SAIN.SAINPlugin').Properties | Where-Object Name -eq 'LoadedPreset')
    if ($preset.Count -ne 1 -or !$preset[0].GetMethod.IsStatic -or !$preset[0].GetMethod.IsPublic) { throw 'LoadedPreset getter drift' }
    $manager = @($sain.MainModule.GetType($preset[0].PropertyType.FullName).Properties | Where-Object Name -eq 'PersonalityManager')
    if ($manager.Count -ne 1 -or !$manager[0].GetMethod.IsPublic) { throw 'PersonalityManager getter drift' }
    $dictionary = @($sain.MainModule.GetType($manager[0].PropertyType.FullName).Fields | Where-Object Name -eq 'PersonalityDictionary')
    if ($dictionary.Count -ne 1 -or !$dictionary[0].IsPublic) { throw 'PersonalityDictionary field drift' }
    $baseType = $sain.MainModule.GetType($dictionary[0].FieldType.FullName).BaseType.FullName
    if ($baseType -ne ('System.Collections.Generic.Dictionary`2<' + $enumName + ',' + $settingsName + '>')) { throw 'Personality dictionary pair type drift' }

    $configure = @($info.Methods | Where-Object Name -eq 'ConfigureBot')
    $operands = ($configure[0].Body.Instructions.Operand | ForEach-Object { if ($_ -ne $null) { $_.ToString() } }) -join "`n"
    $selectionPosition = $operands.IndexOf('::GetPersonality(')
    if ($selectionPosition -lt 0) { throw 'ConfigureBot no longer selects personality' }
    foreach ($later in @('::UpdateSettings(', '::CalcTimeBeforeSearch(', '::CalcHoldGroundDelay(')) {
        if ($operands.IndexOf($later) -le $selectionPosition) { throw "SAIN initialization order changed: $later" }
    }
    $setterCalls = ($setter[0].Body.Instructions.Operand | ForEach-Object { if ($_ -ne $null) { $_.ToString() } }) -join "`n"
    foreach ($required in @('::TryGetValue(', '::set_PersonalitySettingsClass(', '::set_Personality(')) {
        if (!$setterCalls.Contains($required)) { throw "SAIN setter no longer installs a consistent pair: $required" }
    }
    if ($setterCalls.Contains('::CalcTimeBeforeSearch(')) { throw 'SAIN explicit setter timer semantics changed' }
    Write-Output 'ARENA_SAIN_PERSONALITY_CONTRACTS_PASSED'
}
finally { $sain.Dispose(); $shared.Dispose() }
