param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$tauntResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($relative in @('EscapeFromTarkov_Data/Managed', 'BepInEx/core', 'BepInEx/plugins/SAIN', 'BepInEx/plugins/spt')) {
    $tauntResolver.AddSearchDirectory((Join-Path $SptPath $relative))
}
$tauntParameters = [Mono.Cecil.ReaderParameters]::new(); $tauntParameters.AssemblyResolver = $tauntResolver
$tauntPath = Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.dll'
$tauntAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($tauntPath, $tauntParameters)
function Get-Type([string]$Name) {
    $result = $tauntAssembly.MainModule.GetType($Name)
    if ($null -eq $result) { throw "Missing SAIN type $Name" }; return $result
}
function Property($Type, [string]$Name, [string]$Expected = '') {
    while ($null -ne $Type) {
        $found = @($Type.Properties | Where-Object Name -eq $Name)
        if ($found.Count -eq 1) {
            $p = $found[0]
            if (!$p.GetMethod.IsPublic -or $p.GetMethod.IsStatic -or $p.Parameters.Count -ne 0 -or
                ($Expected -and $p.PropertyType.FullName -ne $Expected)) { throw "Bad getter $Name" }
            return $p
        }
        $Type = if ($null -ne $Type.BaseType) { $Type.BaseType.Resolve() } else { $null }
    }
    throw "Missing getter $Name"
}
function Field($Type, [string]$Name, [string]$Expected = '') {
    $found = @($Type.Fields | Where-Object Name -eq $Name)
    if ($found.Count -ne 1 -or $found[0].IsStatic -or ($Expected -and $found[0].FieldType.FullName -ne $Expected)) { throw "Bad field $Name" }
    return $found[0]
}
try {
    if ($tauntAssembly.Name.Version.ToString() -ne '4.5.1.0') { throw 'Review baseline changed: expected installed SAIN 4.5.1.0' }
    $bot = Get-Type 'SAIN.Components.BotComponent'
    $enemy = Get-Type 'SAIN.SAINComponent.Classes.Talk.EnemyTalk'
    $info = Get-Type 'SAIN.SAINComponent.Classes.Info.SAINBotInfoClass'
    $personality = (Property $info 'Personality' 'SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality').PropertyType.Resolve()
    if (!$personality.IsEnum -or @($personality.Fields | Where-Object Name -eq 'Normal').Count -ne 1) { throw 'Normal enum changed' }
    $null = Property $enemy 'Bot' $bot.FullName
    $null = Property $bot 'BotOwner' 'EFT.BotOwner'; $null = Property $bot 'Player' 'EFT.Player'
    $null = Property $bot 'Info' $info.FullName
    $null = Property $info 'Personality' $personality.FullName
    $settings = (Property $info 'FileSettings').PropertyType.Resolve()
    $mind = (Field $settings 'Mind').FieldType.Resolve()
    $null = Field $mind 'BotTaunts' 'System.Boolean'
    $talk = (Property $bot 'Talk').PropertyType.Resolve()
    $null = Property $talk 'EnemyTalk' $enemy.FullName
    $cache = Field $enemy 'CanTaunt' 'System.Boolean'
    if (!$cache.IsPrivate) { throw 'Expected private native eligibility cache' }
    $update = @($enemy.Methods | Where-Object Name -eq 'ManualUpdate')
    if ($update.Count -ne 1 -or !$update[0].IsPublic -or $update[0].IsStatic -or $update[0].Parameters.Count -ne 0 -or $update[0].ReturnType.FullName -ne 'System.Void') { throw 'EnemyTalk.ManualUpdate signature changed' }
    $sites = @($update[0].Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.FullName -eq $cache.FullName })
    if ($sites.Count -ne 1 -or $sites[0].Previous.OpCode.Name -ne 'ldarg.0' -or $sites[0].Next.OpCode.Name -notin @('brfalse','brfalse.s')) { throw 'Combat eligibility operand/branch changed' }
    # Prove this operand still leads into native timer/chance/enemy decisions.
    foreach ($name in @('_tauntTimer','TauntFreq')) {
        if (@($update[0].Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq $name }).Count -eq 0) { throw "Native timer $name missing" }
    }
    foreach ($name in @('RandomBool','TauntEnemy')) {
        if (@($update[0].Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') -and $_.Operand.Name -eq $name }).Count -ne 1) { throw "Native decision $name changed" }
    }
    $preset = @($enemy.Methods | Where-Object Name -eq 'UpdatePresetSettings')[0]
    if (@($preset.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.FullName -eq $cache.FullName }).Count -ne 1 -or
        @($preset.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'BotTaunts' }).Count -ne 1) { throw 'CanTaunt cache initialization changed' }
    # Native outer talk owner continues to own CanTalk and EnemyTalk dispatch.
    $outer = @($talk.Methods | Where-Object Name -eq 'ManualUpdate')[0]
    $dispatch = @($outer.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') -and $_.Operand.Name -eq 'get_EnemyTalk' })
    if (@($outer.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') -and $_.Operand.Name -eq 'get_CanTalk' }).Count -eq 0 -or
        $dispatch.Count -ne 1 -or $dispatch[0].Operand.ReturnType.FullName -ne $enemy.FullName -or
        $dispatch[0].Next.OpCode.Name -ne 'callvirt' -or $dispatch[0].Next.Operand.Name -ne 'ManualUpdate' -or
        $dispatch[0].Next.Operand.DeclaringType.FullName -ne $enemy.BaseType.FullName) { throw 'Native outer CanTalk dispatch changed' }
    Write-Output "BOT_TAUNT_INSTALLED_CONTRACT_PASSED SAIN=$((Get-FileHash -LiteralPath $tauntPath).Hash) shared=$((Get-FileHash -LiteralPath (Join-Path $SptPath 'BepInEx/plugins/SAIN/SAIN.Preset.Shared.dll')).Hash)"
} finally { $tauntAssembly.Dispose(); $tauntResolver.Dispose() }
