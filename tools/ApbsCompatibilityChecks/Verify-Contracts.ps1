#requires -Version 7.0
param([string]$SptRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$SptRoot) { $SptRoot = [IO.Path]::GetFullPath((Join-Path $repo '../..')) }
& (Join-Path $repo 'tools/TeamArmbandChecks/Verify-Contracts.ps1') -SptPath $SptRoot
# The armband verifier loads Cecil. Inspect installed code only; never execute
# server startup or APBS config migration as part of this check.
$core = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'SPT_Runtime/SPTarkov.Server.Core.dll'))
$apbs = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/acidphantasm-progressivebotsystem.dll'))
function Exact($type, $name, $returns, $parameters) {
    $methods = @($type.Methods | Where-Object { $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $parameters })
    if ($methods.Count -ne 1 -or $methods[0].ReturnType.FullName -ne $returns -or
        @($methods[0].CustomAttributes | Where-Object { $_.AttributeType.Name -match '^(Async|Iterator)StateMachineAttribute$' }).Count -gt 0) {
        throw "Synchronous method contract drift: $($type.FullName).$name"
    }
    return $methods[0]
}
function CallOffsets($method, $name) {
    @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name } | ForEach-Object Offset)
}
try {
    $details = 'SPTarkov.Server.Core.Models.Spt.Bots.BotGenerationDetails'
    $mongo = 'SPTarkov.Server.Core.Models.Common.MongoId'
    $bot = 'SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBase'
    $rangeName = 'SPTarkov.Server.Core.Models.Common.MinMax`1<System.Int32>'
    $single = Exact ($core.MainModule.GetType('SPTarkov.Server.Core.Controllers.BotController')) 'TryGenerateSingleBot' $bot "$mongo,$details,System.Int32"
    $clone = @(CallOffsets $single 'Clone')
    $prepare = @(CallOffsets $single 'PrepareAndGenerateBot')
    if ($clone.Count -ne 1 -or $prepare.Count -ne 1 -or $clone[0] -ge $prepare[0]) { throw 'Native per-bot clone/generation order drifted' }
    $generator = $core.MainModule.GetType('SPTarkov.Server.Core.Generators.Bot.BotGenerator')
    $null = Exact $generator 'PrepareAndGenerateBot' $bot "$mongo,$details"
    $patch = $apbs.MainModule.GetType('ProgressiveBotSystem.Patches.GenerateBotLevel')
    $range = Exact $patch 'GetRelativePmcBotLevelRange' $rangeName "$details,$rangeName,System.Int32"
    if (!$range.IsPrivate -or !$range.IsStatic -or @(CallOffsets $range 'get_CurrentRaidLevel').Count -ne 1) { throw 'APBS reviewed global-level helper changed' }
    $prefix = @($patch.Methods | Where-Object Name -eq 'Prefix')[0]
    $rangeCalls = @(CallOffsets $prefix 'GetRelativePmcBotLevelRange')
    $rollCalls = @(CallOffsets $prefix 'ChooseBotLevel')
    if ($rangeCalls.Count -ne 1 -or $rollCalls.Count -ne 1 -or $rangeCalls[0] -ge $rollCalls[0] -or
        @(CallOffsets $prefix 'GetTierByLevel').Count -lt 2) { throw 'APBS level roll/tier continuation changed' }
    $metadata = $apbs.MainModule.GetType('ProgressiveBotSystem.ModMetadata')
    $constructors = @($metadata.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 0 })
    if ($constructors.Count -ne 1 -or !($constructors[0].Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq '2.4.1' })) {
        throw 'Installed APBS metadata is not 2.4.1'
    }
    Write-Output 'ARENA_APBS_CONTRACTS_PASSED'
} finally { $apbs.Dispose(); $core.Dispose() }
