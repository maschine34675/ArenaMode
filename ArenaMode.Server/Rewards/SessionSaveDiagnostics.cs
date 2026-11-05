using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Rewards;

// One authenticated switch attempt. Only safe metadata survives Capture; never retain profile JSON.
internal sealed class SessionSaveDiagnostics
{
    internal const int MaxSnapshotCharacters = 4 * 1024 * 1024;
    private const int MaxFields = 3;
    // Schema labels, never paths discovered in player data. Dictionaries/arrays stay opaque.
    // Specific children precede their parent fallback. A sample is not a complete profile diff.
    private static readonly string[] KnownSections =
    [
        "characters.pmc.Hideout.sptUpdateLastRunTimestamp", "characters.pmc.Hideout.Production",
        "characters.pmc.Hideout.Areas", "characters.pmc.Hideout",
        "characters.pmc.Inventory", "characters.pmc.Info", "characters.pmc.Skills",
        "characters.pmc.Quests", "characters.pmc.Health", "characters.pmc.TradersInfo",
        "characters.pmc.Stats", "characters.pmc", "characters.scav", "inraid", "info", "dialogues"
    ];
    private string _profile = "unavailable";
    private string _role = "unavailable";
    private string _diskMatch = "unknown";
    private string _ramStable = "unknown";
    private string _status = "unavailable";
    private readonly List<string> _fields = [];
    private bool _reported;

    internal void Capture(string profileId, string selectedProfileId, string before, string disk, string after)
    {
        // Diagnostics must not replace the original refusal (including its short-circuit semantics).
        try
        {
            _profile = RewardIdentity.Hash(profileId)[..8];
            _role = profileId == selectedProfileId ? "selected" : "other-loaded";
            if (before.Length > MaxSnapshotCharacters || disk.Length > MaxSnapshotCharacters
                || after.Length > MaxSnapshotCharacters)
            {
                _status = "size-limit";
                return;
            }
            var expected = Parse(before);
            _diskMatch = Compare(expected, disk, "disk");
            _ramStable = Compare(expected, after, "ram");
            _status = _diskMatch == "unknown" || _ramStable == "unknown" ? "partial" : "sample";
        }
        catch { } // Invalid/too-deep JSON or a diagnostic failure is not a different save outcome.
    }

    private string Compare(JsonNode? expected, string json, string direction)
    {
        try
        {
            var actual = Parse(json);
            bool equal = JsonNode.DeepEquals(expected, actual);
            if (!equal) SampleFields(expected, actual, direction);
            return equal ? "true" : "false";
        }
        catch { return "unknown"; }
    }

    private static JsonNode? Parse(string json)
    {
        // Match SameProfile's duplicate rejection and DeepEquals semantics, including numeric values.
        using var document = JsonDocument.Parse(json);
        RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
        return JsonNode.Parse(json);
    }

    private void SampleFields(JsonNode? expected, JsonNode? actual, string direction)
    {
        var found = new List<string>();
        foreach (string path in KnownSections)
        {
            if (found.Any(child => child.StartsWith(path + ".", StringComparison.Ordinal))) continue;
            bool had = TrySection(expected, path, out var a);
            bool has = TrySection(actual, path, out var b);
            if (had == has && JsonNode.DeepEquals(a, b)) continue;
            found.Add(path);
            if (_fields.Count < MaxFields) _fields.Add(direction + ":" + path);
            if (_fields.Count == MaxFields) return;
        }
        if (found.Count == 0 && _fields.Count < MaxFields) _fields.Add(direction + ":unclassified-section");
    }

    private static bool TrySection(JsonNode? node, string path, out JsonNode? value)
    {
        foreach (string part in path.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node))
            {
                value = null;
                return false;
            }
        }
        value = node;
        return true; // An explicit null differs from an absent property.
    }

    internal void WarnRefusal(bool entering, RewardReturnReply? reply, bool resumeDecisionConfirmed,
        Action<string> warning)
    {
        if (_reported || reply is null || reply.Accepted) return;
        _reported = true;
        try
        {
            string readback = reply.Detail == "return-profile-save-not-confirmed"
                ? $" profile={_profile} ({_role}); ramDiskMatch={_diskMatch}; ramStableDuringReadback={_ramStable};"
                    + $" fieldSample=[{string.Join(",", _fields)}]; diagnostics={_status};"
                : "";
            string resume = resumeDecisionConfirmed ? (reply.CanResumeNormal ? "true" : "false") : "unconfirmed";
            warning($"[ArenaMode] Session switch refused: {reply.Stage}/{reply.Detail}; operation={(entering ? "enter" : "return")};"
                + readback + $" active requests={reply.ActiveRequests}, active updates={reply.ActiveUpdates};"
                + $" updateStopStarted={(reply.Held ? "true" : "false")}; canResumeNormal={resume}."
                + (reply.Held ? " Server kept in memory; do not close it; retry the session switch." : ""));
        }
        catch { } // A logging sink must not change the reply, release a hold or schedule shutdown.
    }
}
