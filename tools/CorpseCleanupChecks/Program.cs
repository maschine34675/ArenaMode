using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}

(GameWorld World, Corpse Body, InventoryEquipment Gear) Setup()
{
    CorpseCleanupService.Reset();
    Settings.MaxCorpses.Value = 1;
    Settings.StripCorpseGear.Value = true;
    ArenaSession.IsActive = true;
    ArenaMode.Compat.PitFireTeamCompat.TeamProfile = null;
    ArenaMode.Loot.OpenLootTracker.Open = null;
    Plugin.ModLogger.Warnings.Clear();
    Time.time = 100;
    var world = new GameWorld();
    var body = new Corpse();
    var gear = new InventoryEquipment();
    body.Item = body.ItemOwner.RootItem = gear;
    body.gameObject.Components.Add(new Renderer());
    body.gameObject.Components.Add(new Player());
    world.LootList.Add(body);
    world.LootList.Add(new Corpse { PlayerProfileID = "retained" });
    Singleton<GameWorld>.Instance = world;
    return (world, body, gear);
}
Slot Add(Corpse corpse, InventoryEquipment gear, string id)
{
    var item = new Item { TemplateId = id };
    var slot = new Slot { ID = id, ContainedItem = item, ReactiveItem = item, Body = corpse.gameObject, Model = new Renderer() };
    item.Parent = slot;
    gear.Slots.Add(slot);
    corpse.gameObject.Components.Add(slot.Model);
    return slot;
}

// Observable reproduction of the old bug, independent of production source.
{
    var (_, body, gear) = Setup();
    var slot = Add(body, gear, "old-behavior");
    var item = slot.ContainedItem;
    var result = ItemManipulator.RemoveWithoutRestrictions(item, body.ItemOwner);
    Check(slot.ContainedItem == null && slot.ReactiveItem == item && slot.Returns == 0,
        "physical detachment alone cannot release the slot model");
    result.Value.RaiseEvents(body.ItemOwner, CommandStatus.Succeed);
    Check(body.ItemOwner.MissingBegin == 1 && slot.Returns == 1,
        "positive control reproduces missing Begin while completion still releases the view");
}

{
    var (world, body, gear) = Setup();
    var head = Add(body, gear, "Headwear");
    var backpack = Add(body, gear, "Backpack");
    var child = new Item();
    backpack.ContainedItem.Children.Add(child);
    var backpackItem = backpack.ContainedItem;
    var secure = Add(body, gear, "SecuredContainer");
    var held = Add(body, gear, "FirstPrimaryWeapon");
    body.ItemInHands.Value = held.ContainedItem;
    var pooledRenderer = head.Model;
    CorpseCleanupService.Tick();
    Check(body.ItemOwner.MissingBegin == 0, "successful cleanup has no missing Begin activity");
    Check(body.ItemOwner.ActiveEvents.Count == 0, "successful cleanup leaves no active activities");
    Check(body.ItemOwner.Stages.SequenceEqual(new[] { "detach", "Begin", "Succeed", "detach", "Begin", "Succeed" }),
        "each successful removal pairs Begin and Succeed after physical detachment");
    Check(head.Returns == 1 && backpack.Returns == 1 && head.ReactiveItem == null,
        "successful cleanup returns each removed model exactly once");
    Check(body.ItemOwner.Forgotten.Count == 2 && body.ItemOwner.ResizeNotifications == 4,
        "full native result preserves resize dispatch and search cleanup");
    Check(backpackItem.Children.Count == 1 && backpackItem.Children[0] == child,
        "nested items are not individually stripped");
    Check(secure.ContainedItem != null && held.ContainedItem != null && secure.Returns == 0 && held.Returns == 0,
        "secure container and in-hands weapon are excluded");
    Check(world.LootList.Contains(body) && body.gameObject.GetComponentsInChildren<Player>(true).Length == 1,
        "corpse and player remain registered");
    Time.time += 6;
    CorpseCleanupService.Tick();
    Check(head.Returns == 1 && body.ItemOwner.Attempts == 2 && !pooledRenderer.forceRenderingOff,
        "repeat sweeps neither remove twice nor hide the returned gear renderer");
}

{
    var (_, body, gear) = Setup();
    var busy = Add(body, gear, "Backpack");
    var free = Add(body, gear, "Headwear");
    var original = new RemoveItemEventArgs(busy.ContainedItem, CommandStatus.Begin);
    body.ItemOwner.ActiveEvents.Add(original);
    CorpseCleanupService.Tick();
    Check(busy.ContainedItem != null && busy.Returns == 0 && body.ItemOwner.ActiveEvents.SequenceEqual(new[] { original }),
        "pre-existing same-item activity is preserved without consuming its Begin");
    Check(free.Returns == 1 && body.ItemOwner.RemoveCalls == 1 && body.ItemOwner.MissingBegin == 0,
        "unrelated idle gear can still be cleaned");
}

foreach (var mode in new[] { "refused", "before", "after", "begin", "success" })
{
    var (_, body, gear) = Setup();
    var first = Add(body, gear, "Headwear");
    var item = first.ContainedItem;
    item.RejectRemoval = mode == "refused";
    item.ThrowBeforeRemoval = mode == "before";
    item.ThrowAfterRemoval = mode == "after";
    item.ThrowBeginNotification = mode == "begin";
    item.ThrowSuccessNotification = mode == "success";
    var next = Add(body, gear, "Backpack");
    CorpseCleanupService.Tick();
    Check(next.Returns == 1, "one failed item does not stop remaining gear: " + mode);
    Check(body.ItemOwner.ActiveEvents.Count == 0 && body.ItemOwner.MissingBegin == 0,
        "tested failure boundary leaves no unpaired activity: " + mode);
    Check(Plugin.ModLogger.Warnings.Count == 1, "failure is reported once: " + mode);
    if (mode is "refused" or "before")
        Check(first.ContainedItem == item && body.ItemOwner.Notifications.All(e => e.Item != item),
            "failed removal emits neither Begin nor completion");
    else if (mode == "begin")
        Check(first.Returns == 1 && body.ItemOwner.Notifications.Count(e => e.Item == item && e.Status == CommandStatus.Succeed) == 1,
            "Begin observer failure still completes the committed removal");
    else
        Check(first.ContainedItem == null && !Plugin.ModLogger.Warnings[0].Contains("stayed on"),
            "post-removal exception never falsely claims gear stayed attached");
}

// Integration exclusions exercise the actual public Tick, not a duplicate policy.
foreach (var excluded in new[] { "normal", "disabled", "budget-off", "ragdoll", "team", "own", "open", "interacting", "near", "no-world", "no-player" })
{
    var (world, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    switch (excluded)
    {
        case "normal": ArenaSession.IsActive = false; break;
        case "disabled": Settings.StripCorpseGear.Value = false; break;
        case "budget-off": Settings.MaxCorpses.Value = 0; break;
        case "ragdoll": body.Ragdoll._isPhysicsDone = false; break;
        case "team": ArenaMode.Compat.PitFireTeamCompat.TeamProfile = body.PlayerProfileID; break;
        case "own": body.PlayerProfileID = world.MainPlayer.ProfileId; break;
        case "open": ArenaMode.Loot.OpenLootTracker.Open = gear; break;
        case "interacting": world.MainPlayer.InteractableObject = body; break;
        case "near": body.transform.position = world.MainPlayer.Position; break;
        case "no-world": Singleton<GameWorld>.Instance = null; break;
        case "no-player": world.MainPlayer = null; break;
    }
    CorpseCleanupService.Tick();
    Check(slot.ContainedItem != null && slot.Returns == 0 && body.ItemOwner.Attempts == 0,
        "exclusion preserves all gear without invoking removal: " + excluded);
}

Console.WriteLine($"CORPSE_CLEANUP_CHECKS_PASSED ({assertions} assertions)");
