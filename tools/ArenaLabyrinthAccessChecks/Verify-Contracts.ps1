#requires -Version 7.0
param(
    [string] $SptPath = 'D:\SPT41',
    [string] $PatchSource = (Join-Path $PSScriptRoot '../../ArenaMode.Client/Session/ArenaLocationSelectionPatch.cs')
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$labGamePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$labServerPath = Join-Path $SptPath 'SPT_Runtime/SPTarkov.Server.Core.dll'
$labGame = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($labGamePath)
$labServer = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($labServerPath)

function Method($Type, [string] $Name, [string] $Parameters, [string] $ReturnType) {
    $found = @($Type.Methods | Where-Object {
        $_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Parameters
    })
    if ($found.Count -ne 1 -or $found[0].ReturnType.FullName -ne $ReturnType) {
        throw "Installed method contract differs: $($Type.FullName).$Name($Parameters)"
    }
    return $found[0]
}

function Reads-Field($Method, [string] $Name) {
    return @($Method.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq $Name
    }).Count -gt 0
}

try {
    $location = $labGame.MainModule.GetType('JsonType.LocationSettings/Location')
    foreach ($expected in @(
        @{ Name = 'Enabled'; Type = 'System.Boolean' },
        @{ Name = 'ForceOnlineRaidInPVE'; Type = 'System.Boolean' },
        @{ Name = 'IconY'; Type = 'System.Single' },
        @{ Name = 'AccessKeys'; Type = 'System.String[]' }
    )) {
        $fields = @($location.Fields | Where-Object Name -eq $expected.Name)
        if ($fields.Count -ne 1 -or !$fields[0].IsPublic -or $fields[0].FieldType.FullName -ne $expected.Type) {
            throw "Installed location field differs: $($expected.Name)"
        }
    }
    $button = $labGame.MainModule.GetType('EFT.UI.LocationButton')
    $buttonShow = Method $button 'Show' 'JsonType.LocationSettings/Location,EFT.ESideType,System.Boolean,System.Int32,System.Boolean,System.Boolean,System.Action`1<JsonType.LocationSettings/Location>,System.Boolean,System.Boolean' 'System.Void'
    if (($buttonShow.Parameters.Name -join ',') -ne 'location,chosenSideType,onAtStart,playerLevel,isOverloaded,isNew,onSelected,special,showBoss') {
        throw 'Installed button patch parameter names differ.'
    }
    $screen = $labGame.MainModule.GetType('EFT.UI.Matchmaker.MatchMakerSelectionLocationScreen')
    [void](Method $screen 'ShowInternal' 'EFT.RaidSettings' 'System.Void')
    [void](Method $screen 'OnLocationSelected' 'JsonType.LocationSettings/Location' 'System.Void')
    $selected = Method $screen 'set_SelectedLocation' 'JsonType.LocationSettings/Location' 'System.Void'
    foreach ($expected in @(
        @{ Type = 'EFT.RaidSettings'; Name = 'set_SelectedLocation' },
        @{ Type = 'EFT.UI.LocationInfoPanel'; Name = 'Set' }
    )) {
        if (!($selected.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $expected.Name -and $_.Operand.DeclaringType.FullName -eq $expected.Type
        })) { throw 'Selected-location setter no longer forwards the selected object to raid settings/info panel.' }
    }
    $raid = $labGame.MainModule.GetType('EFT.RaidSettings')
    $online = Method $raid 'get_NeedToGoOnlineInPve' '' 'System.Boolean'
    $panel = $labGame.MainModule.GetType('EFT.UI.LocationInfoPanel')
    $toggle = Method $panel 'SetupPveServerModeToggle' 'System.Boolean' 'System.Void'
    if (!(Reads-Field $online 'ForceOnlineRaidInPVE') -or !(Reads-Field $toggle 'ForceOnlineRaidInPVE')) {
        throw 'Installed local-PVE consumers no longer use projected location metadata.'
    }
    $menu = $labGame.MainModule.GetType('EFT.MainMenuShowOperation')
    $keys = Method $menu 'method_54' '' 'System.Boolean'
    if (!(Reads-Field $keys 'AccessKeys')) { throw 'Installed keycard gate no longer reads AccessKeys.' }

    $lifecycle = $labServer.MainModule.GetType('SPTarkov.Server.Core.Services.InRaid.LocationLifecycleService')
    $generate = Method $lifecycle 'GenerateLocationAndLoot' 'SPTarkov.Server.Core.Models.Common.MongoId,System.String,System.Boolean' 'SPTarkov.Server.Core.Models.Eft.Common.LocationBase'
    if (@($generate.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'AsyncStateMachineAttribute' }).Count -ne 0 -or
        !($generate.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Clone' -and
            $_.Operand.DeclaringType.FullName -eq 'SPTarkov.Server.Core.Utils.Cloners.ICloner'
        })) { throw 'Server location mutation is no longer on the reviewed synchronous clone boundary.' }

    # These source integration checks complement the linked policy tests. They
    # deliberately do not claim to execute Unity navigation or Harmony here.
    $source = [IO.File]::ReadAllText($PatchSource)
    $selectedStart = $source.IndexOf('private sealed class ArenaLocationSelectionSelectedValuePatch', [StringComparison]::Ordinal)
    $buttonStart = $source.IndexOf('private sealed class ArenaLocationSelectionButtonLocationPatch', [StringComparison]::Ordinal)
    $buttonEnd = $source.IndexOf('private sealed class ArenaLocationSelectionFactoryTimePatch', [StringComparison]::Ordinal)
    if ($selectedStart -lt 0 -or $buttonStart -le $selectedStart -or $buttonEnd -le $buttonStart) {
        throw 'Production map projection patch layout changed; review new hook boundaries.'
    }
    $selectionHook = $source.Substring($selectedStart, $buttonStart - $selectedStart)
    $buttonHook = $source.Substring($buttonStart, $buttonEnd - $buttonStart)
    if (!$selectionHook.Contains('value = ArenaLabyrinthAccess.Project(Controller(__instance)?.RaidSettings, value);')) {
        throw 'Selected-location projection hook is missing.'
    }
    foreach ($required in @(
        'onSelected?.Target is not MatchMakerSelectionLocationScreen screen',
        'onSelected.Method.DeclaringType != typeof(MatchMakerSelectionLocationScreen)',
        '!ArenaLabyrinthAccess.CanProject(Controller(screen)?.RaidSettings, location)',
        'location = ArenaLabyrinthAccess.Project(Controller(screen)?.RaidSettings, location);',
        'typeof(System.Action<LocationSettings.Location>)'
    )) {
        if (!$buttonHook.Contains($required)) { throw "Button ownership/signature projection hook is missing: $required" }
    }
    if (!$source.Contains('new ArenaLocationSelectionButtonLocationPatch().Enable();')) { throw 'Button projection patch is not registered.' }
    Write-Output ('Game SHA256 ' + (Get-FileHash -LiteralPath $labGamePath -Algorithm SHA256).Hash)
    Write-Output ('SPT core SHA256 ' + (Get-FileHash -LiteralPath $labServerPath -Algorithm SHA256).Hash)
    Write-Output 'ARENA_LABYRINTH_ACCESS_CONTRACTS_PASSED'
}
finally { $labGame.Dispose(); $labServer.Dispose() }
