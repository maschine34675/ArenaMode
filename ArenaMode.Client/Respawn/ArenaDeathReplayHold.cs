using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Spectator;
using EFT;
using System;
using System.Collections;
using UnityEngine;

namespace ArenaMode.Respawn;

/// <summary>
/// Owns no camera and mutates no replay state. It only postpones a replacement
/// of this exact dead body while the existing camera owner or mode holds it.
/// </summary>
internal sealed class ArenaDeathReplayHold
{
    private readonly LocalGame _game;
    private readonly LocalPlayer _body;
    private readonly int _epoch;
    private readonly ArenaMatch _match;
    private readonly bool _enabled;
    private readonly ArenaReplayWaitPolicy _policy = new();
    private bool _reportedWait;
    private bool _reportedRelease;
    private float _waitStarted;

    internal bool CanRespawn { get; private set; }

    internal ArenaDeathReplayHold(LocalGame game, LocalPlayer body, int epoch)
    {
        _game = game;
        _body = body;
        _epoch = epoch;
        _match = ArenaMatchService.Current;
        _enabled = ArenaSession.ReplaySeconds > 0f;
    }

    private bool IsCurrent => _match != null && ReferenceEquals(_match, ArenaMatchService.Current)
        && ArenaRespawnDriver.IsSameActiveRaid(_game, _epoch) && !ArenaRespawnDriver.StandDown
        && _body != null && ReferenceEquals(_game.LocalPlayer, _body)
        && _body.HealthController?.IsAlive == false;

    internal IEnumerator Wait()
    {
        CanRespawn = false;
        while (IsCurrent)
        {
            var decision = ArenaReplayWaitDecision.Ready;
            string problem = null;
            if (_enabled)
            {
                var state = KillcamCompat.ReadState(out float remaining, out problem);
                decision = _policy.Observe(state, remaining, Time.time);
            }

            if (decision == ArenaReplayWaitDecision.Unsafe)
            {
                if (ArenaMatchResult.OwnsRaid(_match.Mode))
                {
                    // A terminal result must retain death interception until
                    // its native owner is verified. The generic start-abort path
                    // releases StandDown before checking that owner.
                    ArenaMatchResult.ReportProblem(_match.Mode,
                        "Killcam did not release safely: " + (problem ?? "bounded replay wait expired"));
                    ArenaMatchResult.RequestExit(_match.Mode);
                    yield break;
                }
                // The original dead native owner is still intact: use the
                // existing protected exit, never release under unknown camera
                // ownership or pretend StopPlayback cancels a pending replay.
                ArenaMatchService.ScheduleFailedStartAbort(
                    "Killcam did not release camera ownership safely: " + (problem ?? "replay wait exceeded its bound"));
                yield break;
            }
            if (decision == ArenaReplayWaitDecision.Wait)
            {
                SpectatorHealthEffects.ClearForDeathReplay(_game, _body);
                if (!_reportedWait)
                {
                    _reportedWait = true;
                    _waitStarted = Time.time;
                    Plugin.ModLogger.LogDebug("Arena killcam: holding death respawn until replay cleanup completes.");
                }
            }
            else if (_reportedWait && !_reportedRelease)
            {
                _reportedRelease = true;
                Plugin.ModLogger.LogDebug($"Arena killcam: camera released after {Time.time - _waitStarted:0.00}s.");
            }

            // Mode time keeps advancing during playback. No extra fixed delay
            // gets appended after a Tactical round, skip, or missing recording.
            if (decision == ArenaReplayWaitDecision.Ready && !_match.Mode.HoldRespawn)
            {
                CanRespawn = true;
                yield break;
            }
            yield return null;
        }
    }
}
