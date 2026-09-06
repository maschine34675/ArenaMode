using ArenaMode.Config;
using ArenaMode.Compat;
using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.UI;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
int Visible(RectTransform root) => root.Children.Count(v => v.gameObject != null && v.gameObject.activeSelf);
void Reset()
{
    Camera.Reset(); ArenaSession.IsActive = ArenaSession.Exact = true; ArenaSession.ActiveMode = "teamdeathmatch";
    Settings.HudEnabled.Value = Settings.ShowFriendlyMarkers.Value = true; PitFireTeamCompat.TeamArenaActive = true;
    Settings.FriendlyTracerColor.Value = ArenaMode.Effects.ArenaTracerColor.Cyan;
    Singleton<GameWorld>.Instance = new(); Singleton<AbstractGame>.Instance = new LocalGame();
    CameraManager.Instance = new() { Camera = new GameObject().AddComponent<Camera>() };
    ArenaMatchService.Current = new ArenaMatch { Mode = new TeamDeathmatchMode() };
    Physics.Rays = 0; Physics.IsBlocked = () => false;
    RectTransformUtility.ConversionWorks = true; RectTransformUtility.Scale = 1f;
}
Player Player(string id, bool local = false)
{
    var p = new GameObject().AddComponent<Player>(); p.ProfileId = id; p.IsAI = !local;
    p.PlayerBones.Head.Original.position = new Vector3(.5f, .5f, 10f);
    p.AIData.BotOwner = new BotOwner { GetPlayer = p, ProfileId = id };
    var world = Singleton<GameWorld>.Instance; world.AllAlivePlayersList.Add(p); world.Alive[id] = world.Ever[id] = p;
    if (local) world.MainPlayer = p;
    return p;
}
(ArenaFriendlyMarkers markers, RectTransform root) Markers()
{
    var root = (RectTransform)new GameObject("HUD", typeof(RectTransform)).transform;
    var markers = new ArenaFriendlyMarkers(); markers.Create(root); markers.Tick(ArenaMatchService.Current); return (markers, root);
}
Player TeamBot(string id, bool friendly = true)
{
    var p = Player(id); ((TeamDeathmatchMode)ArenaMatchService.Current.Mode).Bodies.Add(p, friendly); return p;
}
void Render() => Camera.Cull(CameraManager.Instance.Camera);
TacticalMode Tactical()
{
    ArenaSession.ActiveMode = "tactical";
    var local = Player("local", true);
    var bots = Enumerable.Range(0, 4).Select(i => Player("follower" + i).AIData.BotOwner).ToArray();
    var match = new ArenaMatch(); var mode = new TacticalMode(match, (LocalGame)Singleton<AbstractGame>.Instance, local, bots);
    match.Mode = mode; ArenaMatchService.Current = match; return mode;
}

Reset(); var localTdm = Player("local", true); var friend = TeamBot("friend"); TeamBot("enemy", false); Player("unowned");
((TeamDeathmatchMode)ArenaMatchService.Current.Mode).Bodies.Add(localTdm, true);
var ui = Markers();
Check(Camera.Subscribers == 1 && ui.root.Children.Count == 11 && Visible(ui.root) == 0, "eleven hidden reused markers and one exact camera callback are created");
Render();
Check(Visible(ui.root) == 1 && Physics.Rays == 1, "only the exact current friendly bot gets one LOS-tested marker");
var first = (RectTransform)ui.root.Children[0];
Check(first.anchoredPosition.Equals(new Vector2(0, 20)), "marker sits slightly above the head in overlay local coordinates");
Check(first.localRotation.Equals(Quaternion.Euler(0, 0, 45)) && first.sizeDelta.Equals(new Vector2(10, 10)), "small square geometry is rotated into a diamond without assets");
Check(!first.gameObject.GetComponent<Image>().raycastTarget && !first.Children[0].gameObject.GetComponent<Image>().raycastTarget, "both diamond images never intercept input");
Check(first.Children[0].gameObject.GetComponent<Image>().color.Equals(new Color(.125f, .625f, 1f, .9f)), "friendly tracer HDR palette is normalized for the UI");
Check(Physics.LastMask == 31 && Physics.LastTrigger == QueryTriggerInteraction.Ignore, "LOS includes scenery terrain doors low-poly and transparent blockers but ignores triggers");
Check(Physics.LastEnd.Equals(friend.PlayerBones.Head.Original.position), "LOS tests the actual head rather than an above-cover marker anchor");

int allocations = GameObject.Created;
for (int i = 0; i < 500; i++) { ui.markers.Tick(ArenaMatchService.Current); Render(); }
Check(GameObject.Created == allocations && ui.root.Children.Count == 11, "hundreds of frames reuse the same bounded marker views");
Physics.IsBlocked = () => true; Render();
Check(Visible(ui.root) == 0, "newly occluded teammates hide immediately without a positive LOS cache");
Physics.IsBlocked = () => false; CameraManager.Instance.Camera.transform.position = new Vector3(2, 3, 4); Render();
Check(Visible(ui.root) == 1 && Physics.LastOrigin.Equals(new Vector3(2, 3, 4)), "LOS uses the final render-camera pose rather than the earlier Update pose");
friend.HealthController.IsAlive = false; Render();
Check(Visible(ui.root) == 0, "dead friendly bodies never retain a marker");
friend.HealthController.IsAlive = true; ((TeamDeathmatchMode)ArenaMatchService.Current.Mode).Bodies.Remove(friend); Render();
Check(Visible(ui.root) == 0, "historical bodies removed from the exact roster are hidden");
var replacement = TeamBot("friend"); Render();
Check(Visible(ui.root) == 1, "only the new exact body can be displayed under a reused profile ID");
ui.markers.Dispose();
Check(Camera.Subscribers == 0 && Visible(ui.root) == 0, "dispose unsubscribes the render callback and destroys all views");
ui.markers.Dispose(); Check(Camera.Subscribers == 0, "disposal is idempotent");

foreach (string invalid in new[] { "normal", "wrongmode", "hudoff", "settingoff", "wrongraid", "wrongmatch", "noworld", "nogame", "nomode", "complete", "cameraoff", "nocamera", "behind", "offscreen", "far", "nan", "nohead", "projectionfailed" })
{
    Reset(); var p = TeamBot("friend"); var item = Markers(); var mode = (TeamDeathmatchMode)ArenaMatchService.Current.Mode;
    switch (invalid)
    {
        case "normal": ArenaSession.IsActive = false; break;
        case "wrongmode": ArenaSession.ActiveMode = "gungame"; break;
        case "hudoff": Settings.HudEnabled.Value = false; break;
        case "settingoff": Settings.ShowFriendlyMarkers.Value = false; break;
        case "wrongraid": ArenaSession.Exact = false; break;
        case "wrongmatch": ArenaMatchService.Current = new ArenaMatch { Mode = mode }; break;
        case "noworld": Singleton<GameWorld>.Instance = null; break;
        case "nogame": Singleton<AbstractGame>.Instance = null; break;
        case "nomode": ArenaMatchService.Current.Mode = null; break;
        case "complete": mode.IsComplete = true; break;
        case "cameraoff": CameraManager.Instance.Camera.isActiveAndEnabled = false; break;
        case "nocamera": CameraManager.Instance.Camera = null; break;
        case "behind": p.PlayerBones.Head.Original.position = new Vector3(.5f, .5f, -10); break;
        case "offscreen": p.PlayerBones.Head.Original.position = new Vector3(2, .5f, 10); break;
        case "far": p.PlayerBones.Head.Original.position = new Vector3(.5f, .5f, 110); break;
        case "nan": p.PlayerBones.Head.Original.position = new Vector3(float.NaN, .5f, 10); break;
        case "nohead": p.PlayerBones.Head = null; break;
        case "projectionfailed": RectTransformUtility.ConversionWorks = false; break;
    }
    Render(); Check(Visible(item.root) == 0, "invalid display remains hidden: " + invalid); item.markers.Dispose();
}

Reset(); TeamBot("friend"); ui = Markers(); Camera.Cull(new GameObject().AddComponent<Camera>());
Check(Visible(ui.root) == 0 && Physics.Rays == 0, "secondary cameras do not perform LOS or show markers");
Render(); ui.markers.Tick(null);
Check(Visible(ui.root) == 0, "Update clears old display even if no game camera renders next");
ui.markers.Tick(ArenaMatchService.Current); Render(); CameraManager.Instance.Camera.isActiveAndEnabled = false; Render();
Check(Visible(ui.root) == 0, "camera disabling clears already displayed markers without waiting for another Update");
ui.markers.Create(ui.root); Check(Camera.Subscribers == 1, "recreating the helper never duplicates render subscriptions"); ui.markers.Dispose();
Reset(); TeamBot("friend"); ui = Markers(); ui.root.Destroyed = true; Render();
Check(Camera.Subscribers == 0, "a destroyed HUD root removes its own camera callback even without normal teardown");

Reset(); for (int i = 0; i < 24; i++) TeamBot("friendly" + i); ui = Markers(); Render();
Check(Visible(ui.root) == 11 && Physics.Rays == 11, "visible views and raycasts are capped even for an oversized collaborator roster");
ui.markers.Dispose(); Reset(); for (int i = 0; i < 40; i++) TeamBot("hidden" + i); Physics.IsBlocked = () => true; ui = Markers(); Render();
Check(Visible(ui.root) == 0 && Physics.Rays == 11, "fully occluded excess candidates cannot cause unbounded raycasts"); ui.markers.Dispose();
Reset(); friend = TeamBot("duplicate"); for (int i = 0; i < 100; i++) Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(friend); ui = Markers(); Render();
Check(Visible(ui.root) == 1 && Physics.Rays == 1, "duplicate native list entries do not produce duplicate diamonds or rays"); ui.markers.Dispose();
Reset(); TeamBot("friend"); ui = Markers(); Physics.IsBlocked = () => throw new Exception("native ray failure"); Render();
Check(Visible(ui.root) == 0, "physics failure hides markers without escaping into other HUD elements"); ui.markers.Dispose();

// Real production partial-method checks: mutate the remaining mode fields only.
foreach (string invalid in new[] { "normal", "notteam", "staging", "aftermath", "rebind", "ended", "complete", "pftlost", "epoch", "wrongmatch", "wronggame", "wrongraid", "leaderlost", "leaderreplaced", "dead", "human", "inactive", "ownerdead", "healthdead", "aliasbody", "aliasowner", "profiledrift", "ownerprofile", "alivealias", "everalias" })
{
    Reset(); var mode = Tactical(); var world = Singleton<GameWorld>.Instance; var p = world.Alive["follower0"];
    Check(mode.IsCurrentFriendlyMarkerBody(p), "exact bound active Tactical follower initially authenticates: " + invalid);
    switch (invalid)
    {
        case "normal": ArenaSession.IsActive = false; break;
        case "notteam": mode.SetTeam(false); break;
        case "staging": mode.SetPhase(TacticalMode.Phase.Staging); break;
        case "aftermath": mode.SetPhase(TacticalMode.Phase.Aftermath); break;
        case "rebind": mode.SetPhase(TacticalMode.Phase.Rebinding); break;
        case "ended": mode.SetPhase(TacticalMode.Phase.Ended); break;
        case "complete": mode.IsComplete = true; break;
        case "pftlost": PitFireTeamCompat.TeamArenaActive = false; break;
        case "epoch": mode.SetEpoch(0); break;
        case "wrongmatch": mode.SetMatch(new ArenaMatch { Mode = mode }); break;
        case "wronggame": Singleton<AbstractGame>.Instance = new LocalGame(); break;
        case "wrongraid": ArenaSession.Exact = false; break;
        case "leaderlost": mode.SetLeader(null); break;
        case "leaderreplaced": world.Ever["local"] = new Player { ProfileId = "local" }; break;
        case "dead": p.HealthController.IsAlive = false; break;
        case "human": p.IsAI = false; break;
        case "inactive": p.AIData.BotOwner.BotState = EBotState.PreActive; break;
        case "ownerdead": p.AIData.BotOwner.IsDead = true; break;
        case "healthdead": p.AIData.BotOwner.HealthController.IsAlive = false; break;
        case "aliasbody": p.AIData.BotOwner.GetPlayer = new Player { ProfileId = p.ProfileId }; break;
        case "aliasowner": p.AIData.BotOwner = new BotOwner { GetPlayer = p, ProfileId = p.ProfileId }; break;
        case "profiledrift": mode.SetBoundProfile(0, "other"); break;
        case "ownerprofile": p.AIData.BotOwner.ProfileId = "other"; break;
        case "alivealias": world.Alive[p.ProfileId] = new Player { ProfileId = p.ProfileId }; break;
        case "everalias": world.Ever[p.ProfileId] = new Player { ProfileId = p.ProfileId }; break;
    }
    Check(!mode.IsCurrentFriendlyMarkerBody(p), "invalid Tactical identity is rejected: " + invalid);
}
Reset(); var tactical = Tactical(); ui = Markers(); Player("enemy"); Render();
Check(Visible(ui.root) == 4 && Physics.Rays == 4, "protected Tactical displays only its four exact frozen followers");
Singleton<GameWorld>.Instance.MainPlayer.HealthController.IsAlive = false; Render();
Check(Visible(ui.root) == 4, "living exact teammates remain identifiable from the post-death current camera");
tactical.SetPhase(TacticalMode.Phase.Rebinding); Render();
Check(Visible(ui.root) == 0, "Tactical round transition immediately removes the old roster's display"); ui.markers.Dispose();

Check(!ArenaFriendlyMarkerPolicy.Projectable(new Vector3(.5f, .5f, 1), .64f), "too-close camera target does not fill the sight picture");
Check(!ArenaFriendlyMarkerPolicy.Projectable(new Vector3(.02f, .5f, 1), 10), "screen edge has padding instead of offscreen arrows");
Check(!ArenaFriendlyMarkerPolicy.Projectable(new Vector3(.5f, .5f, float.PositiveInfinity), 10), "non-finite projection is never displayed");
Console.WriteLine($"ARENA_FRIENDLY_MARKER_CHECKS_PASSED ({checks} assertions)");
