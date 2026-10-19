using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Ballistics;
using HarmonyLib;
using JsonType;

public static class Program
{
    public static ConfigEntry<bool> Keep { get; } = new();
    public static bool SecondChance, HasSecondChance = true, Debouncing, Medicine;
    public static float DamagePercent = 100;
    private static int _checks;
    private const string Token = "11111111111111111111111111111111";
    private static void Need(bool value, string name) { _checks++; if (!value) throw new Exception("ASSERT: " + name); }
    public static bool ForeignKill(ActiveHealthController hc) {
        if (Keep.Value || Debouncing) return false;
        if (SecondChance && HasSecondChance) { HasSecondChance = false; Debouncing = true; hc.Health = 100; return false; }
        return true;
    }
    public static bool ForeignDestroy(ActiveHealthController hc) => !Keep.Value;
    public static bool ForeignFracture(ActiveHealthController hc) => !(Keep.Value && Medicine);
    public static bool ForeignDamage(bool keep, ActiveHealthController hc, Player body, ref float damage) {
        if (Medicine) hc.EffectsRemoved++;
        damage *= DamagePercent / 100;
        if (keep && hc.Health - damage < 1) damage = Math.Max(0, hc.Health - 1);
        return true;
    }
    private static void Reset() {
        Harmony.FailUnpatch = false; ArenaDeminvincibilityCompat.Shutdown(); Harmony.Bound.Clear(); Chainloader.PluginInfos.Clear();
        Harmony.DropReceipt = false; Harmony.FailPatchAt = Harmony.PatchCalls = 0;
        ArenaMode.Plugin.ModLogger.Warnings.Clear(); ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false;
        ArenaMode.Diagnostics.ArenaLog.Messages.Clear();
        Keep.Value = true; SecondChance = true; HasSecondChance = true; Debouncing = false; Medicine = false; DamagePercent = 100;
    }
    private static Fixture Bind() { Reset(); var f = FixtureFactory.Make(); f.Register(); ArenaDeminvincibilityCompat.Initialize(); Need(ArenaDeminvincibilityCompat.GetRejectionReason() == null, "reviewed accepted"); return f; }
    private static LocalGame World(bool arena, bool loading = false) {
        var g = new LocalGame { GameWorld = new(), Profile = new(), Status = loading ? GameStatus.Running : GameStatus.Started };
        Singleton<AbstractGame>.Instance = g; Singleton<IBotGame>.Instance = g; Singleton<GameWorld>.Instance = g.GameWorld;
        ArenaSession.IsActive = arena; ArenaSession.ActiveGame = arena ? g : null; ArenaSession.ActiveWorld = arena ? g.GameWorld : null;
        ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = loading;
        ArenaSession.ArmedMap = loading ? g.GameWorld.LocationId : null; ArenaServerClient.PendingArmToken = loading ? Token : null;
        g._raidSettings.serverId = loading ? Protocol.BuildNativeEndIdentity("native", Token) : "ordinary";
        return g;
    }
    private static ActiveHealthController Body(LocalGame g, bool current = true, bool local = true, bool preview = false) {
        Player b = preview ? new Player() : new LocalPlayer(); b.IsYourPlayer = local; b.GameWorld = g.GameWorld; b.Profile = g.Profile;
        var hc = new ActiveHealthController { Player = b }; b.ActiveHealthController = hc;
        if (current) { g.LocalPlayer = b as LocalPlayer; g.GameWorld.MainPlayer = b; }
        return hc;
    }
    private static bool Prefix(Fixture f, int index, ActiveHealthController hc) {
        var foreign = f.Methods[index]; bool native = true, run = true;
        foreach (var patch in Harmony.GetPatchInfo(foreign)?.Prefixes.ToArray() ?? Array.Empty<Patch>()) {
            object[] p = { hc, hc.Player, native }; if (!(bool)patch.PatchMethod.Invoke(null, p)) run = false; native = (bool)p[2];
        }
        if (run) native = index == 0 ? (bool)foreign.Invoke(null, new object[] { hc, EDamageType.Undefined, hc.Player })
            : (bool)foreign.Invoke(null, new object[] { hc, hc.Player, EBodyPart.Head, EDamageType.Bullet });
        return native;
    }
    private delegate bool DamagePrefix(ActiveHealthController hc, Player player, ref float damage, EBodyPart part, DamageInfo info);
    private static float Damage(Fixture f, ActiveHealthController hc, float damage) {
        var method = f.Methods[2]; IEnumerable<CodeInstruction> code = PatchProcessor.GetOriginalInstructions(method);
        foreach (var t in Harmony.GetPatchInfo(method)?.Transpilers.ToArray() ?? Array.Empty<Patch>())
            code = (IEnumerable<CodeInstruction>)t.PatchMethod.Invoke(null, new object[] { code, method });
        var dm = new DynamicMethod("transformed", typeof(bool), method.GetParameters().Select(p => p.ParameterType).ToArray(), typeof(Program).Module, true);
        var il = dm.GetILGenerator();
        foreach (var c in code) { if (c.operand is MethodInfo m) il.Emit(c.opcode, m); else if (c.operand == null) il.Emit(c.opcode); else throw new Exception("unexpected IL"); }
        var transformed = (DamagePrefix)dm.CreateDelegate(typeof(DamagePrefix)); transformed(hc, hc.Player, ref damage, EBodyPart.Head, new() { DamageType = EDamageType.Bullet }); return damage;
    }
    private static void Register(ActiveHealthController hc) {
        var w = hc.Player.GameWorld; w.RegisterPlayer(hc.Player);
        foreach (var patch in Harmony.GetPatchInfo(typeof(GameWorld).GetMethod("RegisterPlayer"))?.Postfixes.ToArray() ?? Array.Empty<Patch>())
            patch.PatchMethod.Invoke(null, new object[] { w, hc.Player });
    }
    private static void Cases() {
        var f = Bind(); var ordinary = World(false); var ordinaryHc = Body(ordinary);
        Need(!Prefix(f, 0, ordinaryHc), "ordinary immortality retained"); Need(!Prefix(f, 1, ordinaryHc), "ordinary limb protection retained");
        Need(Damage(f, ordinaryHc, 200) == 99, "ordinary clamp retained");
        ordinary._raidSettings.playerSide = ESideType.Savage; Need(!Prefix(f, 0, ordinaryHc), "ordinary Scav retained");
        var g = World(true); var hc = Body(g);
        Need(Prefix(f, 0, hc), "Arena death reaches native"); Need(Prefix(f, 1, hc), "Arena limb destruction reaches native");
        Need(Damage(f, hc, 200) == 200, "Arena clamp bypassed");
        Need(Keep.Value && HasSecondChance && !Debouncing, "foreign settings and chance unchanged");
        Keep.Value = false; Debouncing = true; Need(Prefix(f, 0, hc), "ordinary debounce cannot hold Arena death");
        Debouncing = false; Need(Prefix(f, 0, hc) && HasSecondChance, "Arena does not consume Second Chance"); Keep.Value = true;
        Medicine = true; DamagePercent = 35; Need(Damage(f, hc, 200) == 70 && hc.EffectsRemoved == 1, "unrelated damage and medicine retained");
        Need(!(bool)f.Methods[3].Invoke(null, new object[] { hc, EBodyPart.LeftArm, hc.Player }), "fracture preference retained");
        DamagePercent = 100; Medicine = false;
        var lethal = Body(g); lethal.Health -= Damage(f, lethal, 150);
        if (lethal.Health <= 0 && Prefix(f, 1, lethal)) { lethal.DestroyBodyPart(EBodyPart.Head, EDamageType.Bullet); if (Prefix(f, 0, lethal)) lethal.Kill(EDamageType.Bullet); }
        Need(lethal.Destroyed && !lethal.Alive && lethal.DeathEvents == 1, "complete lethal damage to native event");
        foreach (EDamageType type in Enum.GetValues<EDamageType>()) { var victim = Body(g); if (Prefix(f, 0, victim)) victim.Kill(type); Need(!victim.Alive && victim.DeathEvents == 1, "native death event " + type); }
        // Register leases the entry body before its initial kit replacement, even without a hit.
        var entry = Body(g); Register(entry); var replacement = Body(g); Register(replacement);
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = true;
        Need(Prefix(f, 0, entry), "initial-kit retired body retained"); Need(Prefix(f, 0, replacement), "respawn controller retained");
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 1, "leased callbacks report once when developer enabled");
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false;
        var next = World(false); Need(Prefix(f, 0, entry), "retired lease survives following ordinary raid");
        var nextHc = Body(next); nextHc.Player.Profile = entry.Player.Profile; Need(!Prefix(f, 0, nextHc), "same profile ordinary does not inherit lease");
        Keep.Value = false; Need(!Prefix(f, 0, nextHc) && !HasSecondChance && Debouncing, "following ordinary Second Chance still executes");
        Need(Prefix(f, 0, entry), "pending ordinary Second Chance does not block retired Arena death");
        Debouncing = false; Need(Prefix(f, 0, nextHc), "ordinary debounce completion untouched"); Keep.Value = true;
        entry.Player.GameWorld = next.GameWorld; Need(!Prefix(f, 0, entry), "reused world loses lease");
        g = World(true); hc = Body(g); Need(Prefix(f, 0, hc), "lease prepared"); hc.Player.ActiveHealthController = new() { Player = hc.Player };
        Need(!Prefix(f, 0, hc), "replaced controller not authorized");
        g = World(true); hc = Body(g); Need(Prefix(f, 0, hc), "controller prepared"); var alien = Body(g, current: false); hc.Player = alien.Player;
        Need(!Prefix(f, 0, hc), "controller reassignment not authorized");
        g = World(true); Need(!Prefix(f, 0, Body(g, local: false)), "bot untouched"); Need(!Prefix(f, 0, Body(g, preview: true)), "preview untouched");
        g = World(true); Need(!Prefix(f, 0, Body(g, current: false)), "unregistered arbitrary local untouched");
        g = World(true); var wrongWorld = Body(ordinary, current: false); Need(!Prefix(f, 0, wrongWorld), "foreign world untouched");
        hc = Body(g); Singleton<IBotGame>.Instance = new LocalGame(); Need(!Prefix(f, 0, hc), "wrong bot game untouched");
        g = World(true); g._raidSettings.playerSide = ESideType.Savage; Need(!Prefix(f, 0, Body(g)), "Arena Scav never owned");
    }
    private static void Loading() {
        var f = Bind(); var g = World(false, true); var h = Body(g, current: false); Singleton<IBotGame>.Instance = null; Register(h);
        Need(Prefix(f, 0, h), "confirmed initial loading body leased");
        Action<LocalGame>[] changes = { x => ArenaServerClient.HasPendingArm = false, x => ArenaSession.ArmedForNextRaid = false,
            x => ArenaServerClient.PendingArmToken = "bad", x => x._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token),
            x => x._raidSettings.location = "other", x => ArenaSession.ArmedMap = "other", x => x.Status = GameStatus.Stopped,
            x => x._raidSettings.playerSide = ESideType.Savage };
        string[] names = { "pending", "armed", "token", "start identity", "settings map", "armed map", "stopped", "Scav" };
        for (int i = 0; i < changes.Length; i++) { g = World(false, true); changes[i](g); h = Body(g); Register(h); Need(!Prefix(f, 0, h), "loading rejects " + names[i]); }
        g = World(false, true); h = Body(g, current: false); h.Player.Profile = new(); Singleton<IBotGame>.Instance = null; Register(h);
        Need(!Prefix(f, 0, h), "initial loading exact profile required");
    }
    private static void Rejections() {
        foreach (string defect in new[] { "identity", "attribute", "name", "keep-missing", "keep-duplicate" }) {
            Reset(); var f = FixtureFactory.Make(defect: defect); f.Register(); Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "reject " + defect);
        }
        foreach (var f in new[] { FixtureFactory.Make(version: "2.1.0"), FixtureFactory.Make(metadataVersion: "2.3.0"), FixtureFactory.Make(assemblyName: "other"), FixtureFactory.Make(assemblyVersion: "9.1.0.0") }) {
            Reset(); f.Register(); Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "reject metadata drift");
        }
        Reset(); var newer = FixtureFactory.Make(version: "2.3.0"); newer.Register(); Need(ArenaDeminvincibilityCompat.GetRejectionReason() == null, "forward API accepted");
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "newer warns once"); ArenaDeminvincibilityCompat.GetRejectionReason(); Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "newer warning deduplicated");
        Reset(); var missing = FixtureFactory.Make(); missing.Register(); Harmony.Bound[ArenaDeminvincibilityContract.NativeTargets()[0]].Prefixes.Clear();
        Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "missing foreign hook rejected");
        Reset(); missing = FixtureFactory.Make(); missing.Register(); Harmony.Bound[typeof(GameWorld).GetMethod("OnGameStarted")] = new() { Prefixes = new() { new() { owner = "unexpected", PatchMethod = missing.Methods[0] } } };
        Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "unknown foreign hook rejected");
        Reset(); missing = FixtureFactory.Make(); missing.Register(); Harmony.Bound[typeof(GameWorld).GetMethod("OnGameStarted")] = new() { ILManipulators = new() { new() { owner = "unexpected-il", PatchMethod = missing.Methods[0] } } };
        Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "unknown foreign IL hook rejected");
        Reset(); missing = FixtureFactory.Make(); missing.Register(); Harmony.DropReceipt = true; Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "missing own hook rejected");
        Reset(); missing = FixtureFactory.Make(); missing.Register(); Harmony.FailPatchAt = 2; Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "partial patch rejected");
        Need(Harmony.Bound.Values.All(p => p.Prefixes.All(x => !x.owner.StartsWith("com.maschine"))), "partial hooks cleaned");
        var good = Bind(); var g = World(true); var h = Body(g); Need(Prefix(good, 0, h), "cleanup lease seeded");
        ArenaDeminvincibilityCompat.Shutdown(); Need(!Prefix(good, 0, h), "shutdown restores foreign hook");
        var scope = new ArenaDeminvincibilityScope(); object c = new(), b = new(), w = new(); Need(scope.Owns(c, b, b, c, w, w, true, true, true), "scope seed");
        scope.Clear(); Need(!scope.Owns(c, b, b, c, w, null, true, false, false), "clear releases leases");
        Need(!scope.Owns(c, b, new object(), c, w, w, true, true, true), "scope controller owner required");
        Need(!scope.Owns(c, b, b, new object(), w, w, true, true, true), "scope current controller required");
        Need(!scope.Owns(c, b, b, c, w, new object(), true, true, true), "scope exact body world required");
        Need(!scope.Owns(c, b, b, c, w, w, false, true, true), "scope requires native local");
        Need(scope.Owns(c, b, b, c, w, w, true, true, true), "scope seeded again");
        object differentBody = new(); Need(!scope.Owns(c, differentBody, differentBody, c, w, w, true, false, false), "leased controller new body rejected");
        Need(scope.Owns(c, b, b, c, w, w, true, true, true), "scope reseeded for world");
        object differentWorld = new(); Need(!scope.Owns(c, b, b, c, differentWorld, differentWorld, true, false, false), "leased controller new world rejected");
        good = Bind(); Harmony.Bound[good.Methods[0]].Prefixes.Clear(); Need(ArenaDeminvincibilityCompat.GetRejectionReason() != null, "removed own receipt rejected");
        good = Bind(); var instructions = PatchProcessor.GetOriginalInstructions(good.Methods[2]).ToList();
        void RejectRead(List<CodeInstruction> code, string name) {
            bool rejected = false; try { ArenaDeminvincibilityContract.FindKeepRead(code, good.Instance.GetType()); } catch (MissingMemberException) { rejected = true; }
            Need(rejected, name);
        }
        var helper = PatchProcessor.GetOriginalInstructions(good.Methods[2]).ToList(); helper.Add(new(OpCodes.Call, good.Methods[0])); RejectRead(helper, "unknown damage helper rejected");
        var field = PatchProcessor.GetOriginalInstructions(good.Methods[2]).ToList(); field.Add(new(OpCodes.Ldsfld, good.Instance.GetType().GetField("UnexpectedState"))); RejectRead(field, "unknown damage field rejected");
        var label = PatchProcessor.GetOriginalInstructions(good.Methods[2]).ToList(); label[1].labels.Add(default); RejectRead(label, "branch into config value rejected");
        var block = PatchProcessor.GetOriginalInstructions(good.Methods[2]).ToList(); block[1].blocks.Add(new()); RejectRead(block, "exception block config value rejected");
    }
    public static void Main(string[] args) { PublishedMetadata.Verify(args[0], Need); Cases(); Loading(); Rejections(); Console.WriteLine("DEMINVINCIBILITY_COMPAT_PASSED (" + _checks + ")"); }
}
