using ArenaMode.Areas;
using ArenaMode.Combat.Fire;
using ArenaMode.Shared;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.BigBrainCompat;

public sealed class ArenaFireEscapeLayer : CustomLayer
{
    private ArenaFireEscape _lease;
    private float _next;
    private Vector3 _last;
    private float _lastAt;
    private int _searchCursor;
    public ArenaFireEscapeLayer(BotOwner bot, int priority) : base(bot, priority) { }
    public override string GetName() => "Arena Combat Ground Fire Escape";
    public override bool IsActive()
    {
        try
        {
            if (_lease?.Current() == true) return true;
            if (_lease != null) { _lease.Released = true; _lease = null; }
            if (Time.time < _next || !ArenaFireEscape.Eligible(BotOwner)) return false;
            _next = Time.time + .15f;
            var now = BotOwner.Position;
            var delta = now - _last; float elapsed = Time.time - _lastAt;
            var predicted = now;
            if (elapsed > .05f && elapsed < .5f && delta.sqrMagnitude < 4f)
                predicted += Vector3.ClampMagnitude(delta / elapsed * .5f, 1.5f);
            _last = now; _lastAt = Time.time;
            if (!ArenaFireRuntime.Hot(now, .4f) && !ArenaFireRuntime.Hot(predicted, .4f)) return false;
            bool found = ArenaFireEscape.TryCreate(BotOwner, ref _searchCursor, out _lease);
            if (!found) _next = Time.time + .75f;
            return found;
        }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning("Arena fire escape skipped: " + ex.GetBaseException().Message); _next = Time.time + 5f; return false; }
    }
    public override Action GetNextAction() => new(typeof(ArenaFireEscapeLogic), "Leaving ground fire", new FireAction(_lease));
    public override bool IsCurrentActionEnding()
    { try { return !ReferenceEquals((CurrentAction?.Data as FireAction)?.Lease, _lease) || _lease?.Current() != true; } catch { return true; } }
    public override void Stop() { if (_lease != null) _lease.Released = true; _lease = null; base.Stop(); }
}

internal sealed class FireAction : CustomLayer.ActionData
{ internal readonly ArenaFireEscape Lease; internal FireAction(ArenaFireEscape lease) { Lease = lease; } }

internal sealed class ArenaFireEscape
{
    internal BotOwner Bot;
    internal Player Body;
    internal BotMover Mover;
    internal Vector3 Target;
    internal bool Released;
    private int _generation;
    private float _until;
    internal static bool Eligible(BotOwner bot) => Plugin.RuntimeEnabled && ArenaFireRuntime.HasFire && Time.timeScale > 0
        && bot != null && !bot.IsDead && bot.BotState == EBotState.Active && bot.Mover != null
        && bot.GetPlayer?.IsAI == true && ReferenceEquals(bot.GetPlayer.AIData?.BotOwner, bot)
        && ArenaFireRuntime.LiveActor(bot.GetPlayer)
        && bot.Mover.CurrentState != EBotMoverState.NearDoor && bot.DoorOpener?.Interacting != true
        && Movement.ArenaBotMovementSain.AllowsBoost(bot, bot.GetPlayer)
        && bot.BewareGrenade?.ShallRunAway() != true && bot.BewareBTR?.ShallRunAway() != true
        && bot.ArtilleryDangerPlace?.ShallRunAway() != true && bot.FlashGrenade?.IsFlashed != true;
    internal bool Current() => !Released && Time.time < _until && _generation == ArenaFireRuntime.Generation
        && Eligible(Bot) && ReferenceEquals(Bot.GetPlayer, Body) && ReferenceEquals(Bot.Mover, Mover)
        && !ArenaFireRuntime.Hot(Target, .4f) && (Bot.Position - Target).sqrMagnitude > .16f;
    internal static bool TryCreate(BotOwner bot, ref int cursor, out ArenaFireEscape lease)
    {
        lease = null;
        var start = bot.Position;
        var path = new NavMeshPath();
        int pathAttempts = 0;
        for (int attempt = 0; attempt < 48; attempt++)
        {
            int candidate = cursor++ % 48;
            if (cursor >= 48) cursor = 0;
            int ring = candidate / 16 + 1, i = candidate % 16;
            float angle = i * Mathf.PI / 8f;
            var point = start + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * 2f);
            if (!NavMesh.SamplePosition(point, out var nav, .6f, NavMesh.AllAreas) || Math.Abs(nav.position.y - start.y) > .5f
                || ArenaFireRuntime.Hot(nav.position, .6f)) continue;
            if (++pathAttempts > 8) return false;
            if (!NavMesh.CalculatePath(start, nav.position, NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete || !RouteAllowed(start, path.corners)) continue;
            lease = new ArenaFireEscape { Bot = bot, Body = bot.GetPlayer, Mover = bot.Mover, Target = nav.position,
                _generation = ArenaFireRuntime.Generation, _until = Time.time + 4f };
            Plugin.ModLogger?.LogInfo($"Arena fire escape: {bot.ProfileId}; {start} -> {nav.position}.");
            return true;
        }
        return false;
    }
    internal static bool RouteAllowed(Vector3 start, IReadOnlyList<Vector3> corners)
    {
        if (corners == null || corners.Count == 0) return false;
        var area = ArenaAreaRuntime.CurrentArea;
        if (area != null && !area.Bounds.ContainsPath(new ArenaAreaPoint(start.x, start.y, start.z), corners, true,
            p => new ArenaAreaPoint(p.x, p.y, p.z), .25f)) return false;
        bool left = !ArenaFireRuntime.Hot(start, .35f);
        float distance = 0;
        foreach (var end in corners)
        {
            // The prototype will not take ownership of a route through a closed
            // door. Native/SAIN door movement keeps its own action and callbacks.
            if (!ArenaFireGeometry.Clear(start + Vector3.up * .5f, end + Vector3.up * .5f)) return false;
            float length = (end - start).magnitude; distance += length;
            if (distance > 14f) return false;
            int steps = Math.Max(1, Mathf.CeilToInt(length / .25f));
            for (int s = 1; s <= steps; s++)
            {
                bool hot = ArenaFireRuntime.Hot(Vector3.Lerp(start, end, s / (float)steps), .35f);
                if (left && hot) return false;
                if (!hot) left = true;
            }
            start = end;
        }
        return left;
    }
}

public sealed class ArenaFireEscapeLogic : CustomLogic
{
    private ArenaFireEscape _lease;
    private BotMoverState _state;
    private AbstractBotPath _path;
    private bool _owns;
    private float _checkAt, _progressAt;
    private Vector3 _progressPosition;
    private readonly EscapeMove _move;
    public ArenaFireEscapeLogic(BotOwner bot) : base(bot) { _move = new EscapeMove(bot); }
    public override void Update(CustomLayer.ActionData data)
    {
        try
        {
            var lease = (data as FireAction)?.Lease;
            if (lease?.Current() != true) { StopOwned(); return; }
            if (!ReferenceEquals(lease, _lease)) { StopOwned(); _lease = lease; }
            var handoff = ArenaSainMovementHandoff.EnsureSuspended(BotOwner, out _);
            if (handoff == ArenaSainHandoffState.Unavailable) { StopOwned(); return; }
            if (handoff != ArenaSainHandoffState.Ready) return;
            if (!lease.Current()) { StopOwned(); return; }
            if (!_owns)
            {
                BotOwner.Mover.Stop(); BotOwner.MovementResume();
                var result = BotOwner.GoToPoint(lease.Target, slowAtTheEnd: false, reachDist: .35f, getUpWithCheck: false, mustHaveWay: false);
                if (result != NavMeshPathStatus.PathComplete || BotOwner.Mover.TargetPoint is not Vector3 target
                    || (target - lease.Target).sqrMagnitude > .1225f
                    || !ArenaFireEscape.RouteAllowed(BotOwner.Position, BotOwner.Mover.GetCornersFromIndex(0)))
                { BotOwner.Mover.Stop(); StopOwned(); return; }
                _state = BotOwner.Mover._moverStateMachine.DefaultMoverState; _path = _state.PathController.CurPath; _owns = true;
                _checkAt = 0; _progressAt = Time.time; _progressPosition = BotOwner.Position;
            }
            // Avoid retaining a default route across a native door ownership change.
            if (BotOwner.Mover.CurrentState != EBotMoverState.Default || BotOwner.DoorOpener?.Interacting == true)
            { StopOwned(); return; }
            if (!ReferenceEquals(_state.PathController.CurPath, _path) || _path == null) { StopOwned(); return; }
            if (Time.time >= _checkAt)
            {
                _checkAt = Time.time + .2f;
                if (!ArenaFireEscape.RouteAllowed(BotOwner.Position, BotOwner.Mover.GetCornersFromIndex(_path.CurIndex)))
                { StopOwned(); return; }
                if ((BotOwner.Position - _progressPosition).sqrMagnitude >= .04f)
                { _progressPosition = BotOwner.Position; _progressAt = Time.time; }
                if (Time.time - _progressAt > 1f) { StopOwned(); return; }
            }
            _move.UpdateNodeByBrain(null);
        }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning("Arena fire escape interrupted: " + ex.GetBaseException().Message); StopOwned(); }
    }
    private void StopOwned()
    {
        if (_owns && _lease != null && ReferenceEquals(BotOwner.GetPlayer, _lease.Body) && ReferenceEquals(BotOwner.Mover, _lease.Mover))
        {
            try
            {
                if (_state != null && (ReferenceEquals(_state.PathController.CurPath, _path) || _state.PathController.CurPath == null))
                {
                    _state.PathController.Stop();
                    if (_state.LastTargetPoint is Vector3 target && (target - _lease.Target).sqrMagnitude <= .1225f) _state.LastTargetPoint = null;
                    if (BotOwner.Mover.CurrentState == EBotMoverState.Default) { BotOwner.Mover.Stop(); BotOwner.Sprint(false); }
                }
            }
            catch { /* Native body disposal must not interrupt its owner. */ }
        }
        if (_lease != null) _lease.Released = true;
        _lease = null; _path = null; _state = null; _owns = false;
    }
    public override void Stop() { StopOwned(); base.Stop(); }
    private sealed class EscapeMove : RunToEnemy
    {
        internal EscapeMove(BotOwner bot) : base(bot) { }
        public override void UpdateNodeByBrain(CoreActionResultParams data)
        { _owner.SetPose(1f); _owner.SetTargetMoveSpeed(1f); _owner.Steering.LookToMovingDirection(); _owner.Sprint(_owner.Mover.DistDestination > 1.5f); }
    }
}
