using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Diagnostics;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN.Components;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.SAINComponent.Classes.Talk;
using System.Reflection;
using System.Reflection.Emit;

internal static class Program
{
    private static int _assertions;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private static void Check(bool yes, string name) { _assertions++; if (!yes) throw new Exception("ASSERT: " + name); }
    internal static List<CodeInstruction> NativeOperand(out DynamicMethod method, bool broken = false)
    {
        method = new DynamicMethod("NativeTauntBranch", typeof(bool), [typeof(EnemyTalk)], typeof(Program).Module, true);
        var deny = method.GetILGenerator().DefineLabel();
        var field = typeof(EnemyTalk).GetField("CanTaunt", BindingFlags.NonPublic | BindingFlags.Instance);
        var code = new List<CodeInstruction>
        {
            new(OpCodes.Ldarg_0), new(OpCodes.Ldfld, field), new(OpCodes.Brfalse, deny),
            new(OpCodes.Ldarg_0), new(OpCodes.Call, typeof(EnemyTalk).GetMethod(nameof(EnemyTalk.NativeRemainingChecks))),
            new(OpCodes.Brfalse, deny), new(OpCodes.Ldc_I4_1), new(OpCodes.Ret), new(OpCodes.Ldc_I4_0), new(OpCodes.Ret)
        };
        code[8].labels.Add(deny);
        if (broken) code.RemoveRange(0, 3);
        return code;
    }
    private sealed class Rig
    {
        public GameWorld World = new(); public LocalGame Game = new();
        public Player Player = new(); public BotOwner Owner = new(); public BotComponent Bot = new(); public EnemyTalk Talk = new();
        public Rig()
        {
            Owner.GetPlayer = Player; Owner.Component = Bot; Player.AIData.BotOwner = Owner;
            Bot.BotOwner = Owner; Bot.Player = Player; Bot.Talk.EnemyTalk = Talk; Talk.Bot = Bot;
            World.Alive[Player.ProfileId] = World.Ever[Player.ProfileId] = Player;
            Singleton<GameWorld>.Instance = ArenaSession.World = World; Singleton<AbstractGame>.Instance = ArenaSession.Game = Game;
            ArenaSession.IsActive = true; ArenaRespawnDriver.StandDown = false; PitFireTeamCompat.Team.Clear();
            ArenaMatchService.Current = new(); ArenaMatchService.TeamStartAbortPending = false;
            ArenaMode.Areas.ArenaAreaRuntime.IsPending = false; UnityEngine.Time.timeScale = 1;
        }
    }
    private static void Reject(string name, Action<Rig> change)
    {
        var r = new Rig(); change(r);
        Check(!r.Talk.Execute(), name);
        Check(!r.Talk.Cached && !r.Bot.Info.Settings.CanTaunt, name + " no writes");
        r.Talk.Cached = true;
        Check(r.Talk.Execute(), name + " native true retained");
    }
    private static void Main()
    {
        try
        {
            Check(typeof(EPersonality).Assembly != typeof(Program).Assembly
                && typeof(EPersonality).Assembly.GetName().Name == "SAIN.Preset.Shared", "real shared enum assembly boundary");
            ArenaBotTaunts.Enable();
            Check(Harmony.Hooks.Count == 1 && Harmony.Hooks[0].Name == "ManualUpdate" && Harmony.Hooks[0].DeclaringType == typeof(EnemyTalk), "only combat update patched");
            var r = new Rig();
            Check(r.Talk.Execute(), "live Normal eligible");
            Check(!r.Talk.Cached && !r.Bot.Info.Settings.CanTaunt, "native cache and shared personality unchanged");
            Check(r.Bot.Info.Settings.TauntFrequency == 10 && r.Bot.Info.Settings.TauntChance == 50 && r.Bot.Info.FileSettings.Mind.TalkFrequency == 1, "native timing and chance unchanged");
            foreach (var mode in new IArenaMode[] { new RoundsMode(), new TacticalMode(), new TeamDeathmatchMode(), new GunGameMode() })
            { r = new Rig(); ArenaMatchService.Current.Mode = mode; Check(r.Talk.Execute(), "live mode " + mode.GetType().Name); }
            Scope(); NativeConditions(); TranspilerContracts(); Diagnostics(); BindingFailure();
            Console.WriteLine($"BOT_TAUNT_BEHAVIOR_PASSED ({_assertions} assertions)");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    private static void Scope()
    {
        Reject("ordinary raid unchanged", _ => ArenaSession.IsActive = false);
        Reject("foreign world unchanged", _ => ArenaSession.World = new());
        Reject("foreign game unchanged", _ => ArenaSession.Game = new LocalGame());
        Reject("missing world unchanged", _ => Singleton<GameWorld>.Instance = null);
        Reject("missing game unchanged", _ => Singleton<AbstractGame>.Instance = null);
        Reject("not started unchanged", r => r.Game.Status = GameStatus.Starting);
        Reject("pre-live unchanged", _ => ((RoundsMode)ArenaMatchService.Current.Mode).AssistScoringOpen = false);
        Reject("complete unchanged", _ => ((RoundsMode)ArenaMatchService.Current.Mode).Complete = true);
        Reject("no match unchanged", _ => ArenaMatchService.Current = null);
        Reject("zombie mode unchanged", _ => ArenaMatchService.Current.Mode = new ZombieMode());
        Reject("stand down unchanged", _ => ArenaRespawnDriver.StandDown = true);
        Reject("team abort unchanged", _ => ArenaMatchService.TeamStartAbortPending = true);
        Reject("area pending unchanged", _ => ArenaMode.Areas.ArenaAreaRuntime.IsPending = true);
        Reject("pause unchanged", _ => UnityEngine.Time.timeScale = 0);
        Reject("retired body unchanged", r => r.World.Ever[r.Player.ProfileId] = new());
        Reject("noncurrent alive body unchanged", r => r.World.Alive[r.Player.ProfileId] = new());
        Reject("dead body unchanged", r => r.Player.HealthController.IsAlive = false);
        Reject("preactive bot unchanged", r => r.Owner.BotState = EBotState.NonActive);
        Reject("human unchanged", r => r.Player.IsAI = false);
        Reject("local unchanged", r => r.Player.IsYourPlayer = true);
        Reject("main body unchanged", r => r.World.MainPlayer = r.Player);
        Reject("empty identity unchanged", r => r.Player.ProfileId = "");
        Reject("owner body mismatch", r => r.Owner.GetPlayer = new());
        Reject("body owner mismatch", r => r.Player.AIData.BotOwner = new());
        Reject("foreign component unchanged", r => r.Owner.Component = new BotComponent());
        Reject("foreign talk unchanged", r => r.Bot.Talk.EnemyTalk = new());
        Reject("PFT roster unchanged", r => PitFireTeamCompat.Team.Add(r.Player));
        Reject("native follower unchanged", r => r.Owner.FollowerFlag = true);
        Reject("native boss follower unchanged", r => r.Owner.BotFollower.HaveBoss = true);
        Reject("scav unchanged", r => r.Player.Profile.Info.Settings.Role = WildSpawnType.assault);
        Reject("infected unchanged", r => r.Player.Profile.Info.Settings.Role = WildSpawnType.infectedAssault);
        Reject("Chad unchanged", r => r.Bot.Info.Personality = EPersonality.Chad);
        Reject("Rat unchanged", r => r.Bot.Info.Personality = EPersonality.Rat);
        Reject("Mind.BotTaunts false retained", r => r.Bot.Info.FileSettings.Mind.BotTaunts = false);
        Reject("missing Mind retained", r => r.Bot.Info.FileSettings.Mind = null);
        var bear = new Rig(); bear.Player.Profile.Info.Settings.Role = WildSpawnType.pmcBEAR;
        Check(bear.Talk.Execute(), "BEAR eligible");
        var a = new Rig(); var b = new Rig(); b.Bot.Info.Settings = a.Bot.Info.Settings; b.Bot.Info.FileSettings = a.Bot.Info.FileSettings;
        Check(b.Talk.Execute() && !a.Talk.Execute(), "shared preset live body only");
        Check(ReferenceEquals(a.Bot.Info.Settings, b.Bot.Info.Settings) && !a.Bot.Info.Settings.CanTaunt && !a.Talk.Cached && !b.Talk.Cached, "shared object identity and values retained");
    }
    private static void NativeConditions()
    {
        foreach (var field in new[] { "Cooldown", "Chance", "HasEnemy", "EnemyAllowsTaunt" })
        {
            var r = new Rig(); typeof(EnemyTalk).GetField(field).SetValue(r.Talk, false);
            Check(!r.Talk.Execute() && r.Talk.RemainingCalls == 1, "native " + field + " retained");
        }
        var muted = new Rig(); muted.Bot.Talk.CanTalk = false;
        Check(!muted.Talk.Execute() && muted.Talk.RemainingCalls == 0, "native outer CanTalk retained");
    }
    private static void TranspilerContracts()
    {
        var transpile = typeof(ArenaBotTaunts).GetMethod("Transpile", Private);
        var input = NativeOperand(out _); var result = ((IEnumerable<CodeInstruction>)transpile.Invoke(null, [input])).ToList();
        Check(result.Count == input.Count + 2 && input.All(result.Contains), "native instruction identities retained");
        Check(result.Count(c => c.opcode == OpCodes.Ldfld) == 1 && !result.Any(c => c.opcode == OpCodes.Stfld), "one unchanged cache read no writes");
        foreach (var variant in new[] { "missing", "duplicate", "receiver", "branch" })
        {
            var code = NativeOperand(out _);
            if (variant == "missing") code.RemoveRange(0, 3);
            if (variant == "duplicate") code.InsertRange(0, code.Take(3).ToArray());
            if (variant == "receiver") code[0] = new(OpCodes.Ldnull);
            if (variant == "branch") code[2] = new(OpCodes.Brtrue, code[2].operand);
            bool rejected = false;
            try { ((IEnumerable<CodeInstruction>)transpile.Invoke(null, [code])).ToArray(); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, "drift rejected " + variant);
        }
    }
    private static void Diagnostics()
    {
        ArenaLog.Lines.Clear(); ArenaLog.DeveloperEnabled = false;
        var r = new Rig(); Check(r.Talk.Execute() && ArenaLog.Lines.Count == 0, "diagnostics opt in");
        ArenaLog.DeveloperEnabled = true;
        for (int i = 0; i < 20; i++) Check(r.Talk.Execute(), "diagnostics do not gate behavior");
        Check(ArenaLog.Lines.Count == 1 && ArenaLog.Lines[0].Contains("native=False, applied=True"), "bounded eligible operand evidence");
        for (int i = 0; i < 5; i++)
        {
            var other = new EnemyTalk { Bot = r.Bot }; r.Bot.Talk.EnemyTalk = other;
            Check(other.Execute(), "new talk body eligible");
        }
        Check(ArenaLog.Lines.Count == 3, "three diagnostics per world cap");
        ArenaLog.DeveloperEnabled = false;
    }
    private static void BindingFailure()
    {
        var r = new Rig(); ArenaBotTaunts.Shutdown();
        var eligibility = typeof(ArenaBotTaunts).GetMethod("Eligibility", Private);
        Check(!(bool)eligibility.Invoke(null, [false, r.Talk]) && (bool)eligibility.Invoke(null, [true, r.Talk]), "shutdown native pass-through");
        Harmony.BreakContract = true;
        Check(!ArenaBotTaunts.TryBind(typeof(Program).Assembly) && Harmony.Hooks.Count == 0 && Harmony.Patched == null, "failed binding removes hooks");
        Harmony.BreakContract = false; Check(ArenaBotTaunts.TryBind(typeof(Program).Assembly), "clean rebind");
        Check(new Rig().Talk.Execute(), "rebind retains admission");
        ArenaBotTaunts.Shutdown();
    }
}
