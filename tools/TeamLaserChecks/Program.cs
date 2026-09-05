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
    ArenaSession.IsActive = ArenaSession.Exact = true; ArenaSession.ActiveMode = "teamdeathmatch";
    Settings.TeamLasers.Value = true;
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
Check(first.tactical.LightMod.IsActive && first.tactical.LightMod.SelectedMode == 0 && first.laser.NativeLight.enabled && first.laser.NativeLight.intensity == 3, "light mode enabled state and scalar intensity remain native");
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
Console.WriteLine($"ARENA_TEAM_LASER_CHECKS_PASSED ({count} assertions)");
