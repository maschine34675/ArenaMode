using System.Globalization;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Rewards;

// The payment stores only a catalog-bound decision and the signed delta hash.
// Item quantities are always derived from the reviewed catalog, never imported.
internal static class RewardColaProfile
{
    internal static string? Resolve(string profileJson, string? deltaJson)
    {
        if (deltaJson is null) return null;
        var delta = RewardColaDelta.Read(deltaJson);
        var profile = RewardColaDelta.ParseObject(profileJson, 64 * 1024 * 1024);
        var achievements = Achievements(profile, create: false);
        var added = delta.Achievements.Select(k => RewardColaCatalog.Achievements[k].Id)
            .Where(id => !AlreadyEarned(achievements, id)).Order(StringComparer.Ordinal).ToArray();
        return Write(RewardIdentity.Hash(deltaJson), added);
    }

    internal static bool MatchesClaim(string? payment, string? delta)
    {
        if (payment is null || delta is null) return payment is null && delta is null;
        var resolved = Read(payment);
        var changes = RewardColaDelta.Read(delta);
        return resolved.Hash == RewardIdentity.Hash(delta) && resolved.Ids.All(id =>
            changes.Achievements.Contains(RewardColaCatalog.ById[id].Key, StringComparer.Ordinal));
    }

    internal static IReadOnlyList<string> Validate(string json) => Read(json).Ids;

    internal static void Apply(JsonObject profile, RewardClaim claim, RewardPayment payment)
    {
        if (claim.Cola is null && payment.Cola is null) return;
        if (!MatchesClaim(payment.Cola, claim.Cola) || Resolve(profile.ToJsonString(), claim.Cola) != payment.Cola)
            throw new RewardDataException("cola-payment-claim-mismatch");
        var ids = Read(payment.Cola!).Ids;
        if (ids.Count == 0) return;
        var target = Achievements(profile, create: true);
        foreach (var id in ids) target.Add(id, payment.PaidAtUnixSeconds);
    }

    internal static void ValidateApplied(JsonObject profile, RewardPayment payment)
    {
        if (payment.Cola is null) return;
        var achievements = Achievements(profile, create: false);
        foreach (var id in Read(payment.Cola).Ids)
            if (!achievements.TryGetPropertyValue(id, out var node) || RewardColaDelta.Integer(node) != payment.PaidAtUnixSeconds)
                throw new RewardDataException("cola-paid-achievement-missing-or-changed");
    }

    internal static void AddAttachments(JsonArray items, RewardPayment payment, string stash, int stackMax)
    {
        if (payment.Cola is null) return;
        int index = 0;
        foreach (var id in Read(payment.Cola).Ids)
        {
            foreach (var reward in RewardColaCatalog.ById[id].Items)
            {
                // Bottles and GP coins are safe as one item per unit, independent
                // of modded stack limits. Each catalog RUB grant is <500000.
                bool currency = reward.Template == RewardProfileDocument.RoubleTemplateId;
                int units = currency ? (reward.Count - 1) / stackMax + 1 : reward.Count;
                for (int unit = 0; unit < units; unit++)
                {
                    if (++index > 1024) throw new RewardDataException("cola-attachment-capacity");
                    items.Add(new JsonObject
                    {
                        ["_id"] = RewardIdentity.Hash("arena-cola-item-v1\n" + payment.MessageId + "\n"
                            + index.ToString(CultureInfo.InvariantCulture))[..24],
                        ["_tpl"] = reward.Template, ["parentId"] = stash, ["slotId"] = "main",
                        ["upd"] = new JsonObject
                        {
                            ["StackObjectsCount"] = currency ? Math.Min(stackMax, reward.Count - unit * stackMax) : 1,
                            ["SpawnedInSession"] = true
                        }
                    });
                }
            }
        }
    }

    internal static string MessageSuffix(string? payment) => payment is null ? "" :
        " Tarkova-Cola research recorded; " + Read(payment).Ids.Count.ToString(CultureInfo.InvariantCulture)
        + " new achievement(s) credited. Collect any achievement items below.";

    private static (string Hash, IReadOnlyList<string> Ids) Read(string json)
    {
        var obj = RewardColaDelta.ParseObject(json, RewardColaDelta.MaxBytes);
        RewardColaDelta.Fields(obj, "schema", "catalog", "delta", "ach");
        string hash = RewardColaDelta.Text(obj["delta"]);
        if (RewardColaDelta.Integer(obj["schema"]) != 1
            || RewardColaDelta.Text(obj["catalog"]) != RewardColaCatalog.Version || !RewardIdentity.IsHex(hash, 64))
            throw new RewardDataException("cola-payment-version-or-hash");
        var ids = RewardColaDelta.Set(obj["ach"], RewardColaCatalog.ById.ContainsKey);
        if (json != Write(hash, ids)) throw new RewardDataException("cola-payment-noncanonical");
        return (hash, ids);
    }

    private static string Write(string hash, IEnumerable<string> ids) => new JsonObject
    {
        ["schema"] = 1, ["catalog"] = RewardColaCatalog.Version, ["delta"] = hash,
        ["ach"] = RewardColaDelta.Array(ids)
    }.ToJsonString();

    private static JsonObject Achievements(JsonObject profile, bool create)
    {
        var pmc = RewardColaDelta.Object(RewardColaDelta.Object(profile["characters"])["pmc"]);
        if (pmc["Achievements"] is null)
        {
            if (!create) return new();
            pmc["Achievements"] = new JsonObject();
        }
        return RewardColaDelta.Object(pmc["Achievements"]);
    }

    private static bool AlreadyEarned(JsonObject achievements, string id)
    {
        if (!achievements.TryGetPropertyValue(id, out var node)) return false;
        if (RewardColaDelta.Integer(node) <= 0) throw new RewardDataException("cola-invalid-native-achievement");
        return true;
    }
}
