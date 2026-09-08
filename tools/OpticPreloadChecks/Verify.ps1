param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject = Join-Path $PSScriptRoot 'OpticPreloadChecks.csproj'
$arenaWork = Join-Path $arenaRepo ('artifacts\optic-preload-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaGuard = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Kits\KitOpticPreloadGuard.cs'))
$arenaPreloader = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Kits\KitBundlePreloader.cs'))

function Invoke-Checks([string[]]$Extra = @()) {
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaBuild = @(& dotnet build $arenaProject -t:Rebuild -c Release --nologo @Extra 2>&1)
        $arenaBuildExit = $LASTEXITCODE
        if ($arenaBuildExit -ne 0) { throw ($arenaBuild -join [Environment]::NewLine) }
        $arenaLines = @(& dotnet run --project $arenaProject -c Release --no-build 2>&1)
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    $arenaText = ($arenaLines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    return @{ Exit = $arenaExit; Text = $arenaText }
}
function Assert-Production {
    $arenaResult = Invoke-Checks
    Write-Output $arenaResult.Text
    if ($arenaResult.Exit -ne 0 -or $arenaResult.Text -notmatch 'ARENA_OPTIC_PRELOAD_CONTRACTS_PASSED') {
        throw 'Production-linked optic preload checks failed'
    }
}
function Test-Mutant([string]$Name, [string]$Before, [string]$After, [string]$Assertion, [string]$Property = 'GuardSource') {
    $arenaSource = if ($Property -eq 'GuardSource') { $arenaGuard } else { $arenaPreloader }
    if (-not $arenaSource.Contains($Before)) { throw "Missing exact mutation anchor: $Name" }
    $arenaPath = Join-Path $arenaWork ($Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaSource.Replace($Before, $After))
    $arenaResult = Invoke-Checks @("-p:${Property}=$arenaPath")
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name + '.log')), $arenaResult.Text)
    if ($arenaResult.Exit -eq 0 -or -not $arenaResult.Text.Contains('ASSERT: ' + $Assertion)) {
        Write-Output $arenaResult.Text
        throw "Mutant must fail its named behavioral assertion: $Name"
    }
    Write-Output "Optic preload negative control rejected: $Name -> $Assertion"
}

try {
    Assert-Production
    Test-Mutant 'ordinary-raid-gate-removed' 'Session.ArenaSession.IsActive' 'true' 'normal raid stays vanilla inside stale Arena context'
    Test-Mutant 'resource-ownership-removed' '&& request.Resources.Contains(pool.ResourceKey)' '' 'foreign resource passes through'
    Test-Mutant 'exact-pool-identity-removed' '&& ReferenceEquals(registered.Source.Task.Result, pool)' '' 'foreign registry passes through'
    Test-Mutant 'mounted-optic-gate-removed' '|| __instance.GetComponentInParent<WeaponPrefab>(includeInactive: true) != null' '' 'mounted optic passes through during owned pool call'
    Test-Mutant 'hierarchy-ownership-removed' '|| !__instance.transform.IsChildOf(frame.Parent)' '' 'unrelated optic hierarchy passes through inside owned call'
    Test-Mutant 'epoch-ownership-removed' '&& _generation == _epoch' '' 'raid reset expires old request ownership'
    Test-Mutant 'disposed-request-check-removed' '&& !_disposed' '' 'disposed request invalidates escaped child continuation'
    Test-Mutant 'async-context-replaced-with-thread-local' 'AsyncLocal<Request>' 'ThreadLocal<Request>' 'request ownership flows across asynchronous continuation'
    Test-Mutant 'frame-finalizer-cleanup-removed' 'if (__state != null) _frame = __state.Previous;' '// missing cleanup' 'failed original call clears synchronous ownership window'
    Test-Mutant 'paired-disable-suppression-removed' 'return !request.IsCurrent' 'return true' 'paired role model disable leaves active reticle untouched'
    Test-Mutant 'preloader-request-scope-removed' 'using (KitOpticPreloadGuard.Begin(objectsFactory, keys))' 'using ((IDisposable)null)' 'successful protected preload warms template' 'PreloaderSource'
}
finally {
    # Force an actual rebuild of production sources after switching compiler input.
    Assert-Production
}
Write-Output "Optic preload check evidence: $arenaWork"
Write-Output 'ARENA_OPTIC_PRELOAD_CHECKS_PASSED'
