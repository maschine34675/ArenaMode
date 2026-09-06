#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ArenaNotificationChecks.csproj'
$sourcePath = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Notifications/ArenaProgressNotifications.cs')).Path
$originalHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$scratch = Join-Path $PSScriptRoot ('obj/regression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
function Invoke-Baseline {
    $output = & dotnet run --project $project -c Release 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'ARENA_NOTIFICATION_CHECKS_PASSED') {
        throw "Baseline failed: $($output -join [Environment]::NewLine)"
    }
    $output | Write-Output
}
Invoke-Baseline
$mutants = @(
    @{ Name = 'missed-ui-receive'; From = 'harmony.Patch(receive, prefix: Prefix(nameof(ReceivePrefix)));'; To = '';
       Failure = 'Arena quest/achievement notifications never enter the UI queue' },
    @{ Name = 'missed-final-display'; From = 'harmony.Patch(show, prefix: Prefix(nameof(ShowPrefix)));'; To = '';
       Failure = 'direct native ShowNotification cannot recreate audible progress popup' },
    @{ Name = 'missed-direct-sound'; From = 'harmony.Patch(subtask, prefix: direct);'; To = '';
       Failure = 'direct subtask and NotificationFilter replacement sounds suppressed' },
    @{ Name = 'deferred-achievement'; From = 'harmony.Patch(achievement, prefix: Prefix(nameof(DirectPresentationPrefix)));'; To = '';
       Failure = 'Arena achievement does not schedule a delayed postraid popup' },
    @{ Name = 'wrong-type-filter'; From = '(notification is NotificationQuest || notification is NotificationAchievement)'; To = 'notification != null';
       Failure = 'unrelated warnings remain audible after a dropped queued message' },
    @{ Name = 'stale-raid-latch'; From = '&& ArenaSession.IsExactActiveRaid(game, Singleton<GameWorld>.Instance)'; To = '';
       Failure = 'outside exact Arena including postraid, stale worlds and shutdown is native' },
    @{ Name = 'busy-queue'; From = '__result = false;'; To = '__result = true;';
       Failure = 'direct native ShowNotification cannot recreate audible progress popup' },
    @{ Name = 'no-shutdown-latch'; From = '_enabled = false; // Any surviving callback is immediately fail-open.'; To = '';
       Failure = 'shutdown fail-open flag protects normal behavior even if unpatch throws' }
)
foreach ($mutant in $mutants) {
    $source = Get-Content -LiteralPath $sourcePath -Raw
    if (($source.Split($mutant.From)).Count -ne 2) { throw "Mutation target must occur exactly once: $($mutant.Name)" }
    $copy = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($mutant.From, $mutant.To))
    $output = & dotnet run --project $project -c ('NotificationMutant-' + $mutant.Name) ('-p:NotificationSource=' + $copy) 2>&1
    $exitCode = $LASTEXITCODE
    $combined = $output -join "`n"
    if ($exitCode -eq 0 -or $combined -match 'error CS\d+' -or $combined -notmatch [regex]::Escape($mutant.Failure)) {
        throw "Mutant did not fail its intended behavioral assertion: $($mutant.Name), exit=$exitCode`n$combined"
    }
    Write-Output ('REJECTED ' + $mutant.Name + ': ' + $mutant.Failure)
}
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $originalHash) { throw 'Production source changed during regression run' }
Invoke-Baseline
Write-Output 'ARENA_NOTIFICATION_REGRESSION_PASSED'
