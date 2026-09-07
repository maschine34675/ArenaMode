using ArenaMode.Session;
using EFT;
using Redeploy.Core.Spawning;
using System;
using UnityEngine;

namespace ArenaMode.Areas;

/// <summary>
/// Resolve BEFORE Redeploy deregisters the old body or mutates its profile.
/// Its own Resolve occurs later, after that irreversible local handover began.
/// </summary>
internal static class ArenaAreaRespawn
{
    internal static bool TryPrepare(LocalGame game, ISpawnSelector original, out ISpawnSelector prepared)
    {
        prepared = original;
        var area = ArenaAreaRuntime.CurrentArea;
        if (area == null) return true;
        try
        {
            var point = original.Resolve(game);
            ArenaAreaRuntime.RequirePosition(point.position);
            prepared = new Prepared(game, game.GameWorld, area.Id, point.position, point.rotation);
            return true;
        }
        catch (Exception ex)
        {
            ArenaAreaRuntime.Abort("respawn preflight failed before body handover: " + ex.GetBaseException().Message);
            return false;
        }
    }

    private sealed class Prepared : ISpawnSelector
    {
        private readonly LocalGame _game;
        private readonly GameWorld _world;
        private readonly string _areaId;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        internal Prepared(LocalGame game, GameWorld world, string areaId, Vector3 position, Quaternion rotation)
        { _game = game; _world = world; _areaId = areaId; _position = position; _rotation = rotation; }
        public (Vector3 position, Quaternion rotation) Resolve(LocalGame game)
        {
            if (!ReferenceEquals(game, _game) || !ArenaSession.IsExactActiveRaid(_game, _world)
                || ArenaAreaRuntime.CurrentArea?.Id != _areaId)
                throw new InvalidOperationException("The prepared Arena-area respawn lost its exact raid.");
            ArenaAreaRuntime.RequirePosition(_position);
            return (_position, _rotation);
        }
    }
}
