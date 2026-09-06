#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ArenaAssemblyChecks.csproj'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-assembly-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$mutants = @(
    @{ Name='LiveNotice'; Path='ArenaMode.Client/Modes/TacticalMode.AssemblyStatus.cs'; Property='NoticeSource';
       From='_teamArena && _phase == Phase.Staging'; To='_teamArena && (_phase == Phase.Staging || _phase == Phase.Live)';
       Failure='combat/end hides immediately before refresh timer' },
    @{ Name='NormalRaid'; Path='ArenaMode.Client/Hud/ArenaAssemblyBanner.cs'; Property='BannerSource';
       From='!ArenaSession.IsActive ||'; To=''; Failure='invalid scope stays hidden' },
    @{ Name='PrematureReady'; Path='ArenaMode.Client/Modes/TacticalMode.AssemblyStatus.cs'; Property='NoticeSource';
       From='_teamArena && _phase == Phase.Staging'; To='_teamArena && _phase == Phase.Staging && _squad.Count < 5';
       Failure='assembly persists through full arrival until combat readiness' }
)
foreach ($mutant in $mutants) {
    $path = Join-Path $repo $mutant.Path
    $text = [IO.File]::ReadAllText($path)
    if (!$text.Contains($mutant.From)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $copy = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $text.Replace($mutant.From,$mutant.To))
    $output = & dotnet run --project $project -c ('AssemblyMutant' + $mutant.Name) "-p:$($mutant.Property)=$copy" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains($mutant.Failure)) { throw "Wrong mutant failure: $($mutant.Name)`n$output" }
    Write-Output "Rejected: $($mutant.Name)"
}
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production baseline failed.' }
'ARENA_ASSEMBLY_REGRESSION_PASSED'
