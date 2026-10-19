param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
if (-not ('ArenaMode.Shared.PitFireTeamVersions' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot '../../ArenaMode.Shared/PitFireTeamVersions.cs') }
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$pft = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/pitFireTeam/pitFireTeam.dll'))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
function MethodText($method) { return ($method.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n" }
try {
    if ($pft.Name.Name -ne 'pitFireTeam' -or ![ArenaMode.Shared.PitFireTeamVersions]::IsSupportedAssemblyVersion($pft.Name.Version)) { throw 'Unsupported installed PFT identity' }
    $tracker = $pft.MainModule.GetType('pitTeam.Modules.FollowerGoalEnemyTracker')
    $scope = @($tracker.NestedTypes | Where-Object Name -eq 'GoalEnemySetScope')
    $begin = @($tracker.Methods | Where-Object Name -eq 'Begin')
    $reason = @($tracker.Properties | Where-Object Name -eq 'CurrentReason')
    if ($scope.Count -ne 1 -or !$scope[0].IsValueType -or @($scope[0].Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'System.IDisposable' }).Count -ne 1) { throw 'PFT scope contract drift' }
    if ($begin.Count -ne 1 -or !$begin[0].IsPublic -or !$begin[0].IsStatic -or $begin[0].Parameters.Count -ne 2 -or @($begin[0].Parameters | Where-Object { $_.ParameterType.FullName -ne 'System.String' }).Count -ne 0 -or $begin[0].ReturnType.FullName -ne $scope[0].FullName) { throw 'PFT Begin contract drift' }
    if ($reason.Count -ne 1 -or $reason[0].PropertyType.FullName -ne 'System.String' -or !$reason[0].GetMethod.IsPublic -or !$reason[0].GetMethod.IsStatic) { throw 'PFT CurrentReason contract drift' }
    $context = @($tracker.Fields | Where-Object Name -eq 'currentContext')
    if ($context.Count -ne 1 -or @($context[0].CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.ThreadStaticAttribute' }).Count -ne 1) { throw 'PFT context no longer thread local' }
    $dispose = MethodText ($scope[0].Methods | Where-Object Name -eq 'Dispose')
    if (!$dispose.Contains('::previousContext') -or $dispose -notmatch 'stsfld[^\n]+::currentContext') { throw 'PFT scope no longer restores previous context' }
    $prefix = MethodText ($pft.MainModule.GetType('pitTeam.Patches.FollowerGoalEnemyClearRetentionPatch').Methods | Where-Object Name -eq 'PatchPrefix')
    foreach ($call in @('ShouldBlockUnscopedMemoryOnlyGoal', 'ShouldAllowGoalEnemySet', 'get_CurrentReason')) {
        if (!$prefix.Contains($call)) { throw "PFT native setter veto contract drift: $call" }
    }
    $setter = MethodText ($game.MainModule.GetType('EFT.BotMemory').Methods | Where-Object Name -eq 'set_GoalEnemy')
    foreach ($call in @('GetEverExistedPlayerByID', 'GetAlivePlayerByProfileID', 'remove_BeingHitAction', 'add_BeingHitAction', 'ReportAboutEnemyToAll')) {
        if (!$setter.Contains($call)) { throw "Native goal subscription contract drift: $call" }
    }
    Write-Output 'ARENA_PFT_GOAL_CONTRACTS_PASSED'
}
finally { $pft.Dispose(); $game.Dispose() }
