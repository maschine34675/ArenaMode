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
