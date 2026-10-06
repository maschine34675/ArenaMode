namespace EFT
{
    // Minimal data boundary only; production classifier and scoreboard are linked.
    // ProfileSettings and faction/role enums compile from the local native source.
    public interface IPlayer
    {
        Profile Profile { get; }
        bool IsAI { get; }
    }

    public sealed class TestPlayer : IPlayer
    {
        public Profile Profile { get; set; }
        public bool IsAI { get; set; }
        public bool IsYourPlayer { get; set; }
    }

    public sealed class Profile
    {
        public ProfileInfo Info;
    }

    public sealed class ProfileInfo
    {
        public string Nickname;
        public EPlayerSide Side { get; set; }
        public ProfileSettings Settings { get; set; }
    }
}

namespace EFT.BinarySerialization
{
    public sealed class GenerateBinarySerializationAttribute : Attribute { }
}

// RoleClassifier.Label calls the native WildSpawnTypeExtension.IsInfected
// (global namespace, $(NativeSourceRoot)/WildSpawnTypeExtension.cs:540). That
// native file cannot be linked like the enum contracts above: it drags in
// WildSpawnTypeSettings, EnumExtensions -> EnumHelper<T> (Unity, JetBrains
// annotations, EFT localization) and UnityEngine.Debug. This stand-in mirrors
// the decompiled predicate verbatim, so the ZOMBIE label is checked against
// the same five infected roles the game uses.
public static class WildSpawnTypeExtension
{
    public static bool IsInfected(this EFT.WildSpawnType role)
    {
        if (role != EFT.WildSpawnType.infectedCivil && role != EFT.WildSpawnType.infectedLaborant
            && role != EFT.WildSpawnType.infectedAssault && role != EFT.WildSpawnType.infectedPmc)
        {
            return role == EFT.WildSpawnType.infectedTagilla;
        }
        return true;
    }
}
