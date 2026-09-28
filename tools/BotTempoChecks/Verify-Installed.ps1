param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$tempoResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($relative in @('EscapeFromTarkov_Data/Managed', 'BepInEx/core', 'BepInEx/plugins/SAIN', 'BepInEx/plugins/spt')) {
    $tempoResolver.AddSearchDirectory((Join-Path $SptPath $relative))
}
$tempoParameters = [Mono.Cecil.ReaderParameters]::new(); $tempoParameters.AssemblyResolver = $tempoResolver
$tempoSainPath = Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.dll'
$tempoAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($tempoSainPath, $tempoParameters)
$tempoNative = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'), $tempoParameters)
function Get-SainType([string]$Name) {
    $result = $tempoAssembly.MainModule.GetType($Name)
    if ($null -eq $result) { throw "Missing SAIN type $Name" }; return $result
}
function Property($Type, [string]$Name) {
    while ($null -ne $Type) {
        $found = @($Type.Properties | Where-Object Name -eq $Name)
        if ($found.Count -eq 1) {
            if (!$found[0].GetMethod.IsPublic -or $found[0].GetMethod.IsStatic -or $found[0].Parameters.Count -ne 0) { throw "Bad getter $Name" }
            return $found[0]
        }
        $Type = if ($null -ne $Type.BaseType) { $Type.BaseType.Resolve() } else { $null }
    }
    throw "Missing getter $Name"
}
function Chain($Type, [string]$Path) {
    foreach ($name in $Path.Split('.')) { $p = Property $Type $name; $Type = $p.PropertyType.Resolve() }
    return $Type
}
function Field($Type, [string]$Name, [string]$ValueType) {
    $found = @($Type.Fields | Where-Object Name -eq $Name)
    if ($found.Count -ne 1 -or $found[0].IsStatic -or $found[0].FieldType.FullName -ne $ValueType) { throw "Changed field $Name" }
    return $found[0]
}
try {
    if ($tempoAssembly.Name.Version.ToString() -ne '4.5.1.0') { throw 'Expected SAIN 4.5.1.0' }
    $bot = Get-SainType 'SAIN.Components.BotComponent'
    $search = Get-SainType 'SAIN.Classes.Bot.Search.SearchClass'
    $enemy = Get-SainType 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'
    $path = Get-SainType 'SAIN.SAINComponent.Classes.Mover.BotPathDataManual'
    foreach ($p in @('BotOwner','Player','SAINLayersActive','CurrentAction','Search','Mover','Mover.DogFight','Decision.CurrentSelfDecision',
        'Grenade.GrenadeReactionClass.DangerGrenade','EnemyController.VisibleEnemies','EnemyController.EnemiesInLineOfSight','Mover.ActivePath')) {
        $null = Chain $bot $p
    }
    foreach ($p in @('SearchActive','SearchTarget','CurrentState')) { $null = Chain $search $p }
    foreach ($p in @('Bot','EnemyPlayer','IsVisible','InLineOfSight','Hearing.EnemyHeardFromPeace','Path.PathToEnemy')) { $null = Chain $enemy $p }
    foreach ($p in @('PathFinder','PausedRequested','CancelRequested','PathRecalcRequested','TimeStarted','Id')) { $null = Chain $path $p }
    $null = Field (Get-SainType 'SAIN.Layers.BotAction') '_bot' $bot.FullName
    $null = Field $search '_waitAtPointTimer' 'System.Single'
    $null = Field $path '_unpauseTime' 'System.Single'
    $null = Field $path 'Bot' $bot.FullName
    $flash = Field ($tempoNative.MainModule.GetType('BotFlashGrenade')) '_isFlashed' 'System.Boolean'
    if (!$flash.IsPublic) { throw 'Cached native flash field inaccessible' }

    # Prove exact stack seam in installed IL, not a guessed/global speed setter.
    $dog = Get-SainType 'SAIN.SAINComponent.Classes.Mover.DogFight'
    $dogMethods = @($dog.Methods | Where-Object Name -eq 'DogFightMove')
    if ($dogMethods.Count -ne 1 -or $dogMethods[0].Parameters.Count -ne 2 -or
        $dogMethods[0].Parameters[0].ParameterType.FullName -ne 'System.Boolean' -or
        $dogMethods[0].Parameters[1].ParameterType.FullName -ne $enemy.FullName) { throw 'DogFight signature drifted' }
    $il = @($dogMethods[0].Body.Instructions)
    $sites = @($il | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'STRAFE_SPEED' })
    if ($sites.Count -ne 1 -or $sites[0].Operand.FieldType.FullName -ne 'System.Single' -or
        $sites[0].Next.OpCode.Name -notin @('call','callvirt') -or
        $sites[0].Next.Operand.ToString() -ne 'System.Void SAIN.SAINComponent.Classes.Mover.SAINMoverClass::SetTargetMoveSpeed(System.Single)') {
        throw 'Exact strafe field -> setter seam drifted'
    }
    $settings = Chain $bot 'Info.FileSettings'
    $move = @($settings.Fields | Where-Object Name -eq 'Move')[0].FieldType.Resolve()
    if ($move.FullName -ne $sites[0].Operand.DeclaringType.FullName) { throw 'Move settings binding differs from installed strafe receiver' }
    $wait = @($search.Methods | Where-Object Name -eq 'WaitAtPoint')[0]
    $reload = @($search.Methods | Where-Object Name -eq 'CheckShallWaitandReload')[0]
    foreach ($name in @('Pause','UnPause')) {
        if (@($wait.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') -and $_.Operand.Name -eq $name }).Count -ne 1) { throw "Native wait no longer owns $name" }
    }
    if (@($reload.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') -and $_.Operand.Name -eq 'get_Reloading' }).Count -ne 1) {
        throw 'Wait entry no longer tied to native reload'
    }
    $searchMethods = @($search.Methods | Where-Object Name -eq 'Search')
    if ($searchMethods.Count -ne 1 -or $searchMethods[0].Parameters.Count -ne 2 -or
        $searchMethods[0].Parameters[0].ParameterType.FullName -ne 'System.Boolean' -or
        $searchMethods[0].Parameters[1].ParameterType.FullName -ne $enemy.FullName) { throw 'Search signature drifted' }
    Write-Output "BOT_TEMPO_INSTALLED_CONTRACT_PASSED SAIN=$((Get-FileHash $tempoSainPath).Hash)"
} finally { $tempoAssembly.Dispose(); $tempoNative.Dispose(); $tempoResolver.Dispose() }
