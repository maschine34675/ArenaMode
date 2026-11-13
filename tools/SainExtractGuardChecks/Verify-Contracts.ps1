param([string]$SptRoot = 'D:\SPT41')
# Reads (never loads or changes) the installed SAIN client assembly with Mono.Cecil and checks
# the facts the extraction guard relies on: the admission method's shape, that SAIN's extract
# layer asks it behind SAIN_EXTRACT_TOGGLE, and that the only SAIN code which touches an exit or
# removes a bot from the map is ExtractAction, which only that layer selects.
$ErrorActionPreference = 'Stop'
$cecil = Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll'
$path = Join-Path $SptRoot 'BepInEx/plugins/SAIN/SAIN.dll'
if (-not (Test-Path -LiteralPath $path)) { Write-Output "SAIN_EXTRACT_CONTRACTS_SKIPPED: no SAIN at $path"; return }
Add-Type -Path $cecil
$sain = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    if ($sain.Name.Name -ne 'SAIN' -or $sain.Name.Version -lt [Version]'4.5.1.0') { throw "SAIN version not reviewed: $($sain.Name.Version)" }
    $manager = $sain.MainModule.GetType('SAIN.Components.BotController.BotExtractManager')
    $allowed = @($manager.Methods | Where-Object Name -eq 'IsBotAllowedToExfil')
    if ($allowed.Count -ne 1 -or !$allowed[0].IsPublic -or !$allowed[0].IsStatic -or
        $allowed[0].ReturnType.FullName -ne 'System.Boolean' -or $allowed[0].Parameters.Count -ne 1 -or
        $allowed[0].Parameters[0].ParameterType.FullName -ne 'SAIN.Components.BotComponent') { throw 'IsBotAllowedToExfil signature drift' }
    # Side-effect free: no field writes and no calls besides profile reads.
    foreach ($instruction in $allowed[0].Body.Instructions) {
        if ($instruction.OpCode.Code -in @('Stfld', 'Stsfld')) { throw 'IsBotAllowedToExfil now writes state' }
    }

    $calls = @{}; $types = @{}
    foreach ($type in $sain.MainModule.GetTypes()) {
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $site = $type.FullName + '::' + $method.Name
                $operand = $instruction.Operand
                if ($operand -is [Mono.Cecil.MethodReference]) {
                    $key = $operand.DeclaringType.FullName + '::' + $operand.Name
                    if (-not $calls.ContainsKey($key)) { $calls[$key] = New-Object System.Collections.Generic.HashSet[string] }
                    [void]$calls[$key].Add($site)
                    if ($operand.DeclaringType.FullName -eq 'SAIN.Layers.ExtractAction' -and $operand.Name -eq '.ctor') {
                        if (-not $types.ContainsKey('ctor')) { $types['ctor'] = New-Object System.Collections.Generic.HashSet[string] }
                        [void]$types['ctor'].Add($site)
                    }
                }
                elseif ($operand -is [Mono.Cecil.TypeReference] -and $operand.FullName -eq 'SAIN.Layers.ExtractAction') {
                    if (-not $types.ContainsKey('token')) { $types['token'] = New-Object System.Collections.Generic.HashSet[string] }
                    [void]$types['token'].Add($site)
                }
                elseif ($operand -is [Mono.Cecil.FieldReference] -and $operand.Name -eq 'SAIN_EXTRACT_TOGGLE') {
                    $key = 'toggle'
                    if (-not $calls.ContainsKey($key)) { $calls[$key] = New-Object System.Collections.Generic.HashSet[string] }
                    [void]$calls[$key].Add($site)
                }
            }
        }
    }
    function Sites([string]$key) { if ($calls.ContainsKey($key)) { return @($calls[$key]) } else { return @() } }

    $askers = Sites 'SAIN.Components.BotController.BotExtractManager::IsBotAllowedToExfil'
    if ($askers -notcontains 'SAIN.Layers.ExtractLayer::allowedToExtract') { throw 'ExtractLayer no longer asks IsBotAllowedToExfil' }
    if ((Sites 'toggle') -notcontains 'SAIN.Layers.ExtractLayer::allowedToExtract') { throw 'ExtractLayer no longer reads SAIN_EXTRACT_TOGGLE' }
    if ((Sites 'SAIN.Layers.ExtractLayer::allowedToExtract') -notcontains 'SAIN.Layers.ExtractLayer::IsActive') { throw 'ExtractLayer.IsActive no longer requires allowedToExtract' }

    # Every exit or removal path in SAIN lives in ExtractAction.
    foreach ($key in @('EFT.Interactive.ExfiltrationPoint::OnItemTransferred', 'EFT.Interactive.ExfiltrationPoint::Proceed',
                       'BotLeaveData::RemoveFromMap', 'BotLeaveData::DoLeaveExternal')) {
        foreach ($site in (Sites $key)) {
            if (-not $site.StartsWith('SAIN.Layers.ExtractAction::')) { throw "Exit path outside ExtractAction: $site -> $key" }
        }
    }
    if ((Sites 'BotLeaveData::RemoveFromMap').Count -lt 1) { throw 'ExtractAction no longer removes the bot (contract changed, review again)' }
    # ExtractAction is selected only by ExtractLayer (BigBrain creates it from the type token).
    $selectors = @()
    if ($types.ContainsKey('token')) { $selectors += @($types['token']) }
    if ($types.ContainsKey('ctor')) { $selectors += @($types['ctor']) }
    foreach ($site in $selectors) {
        if ($site -ne 'SAIN.Layers.ExtractLayer::GetNextAction') { throw "ExtractAction selected outside ExtractLayer: $site" }
    }
    if ($selectors -notcontains 'SAIN.Layers.ExtractLayer::GetNextAction') { throw 'ExtractLayer no longer selects ExtractAction' }

    Write-Output ("SAIN_EXTRACT_CONTRACTS_PASSED: SAIN $($sain.Name.Version); IsBotAllowedToExfil asked by " + (($askers | Sort-Object) -join ', '))
}
finally { $sain.Dispose() }
