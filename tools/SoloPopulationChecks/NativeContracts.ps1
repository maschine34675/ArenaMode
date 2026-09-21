param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$path = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
$script:contracts = 0
function Require([bool] $condition, [string] $why) {
    if (!$condition) { throw "Installed solo spawn contract changed: $why" }
    $script:contracts++
}
function Method([string] $type, [string] $name, [string] $signature) {
    $matches = @($assembly.MainModule.GetType($type).Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $signature
    })
    Require ($matches.Count -eq 1) "$type.$name($signature)"
    return $matches[0]
}
function Body($method) {
    $attr = @($method.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'AsyncStateMachineAttribute' })
    if ($attr.Count -eq 0) { return $method }
    return ($attr[0].ConstructorArguments[0].Value.Resolve().Methods | Where-Object Name -eq 'MoveNext')
}
function Calls($method) {
    return @((Body $method).Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.OpCode.Name -in @('call','callvirt')
    } | ForEach-Object { $_.Operand.Name })
}
function BeforeAwait($method, [string] $call) {
    $names = Calls $method
    $target = [Array]::IndexOf($names, $call)
    $await = [Array]::IndexOf($names, 'GetAwaiter')
    Require ($target -ge 0 -and $await -gt $target) ($method.FullName + ': ' + $call + ' before first await')
}
try {
    $list = 'System.Collections.Generic.List`1<EFT.Game.Spawning.ISpawnPoint>'
    $callback = 'System.Action`1<EFT.BotOwner>'
    $group = 'System.Func`3<EFT.BotOwner,BotZone,BotsGroup>'
    $token = 'System.Threading.CancellationToken'
    $dispatch = Method 'EFT.BotSpawner' 'SpawnBotsInZoneOnPositions' "$list,BotZone,BotCreationData,$callback"
    $stage = Method 'EFT.BotSpawner' 'method_7' "$list,BotZone,BotCreationData,$callback,$token"
    $activate = Method 'BotCreatorClient' 'ActivateBot' "BotCreationData,BotZone,System.Boolean,$group,$callback,$token"
    $profileActivate = Method 'BotCreatorClient' 'ActivateBot' "EFT.Profile,PositionNote,BotZone,System.Boolean,$group,$callback,$token"
    $loop = Method 'BotCreatorClient' 'method_0' "BotCreationData,System.Boolean,$callback,$token,System.Boolean"
    $create = Method 'BotCreatorClient' 'CreateBot' "EFT.Profile,PositionNote,$callback,System.Boolean,$token"
    Require ((Calls $dispatch) -contains 'method_7') 'native dispatch invokes method_7'
    BeforeAwait $stage 'method_10'
    BeforeAwait $activate 'method_0'
    BeforeAwait $loop 'CreateBot'
    foreach ($method in @($activate, $profileActivate, $create)) {
        Require ($method.ReturnType.FullName -eq 'System.Threading.Tasks.Task') ($method.Name + ' Task result for safe prefix rejection')
    }
    $finish = Method 'EFT.BotSpawner' 'ActivateBotCallback' "EFT.BotOwner,BotCreationData,$callback,System.Boolean,System.Diagnostics.Stopwatch"
    $finishCalls = Calls $finish
    $add = [Array]::IndexOf($finishCalls, 'Add')
    $after = [Array]::IndexOf($finishCalls, 'AfterCreation')
    $observer = [Array]::IndexOf($finishCalls, 'Invoke', $after + 1)
    Require ($add -ge 0 -and $after -gt $add -and $observer -gt $after) 'register and native death callback precede observer'
    $preactivate = Method 'BotCreatorClient' 'method_3' "BotZone,EFT.BotOwner,$callback,$group"
    $preCalls = Calls $preactivate
    $preIndex = [Array]::IndexOf($preCalls, 'PreActivate')
    $callbackIndex = [Array]::LastIndexOf($preCalls, 'Invoke')
    Require ($preIndex -ge 0 -and $callbackIndex -gt $preIndex) 'arrival sees PreActive owner'
    Write-Output "PASS: $script:contracts installed native IL contracts."
    Write-Output ('Assembly-CSharp SHA256 ' + (Get-FileHash -LiteralPath $path).Hash)
    Write-Output 'SOLO NATIVE CONTRACTS PASSED'
}
finally { $assembly.Dispose() }
