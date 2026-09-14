#nullable disable

using ArenaMode.Combat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Modes;

public enum ArenaScoreboardSide
{
    Friendly,
    Opponent,
}

/// <summary>
/// Plain actor data copied from the current world. A scoreboard never keeps a
/// Player or BotOwner alive: both are disposable bodies in an Arena raid.
/// StableId is reserved for identities that really survive body replacement
/// (currently the local player and authenticated PitFireTeam account IDs).
/// </summary>
public sealed class ArenaScoreboardActor
{
    public ArenaScoreboardActor(
        string profileId,
        string displayName,
        string roleLabel,
        bool alive,
        string stableId = null)
    {
        ProfileId = profileId;
        DisplayName = displayName;
        RoleLabel = roleLabel;
        Alive = alive;
        StableId = stableId;
    }

    public string ProfileId { get; }
    public string DisplayName { get; }
    public string RoleLabel { get; }
    public bool Alive { get; }
    public string StableId { get; }
}

/// <summary>One logical contestant. All fields are match-local presentation data.</summary>
public sealed class ArenaScoreboardEntry
{
    internal ArenaScoreboardEntry(
        string key,
        string displayName,
        string roleLabel,
        ArenaScoreboardSide side,
        bool local,
        int order)
    {
        Key = key;
        DisplayName = displayName;
        RoleLabel = roleLabel;
        Side = side;
        IsLocal = local;
        Order = order;
    }

    public string Key { get; }
    public string DisplayName { get; internal set; }
    public string RoleLabel { get; internal set; }
    public ArenaScoreboardSide Side { get; }
    public bool IsLocal { get; }
    public bool IsActive { get; internal set; }
    public bool IsCurrentRoster { get; internal set; }
    public int RosterEpoch { get; internal set; }
    public int Score { get; internal set; }
    public int Kills { get; internal set; }
    public int Deaths { get; internal set; }
    public int Headshots { get; internal set; }
    public int Streak { get; internal set; }
    public int BestStreak { get; internal set; }

    internal int Order { get; }
    internal string ActiveProfileId { get; set; }
    // Separate from match-long account scoring: only proved kills of the same
    // physical life can make its eventual victim eligible for Shutdown.
    internal long AwardBodyId { get; set; }
    internal int AwardLifeStreak { get; set; }
}

public readonly struct ArenaScoreboardTotals
{
    public ArenaScoreboardTotals(int score, int kills, int deaths, int headshots)
    {
        Score = score;
        Kills = kills;
        Deaths = deaths;
        Headshots = headshots;
    }

    public int Score { get; }
    public int Kills { get; }
    public int Deaths { get; }
    public int Headshots { get; }
}

/// <summary>
/// Match-long standings independent of Unity objects. Ordinary bots are honest
/// one-body contestants keyed by profile ID. Random replacements therefore do
/// not inherit another bot's personal score; the opponent/team total provides
/// the durable comparison. Authenticated PFT companions are the exception:
/// their stable account key deliberately carries their score to a new body.
/// </summary>
public sealed class ArenaScoreboard
{
    private const string LocalKey = "local";
    private const string StableKeyPrefix = "stable:";
    private const string BodyKeyPrefix = "body:";
    private const int RetainedOrdinaryHistoryLimit = 32;
    private const int RetainedTopRuns = RetainedOrdinaryHistoryLimit / 2;

    private readonly Dictionary<string, ArenaScoreboardEntry> _entriesByKey =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArenaScoreboardEntry> _entriesByProfile =
        new(StringComparer.Ordinal);
    private readonly HashSet<ArenaScoreboardEntry> _currentRoster = new();
    private readonly MutableTotals _archivedFriendly = new();
    private readonly MutableTotals _archivedOpponent = new();
    // Voluntary TDM deaths belong to the opposing team, never a made-up bot.
    // Keep these outside participant history so roster compaction cannot erase
    // them or pretend a nonexistent individual earned kills/headshots/streaks.
    private int _opponentForfeitKills;
    private int _nextOrder;
    private int _historicalParticipantCount;
    private int _archivedParticipantCount;

    public ArenaScoreboard(string localProfileId, string localDisplayName, string localRoleLabel)
    {
        Local = Create(
            LocalKey,
            SafeText(localDisplayName, "You"),
            SafeText(localRoleLabel, "PMC"),
            ArenaScoreboardSide.Friendly,
            local: true);
        Local.IsCurrentRoster = true;
        Local.IsActive = true;
        Local.RosterEpoch = 1;
        CurrentRosterEpoch = 1;
        BindProfile(Local, localProfileId);
        _currentRoster.Add(Local);
    }

    public ArenaScoreboardEntry Local { get; }
    public int CurrentRosterEpoch { get; private set; }
    /// <summary>Rows currently retained for standings and identity resolution.</summary>
    public int ParticipantCount => _entriesByKey.Count;
    /// <summary>Logical contestants seen over the whole match, including compacted runs.</summary>
    public int HistoricalParticipantCount => _historicalParticipantCount;
    public int ArchivedParticipantCount => _archivedParticipantCount;
    public int TrackedProfileCount => _entriesByProfile.Count;
    public int Version { get; private set; }

    /// <summary>
    /// Atomically replaces the current physical roster. A rejected stale or
    /// ambiguous snapshot leaves the previous board intact.
    /// </summary>
    public bool TrySynchronizeRoster(
        int epoch,
        ArenaScoreboardActor local,
        IReadOnlyList<ArenaScoreboardActor> friendlies,
        IReadOnlyList<ArenaScoreboardActor> opponents,
        out string problem)
    {
        friendlies ??= Array.Empty<ArenaScoreboardActor>();
        opponents ??= Array.Empty<ArenaScoreboardActor>();

        if (epoch <= 0)
        {
            problem = "roster epoch must be positive";
            return false;
        }

        if (epoch < CurrentRosterEpoch)
        {
            problem = $"roster epoch {epoch} is older than {CurrentRosterEpoch}";
            return false;
        }

        if (!TryPlan(local, friendlies, opponents, out var plan, out problem))
            return false;

        if (!RosterWouldChange(epoch, plan))
        {
            problem = null;
            return true;
        }

        foreach (ArenaScoreboardEntry entry in _currentRoster)
        {
            entry.IsCurrentRoster = false;
            entry.IsActive = false;
            ReleaseCurrentProfile(entry);
        }
        _currentRoster.Clear();

        CurrentRosterEpoch = epoch;
        foreach (RosterBinding binding in plan)
        {
            ArenaScoreboardEntry entry = GetOrCreate(binding);
            entry.IsCurrentRoster = true;
            entry.IsActive = binding.Actor.Alive;
            entry.RosterEpoch = epoch;
            entry.DisplayName = SafeText(binding.Actor.DisplayName, entry.DisplayName);
            entry.RoleLabel = SafeText(binding.Actor.RoleLabel, entry.RoleLabel);
            BindProfile(entry, binding.Actor.ProfileId);
            _currentRoster.Add(entry);
        }

        TrimOrdinaryHistory();
        Version++;
        problem = null;
        return true;
    }

    /// <summary>
    /// Captures every observed kill. Local deaths are deliberately deferred to
    /// ReportLocalDeath: EFT also raises the respawn driver's death event, and
    /// counting both sources would add two deaths for an ordinary gun kill.
    /// </summary>
    public void ReportKill(ArenaKill kill)
    {
        if (kill == null || (!kill.ScoreEligible
                            && !kill.KillerIsFriendly.HasValue && !kill.VictimIsFriendly.HasValue))
            return;

        ArenaScoreboardEntry killer = Resolve(
            kill.KillerProfileId,
            kill.KillerName,
            kill.KillerRoleLabel,
            forceLocal: kill.KilledByLocal,
            friendly: kill.KillerIsFriendly);
        ArenaScoreboardEntry victim = Resolve(
            kill.VictimProfileId,
            kill.VictimName,
            kill.VictimRoleLabel,
            forceLocal: kill.LocalIsVictim,
            friendly: kill.VictimIsFriendly);
        if (killer == null || victim == null || ReferenceEquals(killer, victim))
            return;

        if (kill.ScoreEligible)
        {
            if (kill.AwardEligible && kill.KillerBodyId != 0
                && IsCurrentBody(killer, kill.KillerProfileId))
            {
                if (killer.AwardBodyId != kill.KillerBodyId)
                {
                    killer.AwardBodyId = kill.KillerBodyId;
                    killer.AwardLifeStreak = 0;
                }
                killer.AwardLifeStreak++;
            }
            killer.Kills++;
            killer.Streak++;
            if (killer.Streak > killer.BestStreak)
                killer.BestStreak = killer.Streak;

            int points = ScoreRules.PointsFor(kill.VictimRole);
            if (kill.Headshot)
            {
                killer.Headshots++;
                points += ScoreRules.HeadshotBonus;
            }

            if (killer.Streak > 0 && killer.Streak % ScoreRules.StreakBonusEvery == 0)
                points += ScoreRules.StreakBonus;
            killer.Score += points;
        }

        if (!kill.LocalIsVictim)
        {
            if (victim.AwardBodyId == kill.VictimBodyId)
                victim.AwardLifeStreak = 0;
            victim.Deaths++;
            victim.Streak = 0;
            MarkProfileInactive(victim, kill.VictimProfileId);
        }

        Version++;
    }

    /// <summary>Presentation-only read performed before ReportKill retires the victim.</summary>
    internal bool IsShutdown(ArenaKill kill)
    {
        if (kill == null || !kill.KilledByLocal || kill.LocalIsVictim
            || !kill.AwardEligible || !kill.ScoreEligible || kill.VictimIsFriendly == true
            || kill.KillerIsFriendly == false
            || kill.KillerBodyId == 0 || kill.VictimBodyId == 0
            || kill.KillerBodyId == kill.VictimBodyId
            || Normalize(kill.KillerProfileId) == null
            || !string.Equals(Local.ActiveProfileId, Normalize(kill.KillerProfileId), StringComparison.Ordinal))
            return false;
        string profile = Normalize(kill.VictimProfileId);
        return profile != null && _entriesByProfile.TryGetValue(profile, out var victim)
            && !victim.IsLocal && victim.Side == ArenaScoreboardSide.Opponent
            && IsCurrentBody(victim, profile) && victim.AwardBodyId == kill.VictimBodyId
            && victim.AwardLifeStreak >= 5;
    }

    private bool IsCurrentBody(ArenaScoreboardEntry entry, string profileId)
        => entry.IsActive && entry.IsCurrentRoster && entry.RosterEpoch == CurrentRosterEpoch
            && string.Equals(entry.ActiveProfileId, Normalize(profileId), StringComparison.Ordinal);

    public void ReportLocalDeath()
    {
        Local.Deaths++;
        Local.Streak = 0;
        Local.AwardLifeStreak = 0;
        Local.IsActive = false;
        Version++;
    }

    internal void ReportOpponentForfeitKill()
    {
        _opponentForfeitKills++;
        Version++;
    }

    public void ReportLocalRespawn(string profileId, string displayName, string roleLabel)
    {
        string normalizedProfile = Normalize(profileId);
        if (normalizedProfile != null
            && _entriesByProfile.TryGetValue(normalizedProfile, out var existing)
            && !ReferenceEquals(existing, Local))
        {
            return;
        }

        string safeName = SafeText(displayName, Local.DisplayName);
        string safeRole = SafeText(roleLabel, Local.RoleLabel);
        bool changed = !string.Equals(Local.DisplayName, safeName, StringComparison.Ordinal)
                       || !string.Equals(Local.RoleLabel, safeRole, StringComparison.Ordinal)
                       || !string.Equals(Local.ActiveProfileId, normalizedProfile, StringComparison.Ordinal)
                       || !Local.IsActive
                       || !Local.IsCurrentRoster;
        if (!changed)
            return;

        ReleaseCurrentProfile(Local);
        Local.DisplayName = safeName;
        Local.RoleLabel = safeRole;
        Local.IsActive = true;
        Local.IsCurrentRoster = true;
        Local.RosterEpoch = CurrentRosterEpoch;
        BindProfile(Local, normalizedProfile);
        _currentRoster.Add(Local);
        Version++;
    }

    /// <summary>
    /// Keeps the just-finished line-up visible during an intermission without
    /// claiming that retired bodies are still fighting.
    /// </summary>
    public void MarkCurrentRosterInactive()
    {
        bool changed = false;
        foreach (ArenaScoreboardEntry entry in _currentRoster)
        {
            if (!entry.IsActive && entry.ActiveProfileId == null)
                continue;

            entry.IsActive = false;
            ReleaseCurrentProfile(entry);
            changed = true;
        }

        if (changed)
            Version++;
    }

    public ArenaScoreboardTotals Totals(ArenaScoreboardSide side)
    {
        MutableTotals archived = side == ArenaScoreboardSide.Friendly
            ? _archivedFriendly
            : _archivedOpponent;
        int score = archived.Score;
        int kills = archived.Kills;
        int deaths = archived.Deaths;
        int headshots = archived.Headshots;
        if (side == ArenaScoreboardSide.Opponent)
        {
            kills += _opponentForfeitKills;
            score += _opponentForfeitKills * ScoreRules.PointsFor(ArenaRole.Pmc);
        }
        foreach (ArenaScoreboardEntry entry in _entriesByKey.Values)
        {
            if (entry.Side != side)
                continue;

            score += entry.Score;
            kills += entry.Kills;
            deaths += entry.Deaths;
            headshots += entry.Headshots;
        }

        return new ArenaScoreboardTotals(score, kills, deaths, headshots);
    }

    public ArenaScoreboardEntry Leader(ArenaScoreboardSide side)
        => _entriesByKey.Values
            .Where(entry => entry.Side == side && !entry.IsLocal)
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Kills)
            .ThenByDescending(entry => entry.IsActive)
            .ThenBy(entry => entry.Order)
            .FirstOrDefault();

    /// <summary>Sorted snapshot for UI. The local row remains visible when requested.</summary>
    public IReadOnlyList<ArenaScoreboardEntry> Standings(
        ArenaScoreboardSide? side,
        bool currentRosterOnly,
        int maxRows,
        bool pinLocal)
    {
        maxRows = Math.Max(1, maxRows);
        IEnumerable<ArenaScoreboardEntry> source = currentRosterOnly
            ? _currentRoster
            : _entriesByKey.Values;
        var eligible = source
            .Where(entry => (!side.HasValue || entry.Side == side.Value)
                            && (!currentRosterOnly || entry.IsCurrentRoster))
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Kills)
            .ThenByDescending(entry => entry.IsActive)
            .ThenBy(entry => entry.Deaths)
            .ThenBy(entry => entry.Order)
            .ToList();

        if (eligible.Count <= maxRows)
            return eligible;

        var result = eligible.Take(maxRows).ToList();
        bool localEligible = (!side.HasValue || side == ArenaScoreboardSide.Friendly)
                             && (!currentRosterOnly || Local.IsCurrentRoster);
        if (pinLocal && localEligible && !result.Contains(Local))
        {
            result[result.Count - 1] = Local;
            result = result
                .OrderByDescending(entry => entry.Score)
                .ThenByDescending(entry => entry.Kills)
                .ThenBy(entry => entry.Order)
                .ToList();
        }

        return result;
    }

    public int Count(ArenaScoreboardSide? side, bool currentRosterOnly)
    {
        IEnumerable<ArenaScoreboardEntry> source = currentRosterOnly
            ? _currentRoster
            : _entriesByKey.Values;
        return source.Count(entry =>
            (!side.HasValue || entry.Side == side.Value)
            && (!currentRosterOnly || entry.IsCurrentRoster));
    }

    private bool RosterWouldChange(int epoch, IReadOnlyList<RosterBinding> plan)
    {
        if (epoch != CurrentRosterEpoch || plan.Count != _currentRoster.Count)
            return true;

        foreach (RosterBinding binding in plan)
        {
            if (!_entriesByKey.TryGetValue(binding.Key, out ArenaScoreboardEntry entry)
                || !_currentRoster.Contains(entry)
                || !entry.IsCurrentRoster
                || entry.IsActive != binding.Actor.Alive
                || entry.RosterEpoch != epoch
                || !string.Equals(
                    entry.ActiveProfileId,
                    Normalize(binding.Actor.ProfileId),
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.DisplayName,
                    SafeText(binding.Actor.DisplayName, entry.DisplayName),
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.RoleLabel,
                    SafeText(binding.Actor.RoleLabel, entry.RoleLabel),
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryPlan(
        ArenaScoreboardActor local,
        IReadOnlyList<ArenaScoreboardActor> friendlies,
        IReadOnlyList<ArenaScoreboardActor> opponents,
        out List<RosterBinding> plan,
        out string problem)
    {
        plan = new List<RosterBinding>(1 + friendlies.Count + opponents.Count);
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        if (!TryAddPlan(local, ArenaScoreboardSide.Friendly, LocalKey, "You", true,
                profiles, keys, plan, out problem))
        {
            return false;
        }

        foreach (ArenaScoreboardActor actor in friendlies)
        {
            string stable = Normalize(actor?.StableId);
            string profile = Normalize(actor?.ProfileId);
            string key = stable == null ? BodyKey(profile) : StableKey(stable);
            if (!TryAddPlan(actor, ArenaScoreboardSide.Friendly, key, "Teammate", false,
                    profiles, keys, plan, out problem))
            {
                return false;
            }
        }

        foreach (ArenaScoreboardActor actor in opponents)
        {
            string stable = Normalize(actor?.StableId);
            string profile = Normalize(actor?.ProfileId);
            string key = stable == null ? BodyKey(profile) : StableKey(stable);
            if (!TryAddPlan(actor, ArenaScoreboardSide.Opponent, key, "Opponent", false,
                    profiles, keys, plan, out problem))
            {
                return false;
            }
        }

        problem = null;
        return true;
    }

    private bool TryAddPlan(
        ArenaScoreboardActor actor,
        ArenaScoreboardSide side,
        string key,
        string fallbackName,
        bool local,
        HashSet<string> profiles,
        HashSet<string> keys,
        List<RosterBinding> plan,
        out string problem)
    {
        string profile = Normalize(actor?.ProfileId);
        if (actor == null || profile == null || string.IsNullOrEmpty(key))
        {
            problem = $"{fallbackName.ToLowerInvariant()} roster identity is empty";
            return false;
        }

        if (!profiles.Add(profile))
        {
            problem = $"profile '{profile}' appears more than once in the roster";
            return false;
        }

        if (!keys.Add(key))
        {
            problem = $"logical participant '{key}' appears more than once in the roster";
            return false;
        }

        if (_entriesByProfile.TryGetValue(profile, out var byProfile)
            && (!string.Equals(byProfile.Key, key, StringComparison.Ordinal)
                || byProfile.Side != side))
        {
            problem = $"profile '{profile}' would change scoreboard identity or side";
            return false;
        }

        if (_entriesByKey.TryGetValue(key, out var byKey) && byKey.Side != side)
        {
            problem = $"logical participant '{key}' would change scoreboard side";
            return false;
        }

        if (local && !ReferenceEquals(_entriesByKey[LocalKey], Local))
        {
            problem = "the local scoreboard identity is unavailable";
            return false;
        }

        plan.Add(new RosterBinding(actor, side, key, fallbackName, local));
        problem = null;
        return true;
    }

    private ArenaScoreboardEntry GetOrCreate(RosterBinding binding)
    {
        if (_entriesByKey.TryGetValue(binding.Key, out var entry))
            return entry;

        return Create(
            binding.Key,
            SafeText(binding.Actor.DisplayName, binding.FallbackName),
            SafeText(binding.Actor.RoleLabel, "?"),
            binding.Side,
            binding.Local);
    }

    private ArenaScoreboardEntry Resolve(
        string profileId,
        string displayName,
        string roleLabel,
        bool forceLocal,
        bool? friendly = null)
    {
        string profile = Normalize(profileId);
        if (forceLocal)
        {
            if (profile != null
                && _entriesByProfile.TryGetValue(profile, out var existing)
                && !ReferenceEquals(existing, Local))
            {
                return null;
            }

            if (!string.Equals(Local.ActiveProfileId, profile, StringComparison.Ordinal))
                ReleaseCurrentProfile(Local);
            BindProfile(Local, profile);
            Local.DisplayName = SafeText(displayName, Local.DisplayName);
            Local.IsCurrentRoster = true;
            _currentRoster.Add(Local);
            return Local;
        }

        if (profile == null)
            return null;

        if (_entriesByProfile.TryGetValue(profile, out var known))
            return known;

        var binding = new RosterBinding(
            new ArenaScoreboardActor(profile, displayName, roleLabel, alive: true),
            friendly == true ? ArenaScoreboardSide.Friendly : ArenaScoreboardSide.Opponent,
            BodyKey(profile),
            "Opponent",
            local: false);
        ArenaScoreboardEntry created = GetOrCreate(binding);
        created.IsActive = true;
        created.IsCurrentRoster = true;
        created.RosterEpoch = CurrentRosterEpoch;
        BindProfile(created, profile);
        _currentRoster.Add(created);
        return created;
    }

    private ArenaScoreboardEntry Create(
        string key,
        string displayName,
        string roleLabel,
        ArenaScoreboardSide side,
        bool local)
    {
        var entry = new ArenaScoreboardEntry(
            key,
            displayName,
            roleLabel,
            side,
            local,
            _nextOrder++);
        _entriesByKey.Add(key, entry);
        _historicalParticipantCount++;
        return entry;
    }

    private void BindProfile(ArenaScoreboardEntry entry, string profileId)
    {
        string profile = Normalize(profileId);
        if (profile == null)
            return;

        if (_entriesByProfile.TryGetValue(profile, out var existing))
        {
            if (!ReferenceEquals(existing, entry))
                return;
        }
        else
        {
            _entriesByProfile.Add(profile, entry);
        }

        entry.ActiveProfileId = profile;
    }

    private void ReleaseCurrentProfile(ArenaScoreboardEntry entry)
    {
        string profile = entry.ActiveProfileId;
        if (profile != null
            && entry.IsLocal
            && _entriesByProfile.TryGetValue(profile, out ArenaScoreboardEntry mapped)
            && ReferenceEquals(mapped, entry))
        {
            _entriesByProfile.Remove(profile);
        }

        // Keep old authenticated follower aliases. A grenade or burn owned by a
        // retired PFT body can land after rebind; its kill still belongs to that
        // stable teammate, not to a newly invented opponent row.
        entry.ActiveProfileId = null;
    }

    /// <summary>
    /// Endless modes can create thousands of one-life bot identities. Preserve
    /// exact totals while retaining a fixed mixture of the best and most recent
    /// finished runs for display and delayed-event resolution.
    /// </summary>
    private void TrimOrdinaryHistory()
    {
        List<ArenaScoreboardEntry> candidates = _entriesByKey.Values
            .Where(entry => !entry.IsLocal
                            && entry.Key.StartsWith(BodyKeyPrefix, StringComparison.Ordinal)
                            && !entry.IsCurrentRoster
                            && !entry.IsActive)
            .ToList();
        if (candidates.Count <= RetainedOrdinaryHistoryLimit)
            return;

        var keep = new HashSet<ArenaScoreboardEntry>(candidates
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Kills)
            .ThenBy(entry => entry.Deaths)
            .ThenBy(entry => entry.Order)
            .Take(RetainedTopRuns));
        foreach (ArenaScoreboardEntry entry in candidates.OrderByDescending(entry => entry.Order))
        {
            if (keep.Count >= RetainedOrdinaryHistoryLimit)
                break;
            keep.Add(entry);
        }

        foreach (ArenaScoreboardEntry entry in candidates)
        {
            if (!keep.Contains(entry))
                ArchiveAndRemove(entry);
        }
    }

    private void ArchiveAndRemove(ArenaScoreboardEntry entry)
    {
        MutableTotals totals = entry.Side == ArenaScoreboardSide.Friendly
            ? _archivedFriendly
            : _archivedOpponent;
        totals.Add(entry);
        _entriesByKey.Remove(entry.Key);
        _currentRoster.Remove(entry);

        string[] aliases = _entriesByProfile
            .Where(pair => ReferenceEquals(pair.Value, entry))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (string alias in aliases)
            _entriesByProfile.Remove(alias);

        _archivedParticipantCount++;
    }

    private static void MarkProfileInactive(ArenaScoreboardEntry entry, string profileId)
    {
        string profile = Normalize(profileId);
        if (profile != null
            && string.Equals(entry.ActiveProfileId, profile, StringComparison.Ordinal))
        {
            entry.IsActive = false;
        }
    }

    private static string StableKey(string stableId) => StableKeyPrefix + stableId;
    private static string BodyKey(string profileId)
        => profileId == null ? null : BodyKeyPrefix + profileId;

    private static string Normalize(string value)
    {
        string normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string SafeText(string value, string fallback)
    {
        string text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        text = text
            .Replace('<', '‹')
            .Replace('>', '›')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        const int maxLength = 64;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";
    }

    private sealed class MutableTotals
    {
        internal int Score;
        internal int Kills;
        internal int Deaths;
        internal int Headshots;

        internal void Add(ArenaScoreboardEntry entry)
        {
            Score += entry.Score;
            Kills += entry.Kills;
            Deaths += entry.Deaths;
            Headshots += entry.Headshots;
        }
    }

    private sealed class RosterBinding
    {
        public RosterBinding(
            ArenaScoreboardActor actor,
            ArenaScoreboardSide side,
            string key,
            string fallbackName,
            bool local)
        {
            Actor = actor;
            Side = side;
            Key = key;
            FallbackName = fallbackName;
            Local = local;
        }

        public ArenaScoreboardActor Actor { get; }
        public ArenaScoreboardSide Side { get; }
        public string Key { get; }
        public string FallbackName { get; }
        public bool Local { get; }
    }
}
