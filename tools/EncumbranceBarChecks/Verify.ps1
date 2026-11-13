$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'ArenaMode.Client/Compat/ArenaEncumbranceBarCompat.cs'
$original = [IO.File]::ReadAllText($source)
$dest = Join-Path $repo ('artifacts/encumbrance-mutants/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dest -Force | Out-Null
function Run([string]$path) {
    $output = & dotnet run --project $PSScriptRoot -c Release "-p:EncumbranceCompatSource=$path" 2>&1 | Out-String
    return @{ Code = $LASTEXITCODE; Output = $output }
}
$baseline = Run $source
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('ARENA_ENCUMBRANCE_BAR_CHECKS_PASSED')) { throw $baseline.Output }
$cases = @(
    @{ Name='ordinary-leak'; Anchor='!ArenaSession.IsActive'; Change='false'; Expected='normal raid preserves native' },
    @{ Name='wrong-health'; Anchor='|| !ReferenceEquals(body.HealthController, _health.GetValue(null))'; Change=''; Expected='unrelated panel health' },
    @{ Name='global-limits'; Anchor='_baseLimits.SetValue(bar, body.Physical.BaseOverweightLimits);'; Change='// removed physical limits'; Expected='Arena uses current physical limits' }
)
foreach ($case in $cases) {
    if (!$original.Contains($case.Anchor)) { throw "Missing mutant anchor: $($case.Name)" }
    $path = Join-Path $dest ($case.Name + '.cs')
    [IO.File]::WriteAllText($path, $original.Replace($case.Anchor, $case.Change))
    $result = Run $path
    [IO.File]::WriteAllText((Join-Path $dest ($case.Name + '.log')), $result.Output)
    if ($result.Code -eq 0 -or !$result.Output.Contains($case.Expected)) { throw "Mutant did not fail the intended assertion: $($case.Name)`n$($result.Output)" }
    Write-Output "PASS mutant $($case.Name)"
}
$final = Run $source
if ($final.Code -ne 0) { throw $final.Output }
Write-Output 'ARENA_ENCUMBRANCE_BAR_VERIFIED baseline=9 mutants=3'
