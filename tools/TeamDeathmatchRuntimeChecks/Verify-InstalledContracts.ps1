#requires -Version 7.0
param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$cecilPath = Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll'
$sainPath = Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.dll'
$gamePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
Add-Type -Path $cecilPath
$sain = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($sainPath)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
try {
    $enemy = $sain.MainModule.GetType('SAIN.SAINComponent.Classes.EnemyClasses.Enemy')
    $list = $sain.MainModule.GetType('SAIN.SAINComponent.Classes.EnemyClasses.EnemyListController')
    $base = $sain.MainModule.GetType('SAIN.SAINComponent.BotBase')
    $enemyPlayer = @($enemy.Properties | Where-Object Name -eq 'EnemyPlayer')
    $enemyInfo = @($enemy.Properties | Where-Object Name -eq 'EnemyInfo')
    $listAdd = @($list.Methods | Where-Object { $_.Name -eq 'tryAddEnemy' -and $_.Parameters.Count -eq 1 })
    $owner = @($base.Properties | Where-Object Name -eq 'BotOwner')
    if ($enemyPlayer.Count -ne 1 -or $enemyPlayer[0].PropertyType.FullName -ne 'EFT.Player' -or
        $enemyInfo.Count -ne 1 -or $enemyInfo[0].PropertyType.FullName -ne 'EnemyInfo' -or
        $owner.Count -ne 1 -or $owner[0].PropertyType.FullName -ne 'EFT.BotOwner' -or
        $listAdd.Count -ne 1 -or !$listAdd[0].IsPrivate -or
        $listAdd[0].Parameters[0].ParameterType.FullName -ne 'EFT.IPlayer' -or
        $listAdd[0].ReturnType.FullName -ne $enemy.FullName) {
        throw 'Installed SAIN private admission/body contract differs from the reviewed integration.'
    }
    $player = $game.MainModule.GetType('EFT.Player')
    $group = $game.MainModule.GetType('BotsGroup')
    $memory = $game.MainModule.GetType('EFT.BotMemory')
    # Read the installed IL, not just a test enum: the quiet-exit distinction
    # depends on native Stop committing Stopping before it disposes bots.
    $status = $game.MainModule.GetType('EFT.GameStatus')
    $stopping = @($status.Fields | Where-Object Name -eq 'Stopping')
    $local = $game.MainModule.GetType('EFT.BaseLocalGame`1')
    $stop = @($local.Methods | Where-Object { $_.Name -eq 'Stop' -and $_.Parameters.Count -eq 4 })
    if ($stopping.Count -ne 1 -or $stop.Count -ne 1 -or !$stop[0].HasBody) {
        throw 'Installed native stop/status contract is unavailable.'
    }
    $instructions = @($stop[0].Body.Instructions)
    $commit = -1
    $dispose = -1
    for ($i = 1; $i -lt $instructions.Count; $i++) {
        $instruction = $instructions[$i]
        if ($instruction.Operand -isnot [Mono.Cecil.MethodReference]) { continue }
        if ($instruction.Operand.Name -eq 'set_Status') {
            $previous = $instructions[$i - 1]
            $value = if ($previous.OpCode.Name -match '^ldc\.i4\.([0-8])$') { [int]$Matches[1] }
                elseif ($previous.OpCode.Name -in @('ldc.i4', 'ldc.i4.s')) { [int]$previous.Operand }
                else { -1 }
            if ($value -eq [int]$stopping[0].Constant) { $commit = $i }
        }
        if ($instruction.Operand.Name -eq 'StopGettingInfo') { $dispose = $i }
    }
    if ($commit -lt 0 -or $dispose -le $commit) {
        throw 'Installed native Stop no longer commits Stopping before disposing bots.'
    }
    foreach ($expected in @(
        @{ Type = $group; Method = 'AddEnemy'; Args = 'EFT.IPlayer,EBotEnemyCause'; Returns = 'System.Boolean' },
        @{ Type = $memory; Method = 'AddEnemy'; Args = 'EFT.IPlayer,BotGroupEnemyInfo,System.Boolean'; Returns = 'System.Void' },
        @{ Type = $player; Method = 'ApplyShot'; Args = 'EFT.Ballistics.DamageInfo,EBodyPart,EBodyPartColliderType,EArmorPlateCollider,EFT.Ballistics.ShotId'; Returns = 'EFT.PlayerHitInfo' },
        @{ Type = $player; Method = 'ApplyDamageInfo'; Args = 'EFT.Ballistics.DamageInfo,EBodyPart,EBodyPartColliderType,System.Single'; Returns = 'System.Void' },
        @{ Type = $player; Method = 'ApplyExplosionDamageToArmor'; Args = 'System.Collections.Generic.Dictionary`2<EFT.ExplosionDamageInfo,System.Single>,EFT.Ballistics.DamageInfo'; Returns = 'System.Void' },
        @{ Type = $game.MainModule.GetType('EFT.HealthSystem.ActiveHealthController'); Method = 'ApplyDamage'; Args = 'EBodyPart,System.Single,EFT.Ballistics.DamageInfo'; Returns = 'System.Single' }
    )) {
        $found = @($expected.Type.Methods | Where-Object {
            $_.Name -eq $expected.Method -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $expected.Args
        })
        if ($found.Count -ne 1 -or $found[0].ReturnType.FullName -ne $expected.Returns) {
            throw ('Installed native method contract differs: ' + $expected.Type.FullName + '.' + $expected.Method)
        }
    }
    Write-Output ('SAIN SHA256 ' + (Get-FileHash -LiteralPath $sainPath -Algorithm SHA256).Hash)
    Write-Output 'ARENA_TDM_INSTALLED_CONTRACTS_PASSED'
}
finally { $sain.Dispose(); $game.Dispose() }
