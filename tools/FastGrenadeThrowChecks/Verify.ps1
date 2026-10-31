param([string]$PublishedDll, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'FastGrenadeThrowChecks.csproj'
$evidence = Join-Path $repo ('artifacts\compat-remaining-20261026\items\fastgrenade-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/ | Set-Content (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Fast Grenade Throw actual-EFT API build failed' }
dotnet build $project -c Release --nologo | Set-Content (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Fast Grenade Throw harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\FastGrenadeThrowChecks.exe'
foreach ($testMode in @('normal','newer','drift','older')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Fast Grenade Throw check failed: ' + $testMode) }
}
if ($PublishedDll) { & $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt'); if ($LASTEXITCODE) { throw 'Published Fast Grenade Throw differs from reviewed release' } }
if (-not $SkipMutants) {
    # Each mutant edits one linked source: Fast Grenade Throw (CompatSource, the default) or the PiP preload adapter (PipSource).
    $sources = @{
        CompatSource = Join-Path $repo 'ArenaMode.Client\Compat\FastGrenadeThrow\FastGrenadeThrowCompat.cs'
        PipSource = Join-Path $repo 'ArenaMode.Client\Compat\PipPreloadCompat.cs'
    }
    $mutants = @(
        @{ Name='ignore-arena-scope'; Before='if (!ArenaSession.IsActive || !CurrentPlayer(__0)) return;'; After='if (!CurrentPlayer(__0)) return;'; Expected='ordinary raid preserves foreign behavior' },
        @{ Name='ignore-slot-policy'; Before='candidates.Any(item => ReferenceEquals(item, selected))'; After='true'; Expected='invalid priority leaves foreign result: excluded' },
        @{ Name='bypass-input-lock'; Before='&& !ArenaInputBlocker.Blocked'; After=''; Expected='blocked transition cannot enter foreign throw' },
        @{ Name='accept-old-body'; Before='return CurrentPlayer(__0) &&'; After='return true &&'; Expected='blocked transition cannot enter foreign throw' },
        @{ Name='absent-plugin-unpatches'; Before='if (_patched) { Patches.UnpatchSelf(); _patched = false; }'; After='Patches.UnpatchSelf(); _patched = false;'; Expected="absent plugin polls never walk Harmony's patch table" },
        @{ Name='flag-never-raised'; Before='            _patched = true;'; After=''; Expected='a bound adapter still unbinds itself once its hooks were removed' },
        # The flag raised only after both Patch calls: a failing second call leaves the first postfix behind.
        @{ Name='flag-raised-after-second-patch'; Before='Patches.Patch(_throw, prefix: new HarmonyMethod(typeof(FastGrenadeThrowCompat), nameof(BeforeThrow)) { priority = Priority.First });'; After='_patched = false; Patches.Patch(_throw, prefix: new HarmonyMethod(typeof(FastGrenadeThrowCompat), nameof(BeforeThrow)) { priority = Priority.First }); _patched = true;'; Expected='the bind after a failed second Patch is admitted (the half-applied set was unpatched)' },
        @{ Name='pip-refused-contract-unpatches'; Property='PipSource'; Before='if (!_patched) return;'; After=''; Expected="refused PiP Disabler contract polls never walk Harmony's patch table" }
    )
    foreach ($mutant in $mutants) {
        $property = if ($mutant.Property) { $mutant.Property } else { 'CompatSource' }
        $source = [IO.File]::ReadAllText($sources[$property])
        if (-not $source.Contains($mutant.Before)) { throw ('Mutation anchor missing: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name; New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'; [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:$property=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\FastGrenadeThrowChecks.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for an unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
