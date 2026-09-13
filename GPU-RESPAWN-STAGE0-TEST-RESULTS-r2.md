# Respawn-Engine Stufe 0 r2 — GPU-Ergebnis vom 13.09.2026

**FAIL / Gesamtstopp; keine vollständige Stage-0-Abnahme.** In vier nativen Raids gelangen gewöhnliche Redeploy-Respawns, der vorgeschriebene zusätzliche Raid-Grenzreset, ein Arena-Startkitwechsel und ein Arena-Give-up mit Ersatzkörper. A blieb teilweise ungeprüft und enthält einen Unity-Menüfehler. B löste den geplanten Cooldown-Abbruch nicht aus und bleibt dafür OFFEN. Beim regulären Ende von C erschien eine neue **BotFirstAid+CG_ApplyToSelf.method_1 NullReferenceException**; diese ist nicht die in r2 ausdrücklich ausgenommene BotSurgicalKit-Signatur. Danach keine weiteren Testfälle oder Raids, insbesondere kein D.

Zusätzlich zeigt die Abschlussprüfung einen **Hashwechsel der optional erlaubten BotDropDiag-DLL**. Die acht Kernbindungen bleiben identisch, die Gesamtdateibindung ist dennoch **FAIL**. Urheber, Ursache, exakter Schreibzeitpunkt und Auswirkung auf die bereits laufende Assembly sind nicht bewiesen. Die begrenzten Beobachtungen bleiben dokumentiert; kein behaupteter unveränderter Gesamtbuild über den ganzen Lauf.

Abschlussstand: Hauptmenü wieder erreicht, Einstellungen zurückgestellt, Profilvergleich und finale Log-/Dateiprüfung abgeschlossen. Bericht und Originalbelege gehören ausschließlich zu diesem frischen Lauf nach dem angeforderten Neustart. Der unterbrochene Vorversuch vom 12.09. hatte keinen Raid und wird nicht mitgezählt.

## Auftrag, Bindung und Umgebung

Grundlage ist GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r2.md; AGENTS.md, aktueller HANDOFF und r1-Bericht wurden gegengelesen. Test-ID **respawn-stage0-r2**. Ausschließlich native Bedienung des Spiels und ergänzende lesende Auswertung. Die angeordneten Testwerte wurden über F12 gesetzt und anschließend zurückgestellt. Keine Produktionsdateien oder Profile wurden durch die Testwerkzeuge bearbeitet. Kein Build, Deployment, interner Arm-Aufruf, erzwungenes Matchereignis oder Laufzeitinstrumentierung.

Private Evidenzwurzel: `artifacts/gpu-respawn-stage0/respawn-stage0-r2/2026-09-13T06-09-57-689Z/`. Vollständige Logs, Profile und Einstellungen verbleiben dort. Veröffentlichbare Belege liegen unter [test-evidence/respawn-stage0-r2](test-evidence/respawn-stage0-r2/README.md), private Kennungen in den gezielten Auszügen sind redigiert. r1 bleibt unverändert.

| Merkmal | Beobachteter Stand |
|---|---|
| Arena-Build / Code | b5ff2585f1dea2642835582d7b06bf8e4ec72f65 / f257a78 |
| Redeploy-Build / Code | main 287c2d95a4645ae72dbdf44843958781c84b9b60 / c938f5c |
| Gelesener Checkout | fix/offline-harness-contracts, HEAD 8041cbc872e650ee240f8247de5b89582a50b666; lesende Momentaufnahme vor dem Ergebniscommit, kein Ersatz für die installierten Dateiidentitäten |
| Frischer Server | PID 9780, Start 08:09:58.437 MESZ |
| Frisches Spiel | PID 6332, Start 08:11:24.026 MESZ |
| Launcher | PID 5396, Start 08:09:58.458 MESZ; aus dem tatsächlichen Arbeitsverzeichnis `C:\SPT41\SPT_Runtime` gestartet |
| Darstellung | 1920 × 1080, Borderless (FullScreenMode 1), DLSS aktiviert / Balanced; Graphics.ini bytegleich |
| Zeitangaben | Client-/UI-Zeiten MESZ = UTC+2, Server explizit UTC; Aufnahme-/Eingabezeitpunkt und Spieltimer werden getrennt gehalten |

Vorprüfung **06:09:57.702 UTC**, vor Spielstart **06:11:22.165 UTC** und vor A/B/Reset/C **06:16:24.273 / 06:30:18.083 / 06:36:53.949 / 06:41:31.878 UTC**: jeweils **9/9** Dateien passend. Sechs vorhandene Hashmanifeste belegen diese Lesezeitpunkte.

| Komponente | Vorgeschriebener SHA256 | Endprüfung |
|---|---|---|
| ArenaMode.Client | `A92164BC306762ABD954CF3F2A5AA012FA49E8471626966C5A459A1FEE02843D` | PASS |
| Redeploy.Core | `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` | PASS |
| Redeploy.Client | `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46` | PASS |
| ArenaMode.Server | `5A9201177DC3E4404995EA314FE48A231712435D5E5EB9E1A081C67734D6C04E` | PASS |
| Redeploy.Server | `53F283D8B8F32EC7A7FB94AA1CC7EBC12115169C74A97E77B4088B0E5F06070B` | PASS |
| SAIN | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` | PASS |
| KillcamReplay | `B5F984E6605F4DB206E69C3180DFEE0D743278796C1038204E67EA161BE66CD5` | PASS |
| DynamicMaps | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` | PASS |
| BotDropDiag | `5B37C035BD860E9EED5A9520EE62BF8D607591A77996142BB80CA18BF48E0985` | **FAIL**, Endhash unten |

Endprüfung **06:54:06.092 UTC**: BotDropDiag stattdessen **0D885B3FADCDF448DA71B1D459079253EC15A6DBDB5934AB4959863C2DD686D0**. Gelesener LastWrite-Zeitstempel **06:42:50.7443527 UTC**. Der Dateizeitstempel ist kein bewiesener Verursacher- oder Live-Ladenachweis. Der letzte passende Vor-C-Hash liegt um 06:41:31.878 UTC; ein Wechsel danach ist bei der Abschlussprüfung sicher, dessen Wirkung auf C bleibt offen. Dieser Testlauf hat keine DLL geschrieben; Herkunft der Dateiänderung nicht ermittelt. Die r2-Ausnahme erlaubt genau den Sollhash und hebt die Hashwechsel-Stopregel nicht auf.

## Startprüfung und Zeitbudget

Startup-PASS ausschließlich für die beobachtete Voraussetzung:

| Marker | MESZ / Clientzeile | Anzahl |
|---|---|---:|
| Redeploy.Core 0.9.0.0 initialized | 08:11:42.852 / L3991 | 1 |
| ArenaMode driver, priority 100 | 08:11:44.110 / L4545 | 1 |
| Redeploy driver, priority 0 | 08:11:48.668 / L7153 | 1 |
| Start-Fail-closed-/Installationsfehler | Startup-Audit | 0 |

**4 von maximal 5 Raids, 17:14 native Gesamt-Raidzeit von 40:00.** Die im Raid verstrichene Werkzeug-/Wartezeit wird nicht vom aktiven Budget abgezogen. Der Lauf endete wegen Stopbefunden, nicht wegen Budgetverbrauch. C dauerte 08:54 statt der ungefähr drei Minuten des Zielablaufs; der geplante natürliche Tod gelang nicht. Das verbleibende Budget erlaubt nach dem Gesamtstopp keine Fortsetzung.

| Raid | READY-Eingabe MESZ | Bestätigter Spielkontext MESZ | Natives Ergebnis / Raidzeit | Exit-Bestätigung MESZ |
|---|---|---|---|---|
| A | 08:16:24.342 | ordinary-confirm 08:17:29.421 | LEFT 05:14 | 08:22:42.150 |
| B | 08:30:18.146 | ordinary-confirm 08:31:06.663 | LEFT 02:17 | 08:33:22.651 |
| B-Reset (R) | 08:36:53.984 | ordinary-confirm 08:37:42.039 | LEFT 00:49 | 08:38:30.070 |
| C | 08:41:31.929 | Arena active 08:42:10.525, Matchstart .633 | LEFT 08:54 | 08:51:03.077 |

Die zunächst aus Aufnahmen geschätzten Startzeiten wurden mit den Logmarkern abgeglichen; insbesondere C begann um 08:42:10.525. Die 17:14 stammen aus den vier nativen Enddauern, nicht aus diesen groben Capture-Zeiten. READY bis bestätigter Spielkontext: A 65,079 s, B 48,517 s, R 48,055 s, C 38,596 s, zusammen ungefähr **03:20,247** für Laden und Kontextübergang. Vom ersten READY bis zur protokollierten C-Hauptmenübestätigung um 08:52:16.883 vergehen **35:52,541**; abzüglich 17:14 nativer Raidzeit verbleiben **18:38,541** außerhalb der Raids für Laden, Menüs, Nachbereitung und Werkzeugzeit. Vorbereitung vor dem ersten READY und anschließende Abschlussarbeit sind darin nicht enthalten.

## A — gewöhnlicher Redeploy-Respawn: teilweise PASS

Factory Tag, gewöhnlicher PMC, nativ ausgerüstete Pistole. F12: MaxRespawnsPerRaid **3**, SpawnProtectionSeconds **30**, Cooldown **5**. Tagilla verursacht den natürlichen Tod, das Respawn-Fenster mit Cursor erscheint, „Respawn near corpse“ wird nativ gewählt.

| Unterfall | Status | Tatsächlicher Nachweis / Grenze |
|---|---|---|
| Natürlicher Tod, Fenster, nackter Ersatzkörper | PASS | [a-respawn-near-1-0.png](test-evidence/respawn-stage0-r2/screenshots/a-respawn-near-1-0.png) und [a-respawn-1-arrival-0.png](test-evidence/respawn-stage0-r2/screenshots/a-respawn-1-arrival-0.png); keine native KIA-Endseite statt Respawnfenster |
| Korrigierte Markerfolge | PASS | Death intercepted 0/3 **08:17:41.054** → Schutz 30 s **08:18:05.312** → Respawn complete **08:18:05.691** → Schutzende **08:18:35.623**, jeweils einmal |
| Bewegung und DynamicMaps folgen Ersatzkörper | PASS, nach Schutzende | Kartenposition (3.52, −37.56) → (3.16, −37.79); [a-map-after-movement-0.png](test-evidence/respawn-stage0-r2/screenshots/a-map-after-movement-0.png) / [a-map-position-second-0.png](test-evidence/respawn-stage0-r2/screenshots/a-map-position-second-0.png) |
| Alle Prüfungen innerhalb der 30 Schutzsekunden | OFFEN / Timingvorgabe nicht erfüllt | Positive Bewegungs-/Kartenbelege liegen später; kein rückwirkender Pass des vorgeschriebenen Schutzfensters |
| Sprint und Sprung | OFFEN | Shift+W und Space gesendet, Originalbilder vorhanden; Einzelbilder beweisen keinen eindeutigen vollständigen Ablauf |
| Escape/Resume, später Inventar/Karte und Exit | PASS für Bedienbarkeit | [a-respawn-escape-open-0.png](test-evidence/respawn-stage0-r2/screenshots/a-respawn-escape-open-0.png), [a-menu-safe-resume-0.png](test-evidence/respawn-stage0-r2/screenshots/a-menu-safe-resume-0.png), [a-disconnect-0.png](test-evidence/respawn-stage0-r2/screenshots/a-disconnect-0.png), [a-confirm-leave-0.png](test-evidence/respawn-stage0-r2/screenshots/a-confirm-leave-0.png) |
| Fehlerfreier Menüwechsel | **FAIL** | Ein Unity-Error **08:18:46.434**, L13233: MenuScreen inaktiv; kein zugehöriger Exceptionstack |
| Extraktionstimer | PASS | Native O-Eingabe, Timer **18:14** in [a-respawn-timer-0.png](test-evidence/respawn-stage0-r2/screenshots/a-respawn-timer-0.png); verbleibende Zeit, keine Uhrzeit |
| Vollständige Exfil-Liste | OFFEN | Nicht sichtbar gesichert; Timer ersetzt Exfil-Liste nicht |
| Eigene Leiche looten / eigene Waffe zurückholen | OFFEN | Kein nativer Loot-/Rückholbeleg; „corpse retains raid loadout“ ist kein Ersatz |
| Zweiter Tod / Fenster 1/3 | OFFEN | Ein Tod, ein Respawn; zweiter Fall nicht ausgelöst |
| AutoIFF-/Freecam-/Recorder-Rebind | PASS für beobachteten Übergang | Freecam aktiviert, Recorder 08:18:05.675 und AutoIFF .687 neu gebunden, keine dabei ausgelöste Exception |
| Killcam durch Respawn abgebrochen | OFFEN | Replay endete schon **08:17:59.551** mit live tail complete, vor dem Respawn; kein durch Respawn ausgelöster Abbruch |
| Regulärer LEFT, Hauptmenü, Stash/Charakter | PASS | [a-exit-result-0.png](test-evidence/respawn-stage0-r2/screenshots/a-exit-result-0.png) 05:14, [a-main-menu-confirmed-0.png](test-evidence/respawn-stage0-r2/screenshots/a-main-menu-confirmed-0.png), [a-stash-after-0.png](test-evidence/respawn-stage0-r2/screenshots/a-stash-after-0.png); Server LEFT 06:22:43.874 UTC |

Der einzelne Error lautet vollständig: **Coroutine couldn't be started because the the game object 'MenuScreen' is inactive!** Kein zugehöriger Exceptionstack. Ein zunächst vorsichtiger Skriptstopp wurde nach Einordnung der r2-Regel und erfolgreichem Resume/Inventar/Karte/Exit aufgehoben: ein einzelner Error ist weder neue Exception noch belegte Fehlerflut oder anhaltender Softlock. Der Menü-Unterfall bleibt dennoch FAIL. Kein pauschaler Fehlerfreiheitspass für A.

Der in r2 ausdrücklich entfallene Black-screen-Marker kommt 0-mal vor, kein Fehler. Details: [A-Audit](test-evidence/respawn-stage0-r2/audit/raid-A-end-audit.json) und [A-Logauszüge](test-evidence/respawn-stage0-r2/logs/raid-A-end-excerpts.txt). A ist gewöhnlicher PMC; kein Arena-Profilgleichheitsvergleich vorgeschrieben. Native angebotene Heilung für 0 RUB verwendet, keine Profilreparatur.

## B — Cooldown-Race OFFEN; vorgesehener Fallback PASS

Cooldown **20 s**, sonst r2-Werte 3 Respawns / 30 s Schutz. Natürlicher Tod **08:31:45.986**; Wahl „Respawn near corpse“ **08:31:52.966**. Escape **08:32:02.400**, also innerhalb des Cooldowns, zeigt Killcam ohne erreichbaren nativen Exit. Danach keine Eingabe bis zum regulären Respawn, wie der Auftrag diesen Fall ausdrücklich vorsieht.

| Unterfall | Status | Tatsächlicher Nachweis / Grenze |
|---|---|---|
| Tatsächlicher Raidabbruch innerhalb Cooldown | **OFFEN** | [B-cooldown-escape-0.png](test-evidence/respawn-stage0-r2/screenshots/B-cooldown-escape-0.png) zeigt keinen erreichbaren Exit; kein nativer Abbruch im toten Zustand |
| Erwartete Race-Abbruchmarker | OFFEN, jeweils 0 | External raid end while respawn pending / Respawn cancelled during cooldown nicht ausgelöst; keine erfolgreiche Race-Prüfung daraus ableiten |
| Fallback abwarten / regulärer Respawn | PASS | Schutz aktiv **08:32:13.374**, Respawn complete **.733**, Schutzende **08:32:43.644**. Diese Marker sind beim angeordneten Fallback korrekt, kein B-FAIL wegen ihrer Anwesenheit |
| Danach regulär verlassen, Hauptmenü, Stash | PASS | Escape **08:32:50.342**, Confirm Leave **08:33:22.651**, LEFT 02:17; Server 06:33:24.102 UTC, [B-postraid-0.png](test-evidence/respawn-stage0-r2/screenshots/B-postraid-0.png) / [B-stash-0.png](test-evidence/respawn-stage0-r2/screenshots/B-stash-0.png) |
| Keine neuen Fehler im B-Abschnitt | PASS | 0 Errors/Exceptions, 0 verbotene Marker; Core eine Culling-Warnung, Unity eine Warnung |

B wurde nicht durch erzwungene interne Ereignisse ersetzt. Die vollständige Cooldown-Race-Abnahme bleibt OFFEN, auch wenn der Fallback funktioniert.

## Zusätzlicher B-Reset-Raid (R)

| Unterfall | Status | Nachweis |
|---|---|---|
| Weiterer gewöhnlicher PMC Factory Tag | PASS | Nativ READY → Spawn → Escape → Disconnect → Confirm Leave; eigener zusätzlicher Raid, kein bloßer Menüwechsel |
| Reset an Raid-Grenze | PASS für beobachteten Fall | ordinary-confirm **06:37:42.039 UTC** einmal; 0 Respawn refused, 0 B-Abbruchmarker, 0 Death-/Schutz-/Respawnmarker, 0 Arena-Arm/Active |
| Reguläres Ende / Hauptmenü / Stash | PASS | LEFT **00:49**, Server **06:38:31.508 UTC**; [R-postraid-0.png](test-evidence/respawn-stage0-r2/screenshots/R-postraid-0.png), [R-main-menu-0.png](test-evidence/respawn-stage0-r2/screenshots/R-main-menu-0.png), [R-stash-0.png](test-evidence/respawn-stage0-r2/screenshots/R-stash-0.png) |
| Fehlerfreiheit im Resetabschnitt | PASS | 0 Errors/Exceptions/Serverfehler; 16 Unity-Warnungen, Core/Redeploy/Arena jeweils 0 Warnungen/Errors |

Der Reset bestätigt keinen vorher nicht ausgelösten Cooldown-Abbruch. Quelle [R-Audit](test-evidence/respawn-stage0-r2/audit/raid-R-end-audit.json); [zeitliche Zuordnung](test-evidence/respawn-stage0-r2/audit/timing.json).

## C — Arena: Kit-Swap und Give-up PASS, natürlicher Tod OFFEN, Stopübergang FAIL

Factory Tag / Deathmatch FFA / Endless / Kills Off / Time Off, normale native Auswahl. Die Gesamtdauer **08:54** übersteigt den ungefähr dreiminütigen Zielrahmen; aktive Gesamtzeit dennoch unter 40 Minuten. Kein weiterer Versuch zur Erzwingung des ausstehenden natürlichen Todes nach dem Stopp.

| Unterfall | Status | Tatsächlicher Nachweis / Grenze |
|---|---|---|
| Startkitwechsel mit Waffe auf lokalem Körper | PASS | Arena active **08:42:10.525**, Matchstart .633, **Arena raid-start kit swap complete: Reverend 08:42:12.133**; native Waffenansicht |
| Botreaktion nach Startkitwechsel | PASS im beobachteten Umfang | Botkampf und Treffer am Spieler beobachtet; Gesundheit 440 → 322 → 279 → 226/224. Fünf Mosin-Schüsse sind durch Munitionsabnahme und native Klick-/Kamera-Drag-Eingaben belegt; die Drag-Eingaben lösten dabei ebenfalls Schüsse aus. Keine vollständig passiven Bots in dieser Stichprobe. Kein eindeutiger Kausalnachweis, welcher Reiz welchen Bot aktivierte |
| Natürlicher eigener Tod und daraus folgender Arena-Respawn | **OFFEN** | Trotz beobachteter Treffer nicht erreicht; der spätere Give-up ist kein natürlicher Tod |
| Give up mit Ersatzkörper und HUD-Tod | PASS | BackSpace **08:46:33.626**, Arena death #1 **.767**, Arena respawn timing **08:46:34.993**, total **1,21 s**; nativ VPO-209, 440 HP, Deaths 1 |
| Reguläre Exitfolge / native Endseite | PASS für Ablauf | Escape **08:50:38.712**, Disconnect **08:50:51.985**, Confirm Leave **08:51:03.077**; LEFT 08:54, danach Hauptmenü bestätigt **08:52:16.883** |
| Fehlerfreier Stopübergang | **FAIL / Gesamtstopp** | Neue BotFirstAid-Exception **08:51:03.441**, 364 ms nach protokollierter Leave-Eingabe; keine pauschale Verursachung durch diese Eingabe behauptet |
| 16 geschützte PMC-/Scav-Feldvergleiche | PASS | Inventory, Quests, Skills, Health, XP, Level, TaskConditionCounters und RepeatableQuests jeweils gleich; gesamter Scav gleich |
| Ganze PMC-/JSON-Datei identisch | **FAIL für Gleichheit** | Genau ein Delta: /characters/pmc/Hideout/sptUpdateLastRunTimestamp, **06:41:03 → 06:52:08 UTC**, +665 s. Ursache nicht bewiesen; kein erfundener Profilschutz-Gesamtpass und keine behauptete Datei-Bytegleichheit |
| Verbotene Arena-/Core-Marker in C | **FAIL für gefordertes Nullvorkommen** | Ein verbotener Arena…failed:-Treffer **08:51:09.714**, L14100: Arena burst spawn of pmcBEAR failed: The operation was canceled. Nach dem Stop, dennoch gemäß Vorgabe gezählt; weitere ausgewertete Ablehnungs-/Recoverymarker jeweils 0 |

Scav.RepeatableQuests fehlt in beiden Profilkopien; die Gleichheit dieses Feldvergleichs bedeutet beidseitige Abwesenheit.

Profilbelege: [vollständiger Vergleich](test-evidence/respawn-stage0-r2/audit/C-profile-audit.public.json) und [einzelne Abweichung](test-evidence/respawn-stage0-r2/audit/C-profile-diff-review.public.json). Vollständige JSON-Rekursion, Groß-/Kleinschreibung und Typen bleiben erhalten, Arrays positionsgetreu; fehlend unterscheidet sich von null. Serverseitig Restore **06:51:04.849 UTC**, Protected End mit persistiertem Snapshot **06:51:05.003 UTC**. Davor noch vorläufiges deferred raid-end **06:51:04.723 UTC**; dieses wurde durch den späteren Endpfad aufgelöst. Der vollständige Profilvergleich bleibt davon getrennt. Keine Profilkopie zurückgespielt oder manuell bearbeitet.

## Stopbefund: vollständiger relevanter Stack

Quelle Unity Log, **08:51:03.441 MESZ / 06:51:03.441 UTC**, Client **L14052–14078**, vollständig gesichert in [raid-C-stop-full-stack.txt](test-evidence/respawn-stage0-r2/logs/raid-C-stop-full-stack.txt). Kein Stack aus BotDropDiag-Infozeilen als Ausnahmebeleg verwendet.

```text
[Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object
Stack trace:
BotFirstAid+CG_ApplyToSelf.method_1 (Comfort.Common.Result`1[T] applyResult) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
(wrapper dynamic-method) EFT.Player+MedsController+MedsInHandsOperation.DMD<EFT.Player+MedsController+MedsInHandsOperation::Finish>(EFT.Player/MedsController/MedsInHandsOperation)
(wrapper dynamic-method) EFT.Player+MedsController+MedsInHandsOperation.DMD<EFT.Player+MedsController+MedsInHandsOperation::OnEffectRemoved>(EFT.Player/MedsController/MedsInHandsOperation,EFT.HealthSystem.IHealthEffect)
EFT.HealthSystem.ActiveHealthController+Effect.NextState () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.HealthSystem.ActiveHealthController+Effect.NextState () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.HealthSystem.ActiveHealthController+Effect.ManualUpdate (System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.HealthSystem.ActiveHealthController.ManualEffectUpdate (EFT.HealthSystem.ActiveHealthController+Effect effect, System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.HealthSystem.PlayerHealthController.ManualEffectUpdate (EFT.HealthSystem.ActiveHealthController+Effect effect, System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
EFT.HealthSystem.BaseHealthController`1[TEffectType].ManualUpdate (System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
UnityEngine.DebugLogHandler:LogException(Exception, Object)
LogHandler:UnityEngine.ILogHandler.LogException(Exception, Object)
UnityEngine.Debug:LogException(Exception)
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

Die r2-Ausnahme verlangt als ersten Frame **BotSurgicalKit+CG_ApplyToCurrentPart.method_1** nahe einem nativen Stop. Tatsächlich beginnt dieser Stack bei **BotFirstAid+CG_ApplyToSelf.method_1**. Deshalb greift die Ausnahme ausdrücklich nicht, auch wenn der Ablauf eine Bot-Heilung nahe dem Stop zeigt. Das konkrete Nullobjekt und eine Modursache sind nicht gesichert. Kein Testfall D und keine nachgeordneten r6/r4-Fälle nach diesem Stopp.

## Warnungen, Fehler und Marker — Endzählung

Der frische Clientlog beginnt bei 08:11:33.241 MESZ; der Server wird erst ab dem neuen Prozess um 06:09:58 UTC gezählt. Frühere Serverzeilen und der private Vorstart-Clientlog sind ausgeschlossen. Angaben W/E = Warnungen/Errors; BotDropDiag wird getrennt geführt.

| Fenster | Redeploy.Core W/E | maschine-Redeploy W/E | ArenaMode W/E | Unity W/E | Server W/E |
|---|---:|---:|---:|---:|---:|
| Startup | 0/0 | 0/0 | 0/0 | 0/0 | 1/0, Trader-Standardtimer |
| A | 1/0 | 0/0 | 0/0 | 10/1 | 0/0 |
| B | 1/0 | 0/0 | 0/0 | 1/0 | 0/0 |
| R | 0/0 | 0/0 | 0/0 | 16/0 | 0/0 |
| C bis Hauptmenü | 2/0 | 0/0 | 2/0 | 2/1 | 0/0 |
| Frischer Gesamtclient bis C-Hauptmenü | 4/0 | 0/0 | 2/0 | 29/2 | 1/0 seit Prozessstart |

Startup zusätzlich fünf Serialization-/Fika- und vier Harmony-Warnungen. Bis Ende R zusammen **38 Clientwarnungen / 1 Error**, 0 echte Runtime-Exceptions; Server seit neuem Prozess eine Warnung. Bis C-Hauptmenü zusammen **44 Clientwarnungen / 2 Errors**, davon ein MenuScreen-Error in A und **eine echte Runtime-Exception BotFirstAid** in C. Die ausdrücklich erlaubte exakte BotSurgicalKit-Ausnahme bleibt **0**. Die zwei Arena-Warnungen sind das vorläufige pending Raidende (später Restore/Persist bestätigt) und der Burst-Spawn-Abbruch. Beide Core-Warnungen in C betreffen alte Culling-Sampler. Die finale Cleanup-Kopie bestätigt dieselben Zahlen: Client bis 08:51:57.474 MESZ, Server bis 06:54:46.503 UTC. Keine zusätzliche Exception oder neue Warnung nach dem C-Menüstand.

Endzählung über den gesamten frischen Lauf: **Respawn refused before any teardown = 0; Core/Arena subscriber failed = 0; GameEnd faulted before its first await = 0; Freecam compatibility patches could not be enabled = 0.** Beide B-Abbruchmarker = 0. Die übrigen Arena-Ablehnungs-/Recoverymarker (wrong new body, replacement already in flight, live refused, old-/replacement-body recovery failed) = 0. Gewöhnliches `Player gave up — ending raid as Killed` = 0. Der verbotene `Arena … failed:`-Treffer ist dagegen **1**, der Burst-Abbruch beim C-Ende.

Erfolgsmarker: gewöhnliches `Death intercepted` **2**, gewöhnliches `Respawn complete` **2** (A/B), `Spawn protection active` / `ended` jeweils **3** (A/B/C), Arena-Startkitwechsel **1**, Arena-Give-up-Respawn **1**, Arena-Respawntiming **1**. Der entfallene Black-screen-Blockmarker = 0. Das absichtliche Arena-Give-up wird nicht mit dem verbotenen gewöhnlichen Killed-Marker verwechselt.

BotDropDiag separat: **43 Infoeinträge** im frischen Gesamtclient, davon **32 im C-Fenster**; keine Warnungen oder Errors hinzugerechnet. Rohzeilen sind privat mitgesichert. Die spätere DLL-Abweichung bleibt als eigener Bindungsbefund bestehen.

## Rückstellung, verbleibende Grenzen und Übergabe

Über die native Oberfläche wurden **MaxRespawnsPerRaid 2, Cooldown 5, Schutz 10** auf die persönlichen Ausgangswerte zurückgestellt. Redeploy.cfg anschließend bytegleich. Alle neun Dateien enthalten wieder die ursprünglichen Einstellungswerte; Kommentar-/Metadatenabweichungen bleiben ausdrücklich sichtbar: Arena.cfg nur acht Kommentar-Dezimaltrenner Punkt → Komma; client-settings.json nur UpdatedAt 08:04:11 → 08:16:53; sieben andere Dateien bytegleich. **64 Assets bytegleich**. Der separat festgestellte BotDropDiag-DLL-Hashwechsel bleibt bestehen. Die Rückstellung war um 08:54 MESZ abgeschlossen; die erneute Verifikation um 08:58 MESZ bestätigt die Werte. Alle drei ursprünglichen Prozesse reagieren weiterhin, Spiel und Server wurden während des Laufs nicht neu gestartet. Belege: [Rückstellung](test-evidence/respawn-stage0-r2/audit/cleanup-verification.json), [Prozesse](test-evidence/respawn-stage0-r2/audit/processes-final.json), [Dateivergleich](test-evidence/respawn-stage0-r2/audit/final-settings-assets.json).

| Restfall | Status | Grund |
|---|---|---|
| D, optionaler Rundenwechsel | **SKIP** | Gesamtstopp in C; kein D-Raid gestartet, trotz Restbudget |
| A Sprint, Sprung, Exfil-Liste, Leichenloot, zweiter Tod | OFFEN | Keine ausreichenden nativen Nachweise |
| B echter Cooldown-Abbruch | OFFEN | Native Exitoberfläche im toten Zustand nicht erreichbar; vorgeschriebener Fallback statt erzwungenem Ereignis |
| C natürlicher Tod | OFFEN | Kein natürlicher Tod, nur ein Give-up |
| Fail-closed/Patchausfall, Event-Reentrancy, absichtlich verweigerte Live-Ersetzung | SKIP | Laut Auftrag nicht nativ erzwingen; Markerabwesenheit ist keine Auslösung dieser Schutzfälle |
| Vollständige Stage-0-Abnahme / unveränderte Gesamtbindung | **FAIL** | Nicht erlaubte neue Exception und final abweichende BotDropDiag-DLL; begrenzte Teilerfolge bleiben erhalten |

Die Daten- und Einstellungsnachweise wurden unabhängig gegengelesen. Offene/native nicht ausgelöste Fälle bleiben offen; dieser Bericht erteilt keine Freigabe für weitere Tests auf Grundlage des gestoppten Laufs.

## Installierte Versionsidentitäten und Belege

Versionsressourcen bei der Abschlussprüfung; für die acht Pflichtdateien sind die Endbytes identisch zu den Vor-Raid-Messungen. BotDropDiag in dieser Tabelle ist ausdrücklich der abweichende Endstand.

| Datei | ProductVersion |
|---|---|
| ArenaMode.Client | `0.1.0+b5ff2585f1dea2642835582d7b06bf8e4ec72f65` |
| Redeploy.Core | `0.9.0+287c2d95a4645ae72dbdf44843958781c84b9b60` |
| Redeploy.Client | `0.9.0+c938f5c43cb311a8a9ba1f81b2eb10573b99b936` |
| ArenaMode.Server | `0.1.0+f257a783623daa939ebf6a0750d0521e3b306263` |
| Redeploy.Server | `0.9.0+58abf5876629bb51be808d78ee2cd7a5d34af77a` |
| SAIN | `4.5.1+2d1d9257dbb3af535e9189e6181311f6355aec27` |
| KillcamReplay | `0.9.5+d38aecfdc11298b0d9468befb456663ffebd8ba7` |
| DynamicMaps | `1.2.1+99200c60f084d1f64491dee93ceb09351bb8a2cd` |
| BotDropDiag | `0.1.0+04d579413cbfbadb7e4184341084ab956c577cb5` |

[Alle Bindungsmessungen und Endstatus](test-evidence/respawn-stage0-r2/audit/hash-final.json), [finale Prozessdaten](test-evidence/respawn-stage0-r2/audit/processes-final.json), [Rückstellung](test-evidence/respawn-stage0-r2/audit/cleanup-verification.json), [Log-Endzählung](test-evidence/respawn-stage0-r2/audit/final-cleanup-audit.json).

Originalaufnahmen sind unverändert; Aufnahmezeiten und ursprüngliche Hashes stehen im [Bildindex](test-evidence/respawn-stage0-r2/screenshots/index.json). Alle veröffentlichten Belege sowie dieser Bericht sind im [SHA256-Manifest](test-evidence/respawn-stage0-r2/SHA256SUMS.txt) gebunden. Vollständige Rohlogs und Profile sind ausgeschlossen.
