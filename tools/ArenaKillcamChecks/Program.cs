using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using BepInEx.Bootstrap;
using EFT;
using System.Collections;
using UnityEngine;
using Replay = KillcamReplay.Replay.ReplayController;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            Selection();
            WaitPolicy();
            ReflectionStates();
            Availability();
            ImmediateAndFrozen();
            PlaybackAndModeHolds();
            FailClosed();
            LifetimeCancellation();
            RevalidateAfterPreload();
            Console.WriteLine($"ARENA KILLCAM CONTRACTS PASSED: {_assertions} assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string label)
    {
        ++_assertions;
        if (!condition) throw new InvalidOperationException("CHECK: " + label);
    }

    private static void Selection()
    {
        foreach (float seconds in new[] { -4f, 0f, 0.5f, 3f, 10f, 30f, float.MaxValue })
        {
            ArenaReplaySelection.Capture(seconds, available: true);
            Check(ArenaReplaySelection.Seconds == Math.Clamp(seconds, 0f, 10f), $"selection clamps {seconds}");
            ArenaReplaySelection.Capture(seconds, available: false);
            Check(ArenaReplaySelection.Seconds == 0f, $"missing dependency disables {seconds}");
        }
        foreach (float value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            ArenaReplaySelection.Capture(value, true);
            Check(ArenaReplaySelection.Seconds == 0f, "nonfinite selection disabled");
        }
        float setting = 4f;
        ArenaReplaySelection.Capture(setting, true);
        setting = 8f;
        Check(ArenaReplaySelection.Seconds == 4f, "selection owns captured value, not later UI setting");
        ArenaReplaySelection.Clear();
        Check(ArenaReplaySelection.Seconds == 0f, "selection cleared at raid end");
        ArenaReplaySelection.Capture(setting, true);
        Check(ArenaReplaySelection.Seconds == 8f, "new raid captures new setting");
        ArenaReplaySelection.Clear();
    }

    private static void WaitPolicy()
    {
        foreach (var ready in new[] { OptionalCameraOwnerState.Absent, OptionalCameraOwnerState.Idle })
            Check(new ArenaReplayWaitPolicy().Observe(ready, float.NaN, float.NaN) == ArenaReplayWaitDecision.Ready,
                "absent or settled camera has no synthetic delay");
        foreach (var bad in new[] { OptionalCameraOwnerState.Incompatible, (OptionalCameraOwnerState)57 })
            Check(new ArenaReplayWaitPolicy().Observe(bad, 0f, 10f) == ArenaReplayWaitDecision.Unsafe, "unknown ownership is unsafe");
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -0.01f })
            Check(new ArenaReplayWaitPolicy().Observe(OptionalCameraOwnerState.Busy, invalid, 10f) == ArenaReplayWaitDecision.Unsafe,
                "invalid duration is unsafe");
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            Check(new ArenaReplayWaitPolicy().Observe(OptionalCameraOwnerState.Busy, 1f, invalid) == ArenaReplayWaitDecision.Unsafe,
                "nonfinite clock is unsafe");

        var policy = new ArenaReplayWaitPolicy();
        Check(policy.Observe(OptionalCameraOwnerState.Busy, 2f, 10f) == ArenaReplayWaitDecision.Wait, "first busy frame waits");
        Check(policy.Observe(OptionalCameraOwnerState.Busy, 100f, 16.99f) == ArenaReplayWaitDecision.Wait, "deadline stays open before boundary");
        Check(policy.Observe(OptionalCameraOwnerState.Busy, 100f, 17f) == ArenaReplayWaitDecision.Unsafe,
            "busy deadline is unsafe, never a respawn release");
        Check(policy.Observe(OptionalCameraOwnerState.Idle, 0f, 18f) == ArenaReplayWaitDecision.Ready, "confirmed cleanup may release after deadline");

        var minimum = new ArenaReplayWaitPolicy();
        Check(minimum.Observe(OptionalCameraOwnerState.Busy, 0f, 20f) == ArenaReplayWaitDecision.Wait, "zero remaining still allows cleanup bound");
        Check(minimum.Observe(OptionalCameraOwnerState.Busy, 0f, 24.99f) == ArenaReplayWaitDecision.Wait, "minimum bound before five seconds");
        Check(minimum.Observe(OptionalCameraOwnerState.Busy, 0f, 25f) == ArenaReplayWaitDecision.Unsafe, "minimum bound ends at five seconds");
        var maximum = new ArenaReplayWaitPolicy();
        Check(maximum.Observe(OptionalCameraOwnerState.Busy, float.MaxValue, 100f) == ArenaReplayWaitDecision.Wait, "huge finite replay bound starts waiting");
        Check(maximum.Observe(OptionalCameraOwnerState.Busy, float.MaxValue, 174.99f) == ArenaReplayWaitDecision.Wait, "maximum bound before seventy-five seconds");
        Check(maximum.Observe(OptionalCameraOwnerState.Busy, float.MaxValue, 175f) == ArenaReplayWaitDecision.Unsafe, "maximum bound capped at seventy-five seconds");
    }

    private static void ReflectionStates()
    {
        Reset();
        Chainloader.PluginInfos.Entries.Clear();
        Check(Read(out _, out _) == OptionalCameraOwnerState.Absent, "absent plugin detected");
        Check(KillcamReplayCompat.CameraReleased, "absent camera cannot consume replacement budget");
        Reset();
        Check(Read(out var remaining, out var reason) == OptionalCameraOwnerState.Idle && remaining == 0f && reason == null,
            "no recording is idle without reading replay delay");
        Check(Replay.DelayReads == 0, "idle does not probe duration");
        Check(KillcamReplayCompat.CameraReleased, "settled camera releases protected rebind");
        foreach (var state in new[] { "lead-in", "playback", "cleanup", "pending-and-playing", "all-owned" })
        {
            Reset();
            Replay.PendingReplayKiller = state is "lead-in" or "pending-and-playing" or "all-owned" ? new object() : null;
            KillcamReplay.Plugin.ReplayCtrl.Playing = state is "playback" or "pending-and-playing" or "all-owned";
            KillcamReplay.Plugin.ReplayCtrl.Ready = state is not ("cleanup" or "all-owned");
            Check(Read(out remaining, out reason) == OptionalCameraOwnerState.Busy && remaining == 2f && reason == null,
                "production reflection owns " + state);
            Check(!KillcamReplayCompat.CameraReleased, "protected rebind waits for " + state);
        }
        Reset();
        KillcamReplay.Plugin.ReplayCtrl = null;
        Check(Read(out _, out reason) == OptionalCameraOwnerState.Incompatible && reason.Contains("null"), "missing controller unsafe");
        Check(!KillcamReplayCompat.CameraReleased, "unknown camera cannot release protected rebind");
        Reset();
        KillcamReplay.Plugin.ReplayCtrl.ThrowState = true;
        Check(Read(out _, out reason) == OptionalCameraOwnerState.Incompatible && reason.Contains("fixture read failure"), "state exception unsafe");
        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reset(); Pending(); Replay.Delay = bad;
            Check(Read(out _, out reason) == OptionalCameraOwnerState.Incompatible && reason.Contains("invalid"), "invalid reflected bound unsafe");
        }
        Reset(); Pending(); Replay.ThrowDelay = true;
        Check(Read(out _, out reason) == OptionalCameraOwnerState.Incompatible && reason.Contains("fixture delay failure"), "duration exception unsafe");
        Reset();
        Chainloader.PluginInfos.Entries["com.maschine.KillcamReplay"].Instance = new object();
        Check(Read(out _, out reason) == OptionalCameraOwnerState.Incompatible && reason != null, "unrecognized plugin surface unsafe");
    }

    private static void Availability()
    {
        Reset();
        Check(KillcamReplayCompat.CanPlay(out var reason) && reason == null,
            "enabled plugin with public controller is available in menu");
        KillcamReplay.Plugin.DeathReplayActive = false;
        Check(!KillcamReplayCompat.CanPlay(out reason) && reason.Contains("Enable DeathReplay"),
            "disabled dependency explains unavailable menu choice");
        KillcamReplay.Plugin.DeathReplayActive = true;
        Check(KillcamReplayCompat.CanPlay(out _), "dependency enabled setting is read live in menu");
        KillcamReplay.Plugin.ReplayCtrl = null;
        Check(!KillcamReplayCompat.CanPlay(out reason) && reason.Contains("not ready"),
            "unready controller cannot arm replay");
        Reset(); Chainloader.PluginInfos.Entries.Clear();
        Check(!KillcamReplayCompat.CanPlay(out reason) && reason.Contains("Requires"),
            "absent dependency explains installation requirement");
        Reset(); Chainloader.PluginInfos.Entries["com.maschine.KillcamReplay"].Instance = new object();
        Check(!KillcamReplayCompat.CanPlay(out reason) && reason.Contains("compatibility"),
            "incompatible dependency cannot arm replay");
    }

    private static void ImmediateAndFrozen()
    {
        foreach (bool installed in new[] { false, true })
        {
            var fixture = Reset();
            if (!installed) Chainloader.PluginInfos.Entries.Clear();
            var hold = NewHold(fixture);
            Check(!hold.Wait().MoveNext() && hold.CanRespawn, "missing or no-track camera releases immediately");
            Check(ArenaMatchService.Aborts.Count == 0, "immediate release is not an abort");
        }
        var state = Reset(); Pending(); ArenaReplaySelection.Clear();
        var disabled = NewHold(state);
        ArenaReplaySelection.Capture(5f, true);
        Check(!disabled.Wait().MoveNext() && disabled.CanRespawn, "disabled snapshot remains immediate after setting changes");
        Check(Chainloader.PluginInfos.Reads == 0 && Replay.StateReads == 0 && Replay.DelayReads == 0, "disabled snapshot never probes dependency");

        state = Reset(); Pending();
        var enabled = NewHold(state);
        ArenaReplaySelection.Clear();
        var waiting = enabled.Wait();
        Check(waiting.MoveNext() && !enabled.CanRespawn, "enabled death snapshot cannot bypass pending by changing setting");
        Settled();
        Check(!waiting.MoveNext() && enabled.CanRespawn, "frozen enabled snapshot releases on actual cleanup");
    }

    private static void PlaybackAndModeHolds()
    {
        var state = Reset(); Pending();
        var hold = NewHold(state);
        var wait = hold.Wait();
        Check(wait.MoveNext() && !hold.CanRespawn, "pending lead-in retains dead owner");
        Replay.PendingReplayKiller = null;
        KillcamReplay.Plugin.ReplayCtrl.Playing = true;
        Time.time = 10.3f;
        Check(wait.MoveNext() && !hold.CanRespawn, "playback retains dead owner");
        KillcamReplay.Plugin.ReplayCtrl.Playing = false;
        KillcamReplay.Plugin.ReplayCtrl.Ready = false;
        Time.time = 11f;
        Check(wait.MoveNext() && !hold.CanRespawn, "cleanup retains dead owner");
        Settled(); Time.time = 11.1f;
        Check(!wait.MoveNext() && hold.CanRespawn, "completed cleanup releases in same coroutine tick");
        Check(ArenaMode.Plugin.ModLogger.Entries.Count == 2, "one wait and one release diagnostic per death");
        Check(ArenaMatchService.Aborts.Count == 0, "normal replay sequence never aborts");

        state = Reset(); Pending();
        hold = NewHold(state); wait = hold.Wait();
        Check(wait.MoveNext(), "skippable replay begins held");
        Settled();
        Check(!wait.MoveNext() && hold.CanRespawn, "skip releasing camera has no fixed tail delay");

        state = Reset(); Pending(); ArenaMatchService.Current.Mode.HoldRespawn = true;
        hold = NewHold(state); wait = hold.Wait();
        Check(wait.MoveNext(), "mode and replay initially concurrent");
        Settled(); Time.time = 20f;
        Check(wait.MoveNext() && !hold.CanRespawn, "mode hold survives camera release");
        Check(ArenaMatchService.Aborts.Count == 0, "settled camera does not time out during mode hold");
        ArenaMatchService.Current.Mode.HoldRespawn = false;
        Check(!wait.MoveNext() && hold.CanRespawn, "last mode release resumes without extra replay duration");

        state = Reset(); Pending(); ArenaMatchService.Current.Mode.HoldRespawn = true;
        hold = NewHold(state); wait = hold.Wait();
        Check(wait.MoveNext(), "second mode hold begins");
        ArenaMatchService.Current.Mode.HoldRespawn = false;
        Check(wait.MoveNext() && !hold.CanRespawn, "camera hold survives mode release");
        Settled();
        Check(!wait.MoveNext() && hold.CanRespawn, "both released permits replacement");

        state = Reset(); ArenaReplaySelection.Clear(); ArenaMatchService.Current.Mode.HoldRespawn = true;
        hold = NewHold(state); wait = hold.Wait();
        Check(wait.MoveNext() && !hold.CanRespawn, "mode hold works with killcam disabled");
        ArenaMatchService.Current.Mode.HoldRespawn = false;
        Check(!wait.MoveNext() && hold.CanRespawn && Chainloader.PluginInfos.Reads == 0, "disabled mode hold never probes camera");
    }

    private static void FailClosed()
    {
        var state = Reset(); Pending();
        var hold = NewHold(state); var wait = hold.Wait();
        Check(wait.MoveNext(), "timeout case starts waiting");
        Time.time = 16.9f; Replay.Delay = 70f;
        Check(wait.MoveNext(), "growing external duration cannot slide deadline");
        Time.time = 17f;
        Check(!wait.MoveNext() && !hold.CanRespawn && ArenaMatchService.Aborts.Count == 1,
            "hold timeout safely aborts instead of replacing original dead body");
        Check(ReferenceEquals(state.Game.LocalPlayer, state.Body) && !state.Body.HealthController.IsAlive,
            "unsafe timeout leaves original owner untouched");
        Check(ArenaMatchService.Aborts[0].Contains("bound"), "timeout diagnostic names bound");
        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            state = Reset(); Pending(); Replay.Delay = bad;
            hold = NewHold(state);
            Check(!hold.Wait().MoveNext() && !hold.CanRespawn && ArenaMatchService.Aborts.Count == 1,
                "nonfinite or negative plugin duration aborts closed");
        }
        state = Reset(); Pending(); Time.time = float.NaN;
        hold = NewHold(state);
        Check(!hold.Wait().MoveNext() && !hold.CanRespawn && ArenaMatchService.Aborts.Count == 1, "nonfinite simulation clock aborts closed");
        state = Reset(); KillcamReplay.Plugin.ReplayCtrl = null;
        hold = NewHold(state);
        Check(!hold.Wait().MoveNext() && !hold.CanRespawn && ArenaMatchService.Aborts.Single().Contains("null"),
            "incompatible ownership uses existing protected abort");
    }

    private static void LifetimeCancellation()
    {
        var changes = new (string Name, Action<Fixture> Apply)[]
        {
            ("inactive session", _ => ArenaSession.IsActive = false),
            ("stopped game", f => f.Game.Started = false),
            ("stand down", _ => ArenaRespawnDriver.StandDown = true),
            ("stale epoch", _ => ++ArenaRespawnDriver.Epoch),
            ("cleared match", _ => ArenaMatchService.Current = null),
            ("new match", _ => ArenaMatchService.Current = new ArenaMatch()),
            ("replacement local body", f => f.Game.LocalPlayer = new LocalPlayer()),
            ("missing local body", f => f.Game.LocalPlayer = null),
            ("revived original body", f => f.Body.HealthController.IsAlive = true),
            ("missing health", f => f.Body.HealthController = null),
        };
        foreach (var change in changes)
        {
            foreach (bool startWaiting in new[] { false, true })
            {
                var state = Reset(); Pending();
                var hold = NewHold(state); var wait = hold.Wait();
                if (startWaiting) Check(wait.MoveNext(), change.Name + " begins in pending phase");
                int reads = Replay.StateReads;
                change.Apply(state);
                Check(!wait.MoveNext() && !hold.CanRespawn, "stale lifetime cannot release: " + change.Name);
                Check(Replay.StateReads == reads && ArenaMatchService.Aborts.Count == 0,
                    "cancelled lifetime neither probes nor aborts new raid: " + change.Name);
            }
        }
        var fixture = Reset();
        var noBody = new ArenaDeathReplayHold(fixture.Game, null, ArenaRespawnDriver.Epoch);
        Check(!noBody.Wait().MoveNext() && !noBody.CanRespawn, "null captured body cancels");
        var noGame = new ArenaDeathReplayHold(null, fixture.Body, ArenaRespawnDriver.Epoch);
        Check(!noGame.Wait().MoveNext() && !noGame.CanRespawn, "null game cancels");
        ArenaMatchService.Current = null;
        var noMatch = NewHold(fixture);
        ArenaMatchService.Current = new ArenaMatch();
        Check(!noMatch.Wait().MoveNext() && !noMatch.CanRespawn, "constructor without match cannot adopt later match");
    }

    private static void RevalidateAfterPreload()
    {
        var fixture = Reset();
        var hold = NewHold(fixture);
        Check(!hold.Wait().MoveNext() && hold.CanRespawn, "initial idle pass allows kit preparation");
        Pending();
        var second = hold.Wait();
        Check(second.MoveNext() && !hold.CanRespawn, "second wait rechecks replay after preload");
        Settled();
        Check(!second.MoveNext() && hold.CanRespawn, "second wait releases only after new pending cleanup");

        fixture = Reset();
        hold = NewHold(fixture);
        Check(!hold.Wait().MoveNext() && hold.CanRespawn, "preload body-change case starts ready");
        fixture.Game.LocalPlayer = new LocalPlayer();
        Check(!hold.Wait().MoveNext() && !hold.CanRespawn, "second wait revalidates exact dead body");

        fixture = Reset();
        hold = NewHold(fixture);
        Check(!hold.Wait().MoveNext() && hold.CanRespawn, "preload mode-change case starts ready");
        ArenaMatchService.Current.Mode.HoldRespawn = true;
        second = hold.Wait();
        Check(second.MoveNext() && !hold.CanRespawn, "second wait rechecks changed mode hold");
        ArenaMatchService.Current.Mode.HoldRespawn = false;
        Check(!second.MoveNext() && hold.CanRespawn, "second mode hold cleanly releases");

        fixture = Reset(); Pending();
        hold = NewHold(fixture); var first = hold.Wait();
        Check(first.MoveNext(), "single deadline case starts held");
        Settled(); Time.time = 11f;
        Check(!first.MoveNext() && hold.CanRespawn, "first camera ownership ends before preload");
        Pending(); Time.time = 17f;
        Check(!hold.Wait().MoveNext() && !hold.CanRespawn && ArenaMatchService.Aborts.Count == 1,
            "second wait retains first per-death deadline");
    }

    private sealed record Fixture(LocalGame Game, LocalPlayer Body);

    private static Fixture Reset()
    {
        KillcamReplayCompat.Reset();
        ArenaMode.Plugin.ModLogger.Entries.Clear();
        ArenaMatchService.Aborts.Clear();
        ArenaMatchService.Current = new ArenaMatch();
        ArenaSession.IsActive = true;
        ArenaReplaySelection.Capture(3f, true);
        ArenaRespawnDriver.StandDown = false;
        ArenaRespawnDriver.Epoch = 9;
        Time.time = 10f;
        Replay.PendingReplayKiller = null;
        Replay.Delay = 2f;
        Replay.ThrowDelay = false;
        Replay.DelayReads = 0;
        Replay.StateReads = 0;
        KillcamReplay.Plugin.ReplayCtrl = new Replay();
        KillcamReplay.Plugin.DeathReplayActive = true;
        Chainloader.PluginInfos.Reads = 0;
        Chainloader.PluginInfos.Entries.Clear();
        Chainloader.PluginInfos.Entries.Add("com.maschine.KillcamReplay", new TestPluginInfo { Instance = new KillcamReplay.Plugin() });
        var body = new LocalPlayer();
        return new Fixture(new LocalGame { LocalPlayer = body }, body);
    }

    private static ArenaDeathReplayHold NewHold(Fixture state)
        => new(state.Game, state.Body, ArenaRespawnDriver.Epoch);

    private static OptionalCameraOwnerState Read(out float remaining, out string reason)
        => KillcamReplayCompat.ReadState(out remaining, out reason);

    private static void Pending() => Replay.PendingReplayKiller = new object();

    private static void Settled()
    {
        Replay.PendingReplayKiller = null;
        KillcamReplay.Plugin.ReplayCtrl.Playing = false;
        KillcamReplay.Plugin.ReplayCtrl.Ready = true;
    }
}
