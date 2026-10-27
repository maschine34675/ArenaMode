using ArenaMode.Shared;
using System.Globalization;
using System.Text;

namespace ArenaMode.Session;

internal static class ArenaRewardRecoveryText
{
    internal static string Label(ArenaRewardRecoveryFlow flow)
        => flow.Busy ? "CHECKING…" : flow.Status.Queued ? "RESTART PENDING"
        : flow.Status.RequiresNormalSession ? "REWARD HELP" : flow.Unconfirmed ? "CHECK REWARDS"
        : "RESOLVE REWARDS";

    internal static string Review(RewardRecoveryStatusResponse status)
    {
        if (status.RequiresNormalSession)
            return "Return to normal SPT first.\nThen choose ARENA → RESOLVE REWARDS.";
        if (status.Queued)
            return "Closure queued.\nRestart the normal SPT server to finish.\nYour profile progress stays unchanged.";
        if (!status.Available)
            return "Reward status unavailable.\nCheck again when normal SPT is ready.";
        if (status.Cases == null || status.Cases.Count == 0)
            return "No reward cases to close.\nYour profile progress stays unchanged.";
        if (!status.CanClose)
            return "Reward recovery is currently blocked.\nRestart normal SPT and check again.\nYour profile progress stays unchanged.";
        var text = new StringBuilder();
        text.Append(status.Cases.Count).Append(status.Cases.Count == 1 ? " old reward case\n" : " old reward cases\n");
        for (int index = 0; index < status.Cases.Count && index < 3; index++)
        {
            if (index != 0) text.Append(" · ");
            var item = status.Cases[index];
            text.Append(item?.Kind == "snapshot" ? "Session " : item?.Kind == "payment" ? "Payout " : "Match ")
                .Append(ShortId(item?.ShortId));
        }
        if (status.Cases.Count > 3) text.Append(" · +").Append(status.Cases.Count - 3);
        text.Append('\n');
        decimal roubles = 0, experience = 0;
        int unknown = 0;
        foreach (var item in status.Cases)
        {
            if (item == null || !item.AmountKnown || item.Roubles < 0 || item.Experience < 0) { unknown++; continue; }
            roubles += item.Roubles;
            experience += item.Experience;
        }
        text.Append("Known rewards: ").Append(roubles.ToString("N0", CultureInfo.CurrentCulture))
            .Append(" ₽ + ").Append(experience.ToString("N0", CultureInfo.CurrentCulture)).Append(" XP\n");
        if (unknown != 0) text.Append("Amount unknown: ").Append(unknown).Append(unknown == 1 ? " case\n" : " cases\n");
        text.Append("Close these cases with NO further payout?\n")
            .Append("Profile progress stays. Applies after normal SPT restart.");
        return text.ToString();
    }

    private static string ShortId(string value)
    {
        var text = new StringBuilder();
        if (value != null)
            foreach (char ch in value)
            {
                if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9') text.Append(ch);
                if (text.Length == 8) break;
            }
        return text.Length == 0 ? "?" : text.ToString();
    }
}
