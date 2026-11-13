param([string]$PublishedDll, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ShoulderSwapChecks.csproj'
$evidence = Join-Path $repo ('artifacts\shoulder-swap-checks\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build $project -c Release --nologo > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Shoulder Swap harness did not compile' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\ShoulderSwapChecks.exe'
& $exe 2>&1 | Tee-Object (Join-Path $evidence 'baseline.txt')
if ($LASTEXITCODE) { throw 'Shoulder Swap baseline failed' }
if ($PublishedDll) {
    & $exe $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
    if ($LASTEXITCODE) { throw 'Published Shoulder Swap contract differs' }
}
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client\Compat\ArenaShoulderSwapCompat.cs'))
    $mutants = @(
        @{ Name='allow-knife'; Before='if (!(player.HandsController is Player.FirearmController) || player.HealthController == null'; After='if (player.HealthController == null'; Expected='empty hands excluded' },
        @{ Name='allow-empty-scopes'; Before='pwa.ScopeAimTransforms.Count == 0'; After='pwa.ScopeAimTransforms.Count < 0'; Expected='System.DivideByZeroException' },
        @{ Name='ignore-exact-raid'; Before=' || !ArenaSession.IsExactActiveRaid(game, world)'; After=''; Expected='wrong active game passes through' },
        @{ Name='ignore-body-commit'; Before=' && ReferenceEquals(game.LocalPlayer, player)'; After=''; Expected='successor not yet committed passes through' },
        @{ Name='ignore-camera-owner'; Before='|| !ReferenceEquals(Singleton<PlayerCameraController>.Instance, __0) || HasSafeHands(player)'; After='|| HasSafeHands(player)'; Expected='detached camera is untouched' },
        @{ Name='retry-rejected-instance'; Before='if (ReferenceEquals(_instance, info.Instance)) return _rejection;'; After=''; Expected='refused instance is not retried every display pass' },
        @{ Name='keep-camera-patch'; Before='if (_camera != null) Patches.Unpatch(_camera, HarmonyPatchType.Prefix, Patches.Id);'; After=''; Expected='shutdown removes both owned prefixes' }
    )
    foreach ($mutant in $mutants) {
        if ($source.Split([string[]]@($mutant.Before), [StringSplitOptions]::None).Count -ne 2) { throw ('Non-unique/missing mutation anchor: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'
        [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:CompatSource=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\ShoulderSwapChecks.exe') > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains($mutant.Expected))) { throw ('Unexpected mutant failure: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
