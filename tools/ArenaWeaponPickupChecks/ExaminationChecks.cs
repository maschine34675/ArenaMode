using ArenaMode.Kits;
using ArenaMode.Pickups;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

internal static partial class Program
{
    private static void VerifyExamination()
    {
        Reset();
        var weapon = KitBuilder.Next;
        weapon.ExaminedByDefault = false;
        var attachment = new Item { ExaminedByDefault = false };
        weapon.Contents.Add(attachment);
        var handle = Prepare();
        Check(!Inv.Examined(weapon) && !Inv.Examined(attachment), "unexamined source remains unknown before a valid collection attempt");
        Check(ItemManipulator.Move(weapon, Slot(EquipmentSlot.SecondPrimaryWeapon).CreateItemAddress(), Inv, true).Failed,
            "unexamined fixture reproduces native planning refusal");
        var ordinary = new Item { ExaminedByDefault = false, Id = attachment.Id };
        weapon.Contents.Add(ordinary); // An item added later is not part of the captured generated tree.
        bool? collected = null;
        Check(handle.TryTake(P, () => true, ok => collected = ok), "unknown generated weapon admits native transfer");
        Check(Inv.Examined(weapon) && Inv.Examined(attachment), "generated weapon and original attachments are known in the current body");
        Check(!Inv.Examined(ordinary), "same-id or later attached ordinary loot is not granted knowledge");
        Check(!new InventoryController().Examined(weapon), "other inventory receives no generated item knowledge");
        Check(Inv.BeginMove(), "examination grant does not bypass native source ownership");
        Inv.Finish(true);
        Check(collected == true, "unexamined pickup settles only after native success");
        handle.Dispose();
        Check(Inv.Examined(weapon) && Inv.Examined(attachment), "carried pickup remains usable after its scene handle retires");
        Check(!weapon.ExaminedByDefault && !attachment.ExaminedByDefault, "shared template flags are never changed");

        // Explicitly exercise both recorded and currently installed actor/controller identities.
        foreach (var scenario in new[] { "inactive", "round", "world", "body", "inventory", "dead", "ai", "live-gate" })
        {
            Reset(); weapon = KitBuilder.Next; weapon.ExaminedByDefault = false;
            handle = Prepare(); var inventory = Inv; var player = P;
            Check(handle.TryTake(player, () => true, _ => { }) && inventory.Examined(weapon), "grant precedes " + scenario);
            switch (scenario)
            {
                case "inactive": ArenaSession.IsActive = false; break;
                case "round": ArenaPickupRuntime.Generation++; break;
                case "world": Singleton<GameWorld>.Instance = new(); break;
                case "body": _world.MainPlayer = new(); break;
                case "inventory": player.InventoryController = new Player.SinglePlayerInventoryController(); break;
                case "dead": player.Live = false; break;
                case "ai": player.IsAI = true; break;
                case "live-gate": ArenaPickupRuntime.ScopeLive = false; break;
            }
            Check(!inventory.Examined(weapon), "examination rejects stale " + scenario);
            bool nativeTrue = true;
            ArenaPickupExamination.Apply(inventory, weapon, ref nativeTrue);
            Check(nativeTrue, "native known result preserved for " + scenario);
            // Settle the queued test double without mutating an expired source.
            inventory.Finish(false); handle.Dispose();
        }

        Reset(); bool unchanged = false;
        ArenaPickupExamination.Apply(null, null, ref unchanged);
        Check(!unchanged, "null inventory and item retain native result");
    }
}
