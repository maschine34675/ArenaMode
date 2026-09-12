# GPU Arena refinements r3 – Gesamtstopp im ersten Raid

**FAIL / Gesamtstopp. Keine Feature-Abnahme.** Im ersten Customs-TDM-Raid trat eine neue `NullReferenceException` in `BotWeaponSelector.OnWeaponTaken` auf. Zusätzlich wurde nach einer nativen Grafikmenü-/Resume-Folge ein unerwarteter Munitionsverlust der AK-12 sichtbar. Die Exception ist ein eigenständiger Gesamtstoppgrund. Die genaue Quelle des Munitionsverlusts ist nicht instrumentiert und wird keinem einzelnen Klick sicher zugeordnet. Nach Entdeckung erfolgten nur regulärer Ausstieg, Profilkontrolle und Wiederherstellung eigener Testwerte; kein weiterer Abnahmeraid.

Die vier Refinements sind höchstens kurz sichtbar geworden. Gezielte Marker-Trim-Korrelation, Fallsprung-/Double-Jump-Nachweise, Kontur-Fade/Kollision und DLSS-Vergleich am nachweislich selben Körper fehlen. Diese Fälle bleiben **OFFEN**, die mit aktivem InfiniteStamina/NoFallDamage nicht beobachtbaren Kostenfälle **SKIP**. Die zwischenzeitlich sichtbare Funktion einzelner Oberflächen ist keine Freigabe nach dem neuen Fehler.

Der gesonderte [Deathmatch-r5-Bericht](GPU-DEATHMATCH-MENU-TEST-RESULTS-r5.md), Abschlusscommit `0a49898ca7ba669b705f2a3d68e31412a782ae71`, bleibt unverändert. Seine A/B-Raids gehören nicht zur Raidanzahl dieses Auftrags und sind keine Abnahme der vier Refinements.

## Auftrag, Teststand und Grenzen

Anweisung: [GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r3.md](GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r3.md), Test-ID `arena-refinements-r3`. AGENTS und aktueller HANDOFF wurden gelesen. Installation `C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`. Kein Build, keine Produktions-/HANDOFF-Änderung, kein DLL-Tausch, keine internen Arm-/Matchaufrufe und keine Profilrückkopie.

- Source-/Build-Commit: `f64df5be499d06d29cc4e6b717dd52ea0931725a`.
- Branch `fix/arena-panel-close-input`; F3-Preflight-HEAD `cc75f002650277dcf5d2045dbcc9e366a9f76999`. Der reine r5-Berichtsabschluss `0a49898ca7ba669b705f2a3d68e31412a782ae71` entstand erst während dieses getrennten F3-Laufs.
- Client ProductVersion: `0.1.0+f64df5be499d06d29cc4e6b717dd52ea0931725a`.
- Verwendeter Spielprozess: PID 10268, Start 15:45:36.5518531 UTC; derselbe bereits mit dem gebundenen Client neu gestartete Prozess wie r5 B. Kein zusätzlicher Neustart speziell zwischen r5 und diesem Auftrag wird behauptet.
- Server PID 13660, Start 15:09:24.4621841 UTC; Launcher PID 4288, Start 15:09:58.8178116 UTC.
- F3-Baseline ist die nach r5 wiederhergestellte Einstellungslage. R5 A bestand seine zentrale Close-/Autoexit-Prüfung; im späteren B gab es keine neue Exception oder unsichere Profiländerung. Offener Ergebnisbutton und Budgetfälle waren keine Safety-Stop-Umgehung.
- Originalaufnahmen 1920 × 1080. Clientlogs MESZ (UTC+2), Serverlogs UTC. Dateinamen/Index liefern Archivmarker, keine separaten exakten Pixelaufnahmezeitstempel.

| Tatsächlich installierte Datei, relativ zur Installation | SHA256 |
|---|---|
| BepInEx/plugins/maschine-ArenaMode.Client.dll | `7DCAA94CFDF5F0C7E6736796901BE4C3DCBADCEC6F74C76E1668C22B81201F11` |
| SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll | `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7` |
| BepInEx/plugins/maschine-Redeploy.Core.dll | `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8` |
| BepInEx/plugins/SAIN/SAIN.dll | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` |
| BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` |

Die vier zentral gebundenen DLLs stimmen beim F3-Preflight, vor TDM um 16:06:41.616 UTC, nach dem Raid um 16:13:19.832 UTC und im [Abschlusshashvergleich](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/hash-final-all-stopped.json) um 16:19:23.783 UTC. DynamicMaps AssemblyVersion ist `1.2.1.0`, ProductVersion `1.2.1+99200c60f084d1f64491dee93ceb09351bb8a2cd`; [abschließend verifizierte Kompatibilitätsbindung](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/compatibility-final.json).

Der Startupmarker `Arena DynamicMaps: trimmed-corpse marker cleanup ready (1.2.1).` steht im fortlaufenden Clientlog L4089 um 17:45:56.014 MESZ, also 15:45:56.014 UTC. Er stammt vom früheren Start desselben Prozesses und bestätigt nur Registrierung. Die vier älteren `Arena corpse cleanup: trimmed ...`-Zeilen um 15:51 UTC gehören zu r5 B und werden nicht diesem F3-Raid zugerechnet.

## Ablauf und Budget

**Ein Raid, Customs am Morgen, unabhängiges Team Deathmatch 5v5**, `customs-factory-center-teams-v1`, 50 Kills / 600 Sekunden. Die Teamkontur wurde nativ aus der Auswahl gewählt; keine PFT-Gruppe. Die Konfiguration lässt Movement/DoubleJump aktiviert. `InfiniteStamina=true` und `NoFallDamage=true` blieben unverändert. DynamicMaps-Minimap war in der Baseline unter `[5. Mini-map]` mit `Mini-map enabled=false` ausgeschaltet. DLSS Balanced war nativ verfügbar und anfangs eingestellt; Off wurde im Raid ausgewählt/gespeichert, Quality nicht geprüft.

Das eigene F3-Budget begann 16:00:32.639 UTC mit konservativer Deadline **16:20:32.639 UTC**, höchstens drei Raids und 20 Minuten aktive Testzeit. Menü-/Werkzeug-/Ladefenster werden benannt, nicht als exakt gemessene aktive Zeit ausgegeben. Der Gesamtstopp erfolgte vorher wegen Sicherheit, nicht wegen Budgetende.

| Ereignis | UTC / Beleg | Aussagegrenze |
|---|---|---|
| Budgetstart / TDM-Auswahl | 16:00:32.639 / 16:00:39.307 | Enthält folgende Menü-/Werkzeugpause |
| Customs / Teamkontur / Morgen gewählt | 16:05:57.743 / 16:06:08.631 / 16:06:25.217 | Native Menüpunkte; keine Kontur-Geometriemessung |
| Ready-Klick → Rückgabe | 16:06:54.311 → 16:06:54.732 | Laden danach getrennt |
| Session aktiv / Team-Deathmatch gestartet | 16:07:58.063 / 16:07:58.108, Client L13527/L13544 | Beginn Session/Modus, noch keine externe Featureprüfung |
| Area READY, eingefrorenes 5v5 / Kit fertig | 16:07:59.282 / 16:08:00.924, L13556/L13572 | Interne Bereitschaftsmeldung, kein externer Kollisionsbeweis |
| Assembling→Countdown / Countdown→Live | 16:08:45.930 / 16:08:48.953, L13693/L13694 | 50 Kills / 600s; L13698 meldet 5v5 ready |
| Letzte fehlerfreie Logabfrage vor NRE | 16:09:08.557, Client bis L13719 | Kein kontinuierlicher Exception-Monitor |
| Neue Exception entsteht | **16:09:25.212**, Client L13754 | Eigenständiger Stopgrund, noch nicht sofort entdeckt |
| Balanced-/Teammateaufnahme | 16:09:29.272 Archivmarker | Bereits nach Fehlerzeit, vor dessen Entdeckung |
| Erste Aufnahme nach DLSS Off/Resume | 16:10:33.643 Archivmarker | AK-12 nun 29/30 +1 |
| Gesamtstopp erkannt / Sicherheitsnotiz | **16:10:56.968**, NRE-entdeckende Logprüfung **16:10:57.259** | Danach nur sichere Rückkehr/Bereinigung |
| Escape / Disconnect / Leave-Bestätigung | 16:10:57.267 / 16:11:08.000 / 16:11:27.094 | Native Abbruchfolge; kein weiterer Featurefall |
| Session ended / match ended | 16:11:28.575 / 16:11:28.577, L13925/L13928 | Lokale Summe 0 Kills / 0 Tode / 0 Punkte |
| Hauptmenü bestätigt | 16:13:11.028 Archivmarker | Original f3-mainmenu-confirmed; Profilvergleich danach |

Die weitere Grafikbedienung zwischen 16:09:25 und 16:10:57 erfolgte vor Kenntnis des Logfehlers, aber bereits nach seinem Auftreten. Die betreffenden Bilder werden entsprechend als Beobachtungen im bereits fehlerhaften Raid markiert, nicht als nachträglich fehlerfreie Abnahme. Die 20-Minuten-Grenze und das Stop-Erkennungsfenster sind keine Behauptung einer lückenlos gemessenen aktiven Spielzeit.

## Sicherheitsbefunde

### F3-01 – neue BotWeaponSelector-NRE: FAIL / Gesamtstopp

Die unabhängige Logprüfung findet im vollständigen abgegrenzten F3-Fenster bis Programmende **eine neue Unity-Error/NRE** um 18:09:25.212 MESZ = 16:09:25.212 UTC, Client L13754–13780. Der Einstieg ist `BotWeaponSelector.OnWeaponTaken`, aufgerufen über Waffen-/Hands-/Animator-Callbacks. DMD-Wrapper bezeichnen den betroffenen detourten Methodenpfad; sie identifizieren weder die verantwortliche Mod/Patch noch das konkrete Nullobjekt. Die Exception liegt vor der späteren DLSS-Menüfolge und vor dem Resume-Klick. Ein Zusammenhang mit DLSS oder dem Munitionssignal ist durch den Stack nicht bewiesen.

Vollständiger neuer Stack aus der abgegrenzten Quelle; vorangestellte Zahlen sind Original-Zeilennummern, Zeitstempel und Modulkennungen bleiben erhalten:

```text
13754: [18:09:25.212] [Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object
13755: [18:09:25.212] Stack trace:
13756: [18:09:25.212] (wrapper dynamic-method) BotWeaponSelector.DMD<BotWeaponSelector::OnWeaponTaken>(BotWeaponSelector,Comfort.Common.Result`1<EFT.IHandsController>)
13757: [18:09:25.212] BotWeaponSelector+CG_TryChangeToSlot.method_0 (Comfort.Common.Result`1[T] x) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13758: [18:09:25.212] EFT.Player.Call[T] (Comfort.Common.Result`1[T] result, Comfort.Common.Callback`1[T] callback) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13759: [18:09:25.212] EFT.Player+CG_TryProceed.method_0 (Comfort.Common.Result`1[T] result) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13760: [18:09:25.212] EFT.Player+Process`2[TController,TResult].Complete () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13761: [18:09:25.212] EFT.Player+Process`2+CG_CreateController[T,U].method_0 () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13762: [18:09:25.212] EFT.Player+CG_SpawnController.method_0 () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13763: [18:09:25.212] EFT.Player+FirearmController+CG_Spawn.method_0 () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13764: [18:09:25.212] (wrapper dynamic-method) EFT.Player+FirearmController+SpawnOperation.DMD<EFT.Player+FirearmController+SpawnOperation::WeaponAppeared>(EFT.Player/FirearmController/SpawnOperation)
13765: [18:09:25.212] EFT.Player+FirearmController.method_32 () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13766: [18:09:25.212] (wrapper dynamic-method) EFT.Player+FirearmController.DMD<EFT.Player+FirearmController::IEventsConsumerOnWeapIn>(EFT.Player/FirearmController)
13767: [18:09:25.212] EFT.Player+AbstractHandsController.IEventsConsumer.OnWeapIn () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13768: [18:09:25.212] AnimationControllerEventsTable.FireWeapIn (System.Collections.Generic.List`1[T] eventsConsumers, AnimationEventSystem.AnimationEventParameter parameter) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13769: [18:09:25.212] AnimationControllerEventsTable.AnimatorEventHandler (System.Collections.Generic.List`1[T] eventsConsumers, System.Int32 functionNameHash, AnimationEventSystem.AnimationEventParameter parameter) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13770: [18:09:25.212] ObjectInHandsAnimator.AnimatorEventHandler (System.Int32 functionNameHash, AnimationEventSystem.AnimationEventParameter parameter) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13771: [18:09:25.212] AnimationEventSystem.AnimationEventsEmitter.SendEvent (System.String functionName, System.Int32 functionNameHash, AnimationEventSystem.AnimationEventParameter parameter, System.Int32 stateShortNameHash) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13772: [18:09:25.212] AnimationEventSystem.AnimationEventsEmitter.FireEventIfConditionsPassed (AnimationEventSystem.AnimationEvent event, IAnimator animator, AnimatorStateInfoWrapper& stateInfo, System.Single normalizedTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13773: [18:09:25.212] AnimationEventSystem.AnimationEventsEmitter.EmitEvents () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13774: [18:09:25.212] EFT.Player+ItemHandsController.EmitEvents () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13775: [18:09:25.212] EFT.Player.ArmsLateUpdate (System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13776: [18:09:25.212] EFT.Player.ComplexLateUpdate (EFT.EUpdateQueue queue, System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13777: [18:09:25.212] EFT.Player.LateUpdate () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13778: [18:09:25.212] UnityEngine.DebugLogHandler:LogException(Exception, Object)
13779: [18:09:25.212] LogHandler:UnityEngine.ILogHandler.LogException(Exception, Object)
13780: [18:09:25.212] UnityEngine.Debug:CallOverridenDebugHandler(Exception, Object)
```

Der [unabhängige endgültige Logaudit](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/independent-final-log-audit.json) umfasst Client **L13372–14228** und Server **L8849–9207**, serverseitig ab F3-Fensterstart **16:00:17.789 UTC**. Ergebnis: **Client genau 1 Error / 1 Exceptionblock und 39 Warnings; Server 0 Errors / 0 Warnings.** Die Client-Warnings gliedern sich in 4 WheelCollider-/Rigidbody-Hinweise, 33 BoxCollider-/Negative-Scale-Hinweise, einen stale-PerfectCulling-Hinweis und das anschließend aufgelöste pending raid-end. Diese Hinweise relativieren den eigenständigen Stopgrund nicht.

Nach dem ersten Stop-/Exit-Audit stehen in den verbleibenden Clientzeilen **L13936–14228** und Serverzeilen **L9156–9207 keine zusätzlichen Errors oder Warnings**. Die letzte Clientzeile ist um **16:17:25.014 UTC** eine reguläre Wiederherstellung der AssemblyNames-Liste, die letzte Serverzeile um **16:18:33.276 UTC** eine SaveServer-Meldung. Die Serverlog-Endzeit allein belegt nicht den späteren Ctrl+C-Zeitpunkt; dieser ist separat als Aufräumaktion protokolliert. Die abschließend archivierten Rohlogs sind laut unabhängigem Audit bytegleich mit den geschlossenen Quelldateien. Alle Berichtzeilen verwenden LF-Zählung wie `rg`; sieben ältere einzelne CR im Serverlog vor F3 werden dabei nicht als zusätzliche Zeilen gezählt.

Im F3-Fenster gibt es **0 Corpse-Trim-Meldungen, 0 tatsächliche SAIN-Teardown-Guard-Aufrufe und 0 Pending-Pickup-Abbrüche**. Die Initialisierungsmarker des früheren Starts und die vier Trim-Aufrufe / der eine SAIN-Teardown aus r5 B bleiben ausdrücklich außerhalb dieser F3-Zählung. Fehlende tatsächliche Aufrufe beweisen keine Funktionsabnahme der Guards oder der DynamicMaps-Bereinigung. [Gezielte Originalzeilen einschließlich vollständigem Stack, Initialisierung und Lifecycle](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/targeted-final-log-excerpts.txt).

### F3-02 – unerwarteter Munitionsverlust nach nativer Menüfolge: zusätzlicher Sicherheitsbefund

Das unabhängig gelesene Original [Balanced-/Teammatebild](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-dlss-balanced-teammate.jpg), Archivmarker 16:09:29.272 UTC, zeigt **AK-12 30/30 +1**. Das erste Original [nach DLSS Off und Resume](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-dlss-off-resume.jpg), Archivmarker 16:10:33.643 UTC, zeigt **dieselbe Waffenbezeichnung mit 29/30 +1**. Beide zeigen lokale 0 Kills / 0 Tode und volle Gesundheit. In F3 ist kein absichtlicher Schuss-, Reload- oder Waffenwechselinput aufgezeichnet.

Zwischen den zwei Bildern liegen jedoch **64,371 Sekunden Archivabstand, Escape und sechs native Mausklicks**. Das ist kein isoliertes Close-Vorher/Nachher-Paar wie in r5. Der letzte protokollierte Input war Resume; ohne Schuss-/Befehlstelemetrie ist nicht bewiesen, bei welchem einzelnen Klick oder welcher Phase die Patrone verloren ging. Kein F9-Close-Klick wird als Ursache behauptet, keine sichere DLSS-Ursache und keine Rückdatierung der r5-Befunde.

| Native Aktion zwischen den Munitionsbildern | Eingabebeginn → Rückgabe UTC |
|---|---|
| Escape ins Pausemenü | 16:09:37.448 → 16:09:39.088 |
| Einstellungen öffnen | 16:09:45.849 → 16:09:47.716 |
| Grafik-Tab öffnen | 16:09:55.161 → 16:09:55.765 |
| DLSS-Auswahl öffnen | 16:10:04.466 → 16:10:05.085 |
| Off wählen | 16:10:12.192 → 16:10:14.258 |
| Änderungen speichern | 16:10:22.601 → 16:10:25.083 |
| Resume | 16:10:32.251 → 16:10:33.643 |

**Status:** unerwarteter Munitionsverlust als Sicherheitsbefund; genaue Klick-/Schussquelle **UNKLAR/OFFEN**. Keine weitere Wiederholung zur Provokation. Die neue NRE ist unabhängig vom Munitionsbefund bereits ein ausreichender Gesamtstoppgrund.

## Funktionsfälle – keine pauschalen PASS-Wertungen

| Fall | Status | Tatsächliche Beobachtung / fehlender Nachweis |
|---|---|---|
| DynamicMaps 1.2.1 verfügbar / Full-Map-Oberfläche | VORAUSSETZUNG BEOBACHTET | Passende installierte Version, vorhandener Ready-Marker; vollständige Karte über Tab-/Map-Register geöffnet. Das ist keine Trim-Funktionsabnahme. |
| Getrimmte Leichen verschwinden gezielt von Full Map | OFFEN | Kein zeitlich/identisch zugeordneter Trim-/Skull-Vergleich im F3-Raid. Die sichtbare Leichenbudgetgrenze wurde nicht nachweislich für diesen Unterfall überschritten. |
| Minimap-Skulls nach Trim | OFFEN | End-Tastendruck zeigte keine belegte Minimap. Baseline `Mini-map enabled=false`; der Name f3-minimap-enabled ist ein Aktionslabel, kein Erfolgsbeweis. |
| Wiederöffnen / Corpse-Kategorie aus-/einblenden; alte Marker kehren nicht zurück | OFFEN | Kein gezielter Wiederöffnen-/Togglevergleich derselben getrimmten Körper. |
| Jüngste/ungetrimmte Körper behalten Marker / Schutzfälle | OFFEN | Keine Identitäts-/Trimkorrelation; kein manueller Leichenabbau. |
| Air Jump nach kleiner Kante und klarer Fallkante | OFFEN | Keine belegte Fallkante und kein nachweislicher Extraimpuls aus einem Fall ohne vorausgehenden Sprung. |
| Ground→Extra-Jump bei Aufstieg/Scheitel/Abstieg | OFFEN | Nur ein Space-Versuch nach dem Inventarschließen; zwei Bilder mit Kamerahebung liefern keine vollständige Flugbahn oder sicheren Double-Jump-Nachweis. |
| Ein Impuls; wiederholter Input; erneute Freigabe nach Landung; Boden-/Luft-Toggle | OFFEN | Nicht gezielt geprüft; kein ausreichender Bewegungsclip oder wiederholbarer Zustandsnachweis. |
| Niedrige Decke/Wand, stehendes/gehendes/sprintendes Fallen, Respawn-/Inventar-Stale-Request | OFFEN | Kein geeigneter begrenzter Parcours und keine belegte Fall-/Respawnfolge. Ein späterer Bodenversuch beweist keinen vorangegangenen Tiny-Fall-Reset. |
| Stamina-Debit / verweigerte Mehrfachversuche | SKIP | `InfiniteStamina=true` unverändert; unveränderte HUD-Stamina kann die Debits nicht beweisen. |
| Unveränderte Fallfolgen | SKIP | `NoFallDamage=true` unverändert; kein deaktivierter, sicher begrenzter Vergleich. |
| Teamkontur/5v5-Start | VORAUSSETZUNG BEOBACHTET | Area READY, 5v5 Countdown/Live, sichtbare Scores 2:0 und 3:1. Kein Nachweis der Fade-/Kollisionsunterfälle. |
| Konturfade an langer/diagonaler Seite und Ecke, Annähern/Entfernen bei 20m/6m | OFFEN | Keine gezielte Grenzannäherung, Entfernungsbelege oder Opazitätsvergleich. |
| Endliche Wandsegmente, nur nahe Seiten; horizontal gleicher Ort bei Etagenwechsel | OFFEN | Kein passender Seiten-/Ecken-/Etagenvergleich. |
| Unsichtbare Kontur: Spieler-/Bot-Kollision und Spawn-/Matchablauf unverändert | OFFEN | Internes READY-Logging allein ist kein nativer Außen-/Kollisionstest; Match aus Sicherheitsgründen vorzeitig beendet. |
| Freundlicher Diamond mit DLSS Balanced | BEGRENZTE SICHTBEOBACHTUNG | Kleiner blauer Diamond liegt im Bild über einem aktuell sichtbaren kleinen Botkopf leicht rechts der Mitte; zwei Bilder zeigen Bewegung/Haltung. Exakte Körperidentität ist nicht abgesichert. |
| Derselbe Teammate mit Off/Quality/weiterer Qualität; Zentrum/Rand/Haltungen | OFFEN | Nach Off/Resume kein vergleichbarer Teammate sichtbar; Quality nicht getestet. Keine gleichkörperige dreistufige Alignment-Abnahme. |
| Diamond nach lokalem Respawn/Kamerawechsel, optional Auflösung, Tactical-Spectator | OFFEN | Nicht ausgeführt. |
| Verdeckte/feindliche/tote Körper erhalten keine Marker | OFFEN | Kein gezielter Negativfall mit abgesicherter Identität. |
| Regulärer geschützter Ausstieg / geschützte Felder | PASS, begrenzt | Native Disconnect-/Leave-/Endscreen-/Hauptmenüfolge und Serverrestore belegt; alle 16 geschützten PMC-/Scav-Felder gleich. Das Gesamtprofil unterscheidet sich um einen Hideout-Zeitstempel, siehe unten; kein fehlerfreier Gesamt-Raid. |
| Fehlerfreier Gesamt-Raid | FAIL | Neue BotWeaponSelector-NRE; Munitionssignal zusätzlich. |
| Normaler PMC-Raid ohne Arena-Air-Jump/Grid/Diamonds und mit normalen DynamicMaps-Markern | OFFEN | Wegen Gesamtstopp nicht gestartet. |
| Weitere Raids, Tactical-/Spectator-/Restfälle | OFFEN | Kein weiteres Abnahmeszenario nach erkanntem Gesamtstopp. |

[Full Map](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-dynamicmaps-initial.jpg), [End-Versuch ohne belegte Minimap](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-minimap-enabled.jpg), [Bodensprungversuch](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-ground-jump-attempt.jpg), [Teamkonturwahl](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-customs-team-contour.jpg). Die zwei nahezu gleichzeitigen Originalflächen sind keine Dauer-/Trajektorienmessung.

## Native Rückkehr, Profile und Wiederherstellung

Nach dem Gesamtstopp wurde regulär über Escape, Disconnect und die Leave-Bestätigung zurückgekehrt. Der unabhängig gelesene [native Endscreen](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-safety-return-loading.jpg) zeigt Left the Action, 00:03:31 und 0 EXP; das lokale Log endet mit 0 Kills / 0 Toden. Session-Ende ist 16:11:28.575 UTC. Server-L9074 meldet Snapshot restored um 16:11:28.869, L9077 Protected Arena end completed mit persistiertem Snapshot um 16:11:29.068. Das vorherige deferred raid-end ist damit aufgelöst. [Native Leave-Bestätigung](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-safety-confirm-leave.jpg), [Hauptmenü](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-mainmenu-confirmed.jpg).

Die private Vorherkopie entstand vor Ready um **16:06:41.618 UTC**, die Nachherkopie laut Laufprotokoll um **16:13:19.748 UTC**, nach dem bestätigten vollständigen Hauptmenü um **16:13:11.028 UTC**. Der Abschlussvergleich wurde unmittelbar danach ausgeführt. [Laufprotokoll](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/run-summary.json) und [unabhängiger vollständiger JSON-Vergleich](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/independent-profile-comparison-refinements-r3-TDM.json) halten die Abgrenzung fest.

Inventory, Experience, Level, Quests, TaskConditionCounters, RepeatableQuests, Skills und Health sind getrennt für PMC/Scav gleich: **alle 16 geschützten Feldvergleiche unverändert**, Scav RepeatableQuests fehlt beidseits. Das gesamte Scav-Objekt ist identisch. Das Gesamtprofil ist ausdrücklich **nicht vollständig identisch**: Der rekursive Vergleich findet genau einen Unterschied, `characters.pmc.Hideout.sptUpdateLastRunTimestamp`, **1789229188 → 1789229548**, entsprechend **16:06:28 → 16:12:28 UTC (+360 Sekunden)**. Der Vergleich identifiziert weder den schreibenden Prozess noch die Ursache dieses Zeitstempelupdates. Kein Rohprofil wurde zurückkopiert. Der einzelne Zeitstempelunterschied wird vollständig ausgewiesen; eine vollständige Profilgleichheit wird nicht behauptet.

Die nativen Rückstellungen waren um **16:16:07.923 UTC** belegt: alle **147 Arena-Einstellungswerte** entsprechen der F3-Baseline. DLSS steht wieder auf Balanced, DynamicMaps einschließlich `Mini-map enabled=false` ist unverändert. Sechs von sieben gesicherten Einstellungsdateien sind bytegleich: ArenaMode.cfg, RaidInfoPanels.cfg, Control.ini, Graphics.ini, Server-config.json und DynamicMaps.cfg. Beim siebten Dokument `client-settings.json` ist genau `UpdatedAt` von **`17:55:56` auf `18:15:53`** geändert; das Feld enthält keine Zeitzone. Sämtliche anderen Werte, Einträge, Beschreibungen und Metadaten sind semantisch gleich. [Native Wiederherstellung](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/settings-restored.json), [unabhängiger Vergleich sämtlicher sieben Dateien](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/independent-settings-comparison-refinements-r3.json), [Balanced wieder eingestellt](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-restore-dlss-balanced.jpg).

Alle **33 gesicherten Arena-Assets** sind im [Abschlussvergleich](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/assets-final.json) um 16:19:23.801 UTC hashgleich. InfiniteStamina und NoFallDamage blieben durchgehend unverändert true. Die Wiederherstellung erfolgte über native Oberflächen, ohne alte cfg- oder Profilkopien einzuspielen. Diese Bereinigung hebt den neuen Sicherheitsstopp nicht auf.

Nach Rückstellung wurde das Spiel nativ über Exit um **16:17:14.090 UTC** und die Y-Bestätigung um **16:17:22.748 UTC** beendet; Spielende war um ungefähr 16:17:40 UTC kontrolliert. Der Launcher wurde regulär gegen 16:18:32 UTC geschlossen; der eigene Server erhielt gegen 16:18:44 UTC Ctrl+C. Die abschließende [Prozessprüfung](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/process-cleanup.json) um **16:20:29.505 UTC** findet Spiel, Launcher und Server sämtlich beendet. [Exit-Anforderung](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-game-exit-request.jpg), [Bestätigung](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/f3-game-exit-confirm.jpg). Kein weiterer Raid oder Featurefall wurde nach erkanntem Gesamtstopp gestartet.

## Evidenz

Öffentliche Belege liegen unter `test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/`: **52 unveränderte JPG-Dateien** aus 49 archivierten Aufnahmen, ergänzt um die jeweils zweite Originalfläche des Sprungversuchs, der Balanced-Beobachtung und der Off-/Resume-Beobachtung. Der [Bildindex](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/screenshots.json) enthält Dateinamen, Archivmarker, Abmessungen und SHA256-Werte. Das [SHA256-Manifest](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/SHA256SUMS.txt) erfasst die veröffentlichten Dateien außer sich selbst. Die zwei nahezu gleichzeitigen Flächen sind keine lückenlose Videoaufzeichnung.

[Native Eingaben](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/input-actions.json), [Budget](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/budget.json), [Sicherheitsnotiz](test-evidence/arena-refinements-r3/2026-09-12T16-00-17-789Z/safety-stop.json) und die oben verlinkten abgegrenzten Audits ergänzen die Bilder. Rohlogs, vollständige Profile und private Sicherungen verbleiben im ignorierten F3-Artefaktpfad. Dieser Bericht enthält keine Profil-IDs oder vollständigen Profilinhalte. R5 und seine abgeschlossene Beweiskette bleiben ein separater Auftrag.
