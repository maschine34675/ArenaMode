using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardApprovalTests
{
    [Fact]
    public async Task ValidatedResultProducesApprovalThenExactlyOneNativeMail()
    {
        using var f = new Fixture();
        var before = f.Profile;
        var decision = RewardApprovalProducer.PrepareIfPresent(f.Window, f.Enrollment);
        Assert.Equal(RewardEvaluationStatus.Eligible, decision!.Status);
        Assert.Equal(4500, decision.Roubles);
        Assert.Equal(before, f.Profile); // Producer itself never edits a profile.
        var approval = f.Read<RewardStartupApproval>("approved.json");
        Assert.Equal(f.Candidate.Authorization.CleanProfileRevision, approval.CleanProfileRevision);
        Assert.Equal(4500, approval.Claim.Roubles);
        var result = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
        Assert.Single(f.Ledger);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before)!["characters"], JsonNode.Parse(f.Profile)!["characters"]));
        var paid = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(paid, f.Profile);
        Assert.Equal(approval.Claim.Fingerprint, f.Read<RewardStartupAcknowledgement>("acknowledged.json").Fingerprint);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("run")]
    [InlineData("arm")]
    [InlineData("native-end")]
    [InlineData("restore-generation")]
    [InlineData("pending")]
    [InlineData("expected")]
    [InlineData("persisted")]
    [InlineData("schema")]
    public async Task PositiveRestorationMustMatchEveryTrustedIdentity(string mutation)
    {
        using var f = new Fixture();
        var proof = f.Candidate.Restoration;
        proof = mutation switch
        {
            "binding" => proof with { Binding = proof.Binding with { Epoch = Fixture.Id(99) } },
            "run" => proof with { RunId = Fixture.Id(99) },
            "arm" => proof with { ArmToken = Fixture.Id(99) },
            "native-end" => proof with { NativeEndIdentity = "other" },
            "restore-generation" => proof with { RestorationId = Fixture.Id(99) },
            "pending" => proof with { State = RewardRestorationState.Pending },
            "expected" => proof with { ExpectedProfileRevision = new string('a', 64) },
            "persisted" => proof with { PersistedProfileRevision = new string('a', 64) },
            _ => proof with { Schema = 2 }
        };
        f.Set(f.Candidate with { Restoration = proof });
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        f.AssertUnpaid(before);
        Assert.False(File.Exists(f.PathOf("approved.json")));
    }

    [Theory]
    [InlineData("normal-progress")]
    [InlineData("inraid")]
    [InlineData("wipe")]
    [InlineData("loaded")]
    [InlineData("private-marker")]
    [InlineData("closed")]
    [InlineData("disabled")]
    public async Task UnsafeProfileOrBoundaryNeverGetsAnApproval(string mutation)
    {
        using var f = new Fixture();
        switch (mutation)
        {
            case "normal-progress": f.EditProfile(p => p["foreignMod"]!["progress"] = 999); break;
            case "inraid": f.EditProfile(p => p["inraid"]!["location"] = "factory4_day"); f.RebindCleanRevision(); break;
            case "wipe": f.EditProfile(p => p["info"]!["wipe"] = true); f.RebindCleanRevision(); break;
            case "loaded": f.Unloaded = false; break;
            case "private-marker": File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}"); break;
            case "closed": f.Window.Dispose(); break;
            case "disabled": f.Write("enrollment.json", f.Enrollment with { Enabled = false }); break;
        }
        var before = f.Profile;
        Assert.DoesNotContain((await f.Process()).Result.Status, new[] { RewardSettlementStatus.Paid, RewardSettlementStatus.AlreadyPaid });
        f.AssertUnpaid(before);
        Assert.False(File.Exists(f.PathOf("approved.json")));
    }

    [Fact]
    public async Task ChangedTranscriptCannotHideBehindIdenticalRoubleAmount()
    {
        using var f = new Fixture();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var paid = f.Profile;
        var round = f.Candidate.Report.Rounds[0];
        f.Set(f.Candidate with { Report = f.Candidate.Report with
        {
            Rounds = [round with { Deaths = [round.Deaths[0] with { AtMilliseconds = 1001 }, .. round.Deaths.Skip(1)] }]
        } });
        var result = await f.Process();
        Assert.Equal(RewardSettlementStatus.Quarantined, result.Result.Status);
        Assert.Equal("result-decision-conflict", result.Result.Reason);
        Assert.Equal(paid, f.Profile);
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task WhitespaceAndPropertyOrderDoNotChangeTheSemanticReceipt()
    {
        using var f = new Fixture();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var json = JsonNode.Parse(File.ReadAllText(f.PathOf("candidate.json")))!.AsObject();
        var reordered = new JsonObject();
        foreach (var pair in json.Reverse()) reordered.Add(pair.Key, pair.Value!.DeepClone());
        File.WriteAllText(f.PathOf("candidate.json"), reordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Single(f.Ledger);
    }

    [Theory]
    [InlineData("client-amount")]
    [InlineData("missing-state")]
    [InlineData("missing-policy")]
    [InlineData("duplicate-member")]
    [InlineData("null-report")]
    [InlineData("oversize")]
    public async Task MalformedSerializedInputsNeverDefaultIntoAuthority(string mutation)
    {
        using var f = new Fixture();
        var node = JsonNode.Parse(File.ReadAllText(f.PathOf("candidate.json")))!.AsObject();
        switch (mutation)
        {
            case "client-amount": node["report"]!["roubles"] = 9999999; break;
            case "missing-state": node["restoration"]!.AsObject().Remove("state"); break;
            case "missing-policy": node["authorization"]!.AsObject().Remove("policy"); break;
            case "null-report": node["report"] = null; break;
        }
        var json = node.ToJsonString();
        if (mutation == "duplicate-member") json = json.Insert(1, "\"schema\":1,");
        if (mutation == "oversize") json += new string(' ', 4_194_305);
        File.WriteAllText(f.PathOf("candidate.json"), json);
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        f.AssertUnpaid(before);
        Assert.False(File.Exists(f.PathOf("approved.json")));
    }

    [Theory]
    [InlineData("aborted")]
    [InlineData("no-contest")]
    [InlineData("zero-rates")]
    [InlineData("invalid-death")]
    public async Task TerminalNonpayableDecisionsCannotLaterBeRewritten(string mutation)
    {
        using var f = new Fixture();
        var valid = f.Candidate;
        var candidate = mutation switch
        {
            "aborted" => valid with { Report = valid.Report with { State = RewardReportState.Aborted } },
            "no-contest" => valid with { Report = valid.Report with { State = RewardReportState.NoContest } },
            "zero-rates" => valid with { Authorization = valid.Authorization with
                { Policy = valid.Authorization.Policy with { RoublesPerKill = 0, RoublesPerRoundWin = 0 } } },
            _ => valid with { Report = valid.Report with { Rounds = [valid.Report.Rounds[0] with
                { Deaths = [valid.Report.Rounds[0].Deaths[0] with { Sequence = 8 }] }] } }
        };
        f.Set(candidate);
        var before = f.Profile;
        var first = await f.Process();
        Assert.Equal(mutation == "invalid-death" ? RewardSettlementStatus.Rejected : RewardSettlementStatus.Deferred, first.Result.Status);
        Assert.True(File.Exists(f.DecisionPath));
        Assert.False(File.Exists(f.PathOf("approved.json")));
        f.Set(valid);
        Assert.Equal("result-decision-conflict", (await f.Process()).Result.Reason);
        f.AssertUnpaid(before);
    }

    [Fact]
    public async Task InterruptedBetweenDecisionAndApprovalCanResumeIdentically()
    {
        using var f = new Fixture();
        RewardApprovalProducer.PrepareIfPresent(f.Window, f.Enrollment);
        File.Delete(f.PathOf("approved.json")); // Crash after decision persisted but before approval rename.
        File.WriteAllText(Path.Combine(f.Directory, "decisions", ".arena-reward-" + Fixture.Id(88) + ".tmp"), "unfinished stage");
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task PaymentWithInterruptedFinalJournalCommitDoesNotPayAgain()
    {
        using var f = new Fixture();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var journal = f.Read<RewardJournal>("journal.json");
        f.Write("journal.json", journal with { Entries = [journal.Entries[0] with { Phase = RewardJournalPhase.Attempting }] });
        File.Delete(f.PathOf("acknowledged.json"));
        var paid = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(paid, f.Profile);
        Assert.Equal(RewardJournalPhase.Committed, f.Read<RewardJournal>("journal.json").Entries[0].Phase);
    }

    [Fact]
    public async Task NewCandidateCannotReplaceAnUnpaidDecisionEvenWithFakeAcknowledgement()
    {
        using var f = new Fixture();
        RewardApprovalProducer.PrepareIfPresent(f.Window, f.Enrollment);
        var previous = f.Read<RewardStartupApproval>("approved.json");
        File.Delete(f.PathOf("approved.json")); // Previous decision itself still owns the pending slot.
        f.Write("acknowledged.json", new RewardStartupAcknowledgement(1, previous.Claim.Key, previous.Claim.Fingerprint, Fixture.Id(80)[..24]));
        f.NextRun();
        var before = f.Profile;
        Assert.Equal("earlier-result-not-settled", (await f.Process()).Result.Reason);
        f.AssertUnpaid(before, allowAcknowledgement: true);
        Assert.False(File.Exists(f.DecisionPath));
    }

    [Fact]
    public async Task NextRunAfterCommittedPaymentGetsNewApprovalWithoutOverwritingHistory()
    {
        using var f = new Fixture();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var old = f.Candidate;
        var oldDecision = File.ReadAllText(f.DecisionPath);
        f.NextRun();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(2, f.Ledger.Count);
        Assert.Equal(oldDecision, File.ReadAllText(Path.Combine(f.Directory, "decisions", old.Authorization.RunId + ".json")));
        var newestApproval = File.ReadAllText(f.PathOf("approved.json"));
        f.Set(old);
        Assert.Equal("stale-result-candidate", (await f.Process()).Result.Reason);
        Assert.Equal(newestApproval, File.ReadAllText(f.PathOf("approved.json")));
        Assert.Equal(2, f.Ledger.Count);
    }

    [Fact]
    public async Task MissingLastDecisionIsNotAnEmptyHistoryOrAReusableSequence()
    {
        using var f = new Fixture();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        File.Delete(f.DecisionPath);
        f.NextRun(sequence: 1);
        var before = f.Profile;
        Assert.Equal("payment-without-result-decision", (await f.Process()).Result.Reason);
        Assert.Equal(before, f.Profile);
        Assert.Single(f.Ledger);
    }

    [Theory]
    [InlineData("candidate.json")]
    [InlineData("journal.json")]
    [InlineData("decision")]
    public async Task LockedEvidenceDefersWithoutMutatingProfile(string name)
    {
        using var f = new Fixture();
        if (name == "decision") RewardApprovalProducer.PrepareIfPresent(f.Window, f.Enrollment);
        var path = name == "decision" ? f.DecisionPath : f.PathOf(name);
        var before = f.Profile;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(RewardSettlementStatus.Deferred, (await f.Process()).Result.Status);
        f.AssertUnpaid(before);
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
    }

    [Fact]
    public async Task LinkedDecisionDirectoryCannotRedirectAuthorityWrites()
    {
        using var f = new Fixture();
        var target = Path.Combine(f.Root, "separate");
        Directory.CreateDirectory(target);
        var link = Path.Combine(f.Directory, "decisions");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            var before = f.Profile;
            Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
            Assert.Empty(Directory.GetFileSystemEntries(target));
            f.AssertUnpaid(before);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task GappedSequenceCannotMintApproval()
    {
        using var f = new Fixture();
        f.Set(f.Candidate with { Authorization = f.Candidate.Authorization with { Sequence = 2 } });
        Assert.Equal("result-sequence-or-capacity", (await f.Process()).Result.Reason);
        Assert.False(File.Exists(f.PathOf("approved.json")));
        Assert.Empty(f.Ledger);
    }

    [Theory]
    [InlineData(false, 59999, true)]
    [InlineData(false, 60000, false)]
    [InlineData(true, 59999, true)]
    [InlineData(true, 60000, true)]
    public void TacticalSoloAndTeamHaveDifferentExactDeadlineOrdering(bool team, long eventTime, bool eligible)
    {
        using var f = new Fixture();
        var round = f.Candidate.Authorization.Rounds[0] with { ObservedDurationMilliseconds = 60000 };
        if (team) round = round with { Rule = RewardRoundRule.TeamElimination, Actors = [.. round.Actors,
            new RewardActor("aaaaaaaaaaaaaaaaaaaaaaaa", Fixture.Id(99), RewardSide.Friendly, 0)] };
        var run = f.Candidate.Authorization with { Rounds = [round] };
        var report = f.Candidate.Report with { Rounds = [f.Candidate.Report.Rounds[0] with
            { Deaths = f.Candidate.Report.Rounds[0].Deaths.Select(d => d with { AtMilliseconds = eventTime }).ToArray() }] };
        var outcome = RewardResultValidator.Evaluate(run, report);
        Assert.Equal(eligible ? RewardEvaluationStatus.Eligible : RewardEvaluationStatus.Rejected, outcome.Status);
        Assert.Equal(eligible ? 1 : 0, outcome.RoundWins);
    }

    [Theory]
    [InlineData("money")]
    [InlineData("kills")]
    [InlineData("wins")]
    [InlineData("reason")]
    public async Task MalformedOlderZeroPaymentDecisionsAreNotCleanHistory(string mutation)
    {
        using var f = new Fixture();
        f.Set(f.Candidate with { Report = f.Candidate.Report with { State = RewardReportState.NoContest } });
        Assert.Equal(RewardSettlementStatus.Deferred, (await f.Process()).Result.Status);
        var old = JsonNode.Parse(File.ReadAllText(f.DecisionPath))!;
        switch (mutation)
        {
            case "money": old["evaluation"]!["roubles"] = 1; break;
            case "kills": old["evaluation"]!["eligibleKills"] = -1; break;
            case "wins": old["evaluation"]!["roundWins"] = 101; break;
            case "reason": old["evaluation"]!["reason"] = null; break;
        }
        File.WriteAllText(f.DecisionPath, old.ToJsonString());
        f.NextRun();
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        f.AssertUnpaid(before);
    }

    [Fact]
    public async Task CancellationBeforeEntryCannotPublishAnApproval()
    {
        using var f = new Fixture();
        var before = f.Profile;
        var outcome = await RewardStartupProcessor.ProcessProfileAsync(f.Window, f.Binding.ProfileId, new CancellationToken(true));
        Assert.Equal("cancelled", outcome.Result.Reason);
        Assert.False(File.Exists(f.PathOf("approved.json")));
        Assert.False(File.Exists(f.DecisionPath));
        f.AssertUnpaid(before);
    }

    private sealed class Fixture : IDisposable
    {
        internal static string Id(int value) => value.ToString("x32");
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardApproval.Tests", Guid.NewGuid().ToString("N"));
        internal RewardBinding Binding { get; } = new(Id(1), "0123456789abcdef01234567", Id(2));
        internal string Directory => Path.Combine(Root, "user", "arena-rewards", Binding.ProfileId);
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Binding.ProfileId + ".json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal string DecisionPath => Path.Combine(Directory, "decisions", Candidate.Authorization.RunId + ".json");
        internal RewardStartupEnrollment Enrollment { get; }
        internal RewardStartupWindow Window { get; }
        internal RewardApprovalCandidate Candidate { get; private set; }
        internal bool Unloaded = true;
        internal IReadOnlyList<RewardPayment> Ledger => RewardProfileDocument.ReadLedger(Profile, Binding, new());
        internal string PathOf(string name) => Path.Combine(Directory, name);
        internal void Write<T>(string name, T value) => File.WriteAllText(PathOf(name), JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson));
        internal T Read<T>(string name) => RewardStartupFiles.Read<T>(PathOf(name));
        internal void Set(RewardApprovalCandidate value) { Candidate = value; Write("candidate.json", value); }
        internal Task<RewardStartupOutcome> Process() => RewardStartupProcessor.ProcessProfileAsync(Window, Binding.ProfileId, CancellationToken.None);

        internal Fixture()
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(Root, ".owner"), "synthetic-approval");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            profile["foreignMod"] = new JsonObject { ["progress"] = 123 };
            File.WriteAllText(ProfilePath, RewardProfileDocument.Enroll(profile.ToJsonString(), Binding));
            Enrollment = new(1, true, Root, Binding);
            Write("enrollment.json", Enrollment);
            Write("journal.json", new RewardJournal(1, Binding, []));
            Window = new(Root, () => Unloaded);
            var run = new RewardRunAuthorization(1, Binding, Id(3), 1, Id(4), ArenaMode.Shared.Protocol.BuildNativeEndIdentity("native-end-5", Id(4)), Id(6), Binding.ProfileId, "tactical",
                RewardIdentity.Hash(Profile), new("synthetic-v1", 500, 2000, 100, 10, 100000),
                [new(Id(10), RewardRoundRule.SoloElimination, 6000, 60000, 0,
                    [new(Binding.ProfileId, Id(20), RewardSide.Friendly, 0),
                     new("111111111111111111111111", Id(21), RewardSide.Opponent, 0),
                     new("222222222222222222222222", Id(22), RewardSide.Opponent, 0),
                     new("333333333333333333333333", Id(23), RewardSide.Opponent, 0),
                     new("444444444444444444444444", Id(24), RewardSide.Opponent, 0),
                     new("555555555555555555555555", Id(25), RewardSide.Opponent, 0)])]);
            var report = new RewardMatchReport(1, run.RunId, run.ArmToken, run.NativeEndIdentity, RewardReportState.Completed,
                [new(Id(10), Enumerable.Range(1, 5).Select(i => new RewardDeathReport(i, i * 1000, Id(20 + i), Id(20), RewardDeathKind.Combat)).ToArray())]);
            Candidate = new(1, run, report, Proof(run));
            Set(Candidate);
        }

        private static RewardRestorationEvidence Proof(RewardRunAuthorization run) => new(1, run.Binding, run.RunId, run.ArmToken,
            run.NativeEndIdentity, run.RestorationId, RewardRestorationState.Persisted, run.CleanProfileRevision, run.CleanProfileRevision);
        internal void RebindCleanRevision()
        {
            var run = Candidate.Authorization with { CleanProfileRevision = RewardIdentity.Hash(Profile) };
            Set(Candidate with { Authorization = run, Restoration = Proof(run) });
        }
        internal void NextRun(long? sequence = null)
        {
            var run = Candidate.Authorization with { RunId = Guid.NewGuid().ToString("N"), Sequence = sequence ?? Candidate.Authorization.Sequence + 1,
                ArmToken = Guid.NewGuid().ToString("N"), RestorationId = Guid.NewGuid().ToString("N"),
                CleanProfileRevision = RewardIdentity.Hash(Profile) };
            run = run with { NativeEndIdentity = ArenaMode.Shared.Protocol.BuildNativeEndIdentity(Guid.NewGuid().ToString("N"), run.ArmToken) };
            Set(new(1, run, Candidate.Report with { RunId = run.RunId, ArmToken = run.ArmToken, NativeEndIdentity = run.NativeEndIdentity }, Proof(run)));
        }
        internal void EditProfile(Action<JsonNode> edit)
        {
            var profile = JsonNode.Parse(Profile)!;
            edit(profile);
            File.WriteAllText(ProfilePath, profile.ToJsonString());
        }
        internal void AssertUnpaid(string before, bool allowAcknowledgement = false)
        {
            Assert.Equal(before, Profile);
            Assert.Empty(Ledger);
            Assert.Empty(Read<RewardJournal>("journal.json").Entries);
            if (!allowAcknowledgement) Assert.False(File.Exists(PathOf("acknowledged.json")));
        }
        public void Dispose()
        {
            Window.Dispose();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.RewardApproval.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".owner")) != "synthetic-approval") throw new InvalidOperationException("Not an owned fixture.");
            System.IO.Directory.Delete(Root, recursive: true);
        }
    }
}
