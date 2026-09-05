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
