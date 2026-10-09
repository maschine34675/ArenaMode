$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $PSScriptRoot ('artifacts/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$project = Join-Path $PSScriptRoot 'ZombieVoiceBudgetChecks.csproj'
function Run-Case([string]$name, [string[]]$properties, [bool]$pass) {
    $log = Join-Path $stage ($name + '.log')
    # Windows PowerShell wraps expected mutant stderr as NativeCommandError.
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet run --project $project -c $name --nologo @properties *> $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = 'Stop' }
    $content = Get-Content -LiteralPath $log -Raw
    if ($pass) {
        if ($code -ne 0 -or $content -notmatch 'ZOMBIE_VOICE_BUDGET_CHECKS_PASSED') { throw "Production failed: $log`n$content" }
        Write-Host ([regex]::Match($content, 'ZOMBIE_VOICE_BUDGET_CHECKS_PASSED.*').Value)
        $script:assertions = [int]([regex]::Match($content, 'assertions=(\d+)').Groups[1].Value)
    } elseif ($code -eq 0 -or $content -match 'error [A-Z]+\d+' -or $content -notmatch 'FAILED:') {
        throw "Mutant must compile and fail a runtime assertion: $log`n$content"
    }
}
Run-Case 'Release' @() $true
$mutants = @(
    @{Name='inactive-raid';Old='!ZombieNativeEventBridge.IsActiveZombieRaid()';New='false'},
    @{Name='stale-game-world';Old='&& ArenaSession.IsExactActiveRaid(game, world)';New='&& true'},
    @{Name='boss-human-role';Old='=> role == WildSpawnType.infectedAssault';New='=> true || role == WildSpawnType.infectedAssault'},
    @{Name='dead-body';Old='body.HealthController?.IsAlive != true || ';New=''},
    @{Name='stale-current-body';Old='&& ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)';New='&& true'},
    @{Name='custom-brain';Old='&& owner.Brain?.BaseBrain?.GetType() == typeof(InfectedLayersStrategy)';New='&& true'},
    @{Name='native-follower';Old='&& !owner.IsFollower() && owner.BotFollower?.HaveBoss != true';New='&& true'},
    @{Name='pft-follower';Old='&& !PitFireTeamCompat.IsTeamPlayer(body)';New='&& true'},
    @{Name='critical-voice-lost';Old='phrase != EPhraseTrigger.OnMutter && phrase != EPhraseTrigger.OnEnemyConversation';New='false'},
    @{Name='near-threat-lost';Policy=$true;Old='=> attackPhrase && distanceSquared <= ThreatDistanceSquared;';New='=> false;'},
    @{Name='baseline-suppressed';Old='if (_budgetEnabled) { _skipped++; return false; }';New='if (true) { _skipped++; return false; }'},
    @{Name='stopped-voice-holds-slot';Old='|| !voice.Source.source1.isPlaying';New=''},
    @{Name='interrupted-voice-holds-slot';Old='|| !ReferenceEquals(voice.Source.source1.clip, voice.Clip)';New=''},
    @{Name='replacement-double-counted';Old='if (ReferenceEquals(voice.Body, replacing)) continue;';New='if (false) continue;'},
    @{Name='declined-replacement-frees-old';Old='if (ReferenceEquals(voice.Body, replacing)) continue;';New='if (ReferenceEquals(voice.Body, replacing)) { Voices[i] = default; continue; }'},
    @{Name='wrong-speaker';Old='|| !ReferenceEquals(body.Speaker, speaker)';New=''},
    @{Name='no-cleanup';Old='Array.Clear(Voices, 0, Voices.Length);';New=';'},
    @{Name='seventh-repetitive-voice';Policy=$true;Old='MaximumVoices = 6';New='MaximumVoices = 7'},
    @{Name='fifth-distant-voice';Policy=$true;Old='DistantVoices = 4';New='DistantVoices = 5'}
)
$sourcePath = Join-Path $repo 'ArenaMode.Client/Bots/ZombieVoiceBudget.cs'
$policyPath = Join-Path $repo 'ArenaMode.Client/Bots/ZombieVoiceBudgetPolicy.cs'
$source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")
$policy = [IO.File]::ReadAllText($policyPath).Replace("`r`n", "`n")
foreach ($mutant in $mutants) {
    $original = if ($mutant.Policy) { $policy } else { $source }
    if (!$original.Contains($mutant.Old)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $copy = Join-Path $stage ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $original.Replace($mutant.Old, $mutant.New))
    $property = if ($mutant.Policy) { "-p:PolicySource=$copy" } else { "-p:BudgetSource=$copy" }
    Run-Case $mutant.Name @($property) $false
    Write-Host "Rejected compiled mutant: $($mutant.Name)"
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{
    budgetSha256 = (Get-FileHash -LiteralPath $sourcePath).Hash
    policySha256 = (Get-FileHash -LiteralPath $policyPath).Hash
    assertions = $script:assertions; compiledMutants = $mutants.Count; passed = $true
} | ConvertTo-Json))
Write-Host "ZOMBIE_VOICE_MUTATION_CHECKS_PASSED $($mutants.Count) compiled mutants; evidence: $stage"
