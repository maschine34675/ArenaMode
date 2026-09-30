using System.Reflection;
using ArenaMode;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Session;
using EFT;
using HarmonyLib;

internal static class Program
{
    private static int _checks;
    private static FeedBoundary _feed;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("CHECK: " + label);
    }
    private static Player Body(string id = "bot-profile", bool local = false) => new()
    { ProfileId = id, IsYourPlayer = local, DisplayName = local ? "Local" : "Victim", RoleLabel = local ? "PMC" : "BEAR" };
    private static ArenaMatch Reset()
    {
        ArenaBarbedDeathPatch.Reset();
        ArenaAdministrativeDeathGate.Reset();
        ArenaSession.IsActive = true;
        Settings.ShowKillfeed.Value = Settings.KillfeedOtherKills.Value = true;
        Plugin.ModLogger.Warnings.Clear();
        var match = new ArenaMatch();
        ArenaMatchService.Current = match;
        _feed = new FeedBoundary();
        ArenaHud.SetBoundary(match, _feed);
        return match;
    }
    private static void Death(Player body, EDamageType cause = EDamageType.Barbed, Action between = null)
    {
        ArenaBarbedDeathPatch.Prefix(cause, out var state);
        between?.Invoke();
        ArenaBarbedDeathPatch.Postfix(body, cause, state, body?.ReadAggressorForDriver());
    }
    private static void NoNotice(string label, Action setup, Player body = null, EDamageType cause = EDamageType.Barbed)
    {
        Reset(); setup(); Death(body ?? Body(), cause);
        Check(_feed.Rows.Count == 0, label);
        Check(Plugin.ModLogger.Warnings.Count == 0, label + " is quiet");
    }
    private static void HookContract()
    {
        var target = (MethodBase)typeof(ArenaBarbedDeathPatch).GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(new ArenaBarbedDeathPatch(), null);
        Check(target == typeof(Player).GetMethod(nameof(Player.OnDead)), "hook resolves declared Player.OnDead");
        var field = typeof(Player).GetField("LastAggressor", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(field.IsFamily && field.FieldType == typeof(IPlayer), "native-shaped protected IPlayer field");
        var postfix = typeof(ArenaBarbedDeathPatch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
        Check(postfix.GetParameters().Single(p => p.Name == "___LastAggressor").ParameterType == field.FieldType,
            "Harmony triple-underscore field injection has exact native type");
        var fields = typeof(ArenaBarbedDeath).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name).OrderBy(x => x).ToArray();
        Check(fields.SequenceEqual(new[] { "LocalIsVictim", "VictimName", "VictimProfileId", "VictimRoleLabel" }),
            "presentation DTO carries no attacker score assist or award");
    }
    private static void CaptureChecks()
    {
        NoNotice("ordinary raid untouched", () => ArenaSession.IsActive = false);
        foreach (var cause in Enum.GetValues<EDamageType>().Where(x => x != EDamageType.Barbed))
            NoNotice("non-barbed cause untouched " + cause, () => { }, cause: cause);
        NoNotice("combined flags are not a barbed death", () => { }, cause: EDamageType.Barbed | EDamageType.Bullet);
        NoNotice("living body ignored", () => { }, new Player { HealthController = new Health { IsAlive = true } });
        NoNotice("missing health ignored", () => { }, new Player { HealthController = null });
        NoNotice("missing profile ignored", () => { }, Body(null));
        NoNotice("empty profile ignored", () => { }, Body(""));
        NoNotice("no current match ignored", () => ArenaMatchService.Current = null);
        NoNotice("already completed match ignored", () => ArenaMatchService.Current.Mode.IsComplete = true);
        Reset(); Death(null); Check(_feed.Rows.Count == 0, "null body ignored");
        Reset(); var dead = Body(); Death(dead);
        Check(_feed.Rows.Count == 1, "wire death creates one presentation notice");
        var row = _feed.Rows.Single();
        Check(row.VictimProfileId == dead.ProfileId && row.VictimName == dead.DisplayName
            && row.VictimRoleLabel == dead.RoleLabel && !row.LocalIsVictim, "victim identity and classification preserved");
        Death(dead);
        Check(_feed.Rows.Count == 1, "same physical death displayed once");
        Check(Plugin.ModLogger.Warnings.Count == 0, "duplicate body does not enter failure path");
        Death(Body(dead.ProfileId));
        Check(_feed.Rows.Count == 2, "fresh respawn body with same profile displayed");
        ArenaBarbedDeathPatch.Reset(); Death(dead);
        Check(_feed.Rows.Count == 3, "raid reset discards physical-body dedupe");

        Reset(); dead = Body(); dead.SetAggressor(Body("attacker")); Death(dead);
        Check(_feed.Rows.Count == 0, "existing aggressor avoids duplicate credited kill notice");
        dead.SetAggressor(null); Death(dead);
        Check(_feed.Rows.Count == 1, "aggressor rejection does not consume presentation dedupe");

        Reset(); dead = Body();
        using (ArenaAdministrativeDeathGate.Enter(dead.ProfileId)) Death(dead);
        Check(_feed.Rows.Count == 0, "administrative retirement ignored");
        Death(dead); Check(_feed.Rows.Count == 1, "administrative rejection does not consume presentation dedupe");

        var match = Reset(); dead = Body(); Death(dead, between: () => match.Mode.IsComplete = true);
        Check(_feed.Rows.Count == 1, "final genuine death displayed when native subscriber completes match");
        Death(Body("later")); Check(_feed.Rows.Count == 1, "death after completion stays excluded");
        Reset(); Death(Body(), between: () => ArenaSession.IsActive = false);
        Check(_feed.Rows.Count == 0, "session end during native death rejects stale notice");
        Reset(); Death(Body(), between: () => ArenaMatchService.Current = new ArenaMatch());
        Check(_feed.Rows.Count == 0, "match replacement during native death rejects stale notice");
        Reset(); Death(Body(), between: () => ArenaMatchService.Current = null);
        Check(_feed.Rows.Count == 0, "match removal during native death rejects stale notice");
        Reset(); ArenaBarbedDeathPatch.Postfix(Body(), EDamageType.Barbed, null, null);
        Check(_feed.Rows.Count == 0, "missing prefix state rejected");
    }
    private static void HudChecks()
    {
        NoNotice("HUD master switch honored", () => Settings.ShowKillfeed.Value = false);
        NoNotice("HUD other deaths switch honored", () => Settings.KillfeedOtherKills.Value = false);
        Reset(); Settings.KillfeedOtherKills.Value = false; Death(Body("local-profile", true));
        Check(_feed.Rows.Count == 1 && _feed.Rows[0].LocalIsVictim, "local wire death remains visible with other kills disabled");
        Reset(); Settings.ShowKillfeed.Value = false; Death(Body("local-profile", true));
        Check(_feed.Rows.Count == 0, "master HUD switch also hides local death");
        NoNotice("detached HUD ignored", () => ArenaHud.SetBoundary(ArenaMatchService.Current, null));
        NoNotice("HUD belonging to another match ignored", () => ArenaHud.SetBoundary(new ArenaMatch(), _feed));
        Reset(); var dead = Body(); Settings.ShowKillfeed.Value = false; Death(dead);
        Settings.ShowKillfeed.Value = true; Death(dead);
        Check(_feed.Rows.Count == 0, "hidden death is not replayed after HUD toggled on");
        Reset(); dead = Body(); dead.ThrowDuringClassification = true; Death(dead);
        Check(_feed.Rows.Count == 0 && Plugin.ModLogger.Warnings.Count == 1, "classification exception contained and logged");
        Death(dead); Check(Plugin.ModLogger.Warnings.Count == 1, "failed display is not repeatedly retried");
        Reset(); _feed.ThrowOnPush = true; Death(Body());
        Check(_feed.Rows.Count == 0 && Plugin.ModLogger.Warnings.Count == 1, "HUD exception contained and logged");
    }
    private static void HarmonyProbe()
    {
#if REAL_HARMONY_PROBE
        Reset();
        var target = typeof(Player).GetMethod(nameof(Player.OnDead));
        var harmony = new Harmony("ArenaMode.BarbedDeathChecks.ProtectedField");
        harmony.Patch(target,
            new HarmonyMethod(typeof(ArenaBarbedDeathPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)),
            new HarmonyMethod(typeof(ArenaBarbedDeathPatch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
        var body = Body(); body.OnDead(EDamageType.Barbed);
        Check(_feed.Rows.Count == 1 && body.NativeDeathCalls == 1, "real Harmony calls prefix original postfix");
        body = Body(); body.SetAggressor(Body("attacker")); body.OnDead(EDamageType.Barbed);
        Check(_feed.Rows.Count == 1 && body.NativeDeathCalls == 1, "real Harmony injects nonnull protected aggressor");
        body = Body("local-profile", true); body.DeathSubscriber = () => ArenaMatchService.Current.Mode.IsComplete = true;
        body.OnDead(EDamageType.Barbed);
        Check(_feed.Rows.Count == 2 && _feed.Rows[1].LocalIsVictim, "real Harmony transfers captured state across completion");
        harmony.UnpatchSelf();
        Console.WriteLine($"BARBED HARMONY PROTECTED FIELD PROBE PASSED ({_checks} assertions; desktop CLR only)");
#else
        throw new NotSupportedException("Compile with UseRealHarmony=true for the separate probe.");
#endif
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--probe-harmony")) { HarmonyProbe(); return 0; }
            HookContract(); CaptureChecks(); HudChecks();
            Console.WriteLine($"BARBED DEATH CONTRACTS PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
