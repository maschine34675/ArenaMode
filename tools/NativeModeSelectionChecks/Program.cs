using ArenaMode.Modes;
using ArenaMode.Session;
using System;
using System.Collections.Generic;
using System.Linq;

internal static class Program
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static TException Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception)
        {
            _checks++;
            return exception;
        }
        throw new Exception(message);
    }

    private static int IndexOf(string id)
        => ArenaModeCatalog.All.Select(mode => mode.Id).ToList().IndexOf(id);

    private static void AssertSelection(ArenaModeSelectionState state, string id)
    {
        var index = IndexOf(id);
        Assert(state.SelectedMode == id && state.SelectedIndex == index
               && ReferenceEquals(state.SelectedDefinition, ArenaModeCatalog.All[index]),
            $"selected id, index and catalog definition agree for '{id}'");
    }

    private static void CatalogContract()
    {
        var expected = new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie" };
        Assert(ArenaModeCatalog.All.Select(mode => mode.Id).SequenceEqual(expected),
            "the picker exposes the four stable registry ids in the intended order");
        Assert(ArenaModeCatalog.All.All(mode => !string.IsNullOrWhiteSpace(mode.Label)
                                               && !string.IsNullOrWhiteSpace(mode.Description)),
            "every selectable mode has a label and description");
        Assert(ArenaModeCatalog.All.Select(mode => mode.Label).Distinct().Count() == expected.Length,
            "mode labels are distinct");

        foreach (var id in expected)
        {
            Assert(ArenaModeCatalog.NormalizeId("  " + id.ToUpperInvariant() + "  ") == id,
                $"mode id '{id}' normalizes case and surrounding whitespace");
            Assert(ArenaModeCatalog.CanSelect(id, tacticalOnly: false),
                $"solo Arena permits '{id}'");
        }

        foreach (var invalid in new[] { null, "", " ", "unknown" })
            Assert(ArenaModeCatalog.NormalizeId(invalid) == ArenaModeCatalog.DeathmatchId,
                "invalid stored modes fall back to Endless");

        Assert(ArenaModeCatalog.ResolveSelection("gungame", tacticalOnly: true)
               == ArenaModeCatalog.TacticalId,
            "a protected team resolves an old solo choice to Tactical");
        foreach (var id in expected)
            Assert(ArenaModeCatalog.CanSelect(id, tacticalOnly: true) == (id is "tactical" or "zombie"),
                $"protected-team catalog eligibility is correct for '{id}'");
    }

    // One copy schema for every mode (documented on ArenaModeDefinition, which
    // this test enforces entry by entry): the MATCH RULES facts must read alike
    // and fit the 218 px value column at 17 pt without an ellipsis.
    // RULES  = the objective, at most three words and at most 18 characters.
    // RESPAWN = exactly "Instant" or "Next round" (the view may substitute
    //           "After killcam", which tools/TdmMatchMenuChecks measures).
    // EQUIPMENT = the loadout source, at most three words, at most 18 characters.
    // Description = one sentence, at most 80 characters, ends with a period.
    // A word is a whitespace-separated token containing a letter or digit, so
    // the slash in "Buy / retain kit" does not count.
    private static int Words(string text)
        => text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
               .Count(token => token.Any(char.IsLetterOrDigit));

    private static void CopySchema()
    {
        foreach (var mode in ArenaModeCatalog.All)
        {
            var id = mode.Id;
            Assert(mode.Rules.Length > 0 && mode.Rules.Length <= 18 && Words(mode.Rules) <= 3
                   && mode.Rules == mode.Rules.Trim(),
                $"mode copy schema: RULES is the objective in <= 3 words and <= 18 characters: {id} '{mode.Rules}'");
            Assert(mode.Respawn == "Instant" || mode.Respawn == "Next round",
                $"mode copy schema: RESPAWN is exactly Instant or Next round: {id} '{mode.Respawn}'");
            Assert(mode.Equipment.Length > 0 && mode.Equipment.Length <= 18 && Words(mode.Equipment) <= 3
                   && mode.Equipment == mode.Equipment.Trim(),
                $"mode copy schema: EQUIPMENT is the loadout source in <= 3 words and <= 18 characters: {id} '{mode.Equipment}'");
            Assert(mode.Description.Length > 0 && mode.Description.Length <= 80
                   && mode.Description.EndsWith('.') && !mode.Description.TrimEnd('.').Contains(". ")
                   && char.IsUpper(mode.Description[0]) && mode.Description == mode.Description.Trim(),
                $"mode copy schema: Description is one sentence of <= 80 characters ending with a period: {id} '{mode.Description}'");
        }
        // The Tactical roster lives in the row label, not in RULES.
        var tactical = ArenaModeCatalog.All.Single(mode => mode.Id == ArenaModeCatalog.TacticalId);
        Assert(!tactical.Rules.Contains("1v") && !tactical.Rules.Contains("5v5") && !tactical.Rules.Contains("Solo"),
            "mode copy schema: Tactical RULES carries no roster");
    }

    private static void ProtectedTeamSelection()
    {
        var writes = new List<string>();
        var state = new ArenaModeSelectionState("deathmatch", tacticalOnly: true,
            mode => { writes.Add(mode); return true; });
        Assert(state.TacticalOnly && writes.Count == 0,
            "constructing a protected-team state does not write settings");
        AssertSelection(state, "tactical");
        state.CommitInitialSelection();
        Assert(writes.SequenceEqual(new[] { "tactical" }),
            "initial commit persists the protected-team fallback exactly once");
        state.CommitInitialSelection();
        Assert(writes.Count == 1, "successful initial team commit is idempotent");

        for (var index = 0; index < ArenaModeCatalog.All.Count; index++)
        {
            if (ArenaModeCatalog.All[index].Id is "tactical" or "zombie") continue;
            Assert(!state.CanSelectIndex(index),
                "protected team reports modes without a PFT lifecycle as unavailable");
            Assert(!state.TrySelectIndex(index) && writes.Count == 1,
                "protected team rejects an unsupported mode without calling the writer");
            AssertSelection(state, "tactical");
        }
        Assert(state.CanSelectIndex(IndexOf("tactical"))
               && state.TrySelectIndex(IndexOf("tactical")) && writes.Count == 1,
            "protected team can retain Tactical without rewriting settings");
        Assert(state.CanSelectIndex(IndexOf("zombie")) && state.TrySelectIndex(IndexOf("zombie"))
               && writes.SequenceEqual(new[] { "tactical", "zombie" }),
            "protected team can explicitly choose Zombie and persist that exact mode");
        AssertSelection(state, "zombie");
        Assert(state.TrySelectIndex(IndexOf("zombie")) && writes.Count == 2,
            "retaining protected Zombie does not rewrite the setting");
        Assert(state.TrySelectIndex(IndexOf("tactical")) && writes.Last() == "tactical" && writes.Count == 3,
            "protected team can switch back to Tactical without relaxing other mode gates");

        int zombieWrites = 0;
        var savedZombie = new ArenaModeSelectionState(" ZoMbIe ", true,
            _ => { zombieWrites++; return true; });
        savedZombie.CommitInitialSelection();
        AssertSelection(savedZombie, "zombie");
        Assert(zombieWrites == 0,
            "an existing protected Zombie choice is retained without rewriting it to Tactical");

        var rejectingZombie = new ArenaModeSelectionState("tactical", true, _ => false);
        Assert(!rejectingZombie.TrySelectIndex(IndexOf("zombie")),
            "a rejected protected Zombie write cannot claim the selection succeeded");
        AssertSelection(rejectingZombie, "tactical");
    }

    private static void InitialSelectionAndFallback()
    {
        foreach (var definition in ArenaModeCatalog.All)
        {
            var writes = 0;
            var state = new ArenaModeSelectionState("  " + definition.Id.ToUpperInvariant() + "  ", false,
                mode => { writes++; return true; });
            Assert(!state.TacticalOnly && writes == 0,
                "constructing a valid solo state does not write settings");
            AssertSelection(state, definition.Id);
            state.CommitInitialSelection();
            state.CommitInitialSelection();
            Assert(writes == 0,
                "valid case and whitespace differences do not cause a repair write");
        }

        foreach (var invalid in new[] { null, "", " ", "unknown" })
        {
            var writes = new List<string>();
            var state = new ArenaModeSelectionState(invalid, false,
                mode => { writes.Add(mode); return true; });
            Assert(writes.Count == 0, "constructing an invalid selection does not perform its repair");
            AssertSelection(state, "deathmatch");
            state.CommitInitialSelection();
            state.CommitInitialSelection();
            Assert(writes.SequenceEqual(new[] { "deathmatch" }),
                "invalid solo selection is repaired once by an explicit initial commit");
        }

        var tacticalWrites = 0;
        var tactical = new ArenaModeSelectionState(" TACTICAL ", true,
            mode => { tacticalWrites++; return true; });
        tactical.CommitInitialSelection();
        Assert(tacticalWrites == 0, "an existing normalized team selection does not write settings");

        var replacedWrites = new List<string>();
        var replaced = new ArenaModeSelectionState("unknown", false,
            mode => { replacedWrites.Add(mode); return true; });
        Assert(replaced.TrySelectIndex(IndexOf("gungame")),
            "a user may replace a pending fallback before initial commit");
        replaced.CommitInitialSelection();
        Assert(replacedWrites.SequenceEqual(new[] { "gungame" }),
            "initial commit cannot overwrite a newer accepted choice with the stale fallback");
        AssertSelection(replaced, "gungame");
    }

    private static void SoloSelectionAndBounds()
    {
        var writes = new List<string>();
        ArenaModeSelectionState state = null;
        var previouslySelected = "deathmatch";
        state = new ArenaModeSelectionState(previouslySelected, false, mode =>
        {
            AssertSelection(state, previouslySelected);
            writes.Add(mode);
            return true;
        });
        state.CommitInitialSelection();

        for (var index = 0; index < ArenaModeCatalog.All.Count; index++)
        {
            var id = ArenaModeCatalog.All[index].Id;
            Assert(state.CanSelectIndex(index), $"solo picker allows catalog index {index}");
            var expectedWrites = writes.Count + (previouslySelected == id ? 0 : 1);
            Assert(state.TrySelectIndex(index), $"solo picker accepts '{id}'");
            Assert(writes.Count == expectedWrites
                   && (previouslySelected == id || writes.Last() == id),
                "a changed selection publishes the stable id exactly once");
            AssertSelection(state, id);
            previouslySelected = id;
            Assert(state.TrySelectIndex(index) && writes.Count == expectedWrites,
                "selecting the current mode succeeds without another write");
        }

        foreach (var invalid in new[] { int.MinValue, -1, ArenaModeCatalog.All.Count, int.MaxValue })
        {
            var writesBefore = writes.Count;
            Assert(!state.CanSelectIndex(invalid) && !state.TrySelectIndex(invalid)
                   && writes.Count == writesBefore,
                "an out-of-range index is rejected before any write");
            // The loop above ends on the catalog's LAST entry, whatever that
            // is — currently zombie (the fifth row). Asserting the literal id
            // here on purpose: a silent catalog reorder should fail this test
            // loudly rather than have it quietly track whichever id is last.
            AssertSelection(state, "zombie");
        }
    }

    private static void RejectedAndThrowingWrites()
    {
        var writes = 0;
        var allowWrite = false;
        var state = new ArenaModeSelectionState("deathmatch", false,
            mode => { writes++; return allowWrite; });
        Assert(!state.TrySelectIndex(IndexOf("gungame")) && writes == 1,
            "a rejected selection is reported as unsuccessful");
        AssertSelection(state, "deathmatch");
        allowWrite = true;
        Assert(state.TrySelectIndex(IndexOf("gungame")) && writes == 2,
            "a previously rejected selection can be retried successfully");
        AssertSelection(state, "gungame");

        var failure = new ApplicationException("intentional settings write failure");
        var throwing = new ArenaModeSelectionState("deathmatch", false, mode => throw failure);
        Assert(ReferenceEquals(Throws<ApplicationException>(
                () => throwing.TrySelectIndex(IndexOf("teamdeathmatch")),
                "a throwing writer must propagate its exception"), failure),
            "selection propagates the original writer exception");
        AssertSelection(throwing, "deathmatch");
        Assert(throwing.TrySelectIndex(IndexOf("deathmatch")),
            "retaining the selected mode does not call even a throwing writer");

        var repairWrites = 0;
        var allowRepair = false;
        var repair = new ArenaModeSelectionState("unknown", false,
            mode => { repairWrites++; return allowRepair; });
        Throws<InvalidOperationException>(() => repair.CommitInitialSelection(),
            "a rejected initial repair must abort initialization");
        Assert(repairWrites == 1, "a rejected initial repair calls the writer once");
        allowRepair = true;
        repair.CommitInitialSelection();
        repair.CommitInitialSelection();
        Assert(repairWrites == 2, "a failed initial repair remains retryable until its first success");

        var throwRepair = true;
        var exceptionWrites = 0;
        var throwingRepair = new ArenaModeSelectionState("deathmatch", true, mode =>
        {
            exceptionWrites++;
            if (throwRepair) throw failure;
            return true;
        });
        Assert(ReferenceEquals(Throws<ApplicationException>(() => throwingRepair.CommitInitialSelection(),
                "an initial writer exception must propagate"), failure),
            "initial commit propagates the original writer exception");
        throwRepair = false;
        throwingRepair.CommitInitialSelection();
        throwingRepair.CommitInitialSelection();
        Assert(exceptionWrites == 2, "an initial writer exception does not mark the repair as committed");
    }

    private static void LiveSettingsSynchronization()
    {
        string saved = "tactical";
        int writes = 0;
        bool accept = true;
        var state = new ArenaModeSelectionState(saved, true, mode =>
        {
            writes++;
            if (!accept) return false;
            saved = mode;
            return true;
        });
        Assert(!state.Synchronize(saved, true) && writes == 0, "unchanged polling does not rewrite F12 settings");
        saved = "gungame";
        Assert(state.Synchronize(saved, true) && saved == "tactical" && writes == 1,
            "PFT repairs a forbidden F12 mode even when its displayed resolved mode was already Tactical");
        AssertSelection(state, "tactical");
        saved = "zombie";
        Assert(state.Synchronize(saved, true) && saved == "zombie" && writes == 1,
            "PFT accepts a supported Zombie F12 selection without replacing it with Tactical");
        AssertSelection(state, "zombie");
        saved = "teamdeathmatch";
        Assert(state.Synchronize(saved, false) && !state.TacticalOnly && writes == 1,
            "leaving PFT and accepting a valid F12 mode refreshes without an unnecessary settings write");
        AssertSelection(state, "teamdeathmatch");
        accept = false;
        Throws<InvalidOperationException>(() => state.Synchronize(saved, true),
            "a rejected live team-mode repair cannot claim synchronization succeeded");
        Assert(!state.TacticalOnly && state.SelectedMode == "teamdeathmatch", "failed synchronization preserves previous accepted view state");
    }

    public static void Main()
    {
        CatalogContract();
        CopySchema();
        // Run the protected-team guard before bounds so its mutation fails at
        // the intended policy check, not at an incidental indexing exception.
        ProtectedTeamSelection();
        InitialSelectionAndFallback();
        SoloSelectionAndBounds();
        RejectedAndThrowingWrites();
        LiveSettingsSynchronization();
        Console.WriteLine($"PASS: {_checks} Arena mode-selection state checks (production state and catalog linked).");
    }
}
