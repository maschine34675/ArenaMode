# Respawn-Engine Stufe 0 r1 — Gesamtstop in Raid A

**FAIL / keine vollständige Stage0-Abnahme.** Am 12.09.2026 gelang im ersten gewöhnlichen PMC-Raid ein Redeploy-Respawn mit nacktem Ersatzkörper und zehn Sekunden Spawnschutz. Nach einem zweiten natürlichen Bot-Tod trat beim Raid-Ende eine neue `NullReferenceException` auf. Der gesamte Testlauf wurde gemäß Stopregel beendet; B, C, D sowie die nachgeordneten r6/r4-Tests bleiben **OFFEN**. Der Stack beginnt in `BotSurgicalKit+CG_ApplyToCurrentPart.method_1`; das konkrete Nullobjekt und die Ursache sind mit diesen Beobachtungen nicht geklärt.

Grundlage: [GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r1.md](GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r1.md), Test-ID `respawn-stage0-r1`. AGENTS und der aktuelle HANDOFF-Eintrag „Redeploy.Core stage 0 integrated“ wurden gelesen. Ausschließlich native Spielbedienung, Datei-/Logbeobachtung und reguläre Prozessstarts; keine Builds, Produktionsänderungen, DLL-Tausche, Laufzeitinstrumentierung, internen Arm-Aufrufe oder erzwungenen Matchereignisse.

## Bindung und Umgebung

Installation `C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`. Der vorgeschriebene Arena-Build ist `b5ff2585f1dea2642835582d7b06bf8e4ec72f65`, Code-Stand `f257a78`, Build-Branch `feature/arena-air-control`. Der tatsächliche Checkout stand bei der Vorprüfung auf `984d8fbb6d6ff44dc40b1a9d4d7c16a7a54f984f`, Branch `fix/offline-harness-contracts`; der Dateibindungsnachweis erfolgt über die installierten DLLs. Spätere Dokumentations-/Harness-Arbeiten sind kein Anlass für einen Neubau während dieses GPU-Tests.

Alle acht Sollwerte stimmen bei der [Vorprüfung](../test-evidence/respawn-stage0-r1/hash-preflight.json) **19:21:03.822 UTC**, [vor dem frischen Spielstart](../test-evidence/respawn-stage0-r1/hash-before-launch.json) **19:26:08.341 UTC**, [unmittelbar vor Raid A](../test-evidence/respawn-stage0-r1/hash-before-raid-A.json) **19:35:05.120 UTC** und [am Ende](../test-evidence/respawn-stage0-r1/hash-final.json) **19:41:41.965 UTC** überein:

| Komponente | Installierte SHA256 (= Soll) | Version |
|---|---|---|
| ArenaMode.Client | `A92164BC306762ABD954CF3F2A5AA012FA49E8471626966C5A459A1FEE02843D` | Product `0.1.0+b5ff2585f1dea2642835582d7b06bf8e4ec72f65` |
| Redeploy.Core | `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` | Product `0.9.0+287c2d95a4645ae72dbdf44843958781c84b9b60` |
| Redeploy.Client | `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46` | Product `0.9.0+c938f5c43cb311a8a9ba1f81b2eb10573b99b936` |
| ArenaMode.Server | `5A9201177DC3E4404995EA314FE48A231712435D5E5EB9E1A081C67734D6C04E` | Product `0.1.0+f257a783623daa939ebf6a0750d0521e3b306263` |
| Redeploy.Server | `53F283D8B8F32EC7A7FB94AA1CC7EBC12115169C74A97E77B4088B0E5F06070B` | Product `0.9.0+58abf5876629bb51be808d78ee2cd7a5d34af77a` |
| SAIN | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` | Assembly `4.5.1.0` |
| KillcamReplay | `B5F984E6605F4DB206E69C3180DFEE0D743278796C1038204E67EA161BE66CD5` | Assembly `0.9.5.0` |
| DynamicMaps | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` | Assembly `1.2.1.0` |

**PASS für die Dateibindung: 8/8 Dateien bei allen vier Prüfungen hashgleich.** Der abschließende Checkout-Read findet `508abbb622de3a0c8d04060ef8ba35c37d8ecb1d`; parallel entstandene Repository-Arbeiten wurden nicht in die installierten Testdateien übernommen. Die Native-Abnahme bleibt unabhängig davon FAIL.

| Prozess | PID | Start UTC |
|---|---:|---|
| EscapeFromTarkov | 8508 | 19:26:10.1387498 |
| SPT.Server | 12364 | 19:22:16.1102827 |
| SPT.Launcher | 14260 | 19:23:30.7547633 |

Spielbilder zeigen 1920 × 1080. Die privat gesicherte Graphics.ini nennt diese Auflösung, FullScreenMode `1` und aktiviertes DLSS `Balanced`; die Grafikoptionen wurden für Stage0 nicht geändert. Der DLSS-Modus ist hier durch die Datei belegt, nicht durch einen zusätzlich geöffneten nativen Grafikdialog. [Prozessidentitäten](../test-evidence/respawn-stage0-r1/processes.json).

Vor dem frischen Spielstart scheiterten zwei Launcher-Startversuche um **19:22:02.563** und **19:22:55.935 UTC** mit einer Windows-.NET-`UnauthorizedAccessException`: Das vom Startwerkzeug übernommene Arbeitsverzeichnis lag im Codex-WindowsApps-Verzeichnis; der Launcher wollte dort `app/user/logs` anlegen. Danach wurde der Launcher mit seinem tatsächlichen Arbeitsverzeichnis `C:\SPT41\SPT_Runtime` gestartet. Diese zwei erhaltenen Vorbereitungsfehler liegen außerhalb des anschließend gestarteten Spielprozesses und seiner Core-Laufzeit. Sie werden separat ausgewiesen; eine pauschale Aussage „keine Exceptions im Auftrag“ wäre falsch. Es wurden keine Verzeichnisrechte oder Produktionsdateien verändert.

**Protokollabweichung in der Vorbereitung:** Die Stopregel lautet „Jede neue Exception“ und enthält keine ausdrückliche Launcher-Ausnahme. Die Fortsetzung nach Korrektur des Arbeitsverzeichnisses wich deshalb von ihrer strengen wörtlichen Auslegung ab. Die Erklärung des Startfehlers ist keine rückwirkende Harmlosfreigabe und der Auftrag wird nicht als vollständig protokollkonform dargestellt. Die späteren natürlichen Raidbeobachtungen bleiben als begrenzte Belege erhalten; eine Gesamtabnahme entsteht daraus nicht. Nach Erkennung der neuen Spiel-Runtime-Exception wurde der gesamte Test gestoppt.

Beide Windows-Ereignisse 1026 haben denselben relevanten Stack (nur der Installationspräfix des fehlgeschlagenen Zielpfads ist hier verkürzt):

```text
System.UnauthorizedAccessException: Access to the path '[Codex-WindowsApps]/app/user/logs' is denied.
   at System.IO.FileSystem.CreateDirectory(String fullPath, Byte[] securityDescriptor)
   at System.IO.Directory.CreateDirectory(String path)
   at SPTarkov.Core.Logging.FileLoggerProvider..ctor()
   at SPTarkov.Core.Extensions.LoggingExtensions.AddFileLogger(ILoggingBuilder builder)
   at SPTarkov.Launcher.Launcher.<>c.<Main>b__24_0(ILoggingBuilder builder)
   at Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(IServiceCollection services, Action`1 configure)
   at SPTarkov.Launcher.Launcher.Main(String[] args)
```

## Startprüfung

| Frischer Clientmarker | Anzahl | UTC / Clientzeile | Status |
|---|---:|---|---|
| `Redeploy.Core 0.9.0.0 initialized` | 1 | 19:26:41.454 / L3991 | PASS |
| `Death-interception driver registered: ArenaMode (priority 100)` | 1 | 19:26:42.759 / L4545 | PASS |
| `Death-interception driver registered: Redeploy (priority 0)` | 1 | 19:26:48.170 / L7110 | PASS |
| `Death interception is incomplete`, `could not be installed`, Raid-start-/Raid-end-Patchfehler | jeweils 0 | bis zum Auditstand | PASS für die beobachtete Startvoraussetzung |

Die [Startup-Prüfung](../test-evidence/respawn-stage0-r1/startup-log-audit.json) bis **19:28:45.200 UTC**, Client L1–13073 und Server L20786–21421, findet keine Spiel-/Serverfehler. Sie belegt das Laden der drei Komponenten, noch keinen erfolgreich abgeschlossenen Raid. [Startmarker und Warnungen](../test-evidence/respawn-stage0-r1/startup-markers-and-warnings.txt).

## A — gewöhnlicher Redeploy-Respawn

**Ein einziger Raid:** gewöhnlicher PMC, Factory Tag, vorhandenes GPU-Testprofil. Im nativen Stash wurde eine vorhandene M9A3 ausgerüstet, um nach dem Respawn das Zurückholen der eigenen Ausrüstung prüfen zu können. Ein erster Drag-Versuch hatte keine sichtbare Wirkung; das anschließende native Kontextmenü „Equip“ rüstete die Pistole nachweisbar aus. Keine Arena-Aktivierung; `/arenamode/ordinary-confirm` war erfolgreich.

Vor A wurde in F12 der vorhandene persönliche Wert `MaxRespawnsPerRaid = 2` auf den geforderten Standard **1** zurückgesetzt. `RespawnCooldownSeconds = 5` blieb unverändert; Schutzdauer **10 s**, kein benötigtes Respawn-Item. Der B-Testwert 20 wurde nie gesetzt.

| Unterprüfung | Status | Tatsächlicher Nachweis / Grenze |
|---|---|---|
| Factory Tag als gewöhnlicher PMC, natürliche Botreaktion und erster Tod | PASS | Zwei absichtliche Schussklicks und kurze Rechts-Eingaben; erster Killcam-/Todeseintrag 19:37:05 UTC, Redeploy meldet `0/1` |
| Redeploy-Fenster mit Cursor statt nativem KIA-Bildschirm beim ersten Tod | PASS | Fenster und Killcam gleichzeitig sichtbar; „Respawn near corpse“ nativ angeklickt |
| Cooldown und nackter Ersatzkörper mit Spawnschutz | PASS für diesen Teilablauf | Klick 19:37:18.936 UTC; Schutz aktiv 19:37:24.483; `Respawn complete` 19:37:24.892; nackte Egoansicht im folgenden Originalbild |
| Vollständige geforderte Markerfolge einschließlich Black-screen-Blockmarker | OFFEN / nicht erfüllt | Die vier anderen Marker erscheinen in der geforderten Reihenfolge; `Blocked raid-end black screen while respawn is pending.` erscheint **0-mal**. Kein Ersatznachweis durch andere Marker |
| Ende des Spawnschutzes | PASS für Marker | `Spawn protection ended.` um 19:37:34.811; anschließend zweiter natürlicher Bot-Tod |
| Killcam-Abbruch beim ersten Respawn | PASS für den beobachteten Übergang | `Playback end after 9.96s: camera destroyed` um 19:37:24.906; Recorder bindet den neuen Spieler. In diesem Übergang keine Exception |
| Kamera und gezielte Eingabe am neuen Körper | OFFEN | Ersatzkörper-Egoansicht sichtbar; keine ausreichende kontrollierte Bewegungs-/Kamerasequenz vor dem zweiten Tod |
| Sprint und Sprung am Ersatzkörper | OFFEN | Ein Sprungtastendruck wurde erst 19:37:38.078 protokolliert, nach dem zweiten Tod; kein bestätigter Sprung des lebenden Ersatzkörpers |
| Escape-Menü und sichtbarer Extraktionstimer am Ersatzkörper | OFFEN | Vor dem zweiten Tod nicht bestätigt |
| Eigene Leiche lootbar, M9A3 zurückgeholt | OFFEN | Core meldet, dass die Leiche die Ausrüstung behält; tatsächlich gelootet wurde sie nicht. Dieser Marker ersetzt keinen Lootnachweis |
| DynamicMaps-Marker folgt dem Ersatzkörper | OFFEN | Keine bestätigte Karten-/Bewegungsfolge vor dem zweiten Tod |
| Geplanter regulärer Extract-/Escape-Exit | OFFEN | Stattdessen zweiter natürlicher Tod bei ausgeschöpftem Respawnlimit und native KIA-Abwicklung |
| Fehlerfreiheit des gesamten A-Raids einschließlich Endübergang | **FAIL** | Neue Unity-`NullReferenceException` um 19:37:54.612 UTC; Gesamtstop |
| Hauptmenü und Stash nach A | PASS für Erreichbarkeit | Native KIA-Nachbereitung/Bestätigung; Hauptmenü 19:39:30 UTC, Charakter/Stash 19:39:45 UTC normal geöffnet |

Die [UI-Handlungen](../test-evidence/respawn-stage0-r1/input-actions.json) wurden einzeln mit jeweils anschließender Zustandsaufnahme protokolliert. Die beiden absichtlichen Schussklicks erfolgten um **19:36:34.870** und **19:36:58.923 UTC**; dies sind dokumentierte Eingaben, kein nachträglich gemessener Munitionsverbrauch. Die Rechts-Eingabe um 19:37:09.565 fiel bereits nach dem ersten Tod. Aus Eingabezeitpunkten wird daher kein unbeobachteter Bewegungserfolg abgeleitet.

Nach Ende des Schutzes um 19:37:34.811 meldete KillcamReplay den zweiten Bot-Tod um **19:37:35.279 UTC**, nur **0,468 s später**, und stellte `LocalGame.Stop` zurück. Das sehr kurze Überlebensfenster nach den zehn Schutzsekunden und die einzelnen Aufnahme-/Werkzeugschritte verhinderten die meisten weiteren Funktionsprüfungen; sie bleiben OFFEN. Um **19:37:54.482 UTC** wurde der native Stop erneut aufgerufen; **130 ms später** erschien die Exception. Der Server dokumentiert um **19:37:56.295 UTC** den Ausgang **KILLED**. Der zeitliche Zusammenhang belegt keine Verursachung durch KillcamReplay oder Redeploy.

## Gesamtstop: vollständiger relevanter Stack

Quelle **Unity Log**, Client **L13200–13234**, **21:37:54.612 MESZ = 19:37:54.612 UTC**. Eine Exception, keine Fehlerflut. Im Stack ist kein Redeploy.Core-, Redeploy- oder Arena-Frame enthalten; daraus folgt weder Entlastung noch eine gesicherte Mod-Zuordnung. Das konkrete Nullobjekt wurde nicht instrumentiert oder durch einen weiteren Raid untersucht.

```text
NullReferenceException: Object reference not set to an instance of an object
Stack trace:
BotSurgicalKit+CG_ApplyToCurrentPart.method_1 (Comfort.Common.Result`1[T] applyResult) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.Player+MedsController+MedsInHandsOperation.Finish () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.Player+MedsController+MedsInHandsOperation.OnRemoveItem (EFT.InventoryLogic.RemoveItemEventArgs args) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.InventoryLogic.ItemController.SafeRemoveItemEvent (EFT.InventoryLogic.RemoveItemEventArgs obj) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
UnityEngine.DebugLogHandler:LogException(Exception, Object)
LogHandler:UnityEngine.ILogHandler.LogException(Exception, Object)
UnityEngine.Debug:LogException(Exception)
EFT.InventoryLogic.ItemController:SafeRemoveItemEvent(RemoveItemEventArgs)
EFT.InventoryLogic.ItemController:RaiseRemoveEvent(RemoveItemEventArgs)
EFT.InventoryLogic.ItemAddress:RaiseRemoveEvent(Item, CommandStatus, IItemOwner)
EFT.InventoryLogic.ContainerRemoveResult:RaiseEventsInternal(IItemOwner, CommandStatus)
EFT.InventoryLogic.ItemInContainerOperationResult:RaiseEvents(IItemOwner, CommandStatus)
EFT.InventoryLogic.RemoveResult:RaiseEvents(IItemOwner, CommandStatus)
EFT.InventoryLogic.DiscardResult:RaiseEvents(IItemOwner, CommandStatus)
EFT.HealthSystem.MedEffectHelper:RemoveItem(Item)
EFT.HealthSystem.MedEffect:Residue()
EFT.HealthSystem.Effect:NextState()
EFT.HealthSystem.Effect:ManualUpdate(Single)
EFT.HealthSystem.ActiveHealthController:ManualEffectUpdate(Effect, Single)
EFT.HealthSystem.PlayerHealthController:ManualEffectUpdate(Effect, Single)
EFT.HealthSystem.BaseHealthController`1:ManualUpdate(Single)
EFT.HealthSystem.ActiveHealthController:ManualUpdate(Single)
EFT.Player:HealthControllerUpdate(Single)
EFT.Player:ManualUpdate(Single, Nullable`1, Int32)
EFT.LocalPlayer:ManualUpdate(Single, Nullable`1, Int32)
EFT.Player:ComplexUpdate(EUpdateQueue, Single)
EFT.Player:UpdateTick()
EFT.GameWorld:CG_PlayerTick(Player)
EFT.GameWorld:SafePlayerOperation(Action`1)
EFT.GameWorld:PlayerTick(Single)
ClientLocalGameWorld:PlayerTick(Single)
EFT.GameWorld:DMD<EFT.GameWorld::DoWorldTick>(GameWorld, Single)
EFT.GameWorldUnityTickListener:Update()
```

Nach Erkennung wurden nur noch die native Nachbereitung, Hauptmenü-/Stashprüfung und Wiederherstellung eigener Testwerte bedient. Kein weiterer Raid, keine Give-up-Erzwingung, kein Warten auf MIA und keine Profilrückkopie.

## Logzählung

Der [unabhängige Abschlussaudit](../test-evidence/respawn-stage0-r1/final-log-audit.json), erstellt um **19:43:25.472 UTC**, wertet die bereits nach Hauptmenü und Rückstellung gesicherte feste Kopie `final-after-main-menu-and-settings-restore` aus. Die Erstellungszeit ist kein späteres Laufzeit-Logfenster; die letzte Serverdateiänderung des Snapshots liegt um **19:41:40.059 UTC**. Umfang: frischer Client **L1–13244**, neuer Serverprozess **L20786–21811**. Der A-/Nachbereitungsabschnitt umfasst Client L13074–13244 und Server L21422–21811. Gegenüber dem [ersten Stop-Audit](../test-evidence/respawn-stage0-r1/a-stop-log-audit.json) kamen zwölf Serverzeilen ohne neue Warnung/Fehler hinzu; der Client blieb bytegleich. Clientzeiten sind MESZ/UTC+02:00; Serverzeiten UTC. [Gezielte finale Logauszüge mit vollständigem Stack](../test-evidence/respawn-stage0-r1/final-log-excerpts.txt).

| Quelle / Abschnitt | Warnungen | Fehler |
|---|---:|---:|
| Redeploy.Core, gesamter frischer Client | 1 | 0 |
| maschine-Redeploy, gesamter frischer Client | 0 | 0 |
| maschine-ArenaMode, gesamter frischer Client | 0 | 0 |
| Unity Log, gesamter frischer Client | 1 | 1 |
| maschine-KillcamReplay, gesamter frischer Client | 1 | 0 |
| Andere Clientquellen, ausschließlich Startup | 9 | 0 |
| **Client gesamt** | **12** | **1** |
| Server, neuer Prozess gesamt | 1 | 0 |
| Server, nur A/Nachbereitung | 0 | 0 |
| Launcher-Vorbereitung, Windows-Ereignisprotokoll | separat, nicht als Clientwarnung gezählt | 2 Startfehler |

Die neun Startup-Clientwarnungen sind fünf Ausschlüsse nicht vorhandener Fika-Abhängigkeiten aus dem Serialization Fix und vier HarmonyX-Zielmethodenwarnungen. Die Serverwarnung betrifft einen fehlenden Trader mit temporärem Standardeintrag. Die drei späteren Clientwarnungen:

| UTC / Clientzeile | Quelle | Meldung |
|---|---|---|
| 19:35:35.918 / L13093 | Unity Log | `BoxCollider does not support negative scale or size.` |
| 19:37:24.611 / L13176 | Redeploy.Core | `Destroying stale PerfectCulling sampler 'FPS Camera'.` |
| 19:37:36.216 / L13194 | maschine-KillcamReplay | `Puppet weapon: no meshes found to copy` |

Die Lade-/Culling-Warnungen werden damit getrennt von der neuen Runtime-Exception gezählt. Die beim Start gedruckte Harmony-Finalizer-Signatur mit Rückgabetyp `Exception` sowie die Debug-Konfiguration `Assert.raiseExceptions` sind keine ausgeführten Exceptions.

| Gezählter Marker, gesamter frischer Client | Anzahl |
|---|---:|
| `Death intercepted. Respawns used: 0/1` | 1 |
| `Blocked raid-end black screen while respawn is pending.` | **0** |
| `Spawn protection active for` | 1 |
| `Respawn complete.` | 1 |
| `Spawn protection ended` | 1 |
| `KeyNotFoundException` / `Respawn failed` / `Player gave up` | jeweils 0 |
| `Respawn refused before any teardown` | 0 |
| `subscriber … failed` in Core/Arena | 0 |
| `GameEnd faulted before its first await` | 0 |
| `Freecam compatibility patches could not be enabled` | 0 |
| Unvollständige Death-Interception / `could not be installed` / Raid-start-/Raid-end-Patchfehler | jeweils 0 |
| `External raid end while respawn pending` / `Respawn cancelled: the raid ended during the cooldown` | jeweils 0, B nicht ausgeführt |
| `Arena respawn finished without the exact new local-player body` | 0, kein Arena-Raid |
| `stood down … another body replacement is still in flight` | 0, kein Arena-Raid |
| `live respawn was refused before takeover` | 0, kein Arena-Raid |
| `Arena event … subscriber … failed` / `Arena … failed:` | jeweils 0, kein Arena-Raid |
| `old-body recovery failed` / `replacement-body recovery failed` | jeweils 0, kein Arena-Raid |

Null Treffer bei nicht ausgelösten Szenarien sind nur Abwesenheitszählungen und keine Abnahme des jeweiligen Verhaltens.

## Restfälle, Zeitbudget und Profilgrenze

| Fall | Status | Grund |
|---|---|---|
| A als vollständiger Abnahmefall | **FAIL** | Neue Exception; mehrere Funktionsprüfungen vor dem zweiten Tod unerreicht |
| B: Exit während 20-s-Cooldown | OFFEN | A nicht sicher; Wert 20 nie gesetzt, kein B-Raid |
| B: weiterer kurzer PMC-Raid zum Lifecycle-Reset | OFFEN | Gesamtstop; kein weiterer Ready-/Spawn-Zyklus |
| C: Arena-FFA-Kit-Swap, Tod, Give up und Profilvergleich | OFFEN | B nicht sicher / Gesamtstop |
| D: optionaler Rundenwechsel und Profilvergleich | OFFEN | Gesamtstop; kein optionaler Raid |
| Fail-closed bei Patchausfall, Event-Reentranz, abgelehnte Live-Ersetzung | SKIP | Laut Auftrag nativ nicht auslösbar; keine Probe-Erweiterung gebaut |
| Deathmatch-Menü r6 und Refinements r4 | OFFEN | Vom Nutzer ausdrücklich nach Stage0 eingeordnet; Stage0 gestoppt, alte Berichte unverändert |

**1 von maximal 4 Raids.** Für das aktive Budget werden konservativ **101 Sekunden = 1:41 Minuten** entsprechend der nativen Ergebnisanzeige verbucht; klar unter 30 Minuten. Die genaue Live-Spawnsekunde wurde nicht instrumentiert. Eine frühere Hilfsschätzung aus einem später protokollierten Timer-/Toolzeitpunkt war zu kurz und zählt nicht als präzise Messung.

Der [Budgetbeleg](../test-evidence/respawn-stage0-r1/budget.json) trennt die folgenden Zeiten. Vorbereitung **19:20:26.848–19:35:05.121 UTC**, insgesamt **878,273 s**. Ready bis erfolgreiche gewöhnliche Raidbestätigung **19:36:14.622 UTC**: **69,501 s**. Der Servermarker `Starting: factory4_day` um 19:35:28.592 liegt im Laden; weder er noch die gewöhnliche Bestätigung sind ein exakt instrumentierter Spawnframe. Das erste Spawn-Original wurde um **19:36:22.518 UTC** archiviert. `ordinary-confirm` bis verzögerter nativer Stop ergibt **99,866 s**; für das Budget zählen stattdessen konservativ die nativen **101 s**. Server-KILLED um 19:37:56.295, Hauptmenü um 19:39:30.929, Stash um 19:39:44.995, endgültiges Hauptmenü nach Rückstellung um **19:41:32.334 UTC**. [Raidbeleg](../test-evidence/respawn-stage0-r1/raids.json).

Es gibt **37 protokollierte UI-Eingaben** mit zusammen **3,584 s** zwischen Eingabeanforderung und deren Rückmeldung. Das misst nur diese Aufrufabschnitte, nicht sämtliche Aufnahme-, Lese-, Analyse- und Wartezeit. Ein exakter unabhängiger Gesamtwert aller Werkzeugzeiten ist nicht verfügbar. Während der laufenden Welt verstrichene Werkzeug- und Wartezeit ist in den 101 Sekunden enthalten; Vorbereitungs-, Lade- und Nachbereitungszeiten werden separat ausgewiesen.

A war ein gewöhnlicher PMC-Raid. Reguläre Ausrüstungs-, Gesundheits- und Fortschrittsänderungen sind hier erwartet; der Auftrag verlangt für A keinen Vorher-/Nachher-Profilvergleich. Das vorhandene Testprofil wurde nativ verwendet, danach Charakter und Stash geöffnet. Keine Profil-JSON gelesen, kopiert, bearbeitet oder zurückgespielt. C/D begannen nicht; vollständige PMC-/Scav-Vergleiche für Inventory, Quests, Skills, Health, Experience, Level, TaskConditionCounters, RepeatableQuests und Gesamt-JSON bleiben deshalb **OFFEN**, nicht PASS.

## Wiederherstellung und Belege

Vorher privat gesichert: neun Einstellungsdateien und Hashinventar von 64 Arena-Assetdateien. Nur eigener Redeploy-Testwert `MaxRespawnsPerRaid` wurde von 2 auf 1 geändert. Cooldown blieb 5; kein Audio- oder sonstiger Mod-Testwert wurde verändert.

`MaxRespawnsPerRaid = 2` wurde nativ zurückgestellt; Cooldown ist weiterhin **5**. Die Redeploy-Konfiguration ist danach vollständig bytegleich mit der Sicherung. [Original der Rückstellung](../test-evidence/respawn-stage0-r1/originals/s0-cleanup-settings-restored-2026-09-12T19-40-39-014Z-0.jpg). Der [Abschlussvergleich](../test-evidence/respawn-stage0-r1/settings-restoration.json) findet **6/9 Einstellungen bytegleich**: Redeploy, RaidInfoPanels, Graphics, Control, Arena-Serverkonfiguration und KillcamReplay. ArenaMode.cfg, DynamicMaps.cfg und client-settings.json unterscheiden sich; deshalb wird keine pauschale Bytegleichheit aller Einstellungen behauptet.

Der [unabhängige semantische Vergleich](../test-evidence/respawn-stage0-r1/settings-semantic-audit.json) um **19:44:52.912 UTC** findet keine Änderung der 149 Arena-, 83 DynamicMaps- und sieben Redeploy-Konfigurationswerte. ArenaMode.cfg unterscheidet sich nur in acht generierten Bereichskommentaren mit Dezimalkomma statt Dezimalpunkt. DynamicMaps.cfg enthält zwei an 1920 × 1080 angepasste Bereichskommentare; die eigentlichen Offsetwerte bleiben null. client-settings.json unterscheidet sich ausschließlich in `UpdatedAt` von `21:02:31` zu `21:35:32` ohne gespeicherte Zeitzone; alle 147 Einträge und sonstigen JSON-Inhalte sind gleich. Es wurden keine Sicherungen zurückkopiert. [Alle 64 Arena-Assets sind hashgleich](../test-evidence/respawn-stage0-r1/assets-final.json).

Die abschließende [Prozessbeobachtung um 19:44:20.870 UTC](../test-evidence/respawn-stage0-r1/processes.json) bestätigt weiterhin reagierende Spiel-, Server- und Launcherprozesse mit den oben genannten PIDs. Das Spiel steht nach dem Cleanup im Hauptmenü.

Öffentliche Originalbilder, redigierte gezielte Logauszüge und Manifest gehören unter [test-evidence/respawn-stage0-r1/](../test-evidence/respawn-stage0-r1). Vollständige Logs, Windows-Ereignisse, Einstellungen und Rohdaten liegen privat im ignorierten Verzeichnis `artifacts/gpu-respawn-stage0/respawn-stage0-r1/2026-09-12T19-20-26-848Z/`. Launcherbilder mit privaten Kennungen sowie ein anfänglicher transparenter Spielshot mit Codex-Inhalt werden nicht veröffentlicht. Keine zugeschnittenen oder nachbearbeiteten Ersatzbilder als Originalbeleg.

Die [15 Originalbilder mit SHA256](../test-evidence/respawn-stage0-r1/originals-index.json) sind unveränderte Aufnahmen. Bei zwei zurückgegebenen Spieloberflächen wurde jeweils die erste gewählt. Die Dateizeiten sind Archivierungszeiten, keine exakt instrumentierten Renderframes. Zentrale Belege:

- [Redeploy-Standardwert für A](../test-evidence/respawn-stage0-r1/originals/s0-redeploy-default-respawn-count-2026-09-12T19-28-44-598Z-0.jpg), [ausgerüstete vorhandene M9A3](../test-evidence/respawn-stage0-r1/originals/s0-A-pistol-equipped-2026-09-12T19-30-50-670Z-0.jpg), [Factory/PMC-Startbestätigung](../test-evidence/respawn-stage0-r1/originals/s0-A-insurance-next-2026-09-12T19-34-57-631Z-0.jpg).
- [Erstes Todesfenster mit Killcam](../test-evidence/respawn-stage0-r1/originals/s0-A-step-right-2-2026-09-12T19-37-11-225Z-0.jpg), [Near-corpse-Auswahl](../test-evidence/respawn-stage0-r1/originals/s0-A-respawn-near-corpse-2026-09-12T19-37-20-781Z-0.jpg), [Ersatzkörper](../test-evidence/respawn-stage0-r1/originals/s0-A-respawn-observe-2026-09-12T19-37-28-682Z-0.jpg).
- [Zweiter Todes-/Replayzustand nach verspätetem Sprungversuch](../test-evidence/respawn-stage0-r1/originals/s0-A-replacement-jump-2026-09-12T19-37-39-969Z-0.jpg), [KIA-Ergebnis](../test-evidence/respawn-stage0-r1/originals/s0-A-post-death-transition-2026-09-12T19-38-11-801Z-0.jpg).
- [Hauptmenü](../test-evidence/respawn-stage0-r1/originals/s0-A-cleanup-menu-arrival-2026-09-12T19-39-30-929Z-0.jpg), [normal geöffneter Stash](../test-evidence/respawn-stage0-r1/originals/s0-A-cleanup-stash-open-2026-09-12T19-39-44-995Z-0.jpg), [abschließendes Hauptmenü](../test-evidence/respawn-stage0-r1/originals/s0-cleanup-final-main-menu-2026-09-12T19-41-32-334Z-0.jpg).

Das [SHA256-Manifest](../test-evidence/respawn-stage0-r1/SHA256SUMS.txt) bindet alle veröffentlichten Dateien im Belegordner außer sich selbst. Die dortige `.gitattributes` verhindert Änderungen der Belegbytes durch Git-Zeilenendenkonvertierung. Vollständige Rohlogs und private Kennungen sind darin nicht veröffentlicht.

Historische r6/r4-Berichte aus `bc7f541` bleiben erhalten. Nur dieser neue Bericht und seine eigenen geprüften Belege gehören in den Abschlusscommit; fremde Änderungen bleiben unangetastet. Ein neuer Spieltest benötigt zunächst die Klärung des Stopbefunds und einen dazu passenden neuen Testauftrag; r6/r4 erhalten durch den geglückten einzelnen Respawn keine Abnahme.
