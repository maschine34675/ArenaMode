param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$sain = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.dll'))
function MethodText($method) { return ($method.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n" }
try {
    $controller = $game.MainModule.GetType('BotEnemiesController')
    foreach ($pair in @(@('SortedInfos','EnemyInfo[]'), @('_countEnemies','System.Int32'))) {
        $field = @($controller.Fields | Where-Object Name -eq $pair[0])
        if ($field.Count -ne 1 -or !$field[0].IsPublic -or $field[0].IsInitOnly -or $field[0].FieldType.FullName -ne $pair[1]) { throw "Native field drift: $($pair[0])" }
    }
    $owner = @($game.MainModule.GetType('BotData').Fields | Where-Object Name -eq '_owner')
    if ($owner.Count -ne 1 -or !$owner[0].IsPublic -or $owner[0].FieldType.FullName -ne 'EFT.BotOwner') { throw 'Controller ownership field drift' }
    $botOwner = $game.MainModule.GetType('EFT.BotOwner')
    foreach ($name in @('GetPlayer','EnemiesController')) {
        $members = @($botOwner.Properties | Where-Object Name -eq $name) + @($botOwner.Fields | Where-Object Name -eq $name)
        if ($members.Count -ne 1) { throw "BotOwner member drift: $name" }
    }
    $preactivate = @($botOwner.Methods | Where-Object Name -eq 'PreActivate')
    if ($preactivate.Count -ne 1 -or !$preactivate[0].IsPublic -or $preactivate[0].ReturnType.FullName -ne 'System.Void') { throw 'PreActivate signature drift' }
    $add = @($controller.Methods | Where-Object Name -eq 'SetInfo')
    $remove = @($controller.Methods | Where-Object Name -eq 'Remove')
    if ($add.Count -ne 1 -or !$add[0].IsPublic -or $add[0].Parameters.Count -ne 2 -or $add[0].Parameters[0].ParameterType.FullName -ne 'EFT.IPlayer' -or $add[0].Parameters[1].ParameterType.FullName -ne 'EnemyInfo') { throw 'SetInfo signature drift' }
    if ($remove.Count -ne 1 -or !$remove[0].IsPublic -or $remove[0].Parameters.Count -ne 1 -or $remove[0].Parameters[0].ParameterType.FullName -ne 'EFT.IPlayer') { throw 'Remove signature drift' }
    $addText = MethodText $add[0]
    $removeText = MethodText $remove[0]
    if ($addText -notmatch 'ldfld System.Int32 BotEnemiesController::_countEnemies\s*\n[^\n]+ldarg.2\s*\n[^\n]+stelem.ref') { throw 'Native indexed insert changed' }
    if ($removeText -notmatch 'ldc.i4.1\s*\n[^\n]+add\s*\n[^\n]+ldelem.ref') { throw 'Native sentinel removal changed' }
    if (!(MethodText ($controller.Methods | Where-Object Name -eq '.ctor')).Contains('ldc.i4.s 100')) { throw 'Native initial capacity changed' }

    if ($sain.Name.Name -ne 'SAIN' -or $sain.Name.Version.ToString() -ne '4.5.1.0') { throw 'SAIN version not reviewed' }
    $action = $sain.MainModule.GetType('SAIN.Layers.BotAction')
    $cache = @($action.Fields | Where-Object Name -eq '_bot')
    if ($cache.Count -ne 1 -or $cache[0].FieldType.FullName -ne 'SAIN.Components.BotComponent') { throw 'SAIN action cache drift' }
    $baseStop = @($action.Methods | Where-Object Name -eq 'Stop')
    if (@($baseStop[0].Body.Instructions | Where-Object { $_.OpCode.Name -notin @('nop','ret') }).Count -ne 0) { throw 'SAIN base Stop gained independent cleanup' }
    foreach ($name in @('SAIN.Layers.Combat.Solo.Cover.SeekCoverAction','SAIN.Layers.Combat.Solo.DogFightAction')) {
        $stop = @($sain.MainModule.GetType($name).Methods | Where-Object Name -eq 'Stop')
        if ($stop.Count -ne 1 -or !$stop[0].IsPublic -or $stop[0].IsStatic -or $stop[0].Parameters.Count -ne 0 -or $stop[0].ReturnType.FullName -ne 'System.Void') { throw "SAIN Stop signature drift: $name" }
        $lookup = @($stop[0].Body.Instructions | Where-Object Offset -eq 12)
        if ($lookup.Count -ne 1 -or $lookup[0].Operand.ToString() -notlike '*BotComponent::get_Mover()*') { throw "Observed Stop fault moved: $name" }
    }
    $manager = $sain.MainModule.GetType('SAIN.Components.BotManagerComponent')
    $instance = @($manager.Properties | Where-Object Name -eq 'Instance')
    if ($instance.Count -ne 1 -or !$instance[0].GetMethod.IsPublic -or !$instance[0].GetMethod.IsStatic) { throw 'SAIN manager singleton drift' }
    $lookup = @($manager.Methods | Where-Object Name -eq 'GetSAIN')
    if ($lookup.Count -ne 1 -or !$lookup[0].IsPublic -or $lookup[0].Parameters.Count -ne 2 -or !$lookup[0].Parameters[1].IsOut -or $lookup[0].ReturnType.FullName -ne 'System.Boolean') { throw 'SAIN manager lookup drift' }
    Write-Output 'ARENA_BOT_LIFECYCLE_CONTRACTS_PASSED'
}
finally { $game.Dispose(); $sain.Dispose() }
