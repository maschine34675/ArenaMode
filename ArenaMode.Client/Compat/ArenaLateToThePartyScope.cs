namespace ArenaMode.Compat;

/// <summary>Identity of the native local world; item authority belongs to its factory handle.</summary>
internal static class ArenaLateToThePartyScope
{
    internal static bool IsExactWorld(object game, object botGame, object world, object gameWorld)
        => game != null && world != null && ReferenceEquals(game, botGame)
            && ReferenceEquals(world, gameWorld);
}
