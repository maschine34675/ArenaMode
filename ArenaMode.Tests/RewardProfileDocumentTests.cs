using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArenaMode.Server.Rewards;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

public sealed class RewardProfileDocumentTests
{
    private static readonly RewardBinding Binding = new("11111111111111111111111111111111",
        "0123456789abcdef01234567", "22222222222222222222222222222222");
    private static readonly RewardLimits Limits = new();
    private const string Extension = RewardProfileDocument.ExtensionKey;
    private const string Sender = RewardProfileDocument.SystemSenderId;

    [Fact]
    public void EnrollmentPreservesAllOtherDataAndRefusesReenrollmentEvenWhenNull()
    {
        var original = Fixture();
        var enrolled = JsonNode.Parse(RewardProfileDocument.Enroll(original, Binding))!.AsObject();
        Assert.Empty(RewardProfileDocument.ReadLedger(enrolled.ToJsonString(), Binding, Limits));
        Assert.True(enrolled.Remove(Extension));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), enrolled));
        foreach (JsonNode? existing in new JsonNode?[] { null, new JsonObject(), JsonValue.Create(false) })
        {
            var source = JsonNode.Parse(original)!.AsObject();
            source[Extension] = existing;
            Assert.Throws<RewardDataException>(() => RewardProfileDocument.Enroll(source.ToJsonString(), Binding));
        }
    }

    [Fact]
    public void FullStashStillGetsMailAndExistingDialoguesMetadataRemainUntouched()
    {
        var claim = Claim(750_001);
        var payment = Payment(claim);
        string enrolled = Enrolled();
        string result = RewardProfileDocument.Apply(enrolled, claim, payment, Limits);
        var before = JsonNode.Parse(enrolled)!;
        var after = JsonNode.Parse(result)!;
        Assert.True(JsonNode.DeepEquals(before["characters"], after["characters"]));
        Assert.True(JsonNode.DeepEquals(before["customOtherMod"], after["customOtherMod"]));
        Assert.Equal(3, after["dialogues"]![Sender]!["new"]!.GetValue<int>());
        Assert.Equal(2, after["dialogues"]![Sender]!["attachmentsNew"]!.GetValue<int>());
        Assert.True(after["dialogues"]![Sender]!["pinned"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(before["dialogues"]![Sender]!["customMetadata"],
            after["dialogues"]![Sender]!["customMetadata"]));
        Assert.True(JsonNode.DeepEquals(before["dialogues"]!["999999999999999999999999"],
            after["dialogues"]!["999999999999999999999999"]));
        Assert.True(RewardProfileDocument.HasOriginalMail(result, payment, Limits));
        Assert.Equal(payment, Assert.Single(RewardProfileDocument.ReadLedger(result, Binding, Limits)));

        // Exhaustive comparison outside the exact owned additions/counter changes.
        var restored = after.DeepClone().AsObject();
        restored[Extension] = before[Extension]!.DeepClone();
        restored["dialogues"]![Sender]!["new"] = before["dialogues"]![Sender]!["new"]!.DeepClone();
        restored["dialogues"]![Sender]!["attachmentsNew"] = before["dialogues"]![Sender]!["attachmentsNew"]!.DeepClone();
        restored["dialogues"]![Sender]!["messages"]!.AsArray().RemoveAt(1);
        Assert.True(JsonNode.DeepEquals(before, restored));
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(result, claim, payment, Limits));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(500_000, 1)]
    [InlineData(500_001, 2)]
    [InlineData(10_000_000, 20)]
    public void AttachmentStacksAreBoundedAndSumExactly(long amount, int count)
    {
        var claim = Claim(amount);
        var payment = Payment(claim);
        var result = RewardProfileDocument.Apply(Enrolled(), claim, payment, Limits);
        var mail = LastMail(result);
        var items = mail["items"]!["data"]!.AsArray();
        Assert.Equal(count, items.Count);
        Assert.Equal(amount, items.Sum(item => item!["upd"]!["StackObjectsCount"]!.GetValue<long>()));
        Assert.All(items, item =>
        {
            Assert.InRange(item!["upd"]!["StackObjectsCount"]!.GetValue<long>(), 1, Limits.StackMax);
            Assert.Equal(RewardProfileDocument.RoubleTemplateId, item["_tpl"]!.GetValue<string>());
            Assert.Equal(mail["items"]!["stash"]!.GetValue<string>(), item["parentId"]!.GetValue<string>());
            Assert.Equal("main", item["slotId"]!.GetValue<string>());
            Assert.False(item["upd"]!["SpawnedInSession"]!.GetValue<bool>());
        });
        Assert.Equal(count, items.Select(item => item!["_id"]!.GetValue<string>()).Distinct().Count());
        Assert.Equal(payment.PaidAtUnixSeconds, mail["dt"]!.GetValue<long>());
        Assert.Equal(2_592_000, mail["maxStorageTime"]!.GetValue<int>());
        Assert.True(RewardProfileDocument.HasOriginalMail(result, payment, Limits));
    }

    [Fact]
    public void ConfiguredLimitsAreAppliedAndLedgerCapacityIsNotPruned()
    {
        var limits = new RewardLimits(300, 3, 1, 60);
        var claim = Claim(300);
        var payment = Payment(claim);
        string paid = RewardProfileDocument.Apply(Enrolled(), claim, payment, limits);
        Assert.Equal(100, LastMail(paid)["items"]!["data"]!.AsArray().Count);
        Assert.Equal(60, LastMail(paid)["maxStorageTime"]!.GetValue<int>());
        var next = Claim(1, 2);
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(paid, next, Payment(next), limits));
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.ReadLedger(paid, Binding, limits with { MaxRoublesPerReceipt = 299 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => RewardProfileDocument.ReadLedger(Enrolled(), Binding, limits with { StackMax = 2 }));
        var over = Claim(301);
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(Enrolled(), over, Payment(over), limits));
    }

    [Fact]
    public void MissingOrNullDialoguesAreInitializedAsNativeSystemDialogue()
    {
        foreach (bool remove in new[] { true, false })
        {
            var source = JsonNode.Parse(Enrolled())!.AsObject();
            if (remove) source.Remove("dialogues"); else source["dialogues"] = null;
            var claim = Claim(15);
            var paid = RewardProfileDocument.Apply(source.ToJsonString(), claim, Payment(claim), Limits);
            var dialogue = JsonNode.Parse(paid)!["dialogues"]![Sender]!;
            Assert.Equal(Sender, dialogue["_id"]!.GetValue<string>());
            Assert.Equal(7, dialogue["type"]!.GetValue<int>());
            Assert.False(dialogue["pinned"]!.GetValue<bool>());
            Assert.Equal(1, dialogue["new"]!.GetValue<int>());
            Assert.Equal(1, dialogue["attachmentsNew"]!.GetValue<int>());
            Assert.True(RewardProfileDocument.HasOriginalMail(paid, Payment(claim), Limits));
        }
    }

    [Fact]
    public void NativeProfileRoundTripRetainsPermanentExtensionAndCollectableMailShape()
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters()) options.Converters.Add(converter);
        var claim = Claim(750_001);
        var payment = Payment(claim);
        string paid = RewardProfileDocument.Apply(Enrolled(), claim, payment, Limits);
        var typed = JsonSerializer.Deserialize<SptProfile>(paid, options)!;
        Assert.True(typed.TryGetExtensionData(out var extra));
        Assert.True(extra!.ContainsKey(Extension));
        var dialogue = typed.DialogueRecords![new MongoId(Sender)];
        Assert.Equal(MessageType.SystemMessage, dialogue.Type);
        var mail = dialogue.Messages!.Single(m => m.Id == new MongoId(payment.MessageId));
        Assert.Equal(payment.PaidAtUnixSeconds, mail.DateTime);
        Assert.Equal(Limits.MailLifetimeSeconds, mail.MaxStorageTime);
        Assert.True(mail.HasRewards);
        Assert.False(mail.RewardCollected);
        Assert.Equal(750_001d, mail.Items!.Data!.Sum(item => item.Upd!.StackObjectsCount));
        string serialized = JsonSerializer.Serialize(typed, options);
        Assert.Equal(payment, Assert.Single(RewardProfileDocument.ReadLedger(serialized, Binding, Limits)));
        Assert.True(RewardProfileDocument.HasOriginalMail(serialized, payment, Limits));
        var again = JsonSerializer.Deserialize<SptProfile>(serialized, options)!;
        Assert.True(again.TryGetExtensionData(out var againExtra));
        Assert.True(againExtra!.ContainsKey("customOtherMod"));
        Assert.Equal(typed.CharacterData!.PmcData!.Inventory!.Items!.Count,
            again.CharacterData!.PmcData!.Inventory!.Items!.Count);
    }

    [Theory]
    [InlineData("{\"info\":{\"id\":\"0123456789abcdef01234567\",\"id\":\"0123456789abcdef01234567\"}}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"info\":{\"id\":\"111111111111111111111111\"}}")]
    [InlineData("{\"info\":{\"id\":\"0123456789abcdef01234567\",\"invalidOrUnloadableProfile\":true}}")]
    [InlineData("{\"info\":{\"id\":\"0123456789abcdef01234567\",\"invalidOrUnloadableProfile\":\"false\"}}")]
    public void InvalidProfileIdentityShapeAndDuplicatePropertiesAreRejected(string source)
        => Assert.Throws<RewardDataException>(() => RewardProfileDocument.Enroll(source, Binding));

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    public void NativeAbsentOrNullableInvalidFlagDoesNotInventAnInvalidProfile(string flag)
    {
        var source = JsonNode.Parse(Fixture())!;
        source["info"]!["invalidOrUnloadableProfile"] = JsonNode.Parse(flag);
        Assert.Empty(RewardProfileDocument.ReadLedger(RewardProfileDocument.Enroll(source.ToJsonString(), Binding), Binding, Limits));
    }

    [Fact]
    public void EveryLedgerShapeBindingAndPaymentFieldIsChecked()
    {
        var claim = Claim(10);
        string paid = RewardProfileDocument.Apply(Enrolled(), claim, Payment(claim), Limits);
        var edits = new Action<JsonNode>[]
        {
            p => p[Extension]!["schema"] = 2,
            p => p[Extension]!["schema"] = "1",
            p => p[Extension]!["unexpected"] = true,
            p => p[Extension]!["binding"]!["epoch"] = "33333333333333333333333333333333",
            p => p[Extension]!["binding"]!["profileId"] = "999999999999999999999999",
            p => p[Extension]!["binding"]!["installationId"] = "33333333333333333333333333333333",
            p => p[Extension]!["payments"] = new JsonObject(),
            p => p[Extension]!["payments"]!.AsArray().Add(p[Extension]!["payments"]![0]!.DeepClone()),
            p => p[Extension]!["payments"]![0]!["roubles"] = 0,
            p => p[Extension]!["payments"]![0]!["roubles"] = 10_000_001,
            p => p[Extension]!["payments"]![0]!["roubles"] = 1.5,
            p => p[Extension]!["payments"]![0]!["receiptKey"] = claim.RunId + ":01",
            p => p[Extension]!["payments"]![0]!["receiptKey"] = claim.RunId + ":0",
            p => p[Extension]!["payments"]![0]!["fingerprint"] = "garbage",
            p => p[Extension]!["payments"]![0]!["messageId"] = "999999999999999999999999",
            p => p[Extension]!["payments"]![0]!["paidAtUnixSeconds"] = 0,
            p => p[Extension]!["payments"]![0]!["paidAtUnixSeconds"] = long.MaxValue,
            p => p[Extension]!["payments"]![0]!["extra"] = "not v1"
        };
        foreach (var edit in edits)
        {
            var source = JsonNode.Parse(paid)!;
            edit(source);
            Assert.Throws<RewardDataException>(() => RewardProfileDocument.ReadLedger(source.ToJsonString(), Binding, Limits));
        }
        string duplicateSchema = paid.Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal);
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.ReadLedger(duplicateSchema, Binding, Limits));
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.ReadLedger(Fixture(), Binding, Limits));
    }

    [Fact]
    public void ChangedClaimOrPaymentCannotUseThePreparedMail()
    {
        var claim = Claim(100);
        var payment = Payment(claim);
        foreach (var changed in new[]
        {
            payment with { Roubles = 99 }, payment with { Fingerprint = new string('a', 64) },
            payment with { ReceiptKey = claim.RunId + ":2" }, payment with { MessageId = new string('a', 24) }
        })
            Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(Enrolled(), claim, changed, Limits));
    }

    [Fact]
    public void AnyGeneratedIdentifierAlreadyInProfileRefusesWholeDetachedApply()
    {
        var claim = Claim(750_001);
        var payment = Payment(claim);
        var mail = LastMail(RewardProfileDocument.Apply(Enrolled(), claim, payment, Limits));
        var ids = new[] { payment.MessageId, mail["items"]!["stash"]!.GetValue<string>(),
            mail["items"]!["data"]![0]!["_id"]!.GetValue<string>() };
        foreach (var id in ids)
        {
            var source = JsonNode.Parse(Enrolled())!;
            source["customOtherMod"]!["reservedId"] = id;
            string before = source.ToJsonString();
            Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(before, claim, payment, Limits));
            Assert.Equal(before, source.ToJsonString());
        }
    }

    [Fact]
    public void DamagedNativeSystemDialogueIsNotSilentlyRepaired()
    {
        var claim = Claim(1);
        foreach (var edit in new Action<JsonNode>[]
        {
            p => p["dialogues"] = new JsonArray(),
            p => p["dialogues"]![Sender] = null,
            p => p["dialogues"]![Sender]!["_id"] = "999999999999999999999999",
            p => p["dialogues"]![Sender]!["type"] = 2,
            p => p["dialogues"]![Sender]!["messages"] = null,
            p => p["dialogues"]![Sender]!["new"] = -1,
            p => p["dialogues"]![Sender]!["attachmentsNew"] = int.MaxValue
        })
        {
            var source = JsonNode.Parse(Enrolled())!;
            edit(source);
            Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(source.ToJsonString(), claim, Payment(claim), Limits));
        }
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("7.0")]
    [InlineData("7.5")]
    public void NativeDialogueIntegerCheckDoesNotCoerceStringsOrFloatingValues(string type)
    {
        var source = JsonNode.Parse(Enrolled())!;
        source["dialogues"]![Sender]!["type"] = JsonNode.Parse(type);
        var claim = Claim(1);
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(source.ToJsonString(), claim, Payment(claim), Limits));
    }

    [Fact]
    public void RecoveryProofRequiresOriginalUncollectedMailButLedgerSurvivesCollectionDeletionAndExpiry()
    {
        var claim = Claim(750_001);
        var payment = Payment(claim);
        string paid = RewardProfileDocument.Apply(Enrolled(), claim, payment, Limits);
        foreach (var edit in new Action<JsonNode>[]
        {
            m => m["hasRewards"] = false,
            m => m["rewardCollected"] = true,
            m => m["dt"] = payment.PaidAtUnixSeconds + 1,
            m => m["maxStorageTime"] = 1,
            m => m["uid"] = "999999999999999999999999",
            m => m["type"] = 2,
            m => m["items"]!["data"]![0]!["_id"] = "999999999999999999999999",
            m => m["items"]!["data"]![0]!["parentId"] = "999999999999999999999999",
            m => m["items"]!["data"]![0]!["_tpl"] = "999999999999999999999999",
            m => m["items"]!["data"]![0]!["upd"]!["StackObjectsCount"] = 1,
            m => m["items"]!["data"]!.AsArray().Clear(),
            m => m["items"] = new JsonObject(),
            m => m["profileChangeEvents"] = new JsonArray(new JsonObject { ["arbitrary"] = true })
        })
        {
            var source = JsonNode.Parse(paid)!;
            edit(source["dialogues"]![Sender]!["messages"]![1]!);
            Assert.False(RewardProfileDocument.HasOriginalMail(source.ToJsonString(), payment, Limits));
            Assert.Equal(payment, Assert.Single(RewardProfileDocument.ReadLedger(source.ToJsonString(), Binding, Limits)));
        }
        var deleted = JsonNode.Parse(paid)!;
        deleted["dialogues"]!.AsObject().Remove(Sender);
        Assert.False(RewardProfileDocument.HasOriginalMail(deleted.ToJsonString(), payment, Limits));
        Assert.Equal(payment, Assert.Single(RewardProfileDocument.ReadLedger(deleted.ToJsonString(), Binding, Limits)));
        var duplicated = JsonNode.Parse(paid)!;
        duplicated["dialogues"]![Sender]!["messages"]!.AsArray().Add(LastMail(paid).DeepClone());
        Assert.False(RewardProfileDocument.HasOriginalMail(duplicated.ToJsonString(), payment, Limits));
        var moved = JsonNode.Parse(paid)!;
        moved["characters"]!["pmc"]!["Inventory"]!["items"]!.AsArray()
            .Add(LastMail(paid)["items"]!["data"]![0]!.DeepClone());
        Assert.False(RewardProfileDocument.HasOriginalMail(moved.ToJsonString(), payment, Limits));
    }

    private static RewardClaim Claim(long amount, long sequence = 1) => new(Binding,
        "44444444444444444444444444444444", sequence, amount, "rub-v1");
    private static RewardPayment Payment(RewardClaim claim) => new(claim.Key, claim.Fingerprint, claim.Roubles,
        RewardIdentity.MessageId(claim.Binding, claim.Key), 1_790_000_000);
    private static string Enrolled() => RewardProfileDocument.Enroll(Fixture(), Binding);
    private static JsonNode LastMail(string profile) => JsonNode.Parse(profile)!["dialogues"]![Sender]!["messages"]!.AsArray().Last()!;

    // Synthetic packed inventory. The editor must not need a free cell or rewrite
    // game state. Unknown fields also exercise the native extension-data contract.
    private static string Fixture() => """
    {
      "info":{"id":"0123456789abcdef01234567","username":"synthetic-reward-test","invalidOrUnloadableProfile":false},
      "characters":{"pmc":{"_id":"0123456789abcdef01234567","Inventory":{"stash":"888888888888888888888888","items":[
        {"_id":"888888888888888888888888","_tpl":"566abbc34bdc2d92178b4576"},
        {"_id":"777777777777777777777777","_tpl":"5449016a4bdc2d6f028b456f","parentId":"888888888888888888888888","slotId":"hideout","location":{"x":0,"y":0,"r":"Horizontal"},"upd":{"StackObjectsCount":500000}},
        {"_id":"666666666666666666666666","_tpl":"5449016a4bdc2d6f028b456f","parentId":"888888888888888888888888","slotId":"hideout","location":{"x":1,"y":0,"r":"Horizontal"},"upd":{"StackObjectsCount":500000}}
      ],"customPacking":{"allCellsOccupied":true}}}},
      "customOtherMod":{"nested":[null,{"enabled":true,"largeInteger":9223372036854775807}],"text":"unchanged"},
      "dialogues":{
        "59e7125688a45068a6249071":{"_id":"59e7125688a45068a6249071","type":7,"new":2,"attachmentsNew":1,"pinned":true,"customMetadata":{"foo":"bar"},"messages":[
          {"_id":"555555555555555555555555","uid":"59e7125688a45068a6249071","type":7,"dt":1700000000,"text":"Existing message","hasRewards":false,"rewardCollected":true}
        ]},
        "999999999999999999999999":{"_id":"999999999999999999999999","type":2,"new":4,"attachmentsNew":0,"pinned":false,"custom":{"preserve":1},"messages":[]}
      }
    }
    """;
}
