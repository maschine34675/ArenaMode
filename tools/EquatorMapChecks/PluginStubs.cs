// Only the external BepInEx registry is doubled. Admission uses production code.
namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static readonly System.Collections.Generic.Dictionary<string, object> PluginInfos = new();
    }
}
