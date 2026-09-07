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
        var expected = new[] { "endless", "rounds", "gungame", "tactical", "teamdeathmatch" };
        Assert(ArenaModeCatalog.All.Select(mode => mode.Id).SequenceEqual(expected),
            "the picker exposes the five stable registry ids in the intended order");
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
            Assert(ArenaModeCatalog.NormalizeId(invalid) == ArenaModeCatalog.EndlessId,
                "invalid stored modes fall back to Endless");

        Assert(ArenaModeCatalog.ResolveSelection("gungame", tacticalOnly: true)
               == ArenaModeCatalog.TacticalId,
            "a protected team resolves an old solo choice to Tactical");
        foreach (var id in expected)
            Assert(ArenaModeCatalog.CanSelect(id, tacticalOnly: true) == (id == "tactical"),
                $"protected-team catalog eligibility is correct for '{id}'");
    }

    private static void ProtectedTeamSelection()
    {
        var writes = new List<string>();
        var state = new ArenaModeSelectionState("endless", tacticalOnly: true,
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
            if (ArenaModeCatalog.All[index].Id == "tactical") continue;
            Assert(!state.CanSelectIndex(index),
                "protected team reports non-Tactical choices as unavailable");
            Assert(!state.TrySelectIndex(index) && writes.Count == 1,
                "protected team rejects a non-Tactical selection without calling the writer");
            AssertSelection(state, "tactical");
        }
        Assert(state.CanSelectIndex(IndexOf("tactical"))
               && state.TrySelectIndex(IndexOf("tactical")) && writes.Count == 1,
            "protected team can retain Tactical without rewriting settings");
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
            AssertSelection(state, "endless");
            state.CommitInitialSelection();
            state.CommitInitialSelection();
            Assert(writes.SequenceEqual(new[] { "endless" }),
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
        Assert(replaced.TrySelectIndex(IndexOf("rounds")),
            "a user may replace a pending fallback before initial commit");
        replaced.CommitInitialSelection();
        Assert(replacedWrites.SequenceEqual(new[] { "rounds" }),
            "initial commit cannot overwrite a newer accepted choice with the stale fallback");
        AssertSelection(replaced, "rounds");
    }

    private static void SoloSelectionAndBounds()
    {
        var writes = new List<string>();
        ArenaModeSelectionState state = null;
        var previouslySelected = "rounds";
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
            AssertSelection(state, "teamdeathmatch");
        }
    }

    private static void RejectedAndThrowingWrites()
    {
        var writes = 0;
        var allowWrite = false;
        var state = new ArenaModeSelectionState("rounds", false,
            mode => { writes++; return allowWrite; });
        Assert(!state.TrySelectIndex(IndexOf("gungame")) && writes == 1,
            "a rejected selection is reported as unsuccessful");
        AssertSelection(state, "rounds");
        allowWrite = true;
        Assert(state.TrySelectIndex(IndexOf("gungame")) && writes == 2,
            "a previously rejected selection can be retried successfully");
        AssertSelection(state, "gungame");

        var failure = new ApplicationException("intentional settings write failure");
        var throwing = new ArenaModeSelectionState("rounds", false, mode => throw failure);
        Assert(ReferenceEquals(Throws<ApplicationException>(
                () => throwing.TrySelectIndex(IndexOf("teamdeathmatch")),
                "a throwing writer must propagate its exception"), failure),
            "selection propagates the original writer exception");
        AssertSelection(throwing, "rounds");
        Assert(throwing.TrySelectIndex(IndexOf("rounds")),
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
        var throwingRepair = new ArenaModeSelectionState("endless", true, mode =>
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

    public static void Main()
    {
        CatalogContract();
        // Run the protected-team guard before bounds so its mutation fails at
        // the intended policy check, not at an incidental indexing exception.
        ProtectedTeamSelection();
        InitialSelectionAndFallback();
        SoloSelectionAndBounds();
        RejectedAndThrowingWrites();
        Console.WriteLine($"PASS: {_checks} Arena mode-selection state checks (production state and catalog linked).");
    }
}
