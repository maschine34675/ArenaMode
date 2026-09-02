using ArenaMode.Bots;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;

namespace ArenaMode.BigBrainCompat;

/// <summary>
/// Owns BigBrain's action slot only while Arena has published an exact usable
/// lease. Priority 74 supersedes PitFireTeam's 71-73 and SAIN's ordinary
/// combat layers while leaving higher-priority danger reactions untouched.
/// </summary>
public sealed class ArenaPursuitLayer : CustomLayer
{
    private readonly string _sourceProfileId;
    private int _actionEpoch = -1;
    private long _actionGeneration;
    private bool _sourceRegistered;

    public ArenaPursuitLayer(BotOwner botOwner, int priority)
        : base(botOwner, priority)
    {
        _sourceProfileId = botOwner?.ProfileId?.Trim();
    }

    public override string GetName() => "ArenaPursuit";

    public override bool IsActive()
    {
        if (!ArenaPursuitRuntime.HasExactTeamArenaContext())
        {
            ReleaseSourceRegistration();
            return false;
        }

        EnsureSourceRegistered();
        return Plugin.RuntimeEnabled
               && _sourceRegistered
               && ArenaPursuitRuntime.TryGetUsableLease(BotOwner, out _);
    }

    public override Action GetNextAction()
    {
        EnsureSourceRegistered();
        if (!Plugin.RuntimeEnabled
            || !_sourceRegistered
            || !ArenaPursuitRuntime.TryGetUsableLease(BotOwner, out TeamArenaPursuitLease lease))
        {
            _actionEpoch = -1;
            _actionGeneration = 0;
            return new Action(
                typeof(ArenaPursuitLogic),
                "Arena pursuit lease vanished",
                new ArenaPursuitActionData(-1, 0));
        }

        _actionEpoch = lease.Epoch;
        _actionGeneration = lease.Generation;
        return new Action(
            typeof(ArenaPursuitLogic),
            $"Arena pursuit {lease.Epoch}/{lease.Generation}",
            new ArenaPursuitActionData(lease.Epoch, lease.Generation));
    }

    public override bool IsCurrentActionEnding()
    {
        if (!Plugin.RuntimeEnabled || _actionEpoch < 0 || _actionGeneration <= 0)
            return true;

        return !ArenaPursuitRuntime.TryGetUsableLease(
            BotOwner,
            _actionEpoch,
            _actionGeneration,
            out _);
    }

    public override void Stop()
    {
        _actionEpoch = -1;
        _actionGeneration = 0;
        base.Stop();
    }

    private void OnPlayerDeadOrUnspawn(IPlayer _)
        => ReleaseSourceRegistration();

    private void ReleaseSourceRegistration()
    {
        if (!_sourceRegistered)
            return;

        if (BotOwner?.GetPlayer != null)
            BotOwner.GetPlayer.OnPlayerDeadOrUnspawn -= OnPlayerDeadOrUnspawn;
        TeamArenaPursuitLeaseBridge.UnregisterSource(
            Plugin.PluginGuid,
            _sourceProfileId);
        _sourceRegistered = false;
    }

    private void EnsureSourceRegistered()
    {
        if (_sourceRegistered
            || !Plugin.RuntimeEnabled
            || !ArenaPursuitRuntime.HasExactTeamArenaContext())
            return;

        _sourceRegistered = TeamArenaPursuitLeaseBridge.RegisterSource(
            Plugin.PluginGuid,
            _sourceProfileId);
        if (_sourceRegistered && BotOwner?.GetPlayer != null)
            BotOwner.GetPlayer.OnPlayerDeadOrUnspawn += OnPlayerDeadOrUnspawn;
    }
}

internal sealed class ArenaPursuitActionData : CustomLayer.ActionData
{
    internal ArenaPursuitActionData(int epoch, long generation)
    {
        Epoch = epoch;
        Generation = generation;
    }

    internal int Epoch { get; }
    internal long Generation { get; }
}
