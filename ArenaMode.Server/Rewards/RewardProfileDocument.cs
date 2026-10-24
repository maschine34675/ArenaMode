using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Rewards;

// Detached JSON only. Persistence, exclusive access and notification ordering belong
// to the settlement lease; this editor never opens a profile or sends a notification.
internal static class RewardProfileDocument
{
    internal const string ExtensionKey = "com.maschine.ArenaMode.rewards";
    internal const string SystemSenderId = "59e7125688a45068a6249071";
    internal const string RoubleTemplateId = "5449016a4bdc2d6f028b456f";
    private const long LastUnixSecond = 253402300799;

    internal static string Enroll(string profileJson, RewardBinding binding)
    {
        if (binding is null) throw new RewardDataException("missing-binding");
        binding.Validate();
        var profile = Parse(profileJson);
        ValidateProfile(profile, binding);
        if (profile.ContainsKey(ExtensionKey))
            throw new RewardDataException("reward-enrollment-already-present");
        profile[ExtensionKey] = new JsonObject
        {
            ["schema"] = 1,
            ["binding"] = WriteBinding(binding),
            ["payments"] = new JsonArray()
        };
        return profile.ToJsonString();
    }

    internal static IReadOnlyList<RewardPayment> ReadLedger(string profileJson, RewardBinding binding, RewardLimits limits)
    {
        limits.Validate();
        if (binding is null) throw new RewardDataException("missing-binding");
        binding.Validate();
        return ReadLedger(Parse(profileJson), binding, limits);
    }

    internal static string Apply(string profileJson, RewardClaim claim, RewardPayment payment, RewardLimits limits)
    {
        limits.Validate();
        if (claim is null) throw new RewardDataException("missing-claim");
        claim.Validate(limits);
        ValidatePayment(payment, claim.Binding, limits);
        if (payment.ReceiptKey != claim.Key || payment.Fingerprint != claim.Fingerprint || payment.Roubles != claim.Roubles)
            throw new RewardDataException("payment-claim-mismatch");

        var profile = Parse(profileJson);
        var ledger = ReadLedger(profile, claim.Binding, limits);
        // Duplicate handling is the settlement engine's responsibility. Never append
        // another mail, or silently repair a consumed/changed mail, in this editor.
        if (ledger.Any(p => p.ReceiptKey == payment.ReceiptKey || p.MessageId == payment.MessageId
            || p.Fingerprint == payment.Fingerprint))
            throw new RewardDataException("payment-already-present");
        if (ledger.Count >= limits.MaxReceipts)
            throw new RewardDataException("reward-ledger-full");

        var message = CreateMessage(payment, limits);
        var generatedIds = new HashSet<string>(StringComparer.Ordinal) { payment.MessageId, StashId(payment) };
        foreach (var item in (JsonArray)message["items"]!["data"]!)
            if (!generatedIds.Add(String(item!["_id"])))
                throw new RewardDataException("reward-id-collision");
        if (generatedIds.Count != ((JsonArray)message["items"]!["data"]!).Count + 2
            || ContainsAnyId(profile, generatedIds))
            throw new RewardDataException("reward-id-collision");

        var dialogues = OptionalObject(profile, "dialogues");
        var dialogue = GetOrCreateSystemDialogue(dialogues);
        var messages = RequireArray(dialogue["messages"]);
        // Preserve existing native dialogue metadata and messages. Only the two
        // native unread counters change, exactly as SendMessageToPlayer does.
        dialogue["new"] = IncrementCounter(dialogue, "new");
        dialogue["attachmentsNew"] = IncrementCounter(dialogue, "attachmentsNew");
        messages.Add(message);
        ((JsonArray)profile[ExtensionKey]!["payments"]!).Add(WritePayment(payment));
        return profile.ToJsonString();
    }

    internal static bool HasOriginalMail(string profileJson, RewardPayment payment, RewardLimits limits)
    {
        limits.Validate();
        var profile = Parse(profileJson);
        var extension = RequireObject(profile[ExtensionKey]);
        var binding = ReadBinding(extension["binding"]);
        var ledger = ReadLedger(profile, binding, limits);
        ValidatePayment(payment, binding, limits);
        if (!ledger.Contains(payment)) return false;
        if (profile["dialogues"] is not JsonObject dialogues
            || dialogues[SystemSenderId] is not JsonObject dialogue)
            return false;
        ValidateSystemDialogue(dialogue);
        var messages = RequireArray(dialogue["messages"]);
        var matches = messages.OfType<JsonObject>()
            .Where(m => TryString(m["_id"]) == payment.MessageId).ToList();
        if (matches.Count != 1 || !JsonNode.DeepEquals(matches[0], CreateMessage(payment, limits)))
            return false;

        // A matching mail must not hide a second copy of its message, attachment
        // or attachment-stash identity anywhere else in the detached profile.
        var ids = new HashSet<string>(StringComparer.Ordinal) { payment.MessageId, StashId(payment) };
        foreach (var item in (JsonArray)matches[0]["items"]!["data"]!)
            ids.Add(String(item!["_id"]));
        var withoutOwnedEvidence = (JsonObject)profile.DeepClone();
        ((JsonArray)withoutOwnedEvidence["dialogues"]![SystemSenderId]!["messages"]!)
            .RemoveAt(messages.IndexOf(matches[0]));
        // Ledger messageId is intentionally a reference, not a second native item.
        withoutOwnedEvidence.Remove(ExtensionKey);
        return !ContainsAnyId(withoutOwnedEvidence, ids);
    }

    private static IReadOnlyList<RewardPayment> ReadLedger(JsonObject profile, RewardBinding binding, RewardLimits limits)
    {
        ValidateProfile(profile, binding);
        var extension = RequireObject(profile[ExtensionKey]);
        RequireFields(extension, "schema", "binding", "payments");
        if (Integer(extension["schema"]) != 1 || ReadBinding(extension["binding"]) != binding)
            throw new RewardDataException("reward-enrollment-mismatch");
        var payments = RequireArray(extension["payments"]);
        if (payments.Count > limits.MaxReceipts)
            throw new RewardDataException("reward-ledger-too-large");
        var result = new List<RewardPayment>(payments.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var messages = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in payments)
        {
            var entry = RequireObject(value);
            RequireFields(entry, "receiptKey", "fingerprint", "roubles", "messageId", "paidAtUnixSeconds");
            var payment = new RewardPayment(String(entry["receiptKey"]), String(entry["fingerprint"]),
                Integer(entry["roubles"]), String(entry["messageId"]), Integer(entry["paidAtUnixSeconds"]));
            ValidatePayment(payment, binding, limits);
            if (!keys.Add(payment.ReceiptKey) || !messages.Add(payment.MessageId) || !fingerprints.Add(payment.Fingerprint))
                throw new RewardDataException("duplicate-payment");
            result.Add(payment);
        }
        return result.AsReadOnly();
    }

    private static void ValidatePayment(RewardPayment payment, RewardBinding binding, RewardLimits limits)
    {
        if (payment is null || payment.ReceiptKey is null)
            throw new RewardDataException("invalid-payment");
        var parts = payment.ReceiptKey.Split(':');
        if (parts.Length != 2 || !RewardIdentity.IsGuid(parts[0])
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || sequence <= 0 || sequence.ToString(CultureInfo.InvariantCulture) != parts[1]
            || !RewardIdentity.IsHex(payment.Fingerprint, 64)
            || payment.Roubles <= 0 || payment.Roubles > limits.MaxRoublesPerReceipt
            || payment.MessageId != RewardIdentity.MessageId(binding, payment.ReceiptKey)
            || payment.PaidAtUnixSeconds <= 0 || payment.PaidAtUnixSeconds > LastUnixSecond - limits.MailLifetimeSeconds)
            throw new RewardDataException("invalid-payment");
    }

    private static void ValidateProfile(JsonObject profile, RewardBinding binding)
    {
        var info = RequireObject(profile["info"]);
        if (String(info["id"]) != binding.ProfileId)
            throw new RewardDataException("wrong-profile");
        if (info["invalidOrUnloadableProfile"] is { } invalid && Boolean(invalid))
            throw new RewardDataException("invalid-profile");
    }

    private static RewardBinding ReadBinding(JsonNode? value)
    {
        var binding = RequireObject(value);
        RequireFields(binding, "installationId", "profileId", "epoch");
        var result = new RewardBinding(String(binding["installationId"]), String(binding["profileId"]), String(binding["epoch"]));
        result.Validate();
        return result;
    }

    private static JsonObject WriteBinding(RewardBinding binding) => new()
    {
        ["installationId"] = binding.InstallationId, ["profileId"] = binding.ProfileId, ["epoch"] = binding.Epoch
    };

    private static JsonObject WritePayment(RewardPayment payment) => new()
    {
        ["receiptKey"] = payment.ReceiptKey, ["fingerprint"] = payment.Fingerprint, ["roubles"] = payment.Roubles,
        ["messageId"] = payment.MessageId, ["paidAtUnixSeconds"] = payment.PaidAtUnixSeconds
    };

    private static JsonObject CreateMessage(RewardPayment payment, RewardLimits limits)
    {
        var data = new JsonArray();
        long remaining = payment.Roubles;
        for (int index = 0; remaining > 0; index++)
        {
            long count = Math.Min(remaining, limits.StackMax);
            data.Add(new JsonObject
            {
                ["_id"] = AttachmentId(payment, index), ["_tpl"] = RoubleTemplateId,
                ["parentId"] = StashId(payment), ["slotId"] = "main",
                ["upd"] = new JsonObject { ["StackObjectsCount"] = count, ["SpawnedInSession"] = false }
            });
            remaining -= count;
        }
        return new JsonObject
        {
            ["_id"] = payment.MessageId, ["uid"] = SystemSenderId, ["type"] = 7,
            ["dt"] = payment.PaidAtUnixSeconds,
            ["text"] = "Arena reward: " + payment.Roubles.ToString(CultureInfo.InvariantCulture) + " RUB.",
            ["hasRewards"] = true, ["rewardCollected"] = false,
            ["items"] = new JsonObject { ["stash"] = StashId(payment), ["data"] = data },
            ["maxStorageTime"] = limits.MailLifetimeSeconds
        };
    }

    private static string StashId(RewardPayment payment) => RewardIdentity.Hash("arena-mail-stash-v1\n" + payment.MessageId)[..24];
    private static string AttachmentId(RewardPayment payment, int index) => RewardIdentity.Hash(
        "arena-mail-item-v1\n" + payment.MessageId + "\n" + index.ToString(CultureInfo.InvariantCulture))[..24];

    private static JsonObject GetOrCreateSystemDialogue(JsonObject dialogues)
    {
        if (!dialogues.ContainsKey(SystemSenderId))
            dialogues[SystemSenderId] = new JsonObject
            {
                ["_id"] = SystemSenderId, ["type"] = 7, ["messages"] = new JsonArray(),
                ["pinned"] = false, ["new"] = 0, ["attachmentsNew"] = 0
            };
        var dialogue = RequireObject(dialogues[SystemSenderId]);
        ValidateSystemDialogue(dialogue);
        return dialogue;
    }

    private static void ValidateSystemDialogue(JsonObject dialogue)
    {
        if (String(dialogue["_id"]) != SystemSenderId || Integer(dialogue["type"]) != 7)
            throw new RewardDataException("system-dialogue-mismatch");
        _ = RequireArray(dialogue["messages"]);
    }

    private static int IncrementCounter(JsonObject dialogue, string name)
    {
        long current = dialogue[name] is null ? 0 : Integer(dialogue[name]);
        if (current < 0 || current >= int.MaxValue)
            throw new RewardDataException("invalid-mail-counter");
        return (int)current + 1;
    }

    private static JsonObject OptionalObject(JsonObject parent, string name)
    {
        if (parent[name] is null) parent[name] = new JsonObject();
        return RequireObject(parent[name]);
    }

    private static bool ContainsAnyId(JsonNode? node, HashSet<string> ids)
    {
        if (node is JsonObject obj)
            return obj.Any(p => ids.Contains(p.Key) || ContainsAnyId(p.Value, ids));
        if (node is JsonArray array) return array.Any(value => ContainsAnyId(value, ids));
        return TryString(node) is { } text && ids.Contains(text);
    }

    private static JsonObject Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicateProperties(document.RootElement);
            return RequireObject(JsonNode.Parse(json));
        }
        catch (JsonException) { throw new RewardDataException("invalid-profile-json"); }
        catch (ArgumentException) { throw new RewardDataException("invalid-profile-json"); }
    }

    private static void RejectDuplicateProperties(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new RewardDataException("duplicate-json-property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var value in node.EnumerateArray()) RejectDuplicateProperties(value);
    }

    private static void RequireFields(JsonObject obj, params string[] names)
    {
        if (obj.Count != names.Length || names.Any(name => !obj.ContainsKey(name)))
            throw new RewardDataException("unexpected-reward-fields");
    }

    private static JsonObject RequireObject(JsonNode? node) => node as JsonObject ?? throw new RewardDataException("expected-object");
    private static JsonArray RequireArray(JsonNode? node) => node as JsonArray ?? throw new RewardDataException("expected-array");
    private static string? TryString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string String(JsonNode? node) => TryString(node) ?? throw new RewardDataException("expected-string");
    private static long Integer(JsonNode? node)
    {
        // JsonNode.Parse supplies JsonElement-backed values, whereas newly created
        // native dialogue fields retain their CLR int type. Do not coerce strings
        // or floating-point values while accepting either actual integer form.
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var wide)) return wide;
            if (value.TryGetValue<int>(out var narrow)) return narrow;
        }
        throw new RewardDataException("expected-integer");
    }
    private static bool Boolean(JsonNode node) => node is JsonValue value && value.TryGetValue<bool>(out var flag)
        ? flag : throw new RewardDataException("expected-boolean");
}
