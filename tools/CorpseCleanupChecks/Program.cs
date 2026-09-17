using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
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
    Settings.ReleaseTrimmedCorpses.Value = true;
    ArenaSession.IsActive = true;
    ArenaMode.Compat.PitFireTeamCompat.TeamProfile = null;
    ArenaMode.Loot.OpenLootTracker.Open = null;
    Plugin.ModLogger.Warnings.Clear();
    Plugin.ModLogger.Infos.Clear();
    CorpseWeaponRegistry.Forgotten.Clear();
    CorpseLightService.Forgotten.Clear();
    CorpseTriggerZones.Reset();
    CorpseAggressors.Reset();
    Time.time = 100;
    var world = new GameWorld();
    var body = new Corpse();
    var gear = new InventoryEquipment();
    body.Item = body.ItemOwner.RootItem = gear;
    body.gameObject.Components.Add(new Renderer());
    body.gameObject.Components.Add(new AssetPoolObject());

    // A corpse's owner: the Player on the very GameObject the Corpse component
    // was attached to (Corpse.CreateCorpse), and dead.
    var owner = new Player { ProfileId = body.PlayerProfileID, gameObject = body.gameObject };
    owner.HealthController.IsAlive = false;
    body.gameObject.Components.Add(owner);

    world.LootList.Add(body);
    world.LootList.Add(new Corpse { PlayerProfileID = "retained" });
    Singleton<GameWorld>.Instance = world;
    Singleton<IBotGame>.Instance = new IBotGame
    {
        BotsController = new BotsController { BotSpawner = new BotSpawner { _deadBodiesController = new DeadBodiesController() } },
    };
    Singleton<ObjectsFactory>.Instance = null;
    return (world, body, gear);
}
Player Owner(Corpse body) => body.gameObject.Components.OfType<Player>().Single();
BotsController Bots() => Singleton<IBotGame>.Instance.BotsController;
DeadBodiesController DeadBodies() => Bots().BotSpawner._deadBodiesController;
AssetPoolObject Pool(Corpse body) => body.gameObject.Components.OfType<AssetPoolObject>().Single();
void Sweep()
{
    Time.time += 6;
    CorpseCleanupService.Tick();
}
Corpse AddBody(GameWorld world, string profileId)
{
    var extra = new Corpse { PlayerProfileID = profileId };
    extra.Item = extra.ItemOwner.RootItem = new InventoryEquipment();
    extra.gameObject.Components.Add(new AssetPoolObject());
    var owner = new Player { ProfileId = profileId, gameObject = extra.gameObject };
    owner.HealthController.IsAlive = false;
    extra.gameObject.Components.Add(owner);
    world.LootList.Add(extra);
    return extra;
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
    Check(secure.ContainedItem != null && held.ContainedItem != null,
        "the release does not strip what the trim deliberately left on the body");
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

// A round start clears the field. Each case is its own positive control: the
// ordinary sweep must keep the body first, or the clear proves nothing.
foreach (var kept in new[] { "team", "own", "near" })
{
    var (world, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    switch (kept)
    {
        case "team": ArenaMode.Compat.PitFireTeamCompat.TeamProfile = body.PlayerProfileID; break;
        case "own": body.PlayerProfileID = world.MainPlayer.ProfileId; break;
        case "near": body.transform.position = world.MainPlayer.Position; break;
    }
    CorpseCleanupService.Tick();
    Check(slot.Returns == 0 && body.ItemOwner.Attempts == 0,
        "positive control: the live-round budget keeps this body: " + kept);
    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Check(slot.ContainedItem == null && slot.Returns == 1,
        "a round-start clear removes the body the live round kept: " + kept);
}

// The request is a boundary, not a new policy: it expires on its own.
{
    var (_, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    ArenaMode.Compat.PitFireTeamCompat.TeamProfile = body.PlayerProfileID;
    CorpseCleanupService.ClearAllSoon("round start");
    Time.time += 25;
    CorpseCleanupService.Tick();
    Check(slot.Returns == 0 && body.ItemOwner.Attempts == 0,
        "an expired round-start clear protects the companion body again");
}

// A player who turned the budget off keeps every corpse, round start or not.
{
    var (_, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    Settings.MaxCorpses.Value = 0;
    CorpseCleanupService.ClearAllSoon("round start");
    Time.time += 1;
    CorpseCleanupService.Tick();
    Check(slot.Returns == 0 && body.ItemOwner.Attempts == 0,
        "a round-start clear does not override MaxCorpses = 0");
}

// Native interaction outranks the clear: this body has a window open on it.
foreach (var guard in new[] { "open", "interacting" })
{
    var (world, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    if (guard == "open") ArenaMode.Loot.OpenLootTracker.Open = gear;
    else world.MainPlayer.InteractableObject = body;
    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Check(slot.ContainedItem != null && slot.Returns == 0 && body.ItemOwner.Attempts == 0,
        "a round-start clear never takes a body from under a native interaction: " + guard);
}

// ---------------------------------------------------------------------------
// Release: the hidden body actually leaves the scene. Everything below states
// its own positive control first, because "the body is still there" is also
// what a release that silently never runs would produce.
// ---------------------------------------------------------------------------

{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    var owner = Owner(body);
    var pool = Pool(body);

    CorpseCleanupService.Tick();
    Check(world.LootList.Contains(body) && !body.Destroyed && owner.Disposals == 0,
        "the sweep that hides a body never also destroys it");
    Check(CorpseCleanupService.IsSuccessfullyTrimmed(body),
        "positive control: the body is trimmed and eligible");

    Sweep();
    Check(Bots().Retired.SequenceEqual(new[] { owner }) && owner.Disposals == 1,
        "release retires the AI's knowledge of the person and disposes the body exactly once");
    Check(DeadBodies().Bodies.Count == 0,
        "the dead-body record DestroyInfo re-adds for a dead person is cleared afterwards");
    Check(!world.LootList.Contains(body) && world.Removed.SequenceEqual(new[] { body }),
        "release takes the body out of the world's loot registry");
    Check(!pool.PushedBackIntoPool && pool.Destroyed,
        "a released body is destroyed, never pushed back into the shared player pool");
    Check(body.PlayerBodyDisposals == 1 && body.Destroyed,
        "the body's PlayerBody is disposed and the object leaves the scene");
    Check(CorpseWeaponRegistry.Forgotten.Contains(body) && CorpseLightService.Forgotten.Contains(body),
        "the weapon and light registries stop pinning the destroyed body");
    Check(CorpseTriggerZones.Retired.SequenceEqual(new[] { owner }),
        "the trigger-zone coroutine that reads this body is retired before the body goes");
    Check(!CorpseCleanupService.IsSuccessfullyTrimmed(body) && Plugin.ModLogger.Warnings.Count == 0,
        "a released body leaves no trim record and no warning");
    Check(ArenaMode.Diagnostics.CorpseReleaseLedger.Recorded.SequenceEqual(new[] { "opponent" }),
        "a confirmed release is recorded for the damage diagnostic, under the identity the body had");

    Sweep();
    Check(owner.Disposals == 1 && world.Removed.Count == 1,
        "later sweeps do not act on a body that is already gone");
}

// The ordering trap, isolated: with no dead-body controller to re-add into, the
// record removal has nothing to do — so the assertion above about Bodies being
// empty has to come from the removal running AFTER DestroyInfo, not from the
// re-add never happening.
{
    var (_, body, gear) = Setup();
    Add(body, gear, "Headwear");
    var owner = Owner(body);
    CorpseCleanupService.Tick();
    DeadBodies().Bodies.Clear();
    Bots().ThrowOnDestroyInfo = false;
    Sweep();
    Check(Bots().Retired.Contains(owner) && DeadBodies().Bodies.Count == 0,
        "positive control: DestroyInfo ran and its own re-added record is gone");
}

// Never released, and each case proves the body was still eligible for hiding.
foreach (var kept in new[] { "own", "team", "off", "same-sweep" })
{
    var (world, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    switch (kept)
    {
        case "own": body.PlayerProfileID = world.MainPlayer.ProfileId; break;
        case "team": ArenaMode.Compat.PitFireTeamCompat.TeamProfile = body.PlayerProfileID; break;
        case "off": Settings.ReleaseTrimmedCorpses.Value = false; break;
    }

    // A round-start clear lifts the own/teammate exemptions for HIDING. It must
    // not lift them for destroying, so both are trimmed here on purpose.
    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Check(slot.ContainedItem == null && CorpseCleanupService.IsSuccessfullyTrimmed(body),
        "positive control: a round-start clear hides even this body: " + kept);

    if (kept != "same-sweep") Sweep();
    Check(world.LootList.Contains(body) && !body.Destroyed && Owner(body).Disposals == 0
            && !Pool(body).PushedBackIntoPool,
        "this body is hidden but never taken out of the scene: " + kept);
}

// Conditions that can change in the seconds between hiding and releasing are
// re-read, and each of them only DEFERS the release.
foreach (var blocked in new[] { "interacting", "open", "ragdoll", "alive", "no-owner", "aggressor", "aggressor-unreadable" })
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    CorpseCleanupService.Tick();
    var owner = Owner(body);
    switch (blocked)
    {
        case "interacting": world.MainPlayer.InteractableObject = body; break;
        case "open": ArenaMode.Loot.OpenLootTracker.Open = gear; break;
        case "ragdoll": body.Ragdoll._isPhysicsDone = false; break;
        case "alive": owner.HealthController.IsAlive = true; break;
        case "no-owner": body.gameObject.Components.Remove(owner); break;
        case "aggressor": CorpseAggressors.Pending.Add(owner); break;
        case "aggressor-unreadable": CorpseAggressors.FailToRead = true; break;
    }
    Sweep();
    Check(world.LootList.Contains(body) && !body.Destroyed,
        "a body that stopped being releasable stays: " + blocked);

    switch (blocked)
    {
        case "interacting": world.MainPlayer.InteractableObject = null; break;
        case "open": ArenaMode.Loot.OpenLootTracker.Open = null; break;
        case "ragdoll": body.Ragdoll._isPhysicsDone = true; break;
        case "alive": owner.HealthController.IsAlive = false; break;
        case "no-owner": body.gameObject.Components.Add(owner); break;
        case "aggressor": CorpseAggressors.Pending.Remove(owner); break;
        case "aggressor-unreadable": CorpseAggressors.FailToRead = false; break;
    }
    Sweep();
    Check(!world.LootList.Contains(body) && body.Destroyed,
        "and is released once the condition is gone again: " + blocked);
}

// A body trimmed in another GameWorld is never touched with this world's owners.
{
    var (_, body, gear) = Setup();
    Add(body, gear, "Headwear");
    CorpseCleanupService.Tick();
    var replacement = new GameWorld();
    replacement.LootList.Add(body);
    Singleton<GameWorld>.Instance = replacement;
    Sweep();
    Check(replacement.LootList.Contains(body) && !body.Destroyed && Owner(body).Disposals == 0,
        "a trim record from another world never releases a body");
}

// Every step that can throw: the body stays hidden and disposed rather than
// half-destroyed, it is reported once, and it is never tried again.
foreach (var fault in new[] { "retire", "dispose", "dead-body", "destroy" })
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    CorpseCleanupService.Tick();
    var owner = Owner(body);
    switch (fault)
    {
        case "retire": Bots().ThrowOnDestroyInfo = true; break;
        case "dispose": owner.ThrowOnDispose = true; break;
        case "dead-body": DeadBodies().ThrowOnRemove = true; break;
        case "destroy": world.ThrowOnDestroyLoot = true; break;
    }
    Sweep();
    Check(world.LootList.Contains(body) && !body.Destroyed && !Pool(body).PushedBackIntoPool,
        "a failed release leaves the body in the world, not half-way out: " + fault);
    Check(Plugin.ModLogger.Warnings.Count == 1 && CorpseCleanupService.IsSuccessfullyTrimmed(body),
        "a failed release is reported once and keeps the record that maintains the hidden state: " + fault);
    Check(ArenaMode.Diagnostics.CorpseReleaseLedger.Recorded.Count == 0,
        "a failed release records nothing: the damage diagnostic must never name a body that is still there: " + fault);

    switch (fault)
    {
        case "retire": Bots().ThrowOnDestroyInfo = false; break;
        case "dispose": owner.ThrowOnDispose = false; break;
        case "dead-body": DeadBodies().ThrowOnRemove = false; break;
        case "destroy": world.ThrowOnDestroyLoot = false; break;
    }
    Sweep();
    Check(world.LootList.Contains(body) && !body.Destroyed && Plugin.ModLogger.Warnings.Count == 1,
        "a failed release is never retried, even once the fault is gone: " + fault);
}

// The sweep line is the measurement instrument for this change, so its numbers
// are asserted rather than assumed. None of them searches the scene.
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    Singleton<ObjectsFactory>.Instance = new ObjectsFactory();
    Singleton<ObjectsFactory>.Instance.RaidPools.PlayerAssetPool.TotalCreatedObjectsCount = 121;
    Singleton<ObjectsFactory>.Instance.RaidPools.PlayerAssetPool.CurrentPoolSize = 3;
    CorpseCleanupService.Tick();
    Sweep();
    var line = Plugin.ModLogger.Infos.Last();
    Check(line.Contains("released 1") && line.Contains("1 trimmed and 1 released in total"),
        "the sweep reports the release totals apart from the shrinking trim table: " + line);
    Check(line.Contains("1 corpse(s) in the world") && line.Contains("player bodies 121 created / 3 pooled"),
        "the sweep reports the body population from lists and pool counters: " + line);
    Check(!line.Contains("release failure"), "no failures are claimed when there were none: " + line);

    Singleton<ObjectsFactory>.Instance = null;
    var (other, second, secondGear) = Setup();
    Add(second, secondGear, "Headwear");
    other.ThrowOnDestroyLoot = true;
    CorpseCleanupService.Tick();
    Sweep();
    other.ThrowOnDestroyLoot = false;
    var third = new Corpse { PlayerProfileID = "another" };
    third.Item = third.ItemOwner.RootItem = new InventoryEquipment();
    third.gameObject.Components.Add(new AssetPoolObject());
    var thirdOwner = new Player { ProfileId = third.PlayerProfileID, gameObject = third.gameObject };
    thirdOwner.HealthController.IsAlive = false;
    third.gameObject.Components.Add(thirdOwner);
    other.LootList.Add(third);
    Sweep();
    Sweep();
    Check(Plugin.ModLogger.Infos.Last().Contains("1 release failure(s)"),
        "a release failure is carried in the line: " + Plugin.ModLogger.Infos.Last());
}

// A sweep's release work is capped. Clearing the AI's knowledge walks every
// group and destroying a body tears down a player hierarchy, so a backlog —
// which a round-start clear produces in one go — has to drain over several
// cadences instead of becoming the frame spike it was meant to remove.
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    var extras = Enumerable.Range(1, 10).Select(i => AddBody(world, "opponent-" + i)).ToList();

    CorpseCleanupService.Tick();
    Check(world.Removed.Count == 0 && extras.Count(e => CorpseCleanupService.IsSuccessfullyTrimmed(e)) == 9,
        "positive control: eleven of the twelve bodies are hidden and none released yet");

    Sweep();
    Check(world.Removed.Count == 8, "one sweep never releases more than its budget: " + world.Removed.Count);

    Sweep();
    Check(world.Removed.Count == 10, "the rest drain on the following sweep: " + world.Removed.Count);

    Sweep();
    Check(world.Removed.Count == 10, "a body with no owner to dispose is never released");
}

Console.WriteLine($"CORPSE_CLEANUP_CHECKS_PASSED ({assertions} assertions)");
