param([string]$NativeRoot = (Join-Path $PSScriptRoot '../../../_decompiled'))
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaSourcePath = Join-Path $arenaRepo 'ArenaMode.Client/Bots/ArenaBotPatches.cs'
$arenaProject = Join-Path $PSScriptRoot 'DeathmatchGroupChecks.csproj'
$arenaEvidence = Join-Path $PSScriptRoot ('evidence/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaEvidence) | Out-Null
$arenaOriginal = [IO.File]::ReadAllText($arenaSourcePath)
$arenaHashes = [ordered]@{}
foreach ($arenaPath in @($arenaSourcePath, (Join-Path $NativeRoot 'BotZoneGroups.cs'),
        (Join-Path $NativeRoot 'BotZoneGroupsDictionary.cs'), (Join-Path $NativeRoot 'EFT/BotMemory.cs'),
        (Join-Path $NativeRoot 'BotCreatorClient.cs'), (Join-Path $NativeRoot 'EFT/BotSpawner.cs'))) {
    $arenaAbsolute = [IO.Path]::GetFullPath($arenaPath)
    $arenaHashes[$arenaAbsolute] = (Get-FileHash -LiteralPath $arenaAbsolute -Algorithm SHA256).Hash
}

function Invoke-GroupCase([string]$Name, [string]$Source, [string]$ExpectedFailure = '') {
    $arenaCase = Join-Path $arenaEvidence $Name
    [IO.Directory]::CreateDirectory($arenaCase) | Out-Null
    $arenaCaseSource = Join-Path $arenaCase 'Input-ArenaBotPatches.cs'
    [IO.File]::WriteAllText($arenaCaseSource, $Source)
    & (Join-Path $PSScriptRoot 'Extract-Fragments.ps1') -OutputPath $arenaCase -SourcePath $arenaCaseSource -NativeRoot $NativeRoot
    $arenaOutput = & dotnet run --project $arenaProject -c $Name "-p:FragmentsRoot=$arenaCase" 2>&1 | Out-String
    $arenaExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $arenaCase 'run.log'), $arenaOutput)
    if ($ExpectedFailure) {
        if ($arenaExit -eq 0 -or $arenaOutput -match 'error CS\d+' -or !$arenaOutput.Contains($ExpectedFailure)) {
            throw "Wrong compiled fault-control result $Name ($arenaExit): $arenaOutput"
        }
        Write-Host "Rejected compiled fault control: $Name"
    } else {
        if ($arenaExit -ne 0 -or $arenaOutput -notmatch 'DEATHMATCH_GROUP_RUNTIME_PASSED: (\d+) assertions') {
            throw "Group baseline failed ($arenaExit): $arenaOutput"
        }
        Write-Host $Matches[0]
    }
    return [pscustomobject]@{
        Name = $Name; ExitCode = $arenaExit; ExpectedFailure = $ExpectedFailure;
        Log = (Join-Path $arenaCase 'run.log');
        ProductionFragmentSha256 = (Get-FileHash -LiteralPath (Join-Path $arenaCase 'ArenaBotPatches.cs')).Hash
    }
}

$arenaBaseline = Invoke-GroupCase 'Release' $arenaOriginal
$arenaPreActiveNeedle = 'if (group.MembersCount != 0 || (!owner.ObservedMembers && owner.Bot?.IsDead != true)) continue;'
if (!$arenaOriginal.Contains($arenaPreActiveNeedle)) { throw 'PreActive lifetime fault-control seam changed; review it explicitly.' }
$arenaPreActiveSource = $arenaOriginal.Replace($arenaPreActiveNeedle, 'if (group.MembersCount != 0) continue; /* compiled negative control */')
$arenaPreActive = Invoke-GroupCase 'PrematureRetirement' $arenaPreActiveSource 'PreActive zero-member groups survive after AfterCreation and a long loading delay'
$arenaCohortNeedle = 'if (_reconciling) return;'
if (!$arenaOriginal.Contains($arenaCohortNeedle)) { throw 'Cohort admission fault-control seam changed; review it explicitly.' }
$arenaCohortSource = $arenaOriginal.Replace($arenaCohortNeedle, 'if (true) return; /* compiled negative control */')
$arenaCohort = Invoke-GroupCase 'MissingCohortAdmission' $arenaCohortSource 'activated cohort obtains both reciprocal enemy relationships'
foreach ($arenaPath in $arenaHashes.Keys) {
    if ((Get-FileHash -LiteralPath $arenaPath -Algorithm SHA256).Hash -ne $arenaHashes[$arenaPath]) {
        throw "Source changed while checking native group contracts: $arenaPath"
    }
}
[ordered]@{
    SourceHashes = $arenaHashes; Baseline = $arenaBaseline; NegativePreActive = $arenaPreActive;
    NegativeCohort = $arenaCohort; FullClientBuilt = $false; Deployed = $false; LiveGpuTested = $false;
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $arenaEvidence 'receipt.json')
Write-Output "Evidence: $arenaEvidence/receipt.json"
Write-Output 'DEATHMATCH_GROUP_CHECKS_PASSED'
