using System;
using System.Diagnostics;
using ArenaMode.Session;
using BepInEx;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace ZombieRetreatProbe;

// Temporary GPU-only observation. No setting, decision, result, exception or
// native object is changed. This project has no deployment/build dependency.
[BepInPlugin(Id, "maschine-ZombieRetreatProbe", "0.1.0")]
[BepInDependency("com.maschine.ArenaMode")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Id = "com.maschine.ZombieRetreatProbe";
    private static Plugin _instance;
    private static Func<AbstractGame, GameWorld, bool> _exact;
    private static GameWorld _world;
    private static readonly Row[] Rows = new Row[18];
    private Harmony _harmony;
    private float _start;
    private int _frame;
    private static bool _warned;
    private struct Row
    {
        internal long Calls, Ticks, Max, Success, Exceptions, None, Retreat, Shoot, Pursuit;
        internal long CloseVisible, Hard, Normal;
        internal float DogIn, DogOut;
    }
    public struct Stamp { internal int Row; internal long Start; }

    private void Awake()
    {
        _instance = this;
        try
        {
            _exact = (Func<AbstractGame, GameWorld, bool>)AccessTools.Method(typeof(ArenaSession),
                "IsExactActiveRaid", new[] { typeof(AbstractGame), typeof(GameWorld) })
                .CreateDelegate(typeof(Func<AbstractGame, GameWorld, bool>));
            _harmony = new Harmony(Id);
            _harmony.Patch(AccessTools.Method(typeof(BotDogFight), "ManualUpdate"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeManual)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterVoid)));
            _harmony.Patch(AccessTools.Method(typeof(BotDogFight), "CheckSpaceForRetreat"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeCheck)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterCheck)));
            _harmony.Patch(AccessTools.Method(typeof(BotDogFight), "Fight"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeFight)),
                finalizer: new HarmonyMethod(typeof(Plugin), nameof(AfterVoid)));
            Logger.LogInfo("Retreat probe active: observation only; 30s inclusive method costs; child Check is nested, do not sum. Remove this DLL after testing.");
        }
        catch (Exception ex)
        {
            _harmony?.UnpatchSelf();
            Logger.LogWarning("Retreat probe unavailable: " + ex.Message);
        }
    }

    private static bool Active(out GameWorld world)
    {
        world = null;
        if (!ArenaSession.IsActive || ArenaSession.ActiveMode != "zombie"
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated) return false;
        world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        return world != null && game != null && game.Status == GameStatus.Started
            && _exact != null && _exact(game, world);
    }

    private void Update()
    {
        if (!Active(out var world))
        {
            if (_world != null) Emit("raid-end");
            _world = null;
            return;
        }
        if (!ReferenceEquals(world, _world))
        {
            Array.Clear(Rows, 0, Rows.Length);
            _world = world; _start = Time.realtimeSinceStartup; _frame = Time.frameCount;
        }
        if (Time.realtimeSinceStartup - _start >= 30f) Emit("interval");
    }

    private static Stamp Begin(BotDogFight dog, int method)
    {
        try
        {
            if (!Active(out var world) || !ReferenceEquals(world, _world)) return default;
            var owner = dog?._owner;
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active
                || !ReferenceEquals(owner.DogFight, dog)) return default;
            var body = owner.GetPlayer;
            if (body == null || body.IsYourPlayer || !body.IsAI || body.HealthController?.IsAlive != true
                || !ReferenceEquals(body.AIData?.BotOwner, owner) || string.IsNullOrEmpty(body.ProfileId)
                || !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)) return default;
            var role = body.Profile?.Info?.Settings?.Role;
            if (role != WildSpawnType.infectedAssault && role != WildSpawnType.infectedCivil
                && role != WildSpawnType.infectedLaborant && role != WildSpawnType.infectedPmc) return default;
            if (!(owner.Brain?.BaseBrain is InfectedLayersStrategy strategy) || strategy._attackLayer == null) return default;
            int mode = (int)strategy.CurrentZombieMode - 1;
            if (mode < 0 || mode > 2) return default;
            bool attack = ReferenceEquals(strategy.CurLayer, strategy._attackLayer);
            int index = (mode * 2 + (attack ? 1 : 0)) * 3 + method;
            ref Row row = ref Rows[index];
            row.DogIn = owner.Settings.FileSettings.Mind.DOG_FIGHT_IN;
            row.DogOut = owner.Settings.FileSettings.Mind.DOG_FIGHT_OUT;
            var enemy = owner.Memory?.GoalEnemy;
            if (enemy != null && enemy.IsVisible && enemy.Distance < row.DogIn) row.CloseVisible++;
            if (body.Profile.Info.Settings.BotDifficulty == BotDifficulty.hard) row.Hard++;
            if (body.Profile.Info.Settings.BotDifficulty == BotDifficulty.normal) row.Normal++;
            return new Stamp { Row = index, Start = Stopwatch.GetTimestamp() };
        }
        catch (Exception ex) { Warn(ex); return default; }
    }

    private static void BeforeManual(BotDogFight __instance, out Stamp __state) => __state = Begin(__instance, 0);
    private static void BeforeCheck(BotDogFight __instance, out Stamp __state) => __state = Begin(__instance, 1);
    private static void BeforeFight(BotDogFight __instance, out Stamp __state) => __state = Begin(__instance, 2);
    private static Exception AfterVoid(BotDogFight __instance, Stamp __state, Exception __exception)
    {
        End(__instance, __state, __exception, false); return __exception;
    }
    private static Exception AfterCheck(BotDogFight __instance, Stamp __state, Exception __exception, bool __result)
    {
        End(__instance, __state, __exception, __result); return __exception;
    }
    private static void End(BotDogFight dog, Stamp state, Exception exception, bool success)
    {
        if (state.Start == 0) return;
        long elapsed = Stopwatch.GetTimestamp() - state.Start;
        try
        {
            ref Row row = ref Rows[state.Row];
            row.Calls++; row.Ticks += elapsed; row.Max = Math.Max(row.Max, elapsed);
            if (exception != null) row.Exceptions++;
            if (success) row.Success++;
            if (dog.DogFightState == BotDogFightStatus.none) row.None++;
            if (dog.DogFightState == BotDogFightStatus.dogFight) row.Retreat++;
            if (dog.DogFightState == BotDogFightStatus.shootFromPlace) row.Shoot++;
            if (dog._pursuitInProgress) row.Pursuit++;
        }
        catch (Exception ex) { Warn(ex); }
    }

    private void Emit(string reason)
    {
        float seconds = Time.realtimeSinceStartup - _start;
        int frames = Time.frameCount - _frame;
        Logger.LogInfo($"Retreat probe {reason}: seconds={seconds:F3} frames={frames}; ticks/sec={Stopwatch.Frequency}; main-thread inclusive, classification outside timer; nested child hooks add overhead.");
        for (int i = 0; i < Rows.Length; i++)
        {
            var row = Rows[i];
            if (row.Calls == 0) continue;
            string method = i % 3 == 0 ? "ManualUpdate" : i % 3 == 1 ? "CheckSpaceForRetreat" : "Fight";
            var mode = (InfectedAttackLayer.EZombieMode)(i / 6 + 1);
            Logger.LogInfo($"Retreat probe row mode={mode} attackLayer={i / 3 % 2 == 1} method={method} calls={row.Calls} totalMs={row.Ticks * 1000.0 / Stopwatch.Frequency:F4} maxMs={row.Max * 1000.0 / Stopwatch.Frequency:F4} success={row.Success} exceptions={row.Exceptions} states(none/retreat/shoot)={row.None}/{row.Retreat}/{row.Shoot} pursuit={row.Pursuit} closeVisible={row.CloseVisible} difficulty(normal/hard)={row.Normal}/{row.Hard} lastDogInOut={row.DogIn}/{row.DogOut}");
        }
        Array.Clear(Rows, 0, Rows.Length); _start = Time.realtimeSinceStartup; _frame = Time.frameCount;
    }
    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true; _instance?.Logger.LogWarning("Retreat probe observation failed; native preserved: " + ex.Message);
    }
    private void OnDestroy() { _harmony?.UnpatchSelf(); _world = null; _instance = null; }
}
