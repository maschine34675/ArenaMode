using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.Ballistics;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.SynchronizableObjects;
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
    ArenaMode.Combat.Fire.ArenaFireRuntime.HeldBody = null;
    Settings.MaxCorpses.Value = 1;
    Settings.StripCorpseGear.Value = true;
    Settings.ReleaseTrimmedCorpses.Value = true;
    ArenaSession.IsActive = true;
    ArenaSession.ActiveMode = null;
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

// Zombie's own zero-distance exemption (already shipped, d00cd17): the "near"
// case above holds a body standing at the player's position in every other
// mode, but ActiveMinDistanceMeters() returns 0 instead of 15 while
// ArenaSession.ActiveMode is Zombie, so the same body is trimmed there.
{
    var (world, body, gear) = Setup();
    var slot = Add(body, gear, "Headwear");
    ArenaSession.ActiveMode = "zombie";
    body.transform.position = world.MainPlayer.Position;
    CorpseCleanupService.Tick();
    Check(slot.ContainedItem == null && slot.Returns == 1,
        "Zombie mode's zero-distance exemption trims a body standing at the player's position");
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
// "own" here is the SOLE own corpse in this Setup(), so it is trivially the
// newest (IsNewestOwnCorpse) - that is what keeps it exempt since 2026-09-18,
// not a blanket own exclusion any more. An OLDER own corpse's different fate
// is covered separately below, in "Own corpses release too".
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

    // "own" needs a second sweep to be a meaningful mutation target: the
    // sole/newest own corpse in this Setup() is normally never even asked
    // DamageSourcesQuiet's own-corpse question (IsNewestOwnCorpse already
    // says yes), but a mutant that removes that exemption would still only
    // START its quiet period on the first sweep (see the "Own corpses
    // release too" tests) rather than release it outright - one sweep alone
    // cannot tell "correctly exempt" apart from "wrongly exempt, but still
    // mid-quiet-period". A second sweep, past DamageSourceQuietSeconds,
    // can: correct code stays kept regardless of sweep count, a broken
    // exemption releases by then.
    if (kept == "own") Sweep();

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

// ---------------------------------------------------------------------------
// Damage sources. Every ApplyDamageInfo/ShotDelegate exception the 2026-09-17
// raids logged was a tripwire a dead Labyrinth boss had planted, going off
// after Arena had released him (CORPSE-CLEANUP-REVIEW.md, "Settled"). The
// tripwire names its planter by profile id for its whole life, so the body
// stays until nothing names it any more — and then for a quiet period, because
// the last hit may still sit in BotMemory.GetHit's timer.
// ---------------------------------------------------------------------------

TripwireSynchronizableObject Tripwire(GameWorld world, string placer, ETripwireState state, bool isStatic = false)
{
    var tripwire = new TripwireSynchronizableObject { PlacerPlayerId = placer, TripwireState = state };
    var processor = world.SynchronizableObjectLogicProcessor;
    (isStatic ? processor._staticSynchronizableObjects : processor._activeSynchronizableObjects).Add(tripwire);
    return tripwire;
}
List<Shot> Shots(GameWorld world) => ((BallisticsCalculator)world.SharedBallisticsCalculator).Shots;
bool Kept(GameWorld world, Corpse body) => world.LootList.Contains(body) && !body.Destroyed;
bool Released(GameWorld world, Corpse body) => !world.LootList.Contains(body) && body.Destroyed;

// The measured sequence, one state at a time: planted and armed, triggered with
// the grenade burning (TripwireManager has already dropped it by then), the
// fragments in the air, and a state this code has never heard of.
foreach (var naming in new[] { ETripwireState.Wait, ETripwireState.Active, ETripwireState.Exploding, (ETripwireState)99 })
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    var tripwire = Tripwire(world, body.PlayerProfileID, naming);
    CorpseCleanupService.Tick();
    Check(CorpseCleanupService.IsSuccessfullyTrimmed(body),
        "positive control: the planter's body is hidden: " + naming);

    Sweep();
    Check(Kept(world, body), "a body a tripwire still names stays hidden: " + naming);
    Sweep();
    Check(Plugin.ModLogger.Infos.Count(i => i.Contains("keeping opponent hidden while a tripwire it planted (")) == 1,
        "holding a body is said once, with what holds it: " + naming);

    tripwire.TripwireState = ETripwireState.Exploded;
    Sweep();
    Check(Kept(world, body), "a body is not released on the first reading that finds it quiet: " + naming);
    Check(Plugin.ModLogger.Infos.Count(i => i.Contains("nothing names opponent as a damage source any more")) == 1,
        "letting go of it is said once as well: " + naming);
    Sweep();
    Check(Released(world, body), "and is released once nothing has named it for the quiet period: " + naming);
}

// A tripwire that can no longer hurt anybody holds nothing, and neither does one
// somebody else planted, nor a flare somebody else fired: each of these goes on
// the ordinary one-sweep schedule, which is also the proof that the first
// reading is taken at the trim.
foreach (var finished in new[] { "Exploded", "Inert", "None", "other-planter", "other-flare", "smoke" })
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    if (finished == "other-planter") Tripwire(world, "somebody-else", ETripwireState.Wait);
    else if (finished == "other-flare") world.Grenades.Add(2, new FlareCartridge(new Shooter("somebody-else")));
    // RDG-2B and M18: no explosion distance, contusion or blindness, and never
    // destroyed after going off - it would otherwise hold its thrower all raid.
    else if (finished == "smoke") world.Grenades.Add(4, new Grenade { ProfileId = body.PlayerProfileID, WeaponSource = new ThrowWeap() });
    else Tripwire(world, body.PlayerProfileID, Enum.Parse<ETripwireState>(finished));
    CorpseCleanupService.Tick();
    Sweep();
    Check(Released(world, body), "a source that names nobody holds nothing, on the ordinary schedule: " + finished);
}

// The same code path reached another way. None of these was observed; each is a
// grenade or a shot whose explosion or hit resolves its thrower by profile id.
foreach (var source in new[] { "static-tripwire", "grenade", "flare", "unknown-throwable", "shot", "client-shot" })
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    Action gone = null;
    switch (source)
    {
        case "static-tripwire":
            var tripwire = Tripwire(world, body.PlayerProfileID, ETripwireState.Active, isStatic: true);
            gone = () => tripwire.TripwireState = ETripwireState.Exploded;
            break;
        case "grenade":
            // Exploded, and still in the registry: a destroyed grenade must not
            // hold its thrower forever.
            var grenade = new Grenade
            {
                ProfileId = body.PlayerProfileID,
                // F-1: MaxExplosionDistance 7, Contusion.y 4.
                WeaponSource = new ThrowWeap { MaxExplosionDistance = 7, Contusion = new Vector3(1.5f, 4, 15) },
            };
            world.Grenades.Add(1, grenade);
            gone = () => grenade.Destroyed = true;
            break;
        case "flare":
            var flare = new FlareCartridge(new Shooter(body.PlayerProfileID));
            world.Grenades.Add(2, flare);
            gone = () => flare.Destroyed = true;
            break;
        case "unknown-throwable":
            // A kind the guard has not read may name anybody, so it holds all.
            var unknown = new UnknownThrowable();
            world.Grenades.Add(3, unknown);
            gone = () => unknown.Destroyed = true;
            break;
        case "shot":
            var shot = new Shot { PlayerProfileID = body.PlayerProfileID };
            Shots(world).Add(shot);
            gone = () => Shots(world).Remove(shot);
            break;
        case "client-shot":
            var client = new BallisticsCalculator();
            var clientShot = new Shot { PlayerProfileID = body.PlayerProfileID };
            client.Shots.Add(clientShot);
            world.ClientBallisticCalculator = client;
            gone = () => client.Shots.Remove(clientShot);
            break;
    }
    CorpseCleanupService.Tick();
    Sweep();
    Check(Kept(world, body), "a body something still names as a damage source stays hidden: " + source);

    gone();
    Sweep();
    Sweep();
    Check(Released(world, body), "and goes once that source is gone and has stayed gone: " + source);
}

// The quiet period on its own. A shot in flight at the trim lands between two
// sweeps, so the next reading is the first clear one — and the hit it dealt may
// still be waiting in BotMemory.GetHit's timer.
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    var shot = new Shot { PlayerProfileID = body.PlayerProfileID };
    Shots(world).Add(shot);
    CorpseCleanupService.Tick();
    Shots(world).Remove(shot);

    Sweep();
    Check(Kept(world, body), "the first reading that finds a body quiet never releases it on its own");

    Time.time += 1;
    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Check(Kept(world, body), "a sweep a round-start clear pulled forward does not cut the quiet period short");

    Sweep();
    Check(Released(world, body), "the body goes once it has been quiet for the whole period");
}

// Fails closed: whatever cannot be read keeps the body, it is said once, and the
// body goes when the world can be read again.
foreach (var unreadable in new[] { "no-processor", "no-list", "no-calculator", "unknown-calculator", "no-profile" })
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    CorpseCleanupService.Tick();
    var processor = world.SynchronizableObjectLogicProcessor;
    var calculator = world.SharedBallisticsCalculator;
    switch (unreadable)
    {
        case "no-processor": world.SynchronizableObjectLogicProcessor = null; break;
        case "no-list": processor._staticSynchronizableObjects = null; break;
        case "no-calculator": world.SharedBallisticsCalculator = null; break;
        case "unknown-calculator": world.SharedBallisticsCalculator = new ForeignCalculator(); break;
        case "no-profile": body.PlayerProfileID = null; break;
    }
    Sweep();
    Sweep();
    Check(Kept(world, body), "a body stays hidden while the world cannot say what still names it: " + unreadable);
    Check(unreadable == "no-profile"
            ? Plugin.ModLogger.Warnings.Count == 0
            : Plugin.ModLogger.Warnings.Count == 1 && Plugin.ModLogger.Warnings[0].Contains("damage source"),
        "an unreadable world is reported once per raid: " + unreadable);
    Check(!Plugin.ModLogger.Infos.Any(i => i.Contains("keeping")),
        "an unreadable world is not repeated as a hold line per body: " + unreadable);

    world.SynchronizableObjectLogicProcessor = processor;
    processor._staticSynchronizableObjects ??= new();
    world.SharedBallisticsCalculator = calculator;
    body.PlayerProfileID = "opponent";
    Sweep();
    Sweep();
    Check(Released(world, body), "and goes once the world can be read again: " + unreadable);
}

// ---------------------------------------------------------------------------
// Own corpses release too (2026-09-18). Only the NEWEST own corpse is exempt
// from release now, decided by a live re-read (IsNewestOwnCorpse) rather than
// the trim-time TrimState.Own snapshot; an older own corpse is released like
// anybody else's. The one refinement is in DamageSourcesQuiet: a live source
// sharing an own corpse's profile id (the local player's own current shot,
// grenade or tripwire does, by construction) can only hold a body the
// world's profile-id registry still resolves to it. See
// CORPSE-CLEANUP-REVIEW.md, "Own corpses release too", for the full argument.
// ---------------------------------------------------------------------------

// Two own corpses: the older one releases once it stops being the newest,
// the newest never does. A round-start clear is used to force BOTH into
// Trimmed at once - without it the newest would never become a trim
// candidate at all (protectedOwn already keeps it visible), which would make
// this a trivial, uninteresting pass rather than exercising IsNewestOwnCorpse
// against a body that actually reached MayRelease.
{
    var (world, _, _) = Setup();
    var older = AddBody(world, world.MainPlayer.ProfileId);
    var newer = AddBody(world, world.MainPlayer.ProfileId);

    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Check(CorpseCleanupService.IsSuccessfullyTrimmed(older) && CorpseCleanupService.IsSuccessfullyTrimmed(newer),
        "positive control: a round-start clear trims both own bodies, oldest and newest alike");

    Sweep();
    Check(Kept(world, older),
        "the first reading that finds the older own corpse has left the registry, with nothing in flight, starts its quiet period rather than releasing it on its own");
    Check(Kept(world, newer), "the newest own corpse is never released");

    Sweep();
    Check(Released(world, older), "the older own corpse releases once it is no longer the newest and its quiet period has elapsed");
    Check(Kept(world, newer), "the newest own corpse stays kept across further sweeps");
}

// The registry argument, isolated: an own corpse the world's profile-id
// registry still names stays behind the ordinary damage-source hold exactly
// like before; a sibling it no longer names releases despite a "live" shot
// under the very same profile id - the ambiguity CorpseDamageSources alone
// cannot resolve. `stillNamed` models the moment right after its own death,
// before a replacement has registered; `leftRegistry` models an older own
// death whose replacement (world.MainPlayer stands in for it) already has.
{
    var (world, _, _) = Setup();
    var stillNamed = AddBody(world, world.MainPlayer.ProfileId);
    var leftRegistry = AddBody(world, world.MainPlayer.ProfileId);
    var newest = AddBody(world, world.MainPlayer.ProfileId);
    world.RegisterEverExisted(world.MainPlayer.ProfileId, Owner(stillNamed));

    var shot = new Shot { PlayerProfileID = world.MainPlayer.ProfileId };
    Shots(world).Add(shot);

    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Check(CorpseCleanupService.IsSuccessfullyTrimmed(stillNamed)
        && CorpseCleanupService.IsSuccessfullyTrimmed(leftRegistry)
        && CorpseCleanupService.IsSuccessfullyTrimmed(newest),
        "positive control: a round-start clear trims all three own bodies");

    Sweep();
    Check(Kept(world, stillNamed),
        "an own corpse the registry still names stays held behind a live damage source, exactly like before");
    Check(Kept(world, leftRegistry),
        "an own corpse the registry no longer names still waits out its own quiet period, despite a matching live-profile damage source that cannot actually reach it");
    Check(Kept(world, newest), "the newest own corpse is never released, damage source or not");

    Sweep();
    Check(Kept(world, stillNamed),
        "an own corpse the registry still names stays held behind a live damage source, exactly like before");
    Check(Released(world, leftRegistry),
        "and it releases once its own quiet period has elapsed, despite a matching live-profile damage source");
    Check(Kept(world, newest), "the newest own corpse is never released, damage source or not");

    Shots(world).Remove(shot);
    Sweep();
    Check(Kept(world, stillNamed), "the first reading that finds the remaining own body quiet never releases it on its own");
    Sweep();
    Check(Released(world, stillNamed), "and it releases once nothing names it any more, the ordinary quiet-period schedule");
}

// The registry read failing closed: an own corpse whose registry read throws
// stays held behind a live damage source rather than guessing it has left
// and bypassing the hold outright.
{
    var (world, _, _) = Setup();
    var older = AddBody(world, world.MainPlayer.ProfileId);
    var newer = AddBody(world, world.MainPlayer.ProfileId);
    var shot = new Shot { PlayerProfileID = world.MainPlayer.ProfileId };
    Shots(world).Add(shot);
    world.ThrowOnRegistryRead = true;

    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Sweep();
    Check(Kept(world, older),
        "an unreadable registry holds an own corpse behind a live damage source rather than guessing it has left");
    Check(Plugin.ModLogger.Warnings.Any(w => w.Contains("profile-id registry")),
        "the unreadable registry is reported");

    world.ThrowOnRegistryRead = false;
    Shots(world).Remove(shot);
    Sweep();
    Sweep();
    Check(Released(world, older), "and releases once the registry can be read and the source is gone");
}

// The shot-in-flight refinement (2026-09-18 review pass). ShotInFlight's
// profile-id match is the wrong tool once the registry has moved on - the
// live player's own current shots share the id too - so the own-corpse
// bypass instead checks a shot's frozen Player bridge by REFERENCE against
// this exact body. A shot that IS this body's own still holds it even
// though LeftRegistry is already true; a shot under the same profile id but
// a DIFFERENT body's reference (standing in for the live player's own fire)
// does not.
{
    var (world, _, _) = Setup();
    var leftRegistry = AddBody(world, world.MainPlayer.ProfileId);
    var newest = AddBody(world, world.MainPlayer.ProfileId);
    world.RegisterEverExisted(world.MainPlayer.ProfileId, Owner(newest));

    var ownShot = new Shot { PlayerProfileID = world.MainPlayer.ProfileId, Player = new ObserverBridge(Owner(leftRegistry)) };
    Shots(world).Add(ownShot);

    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Sweep();
    Check(Kept(world, leftRegistry),
        "a shot this exact body fired keeps it hidden even after the registry has moved past it");
    Check(Plugin.ModLogger.Infos.Any(i => i.Contains("still in flight, even though it left")),
        "the shot-in-flight hold is said, distinct from the ordinary damage-source hold line");
    Check(Plugin.ModLogger.Infos.Any(i => i.Contains("left the profile-id registry")),
        "leaving the registry is announced even while the shot-in-flight guard still holds the body");

    Shots(world).Remove(ownShot);
    Sweep();
    Check(Kept(world, leftRegistry),
        "landing is not release: the shot's own deferred-hit hazard (BotMemory.GetHit, up to ~0.4s) still gets its DamageSourceQuietSeconds bridge, the same as the ordinary path");
    Check(Plugin.ModLogger.Infos.Any(i => i.Contains("nothing") && i.Contains("fired is still in flight")),
        "the transition out of the shot-in-flight hold is said as soon as the shot lands");

    Sweep();
    Check(Released(world, leftRegistry),
        "and it releases once that shot has landed AND its own quiet period has elapsed, with nothing left to distinguish it from the live player's own fire");
}

// A timed fire freezes the original bridge too, after its grenade is gone.
{
    var (world, _, _) = Setup();
    var older = AddBody(world, world.MainPlayer.ProfileId);
    var newer = AddBody(world, world.MainPlayer.ProfileId);
    world.RegisterEverExisted(world.MainPlayer.ProfileId, Owner(newer));
    ArenaMode.Combat.Fire.ArenaFireRuntime.HeldBody = Owner(older);
    CorpseCleanupService.ClearAllSoon("fire test"); CorpseCleanupService.Tick(); Sweep();
    Check(Kept(world, older), "exact fire owner retained after registry replacement");
    ArenaMode.Combat.Fire.ArenaFireRuntime.HeldBody = null;
    Sweep(); Check(Kept(world, older), "fire owner has a quiet period after last flame");
    Sweep(); Check(Released(world, older), "fire owner releases after quiet period");
}

// The reference, not the profile id, is what matters: a shot fired by a
// DIFFERENT own body under the same profile id (standing in for the live
// player's own current fire) does not hold a corpse the registry has
// already moved past - the exact self-interference StillNamedInRegistry
// exists to remove, which a profile-id match here would reintroduce.
{
    var (world, _, _) = Setup();
    var leftRegistry = AddBody(world, world.MainPlayer.ProfileId);
    var newest = AddBody(world, world.MainPlayer.ProfileId);
    world.RegisterEverExisted(world.MainPlayer.ProfileId, Owner(newest));

    var liveShot = new Shot { PlayerProfileID = world.MainPlayer.ProfileId, Player = new ObserverBridge(Owner(newest)) };
    Shots(world).Add(liveShot);

    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Sweep();
    Check(Kept(world, leftRegistry),
        "a shot fired by a DIFFERENT own body under the same profile id does not hold this one hostage, but its own quiet period still applies before release");

    Sweep();
    Check(Released(world, leftRegistry), "and it releases once that quiet period has elapsed");
}

// Fails closed, same policy as the registry read itself: an own corpse whose
// shot-in-flight check cannot be read stays held rather than guessing clear.
{
    var (world, _, _) = Setup();
    var older = AddBody(world, world.MainPlayer.ProfileId);
    var newer = AddBody(world, world.MainPlayer.ProfileId);
    world.SharedBallisticsCalculator = new ForeignCalculator();

    CorpseCleanupService.ClearAllSoon("round start");
    CorpseCleanupService.Tick();
    Sweep();
    Check(Kept(world, older),
        "an unreadable shot-in-flight check holds an own corpse that left the registry rather than guessing it is clear");
    Check(Plugin.ModLogger.Warnings.Any(w => w.Contains("still in flight")),
        "the unreadable shot-in-flight check is reported");

    world.SharedBallisticsCalculator = new BallisticsCalculator();
    Sweep();
    Sweep();
    Check(Released(world, older), "and releases once the calculator can be read again");
}

// Foreign-corpse behaviour is untouched: the registry refinement only ever
// applies to an own corpse (MayRelease's isOwnCorpse gate). A foreign body a
// damage source still names stays held even with an ever-existed entry
// registered under its own profile id, proving it is the own-corpse gate,
// not the registry lookup, that protects it from this new code path.
{
    var (world, body, gear) = Setup();
    Add(body, gear, "Headwear");
    var shot = new Shot { PlayerProfileID = body.PlayerProfileID };
    Shots(world).Add(shot);
    world.RegisterEverExisted(body.PlayerProfileID, new Player());
    CorpseCleanupService.Tick();
    Sweep();
    Check(Kept(world, body), "a foreign corpse is held by a live damage source exactly as before the registry refinement");

    Shots(world).Remove(shot);
    Sweep();
    Sweep();
    Check(Released(world, body), "and releases on the ordinary schedule once the source is gone");
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

/// <summary>A calculator the guard does not know how to read.</summary>
sealed class ForeignCalculator : EFT.Ballistics.IBallisticsCalculator { }

/// <summary>A throwable kind the guard does not know how to read.</summary>
sealed class UnknownThrowable : Throwable { }

sealed record Shooter(string ProfileId) : EFT.IPlayer;
