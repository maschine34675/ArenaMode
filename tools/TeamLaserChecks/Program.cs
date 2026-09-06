using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

int count = 0;
void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
bool Near(float a, float b) => Math.Abs(a - b) < 0.00001f;
bool ColorNear(Color a, Color b) => Near(a.r, b.r) && Near(a.g, b.g) && Near(a.b, b.b) && Near(a.a, b.a);
float Peak(Color c) => Math.Max(c.r, Math.Max(c.g, c.b));
void Setup()
{
    ArenaTeamLaserColors.Reset();
    ArenaLaserBrightness.Reset();
    ArenaSession.IsActive = ArenaSession.Exact = true; ArenaSession.ActiveMode = "teamdeathmatch";
    Settings.TeamLasers.Value = true;
    Settings.VisibleLaserBrightness.Value = 1f;
    ArenaMode.Compat.PitFireTeamCompat.TeamArenaActive = true;
    Singleton<GameWorld>.Instance = new(); Singleton<AbstractGame>.Instance = new();
    ArenaMatchService.Current = new(); Time.unscaledTime += 1f;
}
(LaserBeam laser, Player owner, WeaponPrefab weapon, TacticalComboVisualController tactical) Fixture(bool friendly = true)
{
    var owner = new GameObject().Add<Player>();
    var weaponGo = new GameObject(); var weapon = weaponGo.Add<WeaponPrefab>(); weapon.Init(owner);
    owner.HandsController = new Player.FirearmController { ControllerGameObject = weaponGo };
    var tactical = new GameObject { Parent = weaponGo }.Add<TacticalComboVisualController>();
    var laser = new GameObject { Parent = tactical.gameObject }.Add<LaserBeam>();
    laser.BeamMaterial = Material("Custom/LaserBeam", new Color(.8f, .1f, .1f, .37f));
    laser.PointMaterial = Material("Custom/LaserPoint", new Color(6f, 1f, 1f, .81f));
    laser.NativeLight.color = new Color(.2f, .05f, .03f, .9f);
    ((TeamDeathmatchMode)ArenaMatchService.Current.Mode).CurrentBodies.Add(owner, friendly);
    Singleton<GameWorld>.Instance.Alive[owner.ProfileId] = owner;
    Singleton<GameWorld>.Instance.Ever[owner.ProfileId] = owner;
    ArenaLaserBrightness.Capture(laser);
    return (laser, owner, weapon, tactical);
}
Material Material(string shader, Color color) => new() { shader = new Shader { name = shader }, Colors = { [1] = color }, Floats = { [2] = 2.5f } };
void Draw(LaserBeam laser)
{
    var state = ArenaTeamLaserColors.Begin(laser);
    try { laser.LateUpdate(); } finally { ArenaTeamLaserColors.End(state); }
}

Setup();
foreach (bool friendly in new[] { true, false })
{
    var f = Fixture(friendly); var laser = f.laser;
    var b = laser.NativeBeam; var p = laser.NativePoint; var beamMat = laser.BeamMaterial; var dotMat = laser.PointMaterial;
    var nativeBeam = beamMat.GetColor(1); var nativePoint = dotMat.GetColor(1); var light = laser.NativeLight.color;
    Color palette = ArenaTracerPalette.Resolve(friendly ? ArenaTracerColor.Cyan : ArenaTracerColor.Red);
    var state = ArenaTeamLaserColors.Begin(laser);
    Check(state != null, "known current team owner is colored"); laser.LateUpdate();
    Check(ColorNear(laser.DrawnBeam, ArenaTeamLaserPolicy.Tint(nativeBeam, palette)) && ColorNear(laser.DrawnPoint, ArenaTeamLaserPolicy.Tint(nativePoint, palette)), "both team beam and dot follow the tracer hue");
    Check(Near(Peak(laser.DrawnBeam), Peak(nativeBeam)) && Near(laser.DrawnBeam.a, nativeBeam.a) && Near(Peak(laser.DrawnPoint), Peak(nativePoint)) && Near(laser.DrawnPoint.a, nativePoint.a), "native HDR magnitude and alpha are preserved");
    Check(laser.NativeBeam.Floats[3] == 20f && laser.NativeBeam.Floats[4] == 4f && laser.NativePoint.Floats[5] == .07f && laser.NativePoint.Floats[6] == 100f, "native distance intensity and size reach the draw");
    // Installed BrightLasers mutates material colors in its postfix; a submitted
    // DrawMesh property block is an independent color override.
    beamMat.Colors[1] = new Color(0, .8f, 0, nativeBeam.a); dotMat.Colors[1] = new Color(0, 0, 6f, nativePoint.a);
    Check(ColorNear(laser.DrawnBeam, ArenaTeamLaserPolicy.Tint(nativeBeam, palette)), "BrightLasers late material recoloring cannot replace the submitted draw color");
    ArenaTeamLaserColors.End(state);
    Check(ReferenceEquals(b, laser.NativeBeam) && ReferenceEquals(p, laser.NativePoint) && b.Colors.Count == 0 && p.Colors.Count == 0, "original block identity and absence of color override are restored");
    Check(ReferenceEquals(beamMat, laser.BeamMaterial) && ReferenceEquals(dotMat, laser.PointMaterial) && beamMat.GetFloat(2) == 2.5f && laser.IntensityFactor == 4 && laser.LightIntensity == 8 && laser.BeamSize == .004f, "materials brightness width and activation are unchanged");
    Check(ColorNear(laser.NativeLight.color, ArenaTeamLaserPolicy.Tint(light, palette)), "native light retains team color until camera rendering");
    int allocationBaseline = MaterialPropertyBlock.Created;
    for (int i = 0; i < 1000; i++) Draw(laser);
    Check(MaterialPropertyBlock.Created == allocationBaseline, "per-laser property blocks are reused across frames");
    ArenaTeamLaserColors.Reset();
    Check(ColorNear(light, laser.NativeLight.color), "reset restores native light color");
}

foreach (string invalid in new[] { "normal", "tactical", "disabled", "wrongraid", "noworld", "nogame", "nomatch", "unknown", "pooled", "off", "inactive", "helmet", "unknownshader", "irbeam", "irpoint", "missingcolor", "missingfactor", "nanfactor" })
{
    Setup(); var f = Fixture(); var laser = f.laser; var b = laser.NativeBeam; var p = laser.NativePoint;
    switch (invalid)
    {
        case "normal": ArenaSession.IsActive = false; break;
        case "tactical": ArenaSession.ActiveMode = "tactical"; break;
        case "disabled": Settings.TeamLasers.Value = false; break;
        case "wrongraid": ArenaSession.Exact = false; break;
        case "noworld": Singleton<GameWorld>.Instance = null; break;
        case "nogame": Singleton<AbstractGame>.Instance = null; break;
        case "nomatch": ArenaMatchService.Current = null; break;
        case "unknown": ((TeamDeathmatchMode)ArenaMatchService.Current.Mode).CurrentBodies.Clear(); break;
        case "pooled": ((Player.FirearmController)f.owner.HandsController).ControllerGameObject = new GameObject(); break;
        case "off": f.tactical.LightMod.IsActive = false; break;
        case "inactive": laser.isActiveAndEnabled = false; break;
        case "helmet": f.tactical.gameObject.Parent = null; break;
        case "unknownshader": laser.BeamMaterial.shader.name = "Unknown/CustomLaser"; break;
        case "irbeam": laser.BeamMaterial.Floats[2] = 0; break;
        case "irpoint": laser.PointMaterial.Floats[2] = 0; break;
        case "missingcolor": laser.BeamMaterial.Colors.Clear(); break;
        case "missingfactor": laser.PointMaterial.Floats.Clear(); break;
        case "nanfactor": laser.BeamMaterial.Floats[2] = float.NaN; break;
    }
    Check(ArenaTeamLaserColors.Begin(laser) == null, "invalid scope remains native: " + invalid);
    Check(ReferenceEquals(b, laser.NativeBeam) && ReferenceEquals(p, laser.NativePoint), "invalid scope leaves original blocks intact: " + invalid);
}

Setup(); var pooled = Fixture(); Color baseline = pooled.laser.NativeLight.color;
Draw(pooled.laser); pooled.laser.isActiveAndEnabled = false; ArenaTeamLaserColors.Tick();
Check(ColorNear(baseline, pooled.laser.NativeLight.color), "inactive pooled beams restore without another LateUpdate");
pooled.laser.isActiveAndEnabled = true; Draw(pooled.laser);
((TeamDeathmatchMode)ArenaMatchService.Current.Mode).CurrentBodies.Remove(pooled.owner);
Check(ArenaTeamLaserColors.Begin(pooled.laser) == null && ColorNear(baseline, pooled.laser.NativeLight.color), "retired owners cannot keep historical team colors");
var newOwner = new GameObject().Add<Player>(); newOwner.HandsController = new Player.FirearmController { ControllerGameObject = pooled.weapon.gameObject }; pooled.weapon.Init(newOwner);
Singleton<GameWorld>.Instance.Alive[newOwner.ProfileId] = newOwner; Singleton<GameWorld>.Instance.Ever[newOwner.ProfileId] = newOwner;
((TeamDeathmatchMode)ArenaMatchService.Current.Mode).CurrentBodies.Add(newOwner, false); Draw(pooled.laser);
Check(ColorNear(pooled.laser.NativeLight.color, ArenaTeamLaserPolicy.Tint(baseline, ArenaTracerPalette.Resolve(ArenaTracerColor.Red))), "ownership changes use the new current team without compounding tint");
ArenaTeamLaserColors.Reset(); Check(ColorNear(baseline, pooled.laser.NativeLight.color), "owner replacement retains original restoration color");

Setup(); var ex = Fixture(); var original = ex.laser.NativeBeam; var originalP = ex.laser.NativePoint;
var render = ArenaTeamLaserColors.Begin(ex.laser);
try { throw new InvalidOperationException("native draw failure"); } catch { ArenaTeamLaserColors.End(render); }
Check(ReferenceEquals(original, ex.laser.NativeBeam) && ReferenceEquals(originalP, ex.laser.NativePoint), "native draw exception restores both original block identities");
render = ArenaTeamLaserColors.Begin(ex.laser);
Check(ArenaTeamLaserColors.Begin(ex.laser) == null, "nested draw cannot overwrite the outer restore target");
ArenaTeamLaserColors.End(render); Check(ReferenceEquals(original, ex.laser.NativeBeam), "outer draw still restores after nested call");
render = ArenaTeamLaserColors.Begin(ex.laser); var externalBlock = new MaterialPropertyBlock(); ex.laser.ReplaceBlocks(externalBlock, externalBlock); ArenaTeamLaserColors.End(render);
Check(ReferenceEquals(ex.laser.NativeBeam, externalBlock), "third-party block replacement wins over stale restoration");
Color externalColor = new(.1f, .7f, .2f, .5f); ex.laser.NativeLight.color = externalColor; ArenaTeamLaserColors.Reset();
Check(ColorNear(externalColor, ex.laser.NativeLight.color), "third-party light replacement wins over stale restoration");

Setup(); var replace = Fixture(); baseline = replace.laser.NativeLight.color; Draw(replace.laser);
var oldLight = replace.laser.NativeLight; var newLight = new Light { color = externalColor }; replace.laser.ReplaceLight(newLight); Draw(replace.laser);
Check(ColorNear(oldLight.color, baseline), "native light replacement restores the old light");
ArenaTeamLaserColors.Reset(); Check(ColorNear(newLight.color, externalColor), "native light replacement captures its own baseline");
var lease = new ArenaTeamLaserLightLease(); var c = lease.Apply(externalColor, new Color(0, 2, 2)); c = lease.Apply(c, new Color(2, 0, 0));
Check(ColorNear(lease.Restore(c), externalColor), "changing configured hue does not compound or lose baseline");
Check(ArenaTeamLaserPolicy.Tint(new Color(0, 0, 0, .2f), new Color(2, 1, 0)).Equals(new Color(0, 0, 0, .2f)), "zero brightness remains zero");
Check(ArenaTeamLaserPolicy.Tint(externalColor, new Color(0, 0, 0)).Equals(externalColor), "invalid palette preserves native color");

foreach (string exit in new[] { "setting", "mode", "normal", "raididentity", "destroyed" })
{
    Setup(); var f = Fixture(); baseline = f.laser.NativeLight.color; Draw(f.laser);
    switch (exit)
    {
        case "setting": Settings.TeamLasers.Value = false; break;
        case "mode": ArenaSession.ActiveMode = "tactical"; break;
        case "normal": ArenaSession.IsActive = false; break;
        case "raididentity": ArenaSession.Exact = false; break;
        case "destroyed": f.laser.Destroyed = true; break;
    }
    ArenaTeamLaserColors.Tick();
    Check(ColorNear(baseline, f.laser.NativeLight.color), "scope exit restores owned light: " + exit);
}

Setup(); var first = Fixture(); var second = Fixture(false);
second.laser.BeamMaterial = first.laser.BeamMaterial; second.laser.PointMaterial = first.laser.PointMaterial;
Color sharedBeam = first.laser.BeamMaterial.GetColor(1), sharedPoint = first.laser.PointMaterial.GetColor(1);
Draw(first.laser); Draw(second.laser);
Check(!ColorNear(first.laser.DrawnBeam, second.laser.DrawnBeam), "shared native materials still produce distinct team draws");
Check(ColorNear(first.laser.BeamMaterial.GetColor(1), sharedBeam) && ColorNear(first.laser.PointMaterial.GetColor(1), sharedPoint), "shared beam and point materials are never mutated");
Check(first.tactical.LightMod.IsActive && first.tactical.LightMod.SelectedMode == 0 && first.laser.NativeLight.enabled && first.laser.NativeLight.intensity == 32, "light mode enabled state and scalar intensity remain native");
ArenaTeamLaserColors.Reset();

// Invoke the actual Harmony finalizer entrypoint, not just its End helper.
Setup(); var faulted = Fixture(); original = faulted.laser.NativeBeam;
render = ArenaTeamLaserColors.Begin(faulted.laser);
var patch = typeof(ArenaTeamLaserColors).GetNestedType("LateUpdatePatch", System.Reflection.BindingFlags.NonPublic);
var finalizer = patch.GetMethod("Finalizer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var nativeException = new InvalidOperationException("native failure identity");
var result = finalizer.Invoke(null, new object[] { nativeException, render });
Check(ReferenceEquals(nativeException, result) && ReferenceEquals(original, faulted.laser.NativeBeam), "actual finalizer restores blocks and preserves the native exception identity");
ArenaTeamLaserColors.Reset();

// BrightLasers 3.0.0 replay from the inspected installed IL: creation changes
// scalar intensity and beam factor; optional LateUpdate recolors MATERIALS only.
// The renderer remains modeled, so these are draw-input assertions, not visual proof.
Setup(); var bright = Fixture(); Settings.VisibleLaserBrightness.Value = 2f;
float prefabIntensity = bright.laser.IntensityFactor;
bright.laser.IntensityFactor *= 2f; bright.laser.LightIntensity *= 2f;
bright.laser.BeamSize *= 2f; bright.laser.BeamMaterial.Floats[2] *= 2f;
bright.laser.BeamMaterial.Colors[1] = new Color(.87f, .1f, .1f, .37f); // original-color shift, default .35
float brightIntensity = bright.laser.IntensityFactor, brightLight = bright.laser.LightIntensity;
for (int i = 0; i < 500; i++)
{
    Draw(bright.laser);
    Check(Near(bright.laser.DrawnIntensity, brightIntensity) && Near(bright.laser.IntensityFactor, brightIntensity), "BrightLasers 2 plus Arena 2 remains 2, including repeated draws");
    Color drawn = bright.laser.DrawnBeam;
    bright.laser.BeamMaterial.Colors[1] = new Color(.1f, 1f, .2f, .37f);
    bright.laser.PointMaterial.Colors[1] = new Color(1f, .1f, .8f, .81f);
    Check(ColorNear(drawn, bright.laser.DrawnBeam) && Near(drawn.r / Peak(drawn), .125f), "Bright material RGB/override postfix does not replace submitted team hue");
}
Settings.VisibleLaserBrightness.Value = 4f; Draw(bright.laser);
Check(Near(bright.laser.DrawnIntensity, prefabIntensity * 4f) && Near(bright.laser.IntensityFactor, brightIntensity), "Arena 4 raises Bright 2 only to prefab 4");
Check(Near(bright.laser.LightIntensity, brightLight) && Near(bright.laser.NativeLight.intensity, prefabIntensity * 4f * brightLight), "native spot light follows the same draw intensity without changing LightIntensity");
ArenaTeamLaserColors.Reset();
Check(Near(bright.laser.NativeLight.intensity, brightIntensity * brightLight), "boosted deferred light restores to BrightLasers baseline");
Settings.VisibleLaserBrightness.Value = 1f; Draw(bright.laser);
Check(Near(bright.laser.DrawnIntensity, brightIntensity), "setting one disables Arena boost and preserves BrightLasers");

Setup(); var enhanced = Fixture(); Settings.VisibleLaserBrightness.Value = 2f;
original = enhanced.laser.NativeBeam; originalP = enhanced.laser.NativePoint;
float nativeIntensity = enhanced.laser.IntensityFactor;
Draw(enhanced.laser);
Check(Near(enhanced.laser.DrawnIntensity, nativeIntensity * 2f), "default Arena brightness boosts an unmodified native laser");
Check(Near(enhanced.laser.IntensityFactor, nativeIntensity) && ReferenceEquals(original, enhanced.laser.NativeBeam) && ReferenceEquals(originalP, enhanced.laser.NativePoint), "brightness draw restores scalar and both property block identities");
int allocationStart = MaterialPropertyBlock.Created;
for (int i = 0; i < 1000; i++) Draw(enhanced.laser);
Check(MaterialPropertyBlock.Created == allocationStart && Near(enhanced.laser.DrawnIntensity, nativeIntensity * 2f), "brightness reuses blocks and does not accumulate across frames");
Settings.VisibleLaserBrightness.Value = 1f; Draw(enhanced.laser);
Check(Near(enhanced.laser.NativeLight.intensity, nativeIntensity * enhanced.laser.LightIntensity), "reducing brightness restores the native next-frame spot light");
Settings.VisibleLaserBrightness.Value = 2f; Draw(enhanced.laser);
enhanced.laser.isActiveAndEnabled = false; Time.unscaledTime += 1; ArenaTeamLaserColors.Tick();
Check(Near(enhanced.laser.NativeLight.intensity, nativeIntensity * enhanced.laser.LightIntensity), "inactive pooled beam restores deferred brightness without a subsequent draw");
enhanced.laser.isActiveAndEnabled = true; Settings.VisibleLaserBrightness.Value = 3f; Draw(enhanced.laser);
enhanced.laser.NativeLight.intensity = 77f; ArenaTeamLaserColors.Reset();
Check(Near(enhanced.laser.NativeLight.intensity, 77f), "external deferred light intensity wins over cleanup");
Draw(enhanced.laser); // baseline survives a raid-effect reset for reused pooled objects
Check(Near(enhanced.laser.DrawnIntensity, nativeIntensity * 3f), "weak original baseline survives effect reset and pool reuse");

foreach (string fault in new[] { "before", "after", "miss", "external-scalar" })
{
    Setup(); var f = Fixture(); Settings.VisibleLaserBrightness.Value = 2f;
    Draw(f.laser); float prior = f.laser.IntensityFactor;
    f.laser.FailBeforeLight = fault == "before"; f.laser.FailAfterLight = fault == "after"; f.laser.Hit = fault != "miss";
    render = ArenaTeamLaserColors.Begin(f.laser);
    try { f.laser.LateUpdate(); } catch (InvalidOperationException) { }
    if (fault == "external-scalar") f.laser.IntensityFactor = 123f;
    ArenaTeamLaserColors.End(render); ArenaTeamLaserColors.Reset();
    Check(Near(f.laser.IntensityFactor, fault == "external-scalar" ? 123f : prior), "brightness scalar exception/external restoration: " + fault);
    Check(Near(f.laser.NativeLight.intensity, fault == "miss" ? 0f : prior * f.laser.LightIntensity), "deferred light exception/miss restoration: " + fault);
}

foreach (string invalid in new[] { "normal", "wrongraid", "stopped", "complete", "dead", "alive-alias", "ever-alias", "pooled", "inactive", "off", "ir", "missingbaseline" })
{
    Setup(); var f = Fixture(); Settings.VisibleLaserBrightness.Value = 2f;
    Color beforeColor = f.laser.NativeLight.color; float beforeIntensity = f.laser.IntensityFactor;
    var beforeBeam = f.laser.NativeBeam; var beforePoint = f.laser.NativePoint;
    switch (invalid)
    {
        case "normal": ArenaSession.IsActive = false; break;
        case "wrongraid": ArenaSession.Exact = false; break;
        case "stopped": Singleton<AbstractGame>.Instance.Status = GameStatus.Stopped; break;
        case "complete": ArenaMatchService.Current.Mode.IsComplete = true; break;
        case "dead": f.owner.HealthController.IsAlive = false; break;
        case "alive-alias": Singleton<GameWorld>.Instance.Alive[f.owner.ProfileId] = new Player { ProfileId = f.owner.ProfileId }; break;
        case "ever-alias": Singleton<GameWorld>.Instance.Ever[f.owner.ProfileId] = new Player { ProfileId = f.owner.ProfileId }; break;
        case "pooled": ((Player.FirearmController)f.owner.HandsController).ControllerGameObject = new(); break;
        case "inactive": f.laser.isActiveAndEnabled = false; break;
        case "off": f.tactical.LightMod.IsActive = false; break;
        case "ir": f.laser.BeamMaterial.Floats[2] = 0; break;
        case "missingbaseline": ArenaLaserBrightness.Reset(); break;
    }
    render = ArenaTeamLaserColors.Begin(f.laser);
    Check(invalid == "missingbaseline" || render == null, "brightness invalid scope remains native: " + invalid);
    Check(Near(f.laser.IntensityFactor, beforeIntensity), "invalid brightness never changes native scalar: " + invalid);
    ArenaTeamLaserColors.End(render); ArenaTeamLaserColors.Reset();
    Check(ReferenceEquals(beforeBeam, f.laser.NativeBeam) && ReferenceEquals(beforePoint, f.laser.NativePoint) && ColorNear(beforeColor, f.laser.NativeLight.color), "invalid brightness leaves/restores native blocks and light: " + invalid);
}

// The new effect also applies brightness without tint in solo Arena. Team tint
// itself remains restricted to TDM and exact protected Tactical bodies.
foreach (string solo in new[] { "endless", "rounds", "gungame", "tactical" })
{
    Setup(); var f = Fixture(); Singleton<GameWorld>.Instance.MainPlayer = f.owner;
    ArenaMatchService.Current.Mode = solo == "tactical" ? new TacticalMode { _teamArena = false } : new ModeModel { Id = solo };
    ArenaSession.ActiveMode = solo; Settings.VisibleLaserBrightness.Value = 2f;
    Draw(f.laser);
    Check(Near(f.laser.DrawnIntensity, 8f) && ColorNear(f.laser.DrawnBeam, f.laser.BeamMaterial.GetColor(1)), "solo Arena brightness without invented teams: " + solo);
    Settings.TeamLasers.Value = false; Draw(f.laser);
    Check(Near(f.laser.DrawnIntensity, 8f), "brightness works with team colors disabled: " + solo);
}

(TacticalMode mode, (LaserBeam laser, Player owner, WeaponPrefab weapon, TacticalComboVisualController tactical)[] bodies) TacticalFixture()
{
    Setup(); var bodies = Enumerable.Range(0, 10).Select(i => Fixture(i < 5)).ToArray();
    var game = new LocalGame(); Singleton<AbstractGame>.Instance = game;
    var mode = new TacticalMode { _teamGame = game, _boundLocalPlayer = bodies[0].owner, _boundLocalProfileId = bodies[0].owner.ProfileId };
    ArenaMatchService.Current = new ArenaMatch { Mode = mode }; mode._match = ArenaMatchService.Current;
    ArenaSession.ActiveMode = "tactical"; Singleton<GameWorld>.Instance.MainPlayer = bodies[0].owner;
    for (int i = 1; i < 10; i++)
    {
        var p = bodies[i].owner; p.IsAI = true;
        var bot = new BotOwner { GetPlayer = p, ProfileId = p.ProfileId, BotState = EBotState.Active };
        p.AIData.BotOwner = bot;
        if (i < 5) { mode._boundTeamFollowers[i - 1] = bot; mode._boundTeamFollowerProfileIds[i - 1] = p.ProfileId; }
        else { mode._boundTeamEnemies[i - 5] = bot; mode._boundTeamEnemyProfileIds[i - 5] = p.ProfileId; }
    }
    return (mode, bodies);
}

var tacticalMatch = TacticalFixture();
Check(ArenaSession.ActiveMode != "teamdeathmatch", "reproduced prior TDM-only scope omission in a Tactical match");
for (int i = 0; i < 10; i++)
{
    var f = tacticalMatch.bodies[i]; Draw(f.laser);
    Color palette = ArenaTracerPalette.Resolve(i < 5 ? ArenaTracerColor.Cyan : ArenaTracerColor.Red);
    Check(ColorNear(f.laser.DrawnBeam, ArenaTeamLaserPolicy.Tint(f.laser.BeamMaterial.GetColor(1), palette)), "protected Tactical exact roster team color: " + i);
}
tacticalMatch.bodies[0].owner.HealthController.IsAlive = false;
Check(ArenaTeamLaserColors.Begin(tacticalMatch.bodies[0].laser) == null, "dead Tactical local body cannot keep active effects");
for (int i = 1; i < 10; i++) { render = ArenaTeamLaserColors.Begin(tacticalMatch.bodies[i].laser); Check(render != null, "live Tactical bot remains classified after local death"); ArenaTeamLaserColors.End(render); }
foreach (string invalid in new[] { "rebind", "staging", "aftermath", "epoch", "pft", "game", "match", "local-alias", "main-alias", "body-alias", "id-alias", "owner-alias", "dead-owner", "inactive-owner", "wrong-pair", "short-followers", "short-enemies" })
{
    tacticalMatch = TacticalFixture(); var mode = tacticalMatch.mode; var f = tacticalMatch.bodies[1];
    switch (invalid)
    {
        case "rebind": mode._phase = TacticalMode.Phase.Rebinding; break;
        case "staging": mode._phase = TacticalMode.Phase.Staging; break;
        case "aftermath": mode._phase = TacticalMode.Phase.Aftermath; break;
        case "epoch": mode._teamRoundEpoch = 0; break;
        case "pft": ArenaMode.Compat.PitFireTeamCompat.TeamArenaActive = false; break;
        case "game": mode._teamGame = new LocalGame(); break;
        case "match": mode._match = new ArenaMatch { Mode = mode }; break;
        case "local-alias": Singleton<GameWorld>.Instance.Ever[mode._boundLocalProfileId] = new Player { ProfileId = mode._boundLocalProfileId }; break;
        case "main-alias": Singleton<GameWorld>.Instance.MainPlayer = new Player { ProfileId = mode._boundLocalProfileId }; break;
        case "body-alias": mode._boundTeamFollowers[0] = new BotOwner { ProfileId = f.owner.ProfileId }; break;
        case "id-alias": mode._boundTeamFollowerProfileIds[0] = "other"; break;
        case "owner-alias": f.owner.AIData.BotOwner = new BotOwner { GetPlayer = f.owner, ProfileId = f.owner.ProfileId }; break;
        case "dead-owner": f.owner.AIData.BotOwner.IsDead = true; break;
        case "inactive-owner": f.owner.AIData.BotOwner.BotState = EBotState.Inactive; break;
        case "wrong-pair": f.owner.AIData.BotOwner.GetPlayer = new Player { ProfileId = f.owner.ProfileId }; break;
        case "short-followers": mode._boundTeamFollowers = new BotOwner[3]; break;
        case "short-enemies": mode._boundTeamEnemies = new BotOwner[4]; break;
    }
    Check(ArenaTeamLaserColors.Begin(f.laser) == null, "Tactical rejects stale round or body: " + invalid);
}

Setup(); var observed = Fixture(); ArenaSession.IsActive = false;
var savedBeam = observed.laser.NativeBeam; Color savedColor = observed.laser.BeamMaterial.GetColor(1);
ArenaLaserBrightness.Reset(); ArenaLaserBrightness.Capture(observed.laser);
Check(Near(observed.laser.IntensityFactor, 4f) && Near(observed.laser.LightIntensity, 8f)
    && ReferenceEquals(savedBeam, observed.laser.NativeBeam) && ColorNear(savedColor, observed.laser.BeamMaterial.GetColor(1)), "global baseline capture is read-only in ordinary raids");
observed.laser.IntensityFactor = 8f; ArenaLaserBrightness.Capture(observed.laser);
Check(Near(ArenaLaserBrightness.Resolve(observed.laser, 3f), 12f), "repeated baseline observation cannot recapture a modded intensity");
foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
    Check(Near(ArenaLaserBrightness.Resolve(observed.laser, value), 8f), "invalid setting fails open");
Check(Near(ArenaLaserBrightness.Resolve(observed.laser, 99f), 20f), "setting is bounded even outside config validation");

foreach (string replacement in new[] { "world", "game", "match" })
{
    Setup(); var f = Fixture(); Settings.VisibleLaserBrightness.Value = 2f; Draw(f.laser);
    switch (replacement)
    {
        case "world": Singleton<GameWorld>.Instance = new GameWorld { Alive = { [f.owner.ProfileId] = f.owner }, Ever = { [f.owner.ProfileId] = f.owner } }; break;
        case "game": Singleton<AbstractGame>.Instance = new AbstractGame(); break;
        case "match": var mode = ArenaMatchService.Current.Mode; ArenaMatchService.Current = new ArenaMatch { Mode = mode }; break;
    }
    Time.unscaledTime += 1; ArenaTeamLaserColors.Tick();
    Check(Near(f.laser.NativeLight.intensity, 32f), "old context lease retires even with stale active latch: " + replacement);
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference ObserveTemporaryPrefab()
{
    var temporary = new LaserBeam(); ArenaLaserBrightness.Capture(temporary); return new WeakReference(temporary);
}
ArenaTeamLaserColors.Reset();
var weakPrefab = ObserveTemporaryPrefab();
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
Check(!weakPrefab.IsAlive, "weak brightness baseline does not retain an abandoned prefab");

ArenaTeamLaserColors.Reset();
Console.WriteLine($"ARENA_TEAM_LASER_CHECKS_PASSED ({count} assertions)");
