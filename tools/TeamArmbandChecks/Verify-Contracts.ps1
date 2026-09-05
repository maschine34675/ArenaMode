#requires -Version 7.0
param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$armServerPath = Join-Path $SptPath 'SPT_Runtime/SPTarkov.Server.Core.dll'
$armApbsPath = Join-Path $SptPath 'SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/acidphantasm-progressivebotsystem.dll'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$armServer = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($armServerPath)
$armApbs = $null

function Find-Method($Type, [string] $Name) {
    $found = @($Type.Methods | Where-Object Name -eq $Name)
    if ($found.Count -ne 1) { throw "Expected one method $($Type.FullName).$Name" }
    return $found[0]
}

function Calls-Method($Method, [string] $Type, [string] $Name) {
    return @($Method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $Type -and $_.Operand.Name -eq $Name
    }).Count -gt 0
}

try {
    $generator = $armServer.MainModule.GetType('SPTarkov.Server.Core.Generators.Bot.BotGenerator')
    $outer = Find-Method $generator 'PrepareAndGenerateBot'
    $inner = Find-Method $generator 'GenerateBot'
    $controller = Find-Method ($armServer.MainModule.GetType('SPTarkov.Server.Core.Controllers.BotController')) 'TryGenerateSingleBot'
    $argsExpected = 'SPTarkov.Server.Core.Models.Common.MongoId,SPTarkov.Server.Core.Models.Spt.Bots.BotGenerationDetails'
    if (($outer.Parameters.ParameterType.FullName -join ',') -ne $argsExpected -or
        $outer.ReturnType.FullName -ne 'SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBase' -or
        @($outer.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'AsyncStateMachineAttribute' }).Count -ne 0 -or
        !(Calls-Method $outer $generator.FullName 'GenerateBot') -or
        !(Calls-Method $inner 'SPTarkov.Server.Core.Generators.Bot.BotInventoryGenerator' 'GenerateInventory') -or
        !(Calls-Method $controller $generator.FullName 'PrepareAndGenerateBot')) {
        throw 'Installed synchronous outer bot-generation contract differs from the reviewed boundary.'
    }
    $calls = @($controller.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })
    $generationCall = @($calls | Where-Object {
        $_.Operand.DeclaringType.FullName -eq $generator.FullName -and $_.Operand.Name -eq 'PrepareAndGenerateBot'
    })
    $cacheCall = @($calls | Where-Object { $_.Operand.Name -eq 'CacheBot' })
    if ($generationCall.Count -ne 1 -or $cacheCall.Count -ne 1 -or $generationCall[0].Offset -ge $cacheCall[0].Offset) {
        throw 'Installed controller no longer caches the generated result after the armband boundary.'
    }

    # APBS is optional. When installed, validate its actual replacement remains
    # inside our outer synchronous return boundary, without executing its code.
    if (Test-Path -LiteralPath $armApbsPath) {
        $armApbs = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($armApbsPath)
        $patch = $armApbs.MainModule.GetType('ProgressiveBotSystem.Patches.GenerateInventoryPatch')
        $target = Find-Method $patch 'GetTargetMethod'
        $prefix = Find-Method $patch 'Prefix'
        if ($prefix.ReturnType.FullName -ne 'System.Boolean' -or
            !($target.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'GenerateInventory' }) -or
            !($target.Body.Instructions | Where-Object {
                $_.Operand -is [Mono.Cecil.TypeReference] -and $_.Operand.FullName -eq 'SPTarkov.Server.Core.Generators.Bot.BotInventoryGenerator'
            })) {
            throw 'Installed APBS inventory replacement contract differs.'
        }
        foreach ($type in $armApbs.MainModule.Types) {
            foreach ($method in @($type.Methods | Where-Object Name -eq 'GetTargetMethod')) {
                if ($method.HasBody -and ($method.Body.Instructions | Where-Object {
                    $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'PrepareAndGenerateBot'
                })) { throw 'APBS now patches the outer boundary; review postfix ordering before declaring compatibility.' }
            }
        }
        Write-Output ('APBS SHA256 ' + (Get-FileHash -LiteralPath $armApbsPath -Algorithm SHA256).Hash)
    }
    $items = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText((Join-Path $SptPath 'SPT_Runtime/SPT_Data/database/templates/items.json')))
    try {
        foreach ($expected in @(
            @{ Id = '5b3f3af486f774679e752c1f'; Name = 'armband_blue' },
            @{ Id = '5b3f3ade86f7746b6b790d8e'; Name = 'armband_red' }
        )) {
            $item = $items.RootElement.GetProperty($expected.Id)
            if ($item.GetProperty('_name').GetString() -ne $expected.Name -or
                [string]::IsNullOrEmpty($item.GetProperty('_props').GetProperty('Prefab').GetProperty('path').GetString()) -or
                $item.GetProperty('_props').GetProperty('Grids').GetArrayLength() -ne 0) {
                throw "Native cloth armband template/prefab contract differs: $($expected.Id)"
            }
        }
    }
    finally { $items.Dispose() }
    Write-Output ('SPT core SHA256 ' + (Get-FileHash -LiteralPath $armServerPath -Algorithm SHA256).Hash)
    Write-Output 'ARENA_TEAM_ARMBAND_CONTRACTS_PASSED'
}
finally { if ($null -ne $armApbs) { $armApbs.Dispose() }; $armServer.Dispose() }
