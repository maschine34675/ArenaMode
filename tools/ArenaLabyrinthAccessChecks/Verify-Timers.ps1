#requires -Version 7.0
param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$timerRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$timerSource = Join-Path $timerRepo 'ArenaMode.Server/Services/ArenaLocationMutator.cs'
$timerOriginal = [IO.File]::ReadAllText($timerSource)
$timerScratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-labyrinth-timers-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($timerScratch)
$timerNoDeploy = Join-Path $timerScratch 'no-server-deploy'
if (Test-Path -LiteralPath $timerNoDeploy) { throw 'No-deploy path must not exist.' }

Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$timerGamePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$timerGame = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($timerGamePath)

function Native-Method([string] $TypeName, [string] $Name, [string] $Parameters, [string] $ReturnType) {
    $type = $timerGame.MainModule.GetType($TypeName)
    $methods = @($type.Methods | Where-Object {
        $_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Parameters
    })
    if ($methods.Count -ne 1 -or $methods[0].ReturnType.FullName -ne $ReturnType) {
        throw "Installed native timer contract differs: $TypeName.$Name($Parameters)"
    }
    return $methods[0]
}

function Field-Count($Method, [string] $Opcode, [string] $TypeName, [string] $Name) {
    return @($Method.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq $Opcode -and $_.Operand -is [Mono.Cecil.FieldReference] -and
        $_.Operand.DeclaringType.FullName -eq $TypeName -and $_.Operand.Name -eq $Name
    }).Count
}

try {
    $load = Native-Method 'EFT.Interactive.ExitTriggerSettings' 'Load' 'JsonType.BackendExitTriggerSettings,EFT.Interactive.ExfiltrationPoint' 'System.Void'
    foreach ($field in @('MinTime', 'MaxTime', 'ExfiltrationTime')) {
        if ((Field-Count $load 'ldfld' 'JsonType.BackendExitTriggerSettings' $field) -ne 1) {
            throw "Native exit loader no longer reads $field from server settings."
        }
    }
    if ((Field-Count $load 'stfld' 'EFT.Interactive.ExitTriggerSettings' 'StartTime') -ne 1 -or
        !($load.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and
            $_.Operand.FullName -eq 'System.Int32 UnityEngine.Random::Range(System.Int32,System.Int32)'
        })) { throw 'Native availability timer no longer uses the reviewed integer range.' }
    $met = Native-Method 'EFT.Interactive.TimerRequirement' 'Met' 'EFT.Player,EFT.Interactive.ExfiltrationPoint' 'System.Boolean'
    if ((Field-Count $met 'ldfld' 'EFT.Interactive.ExitTriggerSettings' 'StartTime') -ne 1) {
        throw 'Native timer requirement no longer reads the selected start time.'
    }
    $update = Native-Method 'CommonAssets.Scripts.Game.EndByExitTrigerScenario' 'Update' '' 'System.Void'
    if ((Field-Count $update 'ldfld' 'EFT.Interactive.ExitTriggerSettings' 'ExfiltrationTime') -ne 2) {
        throw 'Native individual/shared extraction countdown consumers changed.'
    }
    Write-Output ('Game SHA256 ' + (Get-FileHash -LiteralPath $timerGamePath -Algorithm SHA256).Hash)
    Write-Output 'Native availability and extraction-countdown readers verified.'
}
finally { $timerGame.Dispose() }

function Invoke-Test([string[]] $Arguments) {
    if (Test-Path -LiteralPath $timerNoDeploy) { throw 'No-deploy path was unexpectedly created.' }
    $output = & dotnet test (Join-Path $timerRepo 'ArenaMode.Tests') --no-restore "-p:SPTUserMods=$timerNoDeploy" @Arguments 2>&1 | Out-String
    return @{ Exit = $LASTEXITCODE; Output = $output }
}

foreach ($mutant in @(
    @{ Name = 'Availability'; From = 'MinTime = 0,'; To = 'MinTime = 900,';
       Test = 'WayUpBothTimersAreZeroInEveryArenaMode' },
    @{ Name = 'PveCountdown'; From = 'ExfiltrationTimePVE = 0,'; To = 'ExfiltrationTimePVE = 8,';
       Test = 'WayUpBothTimersAreZeroInEveryArenaMode' },
    @{ Name = 'EveryExit'; From = 'exit?.Name == "labir_exit"'; To = 'true';
       Test = 'OtherExitNamesRemainUntouchedWithoutCreatingWayUp' },
    @{ Name = 'SharedAlias'; From = '? exit with'; To = '? MutateSharedExit(exit) with';
       Test = 'WayUpTimerReplacementDoesNotMutateSharedExitRecords';
       Helper = 'private static Exit MutateSharedExit(Exit exit) { exit.MinTime = 0; return exit; }' }
)) {
    if (!$timerOriginal.Contains($mutant.From)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $source = $timerOriginal.Replace($mutant.From, $mutant.To)
    if ($mutant.Helper) {
        $helperAnchor = 'private static bool? _abpsPresent;'
        if (!$source.Contains($helperAnchor)) { throw 'Missing shared-alias mutant helper anchor.' }
        $source = $source.Replace($helperAnchor, $mutant.Helper + [Environment]::NewLine + $helperAnchor)
    }
    $replacement = Join-Path $timerScratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($replacement, $source)
    $targetsPath = Join-Path $timerScratch ($mutant.Name + '.targets')
    $escapedSource = [Security.SecurityElement]::Escape($replacement)
    $targets = @"
<Project>
  <Target Name="UseLabyrinthTimerMutant" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="Services\ArenaLocationMutator.cs" />
      <Compile Include="$escapedSource" />
    </ItemGroup>
  </Target>
</Project>
"@
    [IO.File]::WriteAllText($targetsPath, $targets)
    $result = Invoke-Test @('-c', ('LabyrinthTimerMutant' + $mutant.Name),
        "-p:CustomAfterMicrosoftCommonTargets=$targetsPath", '--filter', ('FullyQualifiedName~ArenaLabyrinthAccessTests.' + $mutant.Test))
    if ($result.Exit -eq 0 -or !$result.Output.Contains('[FAIL]') -or !$result.Output.Contains($mutant.Test)) {
        throw "Timer mutant did not fail its intended assertion: $($mutant.Name)`n$($result.Output)"
    }
    Write-Output ('Rejected mutant: ' + $mutant.Name)
}

if ([IO.File]::ReadAllText($timerSource) -cne $timerOriginal) { throw 'Production timer source changed.' }
$result = Invoke-Test @('-c', 'LabyrinthTimerChecks', '--filter',
    'FullyQualifiedName~ArenaLabyrinthAccessTests|FullyQualifiedName~LocationMutatorTests')
if ($result.Exit -ne 0 -or !$result.Output.Contains('ArenaMode.Tests.dll')) {
    throw "Production timer baseline failed:`n$($result.Output)"
}
Write-Output $result.Output.Trim()
Write-Output 'ARENA_LABYRINTH_TIMERS_PASSED'
