using ArenaMode.Audio;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using Diz.Resources;
using EFT;
using Redeploy.Core.Engine;
using UnityEngine;

int checks = 0;
void Check(bool result, string text)
{ checks++; if (!result) throw new Exception("FAIL: " + text); }

Player Setup(string mode = "teamdeathmatch")
{
    ArenaPmcVoice.Shutdown();
    Time.time = 100;
    UnityEngine.Random.Value = 0;
    var body = new Player();
    body.HandsController = new Player.FirearmController { _player = body };
    var world = new GameWorld { MainPlayer = body, Registered = body };
    var game = new LocalGame { Status = GameStatus.Started, LocalPlayer = body };
    Singleton<GameWorld>.Instance = world;
    Singleton<AbstractGame>.Instance = game;
    Singleton<IEasyAssets>.Instance = new();
    ArenaSession.IsActive = true; ArenaSession.ArmEpoch++;
    ArenaSession.Game = game; ArenaSession.World = world; ArenaSession.Selected = EPlayerSide.Bear;
    ArenaMatchService.Current = new ArenaMatch { Mode = new Mode { Id = mode } };
    ArenaMatchService.TeamStartAbortPending = false;
    ArenaRespawnDriver.StandDown = ArenaRespawnDriver.IsRespawnInProgress = false;
    ArenaSpectatorService.IsActive = false; KillcamCompat.CameraReleased = true;
    ArenaMode.Areas.ArenaAreaRuntime.IsPending = false;
    Settings.PmcVoiceEnabled.Value = true;
    AnnouncerService.IsPmcVoiceBlocked = false;
    ArenaPmcVoice.Enable();
    return body;
}
void Reload(Player body)
{
    ((Player.FirearmController)body.HandsController).Reloading = true;
    ArenaPmcVoice.Tick();
}
void Excluded(string label, Action<Player> change)
{
    var body = Setup(); change(body); Reload(body);
    Check(body.Speaker.Played.Count == 0 && body.Speaker.PlayerVoice == "Original", "excluded " + label);
}
void Kill(Player body, Action<ArenaKill> change = null)
{
    var kill = new ArenaKill { KillerBodyId = ArenaHitHistory.BodyId(body) };
    change?.Invoke(kill); ArenaMatchService.Current.Kill(kill);
}

Excluded("normal raid", _ => ArenaSession.IsActive = false);
Excluded("stale game", _ => ArenaSession.Game = new LocalGame());
Excluded("stale world", _ => ArenaSession.World = new GameWorld());
Excluded("stopped", _ => Singleton<AbstractGame>.Instance.Status = GameStatus.Stopped);
Excluded("AI", body => body.IsAI = true);
Excluded("foreign player", body => body.IsYourPlayer = false);
Excluded("dead", body => body.HealthController.IsAlive = false);
Excluded("unpublished body", body => ((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer = new Player());
Excluded("registry mismatch", body => Singleton<GameWorld>.Instance.Registered = new Player());
Excluded("profile mismatch", body => body.ProfileId = "another");
Excluded("respawn", _ => ArenaRespawnDriver.IsRespawnInProgress = true);
Excluded("standdown", _ => ArenaRespawnDriver.StandDown = true);
Excluded("spectator", _ => ArenaSpectatorService.IsActive = true);
Excluded("killcam", _ => KillcamCompat.CameraReleased = false);
Excluded("pending area", _ => ArenaMode.Areas.ArenaAreaRuntime.IsPending = true);
Excluded("failed team start", _ => ArenaMatchService.TeamStartAbortPending = true);
Excluded("complete match", _ => ArenaMatchService.Current.Mode.IsComplete = true);
Excluded("disabled", _ => Settings.PmcVoiceEnabled.Value = false);

var p = Setup(); var originalBanks = p.Speaker.PhrasesBanks.ToArray(); ArenaPmcVoice.Tick();
Check(ArenaPmcVoicePolicy.BearVoices.Contains(p.Speaker.PlayerVoice), "BEAR choice assigns BEAR voice");
Check(p.Speaker.SideTag == ETagStatus.Bear, "BEAR speaker tag assigned");
Check(((LocalGame)Singleton<AbstractGame>.Instance).Profile.Side == EPlayerSide.Usec
    && ((LocalGame)Singleton<AbstractGame>.Instance).Profile.Voice == "Original", "profile untouched");
var assetToken = Singleton<IEasyAssets>.Instance.Tokens.Single();
Check(assetToken.Releases == 0, "voice retained during match");
string voice = p.Speaker.PlayerVoice;
Reload(p);
Check(p.Speaker.Played.SequenceEqual(new[] { EPhraseTrigger.OnWeaponReload }), "reload uses native speech");
Check(p.SourceReads == 0, "speech tracking does not allocate source");
for (int i = 0; i < 30; i++) { Time.time += .01f; ArenaPmcVoice.Tick(); }
Check(p.Speaker.Played.Count == 1, "continuous reload speaks once");
Time.time += 1.5f;
((Player.FirearmController)p.HandsController).DryShot(); ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 1, "cooldown suppresses empty-gun followup");
((Player.FirearmController)p.HandsController).Reloading = false; Time.time += 8; ArenaPmcVoice.Tick();
((Player.FirearmController)p.HandsController).DryShot(); ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Last() == EPhraseTrigger.OnOutOfAmmo, "dry shot speaks after cooldown");
Time.time += 8; ArenaPmcVoice.Tick();
var grenadeHands = new Player.BaseGrenadeHandsController { _player = p }; p.HandsController = grenadeHands;
grenadeHands.CreateAndRegisterGrenade(default, default, default); ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Last() == EPhraseTrigger.OnGrenade, "successful grenade speaks");
var previous = p.Speaker.Played.Count;
Time.time += 8;
HarmonyLib.Harmony.Invoke("CreateAndRegisterGrenade", grenadeHands, null); ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == previous, "failed grenade silent");
var foreignHands = new Player.BaseGrenadeHandsController { _player = new Player() };
foreignHands.CreateAndRegisterGrenade(default, default, default); ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == previous, "foreign grenade silent");

// Body handover includes live kit replacements, not only real deaths.
var old = p; var replacement = new Player();
replacement.HandsController = new Player.FirearmController { _player = replacement };
Singleton<GameWorld>.Instance.MainPlayer = Singleton<GameWorld>.Instance.Registered = replacement;
((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer = replacement;
ArenaPmcVoice.Tick();
Check(replacement.Speaker.PlayerVoice == voice && old.Speaker.PlayerVoice == "Original", "replacement keeps match voice and restores old speaker");
Check(originalBanks.All(pair => ReferenceEquals(old.Speaker.PhrasesBanks[pair.Key], pair.Value))
    && old.Speaker.SideTag == ETagStatus.Usec, "old original banks and side restored exactly");
Check(Singleton<IEasyAssets>.Instance.Tokens.Count == 1, "replacement reuses one retained bank");
Settings.PmcVoiceEnabled.Value = false; ArenaPmcVoice.Tick();
Check(replacement.Speaker.PlayerVoice == "Original", "disable restores speaker");
Settings.PmcVoiceEnabled.Value = true; ArenaPmcVoice.Tick();
Check(replacement.Speaker.PlayerVoice == voice, "reenable preserves match voice");
RaidLifecycle.End();
Check(replacement.Speaker.PlayerVoice == "Original" && assetToken.Releases == 1, "raid end restores voice and releases token once");
ArenaPmcVoice.Shutdown(); Check(assetToken.Releases == 1, "shutdown does not double release");

var choices = new HashSet<string>();
for (int i = 0; i < ArenaPmcVoicePolicy.BearVoices.Length; i++)
{ p = Setup(); ArenaPmcVoice.Tick(); choices.Add(p.Speaker.PlayerVoice); }
Check(choices.SetEquals(ArenaPmcVoicePolicy.BearVoices), "rotation includes every Russian and English BEAR voice");
choices.Clear();
for (int i = 0; i < ArenaPmcVoicePolicy.UsecVoices.Length; i++)
{ p = Setup(); ArenaSession.Selected = EPlayerSide.Usec; ArenaPmcVoice.Tick(); choices.Add(p.Speaker.PlayerVoice); }
Check(choices.SetEquals(ArenaPmcVoicePolicy.UsecVoices), "rotation includes every USEC voice");
foreach (string mode in new[] { "gungame", "deathmatch", "tactical", "zombie" })
{
    p = Setup(mode); Reload(p);
    Check(p.Speaker.PlayerVoice == "Original" && p.Speaker.Played.Count == 1
        && Singleton<IEasyAssets>.Instance.Tokens.Count == 0, "other mode retains profile voice " + mode);
}

p = Setup(); ArenaPmcVoice.Tick(); AnnouncerService.IsPmcVoiceBlocked = true; Reload(p);
Check(p.Speaker.Played.Count == 0, "announcer blocks new speech");
AnnouncerService.IsPmcVoiceBlocked = false; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 0, "blocked reload is not replayed late");
p = Setup(); Reload(p); AnnouncerService.IsPmcVoiceBlocked = true; ArenaPmcVoice.Tick();
Check(p.Source.source1.Stops == 1, "announcer stops only owned clip");
p = Setup(); Reload(p); p.HealthController.IsAlive = false; ArenaPmcVoice.Tick();
Check(p.Source.source1.Stops == 1, "death cancels owned speech");
p = Setup(); Reload(p); ArenaRespawnDriver.IsRespawnInProgress = true; ArenaPmcVoice.Tick();
Check(p.Source.source1.Stops == 1, "respawn cancels owned speech");
p = Setup(); Reload(p); ArenaSpectatorService.IsActive = true; ArenaPmcVoice.Tick();
Check(p.Source.source1.Stops == 1, "spectator cancels owned speech");
p = Setup(); Reload(p); var ownSource = p.Source.source1;
p._speechSource = new BetterSource(); AnnouncerService.IsPmcVoiceBlocked = true; ArenaPmcVoice.Tick();
Check(ownSource.Stops == 0 && p.Source.source1.Stops == 0, "pooled source cannot stop foreign audio");
p = Setup(); Reload(p); p.Speaker.Clip = new TaggedClip(); p.Speaker.ReleaseTime += 1;
p.Source.source1.clip = p.Speaker.Clip.Clip; AnnouncerService.IsPmcVoiceBlocked = true; ArenaPmcVoice.Tick();
Check(p.Source.source1.Stops == 0, "native pain clip never interrupted");
p = Setup(); p.Speaker.ReleaseTime = Time.time + 5; Reload(p);
Check(p.Speaker.Played.Count == 0 && p.Speaker.PlayerVoice == "Original", "busy native speaker preserved");
p = Setup(); p.Speaker.QueuedEvent = EPhraseTrigger.OnBeingHurt; p.Speaker.QueuedDelay = Time.time + 5; Reload(p);
AnnouncerService.IsPmcVoiceBlocked = true; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 0 && p.Speaker.QueuedEvent == EPhraseTrigger.OnBeingHurt, "native queued speech preserved");

p = Setup(); ArenaPmcVoice.Tick(); Kill(p); Time.time += .4f; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Last() == EPhraseTrigger.EnemyDown, "valid enemy kill speaks");
foreach (var badKill in new Action<ArenaKill>[] { k => k.LocalIsVictim = true, k => k.KilledByLocal = false,
    k => k.AwardEligible = false, k => k.ScoreEligible = false, k => k.VictimIsFriendly = true, k => k.KillerBodyId++ })
{
    p = Setup(); ArenaPmcVoice.Tick(); Kill(p, badKill); Time.time += .4f; ArenaPmcVoice.Tick();
    Check(p.Speaker.Played.Count == 0, "uncredited friendly or stale kill silent");
}
p = Setup(); ArenaPmcVoice.Tick(); UnityEngine.Random.Value = 99; Kill(p); Time.time += .4f; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 0, "kill probability is bounded");
p = Setup(); ArenaPmcVoice.Tick(); Kill(p); Time.time += 2; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 0, "expired kill not replayed");
p = Setup(); ArenaPmcVoice.Tick(); Kill(p); ArenaRespawnDriver.IsRespawnInProgress = true; ArenaPmcVoice.Tick();
ArenaRespawnDriver.IsRespawnInProgress = false; Time.time += .4f; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 0, "kill invalidated by respawn gap");
p = Setup(); ArenaPmcVoice.Tick(); Kill(p); AnnouncerService.IsPmcVoiceBlocked = true; ArenaPmcVoice.Tick();
AnnouncerService.IsPmcVoiceBlocked = false; Time.time += .4f; ArenaPmcVoice.Tick();
Check(p.Speaker.Played.Count == 0, "announcer invalidates pending kill");

p = Setup(); p.Speaker.AfterPlay = () => ArenaSession.IsActive = false; Reload(p);
Check(p.SourceReads == 0, "native callback cannot allocate retired source");
p = Setup(); p.Speaker.AfterPlay = () => {
    p.Speaker.Clip = new TaggedClip(); p.Speaker.ReleaseTime += 4;
    p.Source.source1.clip = p.Speaker.Clip.Clip;
};
Reload(p); AnnouncerService.IsPmcVoiceBlocked = true; ArenaPmcVoice.Tick();
Check(p.Source.source1.Stops == 0, "reentrant native clip never claimed");
p = Setup(); ArenaPmcVoice.Tick(); assetToken = Singleton<IEasyAssets>.Instance.Tokens.Single();
ArenaMatchService.Current.Mode.IsComplete = true; ArenaPmcVoice.Tick();
Check(assetToken.Releases == 0 && p.Speaker.PlayerVoice == "Original", "result hold retains native last-voice assets");
ArenaMatchService.Current = null; ArenaPmcVoice.Tick();
Check(assetToken.Releases == 0, "retired match holds bank until actual raid end");
RaidLifecycle.End(); Check(assetToken.Releases == 1, "native raid end releases held result bank");
p = Setup(); ArenaPmcVoice.Tick(); p.Speaker.PlayerVoice = "foreign override"; RaidLifecycle.End();
Check(p.Speaker.PlayerVoice == "foreign override", "foreign speaker override preserved");
p = Setup(); ArenaPmcVoice.Tick(); var originalSpeaker = p.Speaker;
p.Speaker = new BaseSpeaker { Owner = p }; ArenaPmcVoice.Tick();
Check(originalSpeaker.PlayerVoice == "Original" && p.Speaker.PlayerVoice != "Original", "same-body speaker replacement handled");

// Loading completion after teardown cannot publish or retain an old world.
p = Setup(); var assets = Singleton<IEasyAssets>.Instance; assets.CompleteImmediately = false;
ArenaPmcVoice.Tick(); var pending = assets.Tokens.Single();
Check(p.Speaker.PlayerVoice == "Original", "pending load cannot change voice");
RaidLifecycle.End();
Check(SpinWait.SpinUntil(() => pending.Releases == 1, 2000), "pending load released at teardown before completion");
pending.Completion.SetResult(true); await Task.Yield();
Check(p.Speaker.PlayerVoice == "Original" && pending.Releases == 1, "late load completion cannot republish or double release");
p = Setup(); assets = Singleton<IEasyAssets>.Instance; assets.CompleteImmediately = false;
ArenaPmcVoice.Tick(); pending = assets.Tokens.Single(); pending.Completion.SetException(new Exception("fixture load failed"));
Check(SpinWait.SpinUntil(() => pending.Releases == 1, 2000), "failed load releases retained token");
p = Setup(); assets = Singleton<IEasyAssets>.Instance; assets.Missing = true; ArenaPmcVoice.Tick();
Check(p.Speaker.PlayerVoice == "Original" && assets.Tokens.Single().Releases == 1, "missing voice preserves native speaker");
ArenaPmcVoice.Shutdown();

Console.WriteLine($"PMC_VOICE_CHECKS_PASSED ({checks} assertions)");
