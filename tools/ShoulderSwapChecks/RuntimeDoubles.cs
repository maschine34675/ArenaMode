using System.Runtime.CompilerServices;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    {
        public string GUID { get; }
        public Version Version { get; }
        public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); }
    }
    public class BaseUnityPlugin { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace ArenaMode { public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
namespace Redeploy.Core.Compat { public static class FikaCompatCheck { public static bool IsFikaLoaded; } }
namespace EFT
{
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public Player LocalPlayer; }
    public class GameWorld { public Player MainPlayer; }
    public class Health { public bool IsAlive = true; }
    public class Player
    {
        public class FirearmController { }
        public bool IsYourPlayer = true, IsAI;
        public Health HealthController = new();
        public object MovementContext = new(), CameraPosition = new(), HandsController = new FirearmController();
        public Animations.ProceduralWeaponAnimation ProceduralWeaponAnimation = new();
        public int NativeLeans;
        public void ToggleLean(float dir) { NativeLeans++; hazelify.StanceSync.Patches.OnLeanPatchPostfix.Invoke(this, dir); }
    }
}
namespace EFT.Animations
{
    public class ProceduralWeaponAnimation
    {
        public int AimIndex;
        public List<object> ScopeAimTransforms = new() { new object() };
    }
}
namespace EFT.CameraControl
{
    public class PlayerCameraController
    {
        public EFT.Player Player;
        public int NativeUpdates;
        public void LateUpdate() { NativeUpdates++; hazelify.StanceSync.Patches.PlayerCameraControllerPatchPrefix.Prefix(this); }
    }
}
namespace hazelify.StanceSync
{
    [BepInEx.BepInPlugin("hazelify.StanceSync", "hazelify.StanceSync", "1.1.1")]
    public class Plugin : BepInEx.BaseUnityPlugin { }
}
namespace hazelify.StanceSync.Patches
{
    public static class ForeignCalls
    {
        public static int Lean, Camera;
        public static void RequireReady(EFT.Player player)
        {
            if (!(player.HandsController is EFT.Player.FirearmController)) throw new InvalidOperationException("non-firearm hands");
            // Deliberately expose the reviewed foreign callback's unchecked empty-list access.
            var scopes = player.ProceduralWeaponAnimation.ScopeAimTransforms;
            if (scopes[player.ProceduralWeaponAnimation.AimIndex % Math.Max(1, scopes.Count)] == null)
                throw new NullReferenceException("missing sight");
        }
    }
    public class OnLeanPatchPostfix
    {
        public static void Invoke(EFT.Player player, float dir) => Postfix(player, dir);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Postfix(EFT.Player __instance, float dir) { ForeignCalls.Lean++; ForeignCalls.RequireReady(__instance); }
    }
    public class PlayerCameraControllerPatchPrefix
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Prefix(EFT.CameraControl.PlayerCameraController __instance)
        { ForeignCalls.Camera++; ForeignCalls.RequireReady(Comfort.Common.Singleton<EFT.GameWorld>.Instance.MainPlayer); }
    }
}
