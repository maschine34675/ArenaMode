using ArenaMode.Kits;

namespace ArenaMode.Tests;

/// <summary>
/// The bookkeeping behind the automatic quick slots, driven with plain ids.
/// Keys mirror the game's number keys; ids stand in for MongoID strings.
/// </summary>
public sealed class AutoQuickSlotLedgerTests
{
    private enum Key
    {
        Four,
        Five,
        Six,
        Ten,
    }

    private static Dictionary<Key, string> Bound(params (Key key, string id)[] bindings)
    {
        var bound = new Dictionary<Key, string>();
        foreach (var (key, id) in bindings)
            bound[key] = id;

        return bound;
    }

    private static HashSet<string> Body(params string[] ids)
        => new HashSet<string>(ids, StringComparer.Ordinal);

    [Fact]
    public void AKeyEmptiedWhileItsItemStaysOnTheBodyIsAClear()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");
        ledger.Observe(Bound((Key.Four, "medkit")), Body("medkit", "bandage"));
        Assert.True(ledger.IsOwned(Key.Four));

        // The player drags the medkit out of the slot; it is still in the rig.
        ledger.Observe(Bound(), Body("medkit", "bandage"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
        Assert.False(ledger.IsOwned(Key.Four));
    }

    [Fact]
    public void AKeyEmptiedBecauseTheItemLeftTheBodyIsNotAClear()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");

        // Used up, or dropped: the key emptied, the item is on no body.
        ledger.Observe(Bound(), Body("bandage"));

        Assert.False(ledger.IsClearedByPlayer(Key.Four));
        Assert.False(ledger.IsOwned(Key.Four));
        Assert.Equal(0, ledger.ClearedKeyCount);
    }

    [Fact]
    public void ABodySwapThatClearsEveryKeyIsNotAClear()
    {
        // Solo: the kit is new, every binding went with the old body.
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-1");
        ledger.NoteBound(Key.Five, "cms-1");

        ledger.Observe(Bound(), Body("medkit-2", "cms-2"));

        Assert.Equal(0, ledger.ClearedKeyCount);
        Assert.Equal(0, ledger.OwnedKeyCount);
    }

    [Fact]
    public void AReissueUnderTheSameKeysKeepsTheKeyOwnedAndALaterClearIsSeen()
    {
        // The protected 5v5 rebuild: fresh ids, bindings carried over, and
        // nothing told the ledger. This is the bug that shipped: judged
        // against the old instance, the later clear read as "item gone".
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-old");
        ledger.NoteBound(Key.Five, "cms-old");

        ledger.Observe(Bound((Key.Four, "medkit-new"), (Key.Five, "cms-new")), Body("medkit-new", "cms-new"));
        Assert.True(ledger.IsOwned(Key.Four));
        Assert.Equal("medkit-new", ledger.OccupantOf(Key.Four));

        // The player empties key 4 on purpose; the medkit stays in the rig.
        ledger.Observe(Bound((Key.Five, "cms-new")), Body("medkit-new", "cms-new"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
        Assert.False(ledger.IsClearedByPlayer(Key.Five));
        Assert.True(ledger.IsOwned(Key.Five));
    }

    [Fact]
    public void RemapAloneCannotTellAClearFromAUsedUpMedkit()
    {
        // NOT the driver's sequence; this is why the driver settles first.
        // The map names every item the match started with, so after a remap
        // over an unsettled ledger a key emptied in the last second before
        // the death reads as a clear whether the player cleared it by hand
        // (the medkit still on the corpse) or used the medkit up (recreated
        // by the rebuild under the new id either way). Same input, two
        // different truths: only the dead body can tell them apart, and only
        // before the rename.
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-old");

        ledger.RemapItems(new Dictionary<string, string> { ["medkit-old"] = "medkit-new" });
        Assert.Equal("medkit-new", ledger.OccupantOf(Key.Four));

        ledger.Observe(Bound(), Body("medkit-new"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
    }

    [Fact]
    public void AMedkitUsedUpJustBeforeTheDeathIsNotAClearAfterTheReissue()
    {
        // The rebuild recreates the entry loadout, so the used-up medkit is
        // back under a fresh id and IN the map. The driver settles the ledger
        // against the dead body first; only then does it remap. Without the
        // settle the emptied key would be renamed to the recreated medkit and
        // read as a clear on the first poll of the new body.
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-old");

        // The settle: key 4 unbound at consumption, the medkit is gone.
        ledger.Observe(Bound(), Body("cms-old"));
        ledger.RemapItems(new Dictionary<string, string> { ["medkit-old"] = "medkit-new", ["cms-old"] = "cms-new" });

        // First poll on the new body: the medkit is back, the key is empty.
        ledger.Observe(Bound(), Body("medkit-new", "cms-new"));

        Assert.False(ledger.IsClearedByPlayer(Key.Four));
        Assert.False(ledger.IsOwned(Key.Four));
    }

    [Fact]
    public void AKeyClearedJustBeforeTheDeathStaysClearedThroughTheReissue()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-old");

        // The settle: key 4 empty, the medkit still in the rig of the corpse.
        ledger.Observe(Bound(), Body("medkit-old"));
        Assert.True(ledger.IsClearedByPlayer(Key.Four));

        ledger.RemapItems(new Dictionary<string, string> { ["medkit-old"] = "medkit-new" });
        ledger.Observe(Bound(), Body("medkit-new"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
        Assert.False(ledger.IsOwned(Key.Four));
    }

    [Fact]
    public void AKeyStillBoundAtTheDeathIsOwnedUnderItsNewIdAndCanBeClearedLater()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-old");

        // The settle sees the binding intact; the remap renames it; the new
        // body's FastAccess carries the binding over under the new id.
        ledger.Observe(Bound((Key.Four, "medkit-old")), Body("medkit-old"));
        ledger.RemapItems(new Dictionary<string, string> { ["medkit-old"] = "medkit-new" });
        ledger.Observe(Bound((Key.Four, "medkit-new")), Body("medkit-new"));
        Assert.Equal("medkit-new", ledger.OccupantOf(Key.Four));

        // Later in the round the player empties the key by hand.
        ledger.Observe(Bound(), Body("medkit-new"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
    }

    [Fact]
    public void AnAnnouncedReissueLeavesUnmappedItemsAlone()
    {
        // An item in a protected special pocket slot — bindable, since its
        // parent slot is Pockets — keeps its object and its id through the
        // rebuild; the map does not mention it.
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit-old");
        ledger.NoteBound(Key.Six, "special-slot-compass");

        ledger.RemapItems(new Dictionary<string, string> { ["medkit-old"] = "medkit-new" });

        Assert.Equal("medkit-new", ledger.OccupantOf(Key.Four));
        Assert.Equal("special-slot-compass", ledger.OccupantOf(Key.Six));

        // A map with nothing to say, or none at all, changes nothing.
        ledger.RemapItems(new Dictionary<string, string>());
        ledger.RemapItems(null!);
        Assert.Equal("medkit-new", ledger.OccupantOf(Key.Four));
        Assert.Equal("special-slot-compass", ledger.OccupantOf(Key.Six));
    }

    [Fact]
    public void AKeyThePlayerFilledThemselvesIsNeverOwned()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");

        ledger.Observe(Bound((Key.Four, "medkit"), (Key.Ten, "stim")), Body("medkit", "stim"));
        Assert.False(ledger.IsOwned(Key.Ten));

        // The player empties their own key with the stim still on the body:
        // not a clear the ledger has any say in, and the key is free again.
        ledger.Observe(Bound((Key.Four, "medkit")), Body("medkit", "stim"));
        Assert.False(ledger.IsClearedByPlayer(Key.Ten));
        Assert.Equal(0, ledger.ClearedKeyCount);
    }

    [Fact]
    public void AnotherItemPutOnOurKeyBecomesTheOccupant()
    {
        // The player binds a grenade over the medkit key and later throws it:
        // the key emptied because the ITEM left, not because the player
        // cleared it. The medkit being elsewhere on the body must not turn
        // that into a clear.
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");

        ledger.Observe(Bound((Key.Four, "grenade")), Body("medkit", "grenade"));
        Assert.Equal("grenade", ledger.OccupantOf(Key.Four));

        ledger.Observe(Bound(), Body("medkit"));
        Assert.False(ledger.IsClearedByPlayer(Key.Four));
        Assert.False(ledger.IsOwned(Key.Four));
    }

    [Fact]
    public void EmptyingAnAdoptedOccupantByHandIsAClear()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");
        ledger.Observe(Bound((Key.Four, "grenade")), Body("medkit", "grenade"));

        ledger.Observe(Bound(), Body("medkit", "grenade"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
    }

    [Fact]
    public void AClearedKeyStaysClearedAcrossLaterPolls()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");
        ledger.Observe(Bound(), Body("medkit"));
        Assert.True(ledger.IsClearedByPlayer(Key.Four));

        // Lives later: the body changed, the key is still empty, the ledger
        // owns nothing, and the key stays the player's.
        ledger.Observe(Bound(), Body("medkit-2"));
        ledger.Observe(Bound((Key.Five, "cms-2")), Body("medkit-2", "cms-2"));

        Assert.True(ledger.IsClearedByPlayer(Key.Four));
        Assert.Equal(1, ledger.ClearedKeyCount);
        Assert.Equal(0, ledger.OwnedKeyCount);
    }

    [Fact]
    public void AnUnchangedOccupantIsLeftAlone()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");

        for (int i = 0; i < 5; i++)
            ledger.Observe(Bound((Key.Four, "medkit")), Body("medkit"));

        Assert.Equal("medkit", ledger.OccupantOf(Key.Four));
        Assert.Equal(1, ledger.OwnedKeyCount);
        Assert.Equal(0, ledger.ClearedKeyCount);
    }

    [Fact]
    public void AnItemWithoutAnIdIsNotWatched()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "");
        ledger.NoteBound(Key.Five, null!);

        Assert.Equal(0, ledger.OwnedKeyCount);

        // Nor does an id-less occupant displace a real one.
        ledger.NoteBound(Key.Four, "medkit");
        ledger.Observe(Bound((Key.Four, "")), Body("medkit"));
        Assert.Equal("medkit", ledger.OccupantOf(Key.Four));
    }

    [Fact]
    public void ObserveToleratesMissingInputs()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");

        // No bindings readable, no body readable: the key is treated as
        // emptied with the item gone. Forgotten, not cleared.
        ledger.Observe(null!, null!);

        Assert.Equal(0, ledger.OwnedKeyCount);
        Assert.Equal(0, ledger.ClearedKeyCount);
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var ledger = new AutoQuickSlotLedger<Key>();
        ledger.NoteBound(Key.Four, "medkit");
        ledger.Observe(Bound(), Body("medkit"));
        ledger.NoteBound(Key.Five, "cms");
        Assert.True(ledger.IsClearedByPlayer(Key.Four));

        ledger.Reset();

        Assert.Equal(0, ledger.OwnedKeyCount);
        Assert.Equal(0, ledger.ClearedKeyCount);
        Assert.False(ledger.IsClearedByPlayer(Key.Four));
        Assert.Null(ledger.OccupantOf(Key.Five));
    }
}
