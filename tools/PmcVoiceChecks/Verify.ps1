$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$voice = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Audio/ArenaPmcVoice.cs')).Path
$policy = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Audio/ArenaPmcVoicePolicy.cs')).Path
$faults = @(
    @{ Name='stale-raid'; Path=$voice; Property='VoiceSource'; Find='&& ArenaSession.IsExactActiveRaid(game, world);'; Replace='&& true;'; Failure='excluded stale game' },
    @{ Name='dead-speaker'; Path=$voice; Property='VoiceSource'; Find='body.HealthController?.IsAlive != true'; Replace='false'; Failure='excluded dead' },
    @{ Name='registry-ignored'; Path=$voice; Property='VoiceSource'; Find='!ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)'; Replace='false'; Failure='excluded registry mismatch' },
    @{ Name='respawn-ignored'; Path=$voice; Property='VoiceSource'; Find='ArenaRespawnDriver.IsRespawnInProgress ||'; Replace='false ||'; Failure='excluded respawn' },
    @{ Name='spectator-ignored'; Path=$voice; Property='VoiceSource'; Find='ArenaSpectatorService.IsActive ||'; Replace='false ||'; Failure='excluded spectator' },
    @{ Name='bank-only-swap'; Path=$voice; Property='VoiceSource'; Find='speaker.PlayerVoice = _voice;'; Replace='// voice name omitted'; Failure='BEAR choice assigns BEAR voice' },
    @{ Name='lost-owned-cancel'; Path=$voice; Property='VoiceSource'; Find="private static void ClearBody()`n    {`n        CancelOwnedSpeech();"; Replace="private static void ClearBody()`n    {"; Failure='death cancels owned speech' },
    @{ Name='pooled-source-stop'; Path=$voice; Property='VoiceSource'; Find='&& ReferenceEquals(SpeechSourceField.GetValue(_playingBody), _playingNativeSource)'; Replace='&& true'; Failure='pooled source cannot stop foreign audio' },
    @{ Name='forgotten-token'; Path=$voice; Property='VoiceSource'; Find='try { token?.Release(); }'; Replace='try { }'; Failure='raid end restores voice and releases token once' },
    @{ Name='stranded-loading'; Path=$voice; Property='VoiceSource'; Find='cancelled?.TrySetResult(true);'; Replace='// no load cancellation'; Failure='pending load released at teardown before completion' },
    @{ Name='reentrant-clip-claimed'; Path=$voice; Property='VoiceSource'; Find='|| emitted == null || !ReferenceEquals(speaker.Clip, emitted)'; Replace=''; Failure='reentrant native clip never claimed' },
    @{ Name='cooldown-removed'; Path=$policy; Property='PolicySource'; Find='now - lastSpoke >= CooldownSeconds'; Replace='true'; Failure='cooldown suppresses empty-gun followup' },
    @{ Name='toggle-gates-identity'; Path=$voice; Property='VoiceSource'; Find='if (!CurrentBody(out var body))'; Replace='if (!Current(out var body))'; Failure='disable retains faction speaker' },
    @{ Name='disabled-action-hooks'; Path=$voice; Property='VoiceSource'; Find='return Settings.PmcVoiceEnabled?.Value == true && CurrentBody(out player);'; Replace='return CurrentBody(out player);'; Failure='disabled hook cannot queue before enabling dry' },
    @{ Name='disabled-reload-cue'; Path=$voice; Property='VoiceSource'; Find='if (commentary && reloading &&'; Replace='if (reloading &&'; Failure='reenabling cannot replay disabled reload' },
    @{ Name='disabled-queue-survives'; Path=$voice; Property='VoiceSource'; Find='if (!commentary || AnnouncerService.IsPmcVoiceBlocked)'; Replace='if (AnnouncerService.IsPmcVoiceBlocked)'; Failure='disable cancels owned clip only and retains voice' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'PmcVoiceChecks.csproj') -Token 'PMC_VOICE_CHECKS_PASSED' -Properties @() -Faults $faults
