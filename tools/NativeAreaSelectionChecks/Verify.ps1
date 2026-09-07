param(
    [Parameter(Mandatory = $true)][ValidateSet('Rules', 'Integration', 'Build')][string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaResults = Join-Path $arenaRepo 'artifacts\area-menu-checks'
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
function Invoke-AreaMenuCheck([string]$Harness) {
    & dotnet run --project "tools\$Harness\$Harness.csproj" -c Release "-p:SPTPath=$SptRoot"
    if ($LASTEXITCODE -ne 0) { throw "Area menu check failed: $Harness" }
}
Push-Location $arenaRepo
Start-Transcript -Path (Join-Path $arenaResults "stage-$Stage.log") -Force | Out-Null
try {
    switch ($Stage) {
        'Rules' {
            Invoke-AreaMenuCheck 'NativeMenuChecks'
            & (Join-Path $PSScriptRoot 'NegativeControls.ps1') -SptRoot $SptRoot
        }
        'Integration' {
            foreach ($arenaHarness in @('NativeAreaHookChecks', 'NativeModeSelectionChecks',
                'NativeMapViewChecks', 'ArenaLabyrinthAccessChecks')) { Invoke-AreaMenuCheck $arenaHarness }
            & (Join-Path $PSScriptRoot 'HookNegativeControls.ps1')
        }
        'Build' {
            # Serial solution + complete server suite + explicit Release client
            # companion build and four installed SHA256 comparisons.
            & (Join-Path $PSScriptRoot '..\ArenaAreaChecks\Verify.ps1') -Stage Build -SptRoot $SptRoot
        }
    }
    Write-Output ('ARENA_AREA_MENU_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
