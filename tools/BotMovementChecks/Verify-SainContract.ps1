param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$sainPath = Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($sainPath)

function Require-Type([string]$Name, [string]$BaseName) {
    $type = $assembly.MainModule.GetType($Name)
    if ($null -eq $type -or $type.BaseType.FullName -ne $BaseName) { throw "SAIN inheritance changed: $Name" }
    return $type
}

function Require-FieldGetter($Type, [string]$Name, [string]$ResultType) {
    $properties = @($Type.Properties | Where-Object Name -eq $Name)
    if ($properties.Count -ne 1) { throw "SAIN property missing/ambiguous: $Name" }
    $property = $properties[0]
    $getter = $property.GetMethod
    if ($property.PropertyType.FullName -ne $ResultType -or $property.Parameters.Count -ne 0 -or
        $null -eq $getter -or !$getter.IsPublic -or $getter.IsStatic -or ($getter.IsVirtual -and !$getter.IsFinal) -or
        $getter.Parameters.Count -ne 0 -or !$getter.HasBody -or
        $getter.Body.Variables.Count -ne 0 -or $getter.Body.ExceptionHandlers.Count -ne 0) {
        throw "SAIN getter signature changed: $($Type.FullName).$Name"
    }
    $il = @($getter.Body.Instructions)
    if ($il.Count -ne 3 -or $il[0].OpCode.Name -ne 'ldarg.0' -or
        $il[1].OpCode.Name -ne 'ldfld' -or $il[2].OpCode.Name -ne 'ret' -or
        $il[1].Operand.DeclaringType.FullName -ne $Type.FullName -or
        $il[1].Operand.FieldType.FullName -ne $ResultType -or
        $il[1].Operand.Name -ne "<$Name>k__BackingField") {
        throw "SAIN read-only getter body changed: $($Type.FullName).$Name"
    }
}

function Require-Method($Type, [string]$Name) {
    $methods = @($Type.Methods | Where-Object Name -eq $Name)
    if ($methods.Count -ne 1 -or !$methods[0].HasBody) { throw "SAIN method changed: $($Type.FullName).$Name" }
    return $methods[0]
}

try {
    if ($assembly.Name.Name -ne 'SAIN' -or $assembly.Name.Version.ToString() -ne '4.5.1.0') {
        throw 'SAIN 4.5.1.0 installed metadata is required for this reviewed contract'
    }
    $component = Require-Type 'SAIN.Components.BotComponent' 'SAIN.Components.BotComponentBase'
    $componentBase = Require-Type 'SAIN.Components.BotComponentBase' 'UnityEngine.MonoBehaviour'
    $door = Require-Type 'SAIN.SAINComponent.Classes.Mover.DoorOpener' 'SAIN.SAINComponent.BotComponentClassBase'
    $doorBase = Require-Type 'SAIN.SAINComponent.BotComponentClassBase' 'SAIN.SAINComponent.BotBase'
    $botBase = Require-Type 'SAIN.SAINComponent.BotBase' 'System.Object'
    Require-FieldGetter $componentBase 'BotOwner' 'EFT.BotOwner'
    Require-FieldGetter $componentBase 'Player' 'EFT.Player'
    Require-FieldGetter $component 'DoorOpener' $door.FullName
    Require-FieldGetter $botBase 'Bot' $component.FullName
    Require-FieldGetter $door 'Interacting' 'System.Boolean'

    # Prove that this property encloses SAIN's temporary door-collision ignore.
    foreach ($case in @(@('TryInteractWithDoor', 'ldc.i4.1'), @('Clear', 'ldc.i4.0'))) {
        $method = Require-Method $door $case[0]
        $instructions = @($method.Body.Instructions)
        $interactionIndex = -1
        $collisionIndex = -1
        for ($i = 1; $i -lt $instructions.Count; $i++) {
            $instruction = $instructions[$i]
            if ($instruction.OpCode.Name -notin @('call', 'callvirt')) { continue }
            $operand = $instruction.Operand.ToString()
            if ($operand -eq 'System.Void SAIN.SAINComponent.Classes.Mover.DoorOpener::set_Interacting(System.Boolean)') {
                if ($instructions[$i - 1].OpCode.Name -ne $case[1]) { throw "SAIN interaction value changed: $($case[0])" }
                $interactionIndex = $i
            }
            if ($operand -eq 'System.Void EFT.MovementContext::IgnoreInteractionCollision(UnityEngine.Collider,System.Boolean)') {
                if ($instructions[$i - 1].OpCode.Name -ne $case[1]) { throw "SAIN door collision value changed: $($case[0])" }
                $collisionIndex = $i
            }
        }
        if ($interactionIndex -lt 0 -or $collisionIndex -lt 0 -or
            ($case[0] -eq 'TryInteractWithDoor' -and $interactionIndex -ge $collisionIndex) -or
            ($case[0] -eq 'Clear' -and $interactionIndex -le $collisionIndex)) {
            throw "SAIN interaction flag no longer encloses collision ignore: $($case[0])"
        }
    }

    # BotComponent is attached to the same GameObject queried by AllowsBoost.
    $spawn = $assembly.MainModule.GetType('SAIN.Components.BotController.BotSpawnController')
    if ($null -eq $spawn) { throw 'SAIN bot spawn controller changed' }
    $attachmentFound = $false
    foreach ($method in $spawn.Methods | Where-Object { $_.Name -eq 'AddBot' -and $_.HasBody -and
        !$_.IsStatic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.BotOwner' }) {
        $instructions = @($method.Body.Instructions)
        for ($i = 2; $i -lt $instructions.Count; $i++) {
            $operand = $instructions[$i].Operand
            if ($null -ne $operand -and $operand.ToString() -eq
                '!!0 UnityEngine.GameObject::AddComponent<SAIN.Components.BotComponent>()' -and
                $instructions[$i - 1].Operand.ToString() -eq 'UnityEngine.GameObject UnityEngine.Component::get_gameObject()' -and
                $instructions[$i - 2].OpCode.Name -eq 'ldarg.1') {
                $attachmentFound = $true
            }
        }
    }
    if (!$attachmentFound) { throw 'SAIN component attachment contract changed' }
    Write-Output ('SAIN SHA256: ' + (Get-FileHash -LiteralPath $sainPath -Algorithm SHA256).Hash)
    Write-Output 'BOT_MOVEMENT_SAIN_CONTRACT_PASSED'
}
finally { $assembly.Dispose() }
