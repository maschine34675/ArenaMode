$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $arenaRepo 'ArenaMode.Client/Session/ArenaPostRaidScreens.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$hash = (Get-FileHash -LiteralPath $sourcePath).Hash
$project = Join-Path $PSScriptRoot 'ArenaPostRaidChecks.csproj'
$evidence = Join-Path $arenaRepo ('artifacts/postraid-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
function Invoke-Case([string]$name, [string]$path) {
    $text = & dotnet run --project $project -c $name "-p:PostRaidSource=$path" -- $arenaRepo 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence ($name + '.log')), $text)
    return @{ Code=$exitCode; Text=$text }
}
$baseline = Invoke-Case 'Release' $sourcePath
if ($baseline.Code -ne 0 -or !$baseline.Text.Contains('ARENA_POSTRAID_PRODUCTION_CHECKS_PASSED')) { throw $baseline.Text }
Write-Output $baseline.Text.Trim()
$mutations = @(
    @{Name='MissingEndedRaid'; From='!ReferenceEquals(_endedRaid, endedRaid)'; To='false'; Failure='wrong native raid capture rejected 0'},
    @{Name='OrdinaryResult'; From='=> ArenaSession.LastRaidWasArena && !ArenaSession.IsActive'; To='=> !ArenaSession.IsActive'; Failure='invalid scope warning unchanged 1'},
    @{Name='ForeignOperation'; From='&& ReferenceEquals(operation, _operation)'; To=''; Failure='second operation cannot steal result'},
    @{Name='KeepExperience'; From='operation.ShowHealthTreatmentScreen();'; To='return true;'; Failure='XP uses native health continuation'},
    @{Name='LoseHealthHistory'; From='new[] { controllers[0], controllers[1], controllers[3] }'; To='new[] { controllers[0], controllers[1], controllers[2] }'; Failure='native main-menu history omits XP only'},
    @{Name='ForeignHistory'; From='!ReferenceEquals(controllers[2], operation._experienceScreenController)'; To='false'; Failure='foreign sequence retained 2'}
)
foreach ($mutation in $mutations) {
    if (!$source.Contains($mutation.From) -or $source.IndexOf($mutation.From) -ne $source.LastIndexOf($mutation.From)) { throw "Mutation anchor invalid: $($mutation.Name)" }
    $path = Join-Path $evidence ($mutation.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($mutation.From, $mutation.To))
    $result = Invoke-Case $mutation.Name $path
    if ($result.Code -eq 0 -or $result.Text.Contains('error CS') -or !$result.Text.Contains('ASSERTION_FAILED: ' + $mutation.Failure)) {
        throw "Unexpected defect result $($mutation.Name): $($result.Text)"
    }
    Write-Output "Rejected compiled defect: $($mutation.Name)"
}
if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne $hash) { throw 'Production input changed during checks.' }
$hash | Set-Content -LiteralPath (Join-Path $evidence 'source-sha256.txt')
& dotnet run --project (Join-Path $arenaRepo 'tools/ArenaUiInteractionChecks/ArenaUiInteractionChecks.csproj') -c Release -- $arenaRepo
if ($LASTEXITCODE -ne 0) { throw 'Existing raid-exit regression failed.' }
Write-Output "Evidence: $evidence"
Write-Output 'ARENA POSTRAID CHECKS PASSED'
