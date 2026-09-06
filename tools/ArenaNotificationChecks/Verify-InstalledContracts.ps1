#requires -Version 7.0
param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$gamePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$filter = $null
$reflection = $null
function Require-Method($assembly, [string] $typeName, [string] $name, [string] $arguments, [string] $returns) {
    $type = $assembly.MainModule.GetType($typeName)
    if ($null -eq $type) { throw "Missing native type $typeName" }
    $methods = @($type.Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $arguments
    })
    if ($methods.Count -ne 1 -or $methods[0].ReturnType.FullName -ne $returns) {
        throw "Native method contract drift: $typeName.$name"
    }
    return $methods[0]
}
function Require-Call($method, [string] $callee) {
    $calls = @($method.Body.Instructions | Where-Object {
        $_.OpCode.Code.ToString() -in @('Call', 'Callvirt', 'Newobj')
    } | ForEach-Object { $_.Operand.DeclaringType.FullName + '::' + $_.Operand.Name })
    if ($calls -notcontains $callee) { throw "Native presentation flow drift: $($method.FullName) no longer calls $callee" }
}
try {
    $notification = 'EFT.Communications.Notification'
    $display = Require-Method $game 'EFT.Communications.NotificationManager' 'DisplayNotification' $notification 'System.Void'
    $add = Require-Method $game 'EFT.Communications.NotificationManager' 'AddNotification' $notification 'System.Void'
    $receive = Require-Method $game 'EFT.UI.NotifierView' 'NotificationReceivedHandler' $notification 'System.Void'
    $show = Require-Method $game 'EFT.UI.NotifierView' 'ShowNotification' $notification 'System.Boolean'
    $sound = Require-Method $game 'EFT.UI.NotifierView' 'PlaySound' $notification 'System.Void'
    $queue = Require-Method $game 'EFT.UI.NotifierView' 'ProcessQueuedNotifications' '' 'System.Void'
    Require-Call $display 'EFT.Communications.NotificationManager::AddNotification'
    Require-Call $add 'System.Action`1<EFT.Communications.Notification>::Invoke'
    Require-Call $receive 'EFT.UI.NotifierView::ShowNotification'
    Require-Call $receive 'System.Collections.Generic.Queue`1<EFT.Communications.Notification>::Enqueue'
    Require-Call $queue 'EFT.UI.NotifierView::ShowNotification'
    Require-Call $show 'EFT.Communications.Notification::CreateView'
    Require-Call $show 'EFT.UI.NotifierView::PlaySound'
    Require-Call $sound 'EFT.UI.GUISounds::PlayUISound'
    Require-Call $sound 'EFT.UI.GUISounds::PlayNotificationSound'

    $statistic = $game.MainModule.GetType('EFT.Communications.StatisticNotification')
    if ($statistic.BaseType.FullName -ne $notification) { throw 'XP notification no longer follows native Notification presentation' }
    $xpConstructor = Require-Method $game 'EFT.Communications.StatisticNotification' '.ctor' 'EFT.LocalizationKey,EFT.LocalizationKey,System.Int32' 'System.Void'
    Require-Call $xpConstructor 'EFT.Communications.Notification::.ctor'
    foreach ($statisticsType in @('EFT.LocalStatisticManager', 'EFT.OfflineStatisticManager')) {
        $methods = @($game.MainModule.GetType($statisticsType).Methods | Where-Object {
            $_.HasBody -and (($_.Body.Instructions.Operand | Out-String).Contains('StatisticNotification::.ctor'))
        })
        if ($methods.Count -eq 0) { throw "XP producer contract drift: $statisticsType" }
        foreach ($method in $methods) { Require-Call $method 'EFT.Communications.NotificationManager::DisplayNotification' }
    }

    $subtask = Require-Method $game 'EFT.Quests.QuestControllerClient' 'TryNotifyConditionChanged' 'EFT.Quests.Quest' 'System.Void'
    Require-Call $subtask 'EFT.UI.GUISounds::PlayUISound'
    Require-Call $subtask 'EFT.Communications.NotificationManager::DisplayNotification'
    # Only presentation calls occur in the method whose original is skipped.
    # Reject new quest/condition mutation calls instead of trusting its name.
    $allowedSubtask = @('EFT.Quests.Quest::get_Template', 'EFT.Quests.QuestTemplate::get_CanShowNotificationsInGame',
        'Comfort.Common.Singleton`1<EFT.UI.GUISounds>::get_Instance', 'EFT.UI.GUISounds::PlayUISound',
        'EFT.Communications.NotificationQuest::.ctor', 'EFT.LocalizationExtensions::Localized',
        'EFT.Quests.QuestTemplate::get_Name', 'System.String::Concat',
        'EFT.Communications.NotificationManager::DisplayNotification')
    foreach ($instruction in $subtask.Body.Instructions) {
        if ($instruction.OpCode.Code.ToString() -in @('Call', 'Callvirt', 'Newobj')) {
            $call = $instruction.Operand.DeclaringType.FullName + '::' + $instruction.Operand.Name
            if ($allowedSubtask -notcontains $call) { throw "Direct subtask method gained non-reviewed behavior: $call" }
        }
        if ($instruction.OpCode.Code.ToString() -eq 'Stfld' -and
            $instruction.Operand.FullName -ne 'System.String EFT.Communications.NotificationWithText::Text') {
            throw "Direct subtask method gained a non-presentation field write: $($instruction.Operand.FullName)"
        }
    }
    $status = Require-Method $game 'EFT.Quests.QuestControllerClient' 'TryNotifyConditionalStatusChanged' 'EFT.Quests.Quest' 'System.Void'
    Require-Call $status 'EFT.Communications.NotificationQuest::.ctor'
    Require-Call $status 'EFT.Communications.NotificationManager::DisplayNotification'
    $achievement = Require-Method $game 'EFT.Achievements.AchievementsControllerClient' 'TryNotifyConditionalStatusChanged' 'EFT.Achievements.Achievement' 'System.Void'
    Require-Call $achievement 'EFT.Achievements.AchievementsControllerClient::method_4'
    Require-Call $achievement 'EFT.Achievements.AchievementsControllerClient::method_5'
    $allowedAchievement = @('EFT.Quests.Conditional`1<EFT.Achievements.Achievement>::IsDone',
        'Comfort.Common.Singleton`1<EFT.Communications.NotificationManager>::get_Instantiated',
        'EFT.Quests.Conditional`1<EFT.Achievements.Achievement>::get_Sprite', 'UnityEngine.Object::op_Equality',
        'EFT.Achievements.AchievementsControllerClient::method_5', 'TasksExtensions::HandleExceptions',
        'EFT.Achievements.Achievement::get_Template', 'EFT.Achievements.AchievementsControllerClient::method_4')
    foreach ($instruction in $achievement.Body.Instructions) {
        if ($instruction.OpCode.Code.ToString() -in @('Stfld', 'Stsfld')) { throw 'Achievement notification method gained state writes' }
        if ($instruction.OpCode.Code.ToString() -in @('Call', 'Callvirt', 'Newobj')) {
            $call = $instruction.Operand.DeclaringType.FullName + '::' + $instruction.Operand.Name
            if ($allowedAchievement -notcontains $call) { throw "Achievement notification method gained non-reviewed behavior: $call" }
        }
    }
    $achievementDisplay = Require-Method $game 'EFT.Achievements.AchievementsControllerClient' 'method_4' 'EFT.Achievements.AchievementTemplate' 'System.Void'
    Require-Call $achievementDisplay 'EFT.Communications.NotificationAchievement::.ctor'
    Require-Call $achievementDisplay 'EFT.Communications.NotificationManager::DisplayNotification'
    $deferred = Require-Method $game 'EFT.Achievements.AchievementsControllerClient' 'method_5' 'EFT.Achievements.Achievement' 'System.Threading.Tasks.Task'
    $machineAttribute = @($deferred.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.CompilerServices.AsyncStateMachineAttribute' })
    if ($machineAttribute.Count -ne 1) { throw 'Achievement sprite loading no longer has the reviewed async contract' }
    $machineName = $machineAttribute[0].ConstructorArguments[0].Value.FullName
    $moveNext = Require-Method $game $machineName 'MoveNext' '' 'System.Void'
    Require-Call $moveNext 'EFT.Quests.Conditional`1<EFT.Achievements.Achievement>::LoadSprite'
    Require-Call $moveNext 'EFT.Achievements.AchievementsControllerClient::method_4'

    $filterPath = Join-Path $SptPath 'BepInEx/plugins/maschine-NotificationFilter.dll'
    if (Test-Path -LiteralPath $filterPath) {
        $filter = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($filterPath)
        $prefix = Require-Method $filter 'NotificationFilter.SilenceRaidSubtaskSoundPatch' 'Prefix' 'EFT.Quests.Quest' 'System.Boolean'
        Require-Call $prefix 'EFT.Communications.NotificationManager::DisplayNotification'
        $reflection = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/spt/spt-reflection.dll'))
        $moduleCtor = Require-Method $reflection 'SPT.Reflection.Patching.ModulePatch' '.ctor' 'System.String' 'System.Void'
        Require-Call $moduleCtor 'HarmonyLib.Harmony::.ctor'
        Require-Call $moduleCtor 'System.Reflection.MemberInfo::get_Name'
        Write-Output ('NotificationFilter SHA256 ' + (Get-FileHash -LiteralPath $filterPath -Algorithm SHA256).Hash)
    }
    Write-Output ('Native game SHA256 ' + (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash)
    Write-Output 'ARENA_NOTIFICATION_INSTALLED_CONTRACTS_PASSED'
}
finally { $game.Dispose(); if ($filter) { $filter.Dispose() }; if ($reflection) { $reflection.Dispose() } }
