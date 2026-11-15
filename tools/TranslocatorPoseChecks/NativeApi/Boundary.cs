namespace ArenaMode.Combat.Translocator
{
 internal static class ArenaTranslocatorRuntime { internal static bool IsLivePlayer(EFT.Player player)=>false; }
 internal static class ArenaTranslocatorSupply
 { internal static bool HasVerifiedTemplate=>false; internal static bool Owns(EFT.Player player,EFT.InventoryLogic.Item item)=>false; }
}
namespace ArenaMode.Combat { internal static class WeaponSwapService { internal static bool IsSwapping=>false; } }
namespace ArenaMode
{
 internal static class Plugin { internal const string PluginGuid="pose-api-check";internal static readonly Log ModLogger=new(); }
 internal sealed class Log { internal void LogWarning(string value) { } }
}
