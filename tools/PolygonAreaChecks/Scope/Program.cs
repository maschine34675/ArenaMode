using ArenaMode;
using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Shared;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.UI.Screens;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static int Main()
    {
        try
        {
            DormantScopes();
            NormalPmcCapture();
            ExactIdentityAndSuspensions();
            RetentionAndReset();
            RealInputOrdering();
            AimOwnershipAndNearestSurface();
            ExplicitRealFileExport();
            FaultContainmentAndPreview();
            Console.WriteLine($"ARENA_POLYGON_SCOPE_PASSED {_checks} assertions (modeled host; no Unity runtime claim)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + name);
    }

    private static void DormantScopes()
    {
        foreach (var scenario in new (string Name, Action<Fixture> Change)[]
        {
            ("disabled", _ => Settings.AreaCaptureEnabled.Value = false),
            ("unbound setting", _ => Settings.AreaCaptureEnabled = null),
            ("normal raid", _ => ArenaSession.IsActive = false),
            ("no game", _ => Singleton<AbstractGame>.Instance = null),
            ("no world", _ => Singleton<GameWorld>.Instance = null),
            ("unsupported map", f => f.World.LocationId = "woods"),
            ("not started", f => f.Game.Status = GameStatus.Starting),
            ("stopping", f => f.Game.Status = GameStatus.Stopping),
            ("stopped", f => f.Game.Status = GameStatus.Stopped),
            ("foreign game type", f =>
            {
                var other = new AbstractGame { GameWorld = f.World };
                Singleton<AbstractGame>.Instance = other; ArenaSession.ExactGame = other;
            })
        })
        {
            using var f = new Fixture();
            scenario.Change(f);
            f.Press(KeyCode.F7); f.Press(KeyCode.F6); f.Press(KeyCode.F9); f.Call("OnGUI");
            Check(f.Draft == null, scenario.Name + ": draft remains dormant");
            Check(Input.Reads == 0 && Physics.Calls == 0, scenario.Name + ": no input or physics work");
            Check(f.Files().Length == 0 && GUI.Labels == 0 && GUI.Textures == 0, scenario.Name + ": no export or preview");
            Check(Plugin.ModLogger.Warnings.Count == 0, scenario.Name + ": expected absence is not a fault");
        }
    }

    private static void NormalPmcCapture()
    {
        // This is the actual reported workflow: normal Customs, far outside
        // the current Arena box, with both explicit capture switches enabled.
        foreach (var side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
        {
            using var f = new Fixture();
            f.NormalPmc(); f.World.MainPlayer.Profile.Info.Side = side;
            var settings = f.Game.RaidSettings;
            var frozenArea = ArenaAreaRuntime.CurrentArea;
            foreach (var point in new[] { new Vector3(450, 1, -80), new Vector3(465, 1, -80), new Vector3(450, 1, -60) })
            {
                Check(!ArenaAreaCatalog.CustomsFactoryCenter.Bounds.Contains(point.x, point.y, point.z), "normal survey points lie outside the old Arena contour");
                f.World.MainPlayer.Position = point; f.Press(KeyCode.F7);
            }
            f.Press(KeyCode.F9); f.Call("OnGUI");
            Check(f.Draft?.Points.Count == 3 && f.Files().Length == 1, "normal PMC captures and exports outside the Arena boundary");
            Check(GUI.Labels > 0 && GUI.Boxes == 1, "normal PMC preview is reachable");
            Check(!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid && ReferenceEquals(settings, f.Game.RaidSettings)
                && settings.serverId == "native-pmc-raid" && ReferenceEquals(frozenArea, ArenaAreaRuntime.CurrentArea),
                "normal capture leaves session, raid identity and existing area untouched");
            using var body = JsonDocument.Parse(File.ReadAllText(f.Files()[0]));
            Check(body.RootElement.GetProperty("sourceAreaId").GetString() == ArenaAreaCatalog.FullMapId
                && body.RootElement.GetProperty("geometry").GetProperty("mapId").GetString() == ArenaMapCatalog.CustomsId,
                "normal export has whole-map source and actual Customs map");
        }
        foreach (var scenario in new (string Name, Action<Fixture> Change)[]
        {
            ("normal opt-in off", _ => Settings.AreaCaptureAllowInNormalPmc.Value = false),
            ("normal opt-in absent", _ => Settings.AreaCaptureAllowInNormalPmc = null),
            ("master off in normal", _ => Settings.AreaCaptureEnabled.Value = false),
            ("normal while Arena armed", _ => ArenaSession.ArmedForNextRaid = true),
            ("missing normal settings", f => f.Game.RaidSettings = null),
            ("missing normal identity", f => f.Game.RaidSettings.serverId = null),
            ("blank normal identity", f => f.Game.RaidSettings.serverId = " "),
            ("normal location mismatch", f => f.Game.RaidSettings.location = ArenaMapCatalog.FactoryDayId),
            ("scav request", f => f.Game.RaidSettings.playerSide = JsonType.ESideType.Savage),
            ("scav body", f => f.World.MainPlayer.Profile.Info.Side = EPlayerSide.Savage),
            ("missing PMC profile", f => f.World.MainPlayer.Profile = null),
            ("missing PMC profile info", f => f.World.MainPlayer.Profile.Info = null),
            ("normal foreign own body", f => f.World.MainPlayer.IsYourPlayer = false),
            ("normal local body mismatch", f => f.Game.LocalPlayer = new Player()),
            ("normal world mismatch", f => f.Game.GameWorld = new GameWorld()),
            ("active foreign Arena with normal opt-in", _ => { ArenaSession.IsActive = true; ArenaSession.ExactGame = new LocalGame(); }),
            ("normal menu", _ => EftScreenManager.Instance.Screen = EEftScreenType.Menu),
            ("normal unlocked cursor", _ => Cursor.lockState = CursorLockMode.None),
            ("normal dead", f => f.World.MainPlayer.HealthController.IsAlive = false),
            ("normal stopping", f => f.Game.Status = GameStatus.Stopping),
            ("normal spectating", _ => ArenaSpectatorService.IsActive = true),
            ("normal respawning", _ => ArenaRespawnDriver.IsRespawnInProgress = true),
            ("normal foreign game type", f => Singleton<AbstractGame>.Instance = new AbstractGame { GameWorld = f.World })
        })
        {
            using var f = new Fixture(); f.NormalPmc(); f.SeedTriangle();
            scenario.Change(f); Input.Reads = 0;
            f.Call("OnGUI"); f.Press(KeyCode.F9); f.Press(KeyCode.F7); f.Press(KeyCode.F6);
            Check(f.Files().Length == 0 && Input.Reads == 0 && Physics.Calls == 0 && GUI.Labels == 0,
                scenario.Name + ": normal capture/export blocked");
        }
        // Since cba148e the end marker is the exact "|arenamode:" tag (Protocol.HasNativeEndMarker): a raid the
        // native game still labels with Arena's end identity after IsActive cleared is never reclassified as
        // ordinary. Start-like or malformed identities are not end transactions and capture like any ordinary
        // raid; until 2026-10-23 this harness still expected the old "arenamode" substring rule for them.
        foreach (string marker in new[] { "native|arenamode:token", "native|ARENAmoDE:token" })
        {
            using var f = new Fixture(); f.NormalPmc(); f.SeedTriangle();
            f.Game.RaidSettings.serverId = marker; Input.Reads = 0;
            f.Call("OnGUI"); f.Press(KeyCode.F9);
            Check(f.Draft == null && Input.Reads == 0 && f.Files().Length == 0 && GUI.Labels == 0,
                "Arena end marker cannot enter normal capture");
        }
        foreach (string marker in new[] { "arenamode-start:token", "ARENAmodE-invalid", "arenamode-ordinary-start:bad" })
        {
            using var f = new Fixture(); f.NormalPmc(); f.SeedTriangle();
            f.Game.RaidSettings.serverId = marker;
            f.Press(KeyCode.F9);
            Check(f.Files().Length == 1, "a start-like or malformed identity without the end marker captures like an ordinary raid");
        }
        using (var f = new Fixture())
        {
            f.NormalPmc();
            f.Game.RaidSettings.serverId = Protocol.BuildNativeOrdinaryStartIdentity(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            f.SeedTriangle(); f.Press(KeyCode.F9);
            Check(f.Files().Length == 1, "strict valid ordinary-start marker remains supported");
        }
        using (var f = new Fixture())
        {
            Settings.AreaCaptureAllowInNormalPmc.Value = true; f.SeedTriangle();
            ArenaSession.IsActive = false; f.Press(KeyCode.F9);
            Check(f.Draft == null && f.Files().Length == 0, "ending Arena cannot export through enabled normal fallback");
        }
        using (var f = new Fixture())
        {
            f.NormalPmc(); f.SeedTriangle(); var old = f.Draft;
            f.World.LocationId = ArenaMapCatalog.FactoryDayId; f.Game.RaidSettings.location = f.World.LocationId;
            f.Call("OnGUI"); Check(GUI.Labels == 0, "changed normal map cannot display old points before Update");
            f.Step();
            Check(!ReferenceEquals(old, f.Draft) && f.Draft?.Points.Count == 0, "same-world map transition clears old contour");
            f.SeedTriangle(); old = f.Draft; ArenaSession.IsActive = true;
            f.Call("OnGUI"); f.Step();
            Check(GUI.Labels == 0 && !ReferenceEquals(old, f.Draft) && f.Draft?.Points.Count == 0,
                "same-world mode transition clears old contour");
        }
    }

    private static void ExactIdentityAndSuspensions()
    {
        foreach (var scenario in new (string Name, Action<Fixture> Change, bool Retains)[]
        {
            ("exact game mismatch", f => ArenaSession.ExactGame = new LocalGame { GameWorld = f.World }, false),
            ("exact world mismatch", _ => ArenaSession.ExactWorld = new GameWorld(), false),
            ("game world mismatch", f => f.Game.GameWorld = new GameWorld(), false),
            ("same world foreign local game", f => Singleton<AbstractGame>.Instance = new LocalGame { GameWorld = f.World, LocalPlayer = f.World.MainPlayer }, false),
            ("singleton foreign world", f => Singleton<GameWorld>.Instance = new GameWorld { LocationId = f.World.LocationId, MainPlayer = f.World.MainPlayer }, false),
            ("foreign main body", f => f.World.MainPlayer.IsYourPlayer = false, true),
            ("main and local body mismatch", f => f.Game.LocalPlayer = new Player(), true),
            ("no local body", f => f.Game.LocalPlayer = null, true),
            ("no main body", f => f.World.MainPlayer = null, true),
            ("dead body", f => f.World.MainPlayer.HealthController.IsAlive = false, true),
            ("missing health", f => f.World.MainPlayer.HealthController = null, true),
            ("respawning", _ => ArenaRespawnDriver.IsRespawnInProgress = true, true),
            ("spectating", _ => ArenaSpectatorService.IsActive = true, true),
            ("unlocked cursor", _ => Cursor.lockState = CursorLockMode.None, true),
            ("menu screen", _ => EftScreenManager.Instance.Screen = EEftScreenType.Menu, true),
            ("no screen manager", _ => EftScreenManager.Instance = null, true)
        })
        {
            using var f = new Fixture();
            f.SeedTriangle();
            var draft = f.Draft;
            f.World.MainPlayer.Position = new Vector3(6, 1, 6);
            scenario.Change(f);
            Input.Reads = 0;
            // Check preview before Update can reset a stale draft; attempt the
            // export while the seeded triangle is still known valid.
            f.Call("OnGUI");
            f.Press(KeyCode.F9); f.Press(KeyCode.F7); f.Press(KeyCode.F6); f.Press(KeyCode.F8); f.Call("OnGUI");
            Check(scenario.Retains ? ReferenceEquals(draft, f.Draft) : f.Draft == null,
                scenario.Name + ": intended draft lifetime");
            Check(draft.Points.Count == 3, scenario.Name + ": blocked feet and undo preserve authored points");
            Check(Input.Reads == 0 && Physics.Calls == 0, scenario.Name + ": interaction gated before polling input");
            Check(f.Files().Length == 0, scenario.Name + ": valid existing draft still cannot export");
            Check(GUI.Labels == 0 && GUI.Textures == 0, scenario.Name + ": no preview draw");
            Check(Plugin.ModLogger.Warnings.Count == 0, scenario.Name + ": no exception fallback needed");
        }
    }

    private static void RetentionAndReset()
    {
        using (var f = new Fixture())
        {
            f.SeedTriangle();
            var draft = f.Draft;
            ArenaRespawnDriver.IsRespawnInProgress = true;
            f.World.MainPlayer = null; f.Game.LocalPlayer = null;
            f.Press(KeyCode.F9);
            Check(ReferenceEquals(draft, f.Draft) && draft.Points.Count == 3, "body gap preserves exact-world draft");
            var replacement = new Player { Position = new Vector3(6, 1, 6) };
            f.World.MainPlayer = replacement; f.Game.LocalPlayer = replacement;
            ArenaRespawnDriver.IsRespawnInProgress = false;
            f.Press(KeyCode.F7);
            Check(ReferenceEquals(draft, f.Draft) && draft.Points.Count == 4, "new local body resumes same draft");
            Check(f.Files().Length == 0, "blocked respawn export was not deferred");
        }
        foreach (var scenario in new (string Name, Action<Fixture> Change, bool NewScope)[]
        {
            ("disable setting", _ => Settings.AreaCaptureEnabled.Value = false, false),
            ("raid ended", _ => ArenaSession.IsActive = false, false),
            ("game stopped", f => f.Game.Status = GameStatus.Stopped, false),
            ("new exact game same world", f => { f.Game = new LocalGame { GameWorld = f.World, LocalPlayer = f.World.MainPlayer }; f.Bind(); }, true),
            ("new exact world same game", f =>
            {
                var body = new Player();
                f.World = new GameWorld { LocationId = ArenaMapCatalog.CustomsId, MainPlayer = body };
                f.Game.GameWorld = f.World; f.Game.LocalPlayer = body;
                ArenaAreaRuntime.CurrentArea = ArenaAreaCatalog.CustomsFactoryCenter; f.Bind();
            }, true)
        })
        {
            using var f = new Fixture();
            f.SeedTriangle(); var previous = f.Draft;
            scenario.Change(f); f.Step();
            Check(!ReferenceEquals(previous, f.Draft), scenario.Name + ": old draft detached");
            Check(scenario.NewScope ? f.Draft?.Points.Count == 0 : f.Draft == null, scenario.Name + ": no carried points");
            Check(f.Files().Length == 0, scenario.Name + ": reset never exports");
        }
        foreach (string method in new[] { "OnDisable", "OnDestroy" })
        {
            using var f = new Fixture();
            f.SeedTriangle(); f.Call(method);
            Check(f.Draft == null && !f.Faulted, method + ": owned draft cleared");
        }
        using (var f = new Fixture())
        {
            f.SeedTriangle(); Settings.AreaCaptureEnabled.Value = false; f.Step();
            Settings.AreaCaptureEnabled.Value = true; f.Step();
            Check(f.Draft?.Points.Count == 0, "off/on starts an empty draft");
        }
    }

    private static void RealInputOrdering()
    {
        using var f = new Fixture();
        f.Press(KeyCode.F7, withModifier: false);
        Check(f.Draft?.Points.Count == 0, "modifier required for actual shortcut");
        f.Press(KeyCode.F7);
        Check(f.Draft.Points.Count == 1, "feet shortcut handled by actual Update");
        f.World.MainPlayer.Position = new Vector3(8, 1, 0); f.Press(KeyCode.F7);
        f.World.MainPlayer.Position = new Vector3(0, 1, 8); f.Press(KeyCode.F7);
        f.Step(new[] { KeyCode.F8, KeyCode.F9, KeyCode.F7 }, new[] { KeyCode.LeftControl });
        Check(f.Draft.Points.Count == 2 && f.Files().Length == 0, "undo wins over export and add in same frame");
        f.Step(new[] { KeyCode.F9, KeyCode.F7 }, new[] { KeyCode.LeftControl });
        Check(f.Draft.Points.Count == 2 && f.Files().Length == 0, "invalid export consumes frame without adding");
        f.Press(KeyCode.F7); Check(f.Draft.Points.Count == 3, "later explicit add remains possible");
        f.Step(); Check(f.Draft.Points.Count == 3 && f.Files().Length == 0, "idle frame does not capture or export");
    }

    private static void AimOwnershipAndNearestSurface()
    {
        using var f = new Fixture();
        var owned = new Collider { Name = "Arena-owned boundary" };
        var body = new Collider { ParentPlayer = new Player { IsYourPlayer = false } };
        var near = new Collider { Name = "native map wall" };
        var far = new Collider { Name = "native map floor" };
        ArenaAreaRuntime.Owned.Add(owned);
        Physics.Hits = new[]
        {
            Hit(far, 20, 9, 9), Hit(owned, 2, 99, 99), Hit(near, 5, 2, 3),
            Hit(body, 1, 88, 88), Hit(null, 0.5f, 77, 77)
        };
        var originalPosition = f.World.MainPlayer.Position;
        f.Press(KeyCode.F6);
        Check(f.Draft.Points.Count == 1 && f.Draft.Points[0].X == 2 && f.Draft.Points[0].Z == 3,
            "aim sorts unsorted hits and ignores only owned barrier/player/null colliders");
        Check(Physics.Calls == 1 && Physics.LastDistance == 250 && Physics.LastLayers == Physics.DefaultRaycastLayers
            && Physics.LastTrigger == QueryTriggerInteraction.Ignore, "explicit aim requests bounded native default-layer non-trigger ray");
        Check(f.World.MainPlayer.Position.Equals(originalPosition) && ArenaAreaRuntime.Owned.Contains(owned),
            "capture does not move player or remove owned barrier");
        var foreignBarrier = new Collider { Name = "Arena-owned boundary" };
        Physics.Hits = new[] { Hit(near, 5, 3, 4), Hit(foreignBarrier, 3, 4, 5), Hit(owned, 1, 99, 99) };
        f.Press(KeyCode.F6);
        Check(f.Draft.Points.Count == 2 && f.Draft.Points[1].X == 4 && f.Draft.Points[1].Z == 5,
            "unowned collider with Arena-like name is retained as nearest surface");
        Physics.Hits = Array.Empty<RaycastHit>(); f.Press(KeyCode.F6);
        Check(f.Draft.Points.Count == 2, "empty aim result does not fabricate a point");
        int calls = Physics.Calls;
        CameraManager.Instance.Camera.isActiveAndEnabled = false; f.Press(KeyCode.F6);
        CameraManager.Instance.Camera = null; f.Press(KeyCode.F6);
        CameraManager.Instance = null; f.Press(KeyCode.F6);
        Check(Physics.Calls == calls && f.Draft.Points.Count == 2, "missing or disabled camera avoids physics and points");
    }

    private static RaycastHit Hit(Collider collider, float distance, float x, float z)
        => new() { collider = collider, distance = distance, point = new Vector3(x, 1, z) };

    private static void ExplicitRealFileExport()
    {
        using var f = new Fixture();
        f.Press(KeyCode.F9);
        Check(f.Files().Length == 0, "invalid empty draft cannot export");
        f.SeedTriangle(); var draft = f.Draft; var activeArea = ArenaAreaRuntime.CurrentArea;
        f.Press(KeyCode.F9);
        var first = f.Files(); Check(first.Length == 1, "explicit valid export writes one actual file");
        string contents = File.ReadAllText(first[0]);
        using (var json = JsonDocument.Parse(contents))
        {
            Check(json.RootElement.GetProperty("draftOnly").GetBoolean(), "controller export is draft-only");
            Check(json.RootElement.GetProperty("geometry").GetProperty("mapId").GetString() == f.World.LocationId,
                "controller export uses exact captured map");
            Check(json.RootElement.GetProperty("sourceAreaId").GetString() == activeArea.Id,
                "controller export records frozen source area");
        }
        f.Press(KeyCode.F9);
        Check(f.Files().Length == 2 && File.ReadAllText(first[0]) == contents, "repeat export creates new file without overwriting first");
        Check(ReferenceEquals(draft, f.Draft) && draft.Points.Count == 3 && ReferenceEquals(activeArea, ArenaAreaRuntime.CurrentArea),
            "export preserves draft and live geometry");
        Check(!Directory.EnumerateFiles(f.TempDirectory, "*.tmp", SearchOption.AllDirectories).Any(), "successful exports leave no incomplete temporary file");
        Settings.AreaCaptureMaxY.Value = -1; f.Press(KeyCode.F9);
        Check(f.Files().Length == 2, "changed invalid height is revalidated before export");
    }

    private static void FaultContainmentAndPreview()
    {
        using (var f = new Fixture())
        {
            Physics.Throw = new InvalidOperationException("fake physics failure"); f.Press(KeyCode.F6);
            Check(f.Faulted && Plugin.ModLogger.Warnings.Count == 1, "capture exception latches one contained warning");
            f.Press(KeyCode.F6); f.Press(KeyCode.F7); f.Press(KeyCode.F9);
            Check(Physics.Calls == 1 && Plugin.ModLogger.Warnings.Count == 1 && f.Draft.Points.Count == 0 && f.Files().Length == 0,
                "fault latch prevents repeated work and log spam");
            Settings.AreaCaptureEnabled.Value = false; f.Step();
            Physics.Throw = null; Settings.AreaCaptureEnabled.Value = true; f.Press(KeyCode.F7);
            Check(!f.Faulted && f.Draft.Points.Count == 1, "explicit off/on clears fault and prior draft");
        }
        using (var f = new Fixture())
        {
            f.SeedTriangle();
            var color = new Color(.1f, .2f, .3f); var matrix = new Matrix4x4 { Stamp = 19 };
            GUI.color = color; GUI.matrix = matrix; f.Call("OnGUI");
            Check(GUI.Labels > 0 && GUI.Textures > 0 && GUI.Boxes == 1, "eligible repaint reaches actual preview drawing");
            Check(GUI.color.Equals(color) && GUI.matrix.Equals(matrix), "preview restores incoming GUI state");
            int labels = GUI.Labels, textures = GUI.Textures;
            Event.current.type = EventType.Layout; f.Call("OnGUI");
            Check(GUI.Labels == labels && GUI.Textures == textures, "non-repaint does not draw");
            Event.current.type = EventType.Repaint; GUI.ThrowOnDraw = true; f.Call("OnGUI");
            Check(f.Faulted && Plugin.ModLogger.Warnings.Count == 1, "preview draw exception is contained");
            Check(GUI.color.Equals(color) && GUI.matrix.Equals(matrix), "failed preview restores color and matrix");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "ArenaPolygonScope-" + Guid.NewGuid().ToString("N"));
        public readonly ArenaAreaCapture Capture = new();
        public LocalGame Game;
        public GameWorld World;
        public ArenaAreaDraft Draft => (ArenaAreaDraft)Field("_draft").GetValue(Capture);
        public bool Faulted => (bool)Field("_faulted").GetValue(Capture);
        private static FieldInfo Field(string name) => typeof(ArenaAreaCapture).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ArenaAreaCapture).FullName, name);

        public Fixture()
        {
            Settings.Reset();
            var body = new Player { Position = new Vector3(0, 1, 0) };
            World = new GameWorld { LocationId = ArenaMapCatalog.FactoryDayId, MainPlayer = body };
            Game = new LocalGame { GameWorld = World, LocalPlayer = body,
                RaidSettings = new LocalRaidSettings { location = World.LocationId, playerSide = JsonType.ESideType.Pmc,
                    serverId = Protocol.BuildNativeEndIdentity("native-raid", "test-token") } };
            Bind();
            ArenaSession.ArmedForNextRaid = false;
            ArenaRespawnDriver.IsRespawnInProgress = false; ArenaSpectatorService.IsActive = false;
            Cursor.lockState = CursorLockMode.Locked; EftScreenManager.Instance = new();
            CameraManager.Instance = new() { Camera = new Camera() };
            ArenaAreaRuntime.CurrentArea = ArenaAreaCatalog.FactorySouth; ArenaAreaRuntime.Owned.Clear();
            Physics.Calls = 0; Physics.Hits = Array.Empty<RaycastHit>(); Physics.Throw = null; Input.Reads = 0;
            GUI.Labels = 0; GUI.Boxes = 0; GUI.Textures = 0; GUI.ThrowOnDraw = false;
            GUI.color = Color.white; GUI.matrix = default; Event.current = new();
            Plugin.ModLogger.Warnings.Clear(); Plugin.ModLogger.Infos.Clear(); ArenaToasts.Messages.Clear();
            Plugin.Instance = new Plugin { Info = new Plugin.PluginInfo { Location = Path.Combine(TempDirectory, "maschine-ArenaMode.dll") } };
        }

        public void Bind()
        {
            Singleton<AbstractGame>.Instance = Game; Singleton<GameWorld>.Instance = World;
            ArenaSession.IsActive = true; ArenaSession.ExactGame = Game; ArenaSession.ExactWorld = World;
        }
        public void NormalPmc()
        {
            ArenaSession.IsActive = false;
            Settings.AreaCaptureAllowInNormalPmc.Value = true;
            World.LocationId = ArenaMapCatalog.CustomsId;
            Game.RaidSettings.location = World.LocationId;
            Game.RaidSettings.serverId = "native-pmc-raid";
        }
        public void Press(KeyCode key, bool withModifier = true)
            => Step(new[] { key }, withModifier ? new[] { key, KeyCode.LeftControl } : new[] { key });
        public void Step(KeyCode[] down = null, KeyCode[] held = null)
        {
            Input.Frame(down ?? Array.Empty<KeyCode>(), held ?? Array.Empty<KeyCode>());
            Call("Update");
        }
        public void SeedTriangle()
        {
            foreach (var point in new[] { new Vector3(0, 1, 0), new Vector3(8, 1, 0), new Vector3(0, 1, 8) })
            { World.MainPlayer.Position = point; Press(KeyCode.F7); }
            Check(Draft?.Points.Count == 3, "fixture seeds valid triangle through actual input path");
        }
        public void Call(string method)
        {
            var info = typeof(ArenaAreaCapture).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(ArenaAreaCapture).FullName, method);
            try { info.Invoke(Capture, null); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("Actual capture callback escaped: " + method, ex.InnerException); }
        }
        public string[] Files() => Directory.Exists(TempDirectory)
            ? Directory.GetFiles(TempDirectory, "*.json", SearchOption.AllDirectories) : Array.Empty<string>();
        public void Dispose()
        {
            // Delete only this harness-generated GUID directory, never the real
            // plugin location or a path obtained from an export payload.
            string resolved = Path.GetFullPath(TempDirectory);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(parent + "ArenaPolygonScope-", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(resolved).Length != "ArenaPolygonScope-".Length + 32)
                throw new InvalidOperationException("Refusing to clean an unexpected scope-harness path.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
