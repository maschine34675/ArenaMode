# Respawn-Engine Stufe 0 r3 — GPU-Ergebnisse vom 13.09.2026

**Keine vollständige Abnahme.** Nach drei regulär beendeten Raids wurde der Lauf wegen zweier SAIN-Errorzeilen in C vorsorglich gestoppt. E bestätigt den geforderten Lognachweis gegen Itemwürfe sowie Botkampf und einen natürlichen Arena-Respawn. Die Spawnzonenbilder und vollständige Leichenloot-Beobachtung fehlen. A erreichte keinen natürlichen Tod; alle fünf Unterfälle nach einem gewöhnlichen Respawn bleiben offen. C bestätigt den einmaligen Give-up-Fallback, erreicht aber keinen natürlichen Tod. D wurde wegen des Stopps nicht ausgeführt; es gab keinen A-Wiederholungsraid.

| Unterfall | Ergebnis | Beleg und Grenze |
|---|---|---|
| E1: keine THROW/DROPPER mit OnStartPeacefulMove in den ersten drei Matchminuten | **PASS** | Jeweils 0, auch die Gesamtzahlen beider Kategorien 0 |
| E2: Bilder beider Spawnzonen bei etwa 60 Sekunden | **OFFEN** | Die geforderten Originalbilder fehlen wegen einer Beobachtungslücke |
| E2: Bilder beider Spawnzonen bei etwa 180 Sekunden | **OFFEN** | Gleiche Beleglücke; spätere Bilder ersetzen diese Zeitpunkte nicht |
| E3: Bots kämpfen und treffen den Spieler | **PASS** | Natürlicher Tod, HUD-Todeszähler 1, Teamstand 4:3 und neuer Körper mit MCX |
| E4: kein plündernder Bot über einer Leiche | **OFFEN** | Kein solcher Vorfall im beobachteten Ausschnitt; keine vollständige Negativprüfung |
| E5: Heilungsdiagnosen zählen | **PASS** | 0 SURGERY-START, 8 MEDS-FINISH in E |
| A1: Sprint und Sprung im Schutzfenster | **OFFEN** | Kein natürlicher Tod, kein gewöhnlicher Respawn und kein Schutzfenster |
| A2: Escape öffnen/schließen nach Respawn | **OFFEN** | Vor einem Tod bedienbar; Ersatzkörper nicht erreicht |
| A3: Timer und Exfil-Liste nach Respawn | **OFFEN** | Geforderter Zustand nicht erreicht |
| A4: eigene Leiche looten und MP5 zurückholen | **OFFEN** | Keine eigene Leiche |
| A5: zweiter Tod und Fenster „Respawns used: 1/3“ | **OFFEN** | Bereits erster natürlicher Tod nicht erreicht |
| A: reguläres Ende, Charakter und Stash | **PASS** | LEFT, kostenlose native Behandlung auf 440 HP; Charakter/Stash und Rückkehr zum Hauptmenü bedienbar |
| C: natürlicher Tod ohne Schüsse | **OFFEN** | Kein natürlicher Tod; Give up erst nach ungefähr 4:17 statt spätestens vier Minuten |
| C: einmaliger Give-up-Fallback | **PASS** | Ein BackSpace, Arena death #1, neues MCX-Kit, 440 HP, HUD-Tod 1, Respawn 1,19 s |
| C: reguläres Ende / Profilregel | **PASS** | Native Ergebnisseite 05:05; 16/16 geschützte Felder und ganzer Scav gleich, nur erlaubtes Hideout-Timestampdelta |
| D: Rundenpause / Rundenrespawn | **OFFEN** | Wegen des vorsorglichen Stopps nach C nicht ausgeführt |
| B und absichtlich erzwungene Fehlerfälle | **SKIP** | In r3 ausdrücklich nicht angefordert |

## Gebundener Teststand und Methode

Auftrag: GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r3.md. Checkout **fix/offline-harness-contracts**, HEAD **1f3f9f0fbc26c496b07ea6f17d476791d8ca0a63**. Installierter Arena-Code **a2fcced88e7f9ca9cf7fdd1eacb7c81525008755**, ProductVersion **0.1.0+a2fcced88e7f9ca9cf7fdd1eacb7c81525008755**; Server neu gebaut bei seit f257a78 unveränderter Quelle. Redeploy main **287c2d95a4645ae72dbdf44843958781c84b9b60**, Client-Code c938f5c, Core 0.9.0. Spätere Repository-/HUD-Arbeiten ersetzen diese installierte Bindung nicht.

Frischer Server **PID 3000**, Start **08:16:21.539 UTC**; frisches Spiel **PID 10008**, Start **08:19:27.362 UTC**; Launcher **PID 9912**, Start **08:16:21.550 UTC**. Darstellung vor Start und im nativen Abschlussbild: **1920 × 1080, Borderless, DLSS Balanced**. NoBotGestures und NoCorpseLooting waren beide true. Keine Produktionsänderung, kein Neubau oder DLL-Tausch und keine Profilbearbeitung durch den Testlauf. Native Eingaben und Originalbilder dienten als UI-Belege; Logmarker wurden separat geprüft.

| Installierte Komponente | Soll-SHA256 |
|---|---|
| ArenaMode.Client | E4CD2ACB1B2230CFE5C78E71815E4EDAB5034056FC2198B099BDF48527EA1BF5 |
| Redeploy.Core | FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C |
| Redeploy.Client | 7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46 |
| ArenaMode.Server | 71C0AEC170CDBE3DFF19D718B73F1790639728DA618F388CABF4000D960ED19F |
| Redeploy.Server | 53F283D8B8F32EC7A7FB94AA1CC7EBC12115169C74A97E77B4088B0E5F06070B |
| SAIN 4.5.1.0 | C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489 |
| KillcamReplay 0.9.5 | B5F984E6605F4DB206E69C3180DFEE0D743278796C1038204E67EA161BE66CD5 |
| DynamicMaps 1.2.1.0 | C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8 |
| BotDropDiag 0.1.0 | 0D885B3FADCDF448DA71B1D459079253EC15A6DBDB5934AB4959863C2DD686D0 |

Vorprüfung **08:15:06.109 UTC**, vor Spielstart **08:19:25.522**, vor E **08:23:12.561**, vor A **08:34:53.423**, vor C und beim Abschluss: jeweils **9/9 passend**. Der alte BotDropDiag-Sollhash aus r2 gilt hier nicht.

Alle vorgeschriebenen Startup-Marker sind genau einmal belegt: Core initialized **10:19:46.418 MESZ**, Arena-Treiber priority 100 **10:19:47.876**, Redeploy-Treiber priority 0 **10:19:52.457**. Harmony meldet BotPeacefulActions::SetActiveAction mit einem NoBotGestures-Prefix um **10:19:48.559** und BotDeadBodyWork::UpdateCheck mit einem NoCorpseLooting-Prefix um **10:19:48.569**. Kein incomplete-/could-not-be-installed-Startfehler.

Nur für die vorab benannte NRE-Meds-Familie gilt die r3-Ausnahme: erster Frame BotSurgicalKit+CG_ApplyToCurrentPart, BotFirstAid+CG_ApplyToSelf oder BotStimulators+CG_TryApply, method_0/method_1, MedsInHandsOperation.Finish im Stack und innerhalb 15 Sekunden nach nativem Stop/Confirm Leave. Die zwei SAIN-Errorzeilen in C gehören nicht zu dieser Familie; ein angehängter Exceptionstack fehlt. Sie werden als Errors gezählt und nicht zu Runtime-Exceptions umgedeutet.

## Tatsächlicher Ablauf und Zeitbudget

**Drei Raids; native Ergebnisdauern insgesamt 21:31.** Das Limit war fünf Raids / 40 Minuten aktive Zeit. Die Logintervalle vom jeweiligen bestätigten Raidstart bis zum geloggten Ende ergeben **21:30,678**, die kürzeren Intervalle bis Confirm Leave **21:26,113**. Sämtliche darin verstrichene Eingabe-, Beobachtungs-, Werkzeug- und Wartezeit zählt mit. Die nativen Results-Dauern und die Logintervalle werden nicht gleichgesetzt.

Der gesamte Testzeitraum **08:15:06.087 bis 09:03:54.648 UTC** dauerte **48:48,561**. Nach Logabgrenzung liegen davon **27:17,883 außerhalb der Raids**. Gesondert erfasst: READY → Raidstart **02:46,487**, geloggtes Raidende → Hauptmenü **09:01,382** einschließlich A-Behandlung und Kontextunterbrechung. Die Hauptmenüzeiten sind native Bedienbestätigungen; sie werden nicht als framegenaue Bildzeiten ausgegeben.

Messbarer Werkzeuganteil: **121 native Eingaben, 11,811 s**, sowie **148 Aufnahmen, 105,698 s**. Davon liegen **6,737 s Eingabezeit und 48,240 s Aufnahmezeit bereits innerhalb der Raidintervalle** und werden nicht zusätzlich auf die aktive Zeit addiert. Das ist eine gemessene Teilmenge, keine vollständige Messung aller Werkzeug- und Bearbeitungszeiten.

| Raid | Konstellation | READY UTC | Bestätigter Start UTC | Confirm Leave UTC | Bis Confirm | Native Dauer |
|---|---|---|---|---|---|---|
| E | Customs / TDM 5v5 / Endless / beide Limits Off / customs-factory-center-teams-v1 | 08:23:12.618 | 08:24:28.578 | 08:30:18.493 | 05:49,915 | 05:52 |
| A | Gewöhnlicher PMC / Factory Tag / MP5 / Max 3, Cooldown 5, Schutz 30 | 08:35:12.710 | 08:36:03.554 | 08:46:36.062 | 10:32,508 | 10:34 |
| C | Factory / Deathmatch FFA / Endless / beide Limits Off / Whole map | 08:54:56.398 | 08:55:36.081 | 09:00:39.771 | 05:03,690 | 05:05 |

Die Tabelle verwendet tatsächliche Inputzeitpunkte. Die kurz davor gesetzten Budgetmarker sind in der Zeitdatei getrennt dokumentiert, insbesondere C-Confirm **09:00:39.574 UTC** gegenüber tatsächlichem Input **09:00:39.771**. Client-/HUD-Beobachtungen verwenden MESZ (UTC+2), Server-/Dateimarker UTC. Ein Archivierungsmarker ist keine framegenaue Aufnahmezeit. E dauerte länger als die ungefähr vier Minuten des Zielablaufs. C überschritt die natürliche-Tod-Wartegrenze um rund **17 Sekunden**. Diese Ablaufabweichungen bleiben Teil des Ergebnisses.

## E: keine Wurfdiagnosen, natürlicher Arena-Respawn belegt

Die tatsächliche Customs-Konstellation wurde erreicht. Initial wurden neun Bots zugelassen, vier USEC und fünf BEAR neben dem lokalen PMC; Zulassungszahlen sind keine lückenlose native Bewegungsbeobachtung. Für **[10:24:28.578, 10:27:28.578) MESZ** enthält das Log **0 THROW und 0 DROPPER**, jeweils auch mit OnStartPeacefulMove im Stack 0. Ganz E bleibt in beiden Kategorien bei 0. Daraus wird kein vollständiger Sichtnachweis „keine Kits am Boden“ abgeleitet. Die vorgeschriebenen beiden Spawnzonenbilder bei 60 und 180 Sekunden fehlen. Im beobachteten Ausschnitt wurde kein plündernder Bot über einer Leiche festgestellt; der Beobachtungsausfall lässt E4 offen.

Der natürliche eigene Tod ist um **10:27:05.020 MESZ** geloggt. Kit Wraith, Equipment swap **.098**, Freecam-Rebind **.339**, Schutz aktiv **.567**, Recorder-Rebind **10:27:06.202**, AutoIFF **.219**, Respawn fertig **.715**, total **1,69 s**; Schutzende **10:27:10.784**. Give-up-Marker: **0**. E-resume-observation-0.jpg belegt den neuen Körper mit MCX, eigenen Tod 1 und Teamstand 4:3. Dieser natürliche TDM-Tod ersetzt C im FFA nicht.

E-Team-READY ist erst um **10:25:18.711 MESZ** geloggt, 50,133 Sekunden nach Matchstart. Das spätere Bild hat Archivmarker **08:29:20.870 UTC** und HUD **03:59**, entsprechend 292,292 Sekunden seit Matchstart bzw. 242,159 seit Team-READY. Diese unterschiedlichen Zeitbezüge werden nicht durch eine unbelegte Pause oder einen angenommenen HUD-Reset angeglichen.

Reguläres Ende über Escape → Disconnect → Confirm Leave; Hauptmenü **08:31:25.497 UTC**. Server: pending **08:30:20.016**, Restore **08:30:20.179**, Protected End/Persist **08:30:20.263**. E-Profilprüfung: **16/16 geschützte Feldvergleiche gleich, gesamter Scav gleich, genau das vorab zulässige numerische Delta /characters/pmc/Hideout/sptUpdateLastRunTimestamp, 0 unerwartete Unterschiede: PASS nach r3.** Gesamter PMC und gesamte JSON sind deshalb nicht identisch. Fehlend und null wurden unterschieden; Scav-RepeatableQuests fehlt beidseitig. Keine Profilrückkopie oder Reparatur.

## A: ursprünglicher Körper bedienbar, Respawn-Unterfälle nicht erreicht

Die MP5 wurde im Stash über Equip ausgerüstet. ordinary-confirm bestätigt um **08:36:03.554 UTC** den gewöhnlichen PMC-Kontext; Arena armed/active jeweils 0. Der Spieler spawnte im Tankbereich. In gut zehn Minuten trat kein natürlicher eigener Tod ein, und Death intercepted / gewöhnliches Respawn complete bleiben jeweils 0. Damit wurde die tatsächliche Todesinterception in diesem Versuch nicht ausgelöst.

Bewegung, Crouch, Prone, Jump, Escape, Inventar und DynamicMaps waren am ursprünglichen Körper bedienbar. Im Eingabeablauf standen nur kurze native Einzeltastendrücke zur Verfügung. Daraus ist keine Softwareblockade belegt. Kamera-Drags lösten teilweise Schüsse aus; ein Treffer oder ein kausales „Aufwecken“ von Bots wird daraus nicht abgeleitet. Botaktivität ist durch Heilungsdiagnosen und Killcam-Live-Tracks belegt, zuletzt sieben Tracks um **10:44:50.166 MESZ**. Tracks und vorbereitete Server-Botprofile werden nicht als exakte Zahl gleichzeitig lebender Bots ausgegeben.

Native Ergebnisseite **LEFT 10:34**, serverseitiges LEFT **08:46:37.631 UTC**. Gewöhnliche HP-/Waffenverluste nach Verlassen; kostenlose Behandlung nativ bestätigt, anschließend **440 HP**. Hauptmenü **08:53:13.430 UTC**, Stash geöffnet **08:53:20.562**. A-stash-check-0.jpg zeigt Gear/Charakter links und Stash rechts; Rückkehr zum Hauptmenü normal. Für A war ausdrücklich kein Arena-Profilgleichheitsvergleich vorgeschrieben. Der normale Gesten-Gegenbeleg bleibt ohne THROW/DROPPER offen; deren Ausbleiben ist kein FAIL.

## C: Give-up-Respawn funktioniert; vorsorglicher Stopp bei SAIN-Errors

Nach dem Startkitwechsel blieb der Spieler ohne eigene Schüsse im Raid. AKS-Munition blieb **30/30 + 1**; wenige W/S/A-Eingaben und rechte Maustaste für ADS dienten der Positionierung und Sicht. Ein natürlicher Tod trat nicht ein. Der Budgetmarker vor dem einmaligen BackSpace lautet **08:59:53.491 UTC**, die protokollierte Give-up-Eingabe **10:59:53.691 MESZ**, Arena death #1 **.779**. Gegenüber Matchstart **08:55:36.081 UTC** sind dies **4:17,410 bis Budgetmarker** bzw. **4:17,610 bis Inputlog**. Die vierminütige Obergrenze wurde um rund 17 Sekunden überschritten. Neues MCX-Kit auf dem Ersatzkörper, 440 HP, HUD-Tod 1 und Respawn fertig **10:59:54.973 MESZ**, total **1,19 s**, sind belegt. C bleibt für den natürlichen Tod OFFEN.

Um **10:59:59.216 MESZ**, Logzeilen 15246/15247, meldete SAIN einen BrainKilla-Fehler bei PlayerScav (Killa für diesen Bot nicht zulässig) sowie **Init SAIN ERROR** mit Disposing. Dies sind zwei Errorzeilen, **keine belegte Runtime-Exception und kein angehängter Exceptionstack**. Eine bestimmte Codeursache wird nicht behauptet. Der Lauf wurde vorsorglich beendet, C regulär über Confirm Leave verlassen und die Ergebnisseite **05:05** erreicht. D und weitere Wiederholungen entfielen.

Server-Restore **09:00:41.300 UTC**, Persist **09:00:41.347**. C-Profilvergleich: **16/16 geschützte Felder gleich, gesamter Scav gleich, einzig zulässiges Hideout-Timestampdelta +425 Sekunden: PASS nach r3**. Keine vollständige Dateigleichheit behauptet; keine anderen geschützten Unterschiede und keine Profiländerung durch den Test.

## Abgegrenzte Logzahlen

Die E-Auswertung endet beim Client **10:31:02.609 MESZ**, Server **08:31:21.507 UTC**. A wurde separat bis Client **10:52:53.913 MESZ** und Server **08:53:02.955 UTC** gesichert. Das finale Clientlog umfasst **15.309 Zeilen bis 11:01:33.369 MESZ**, der finale Serverstand reicht bis **09:04:08.008 UTC**. Diese festen Grenzen verhindern, dass spätere C-Ereignisse rückwirkend E oder A zugerechnet werden.

| Quelle | E Warnungen / Errors | A Warnungen / Errors | C Warnungen / Errors | Frischer Gesamtlauf Warnungen / Errors |
|---|---:|---:|---:|---:|
| Redeploy.Core | 2 / 0 | 0 / 0 | 2 / 0 | 4 / 0 |
| maschine-Redeploy | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| maschine-ArenaMode | 1 / 0 | 0 / 0 | 1 / 0 | 2 / 0 |
| Unity Log | 37 / 0 | 13 / 0 | 22 / 0 | 72 / 0 |
| SAIN | 0 / 0 | 0 / 0 | 0 / 2 | 0 / 2 |
| Serialization/Fika | 0 / 0 | 0 / 0 | 0 / 0 | 5 / 0 |
| Harmony | 0 / 0 | 0 / 0 | 0 / 0 | 4 / 0 |
| Gesamtclient | **40 / 0** | **13 / 0** | **25 / 2** | **87 / 2** |
| Server | **0 / 0** | **0 / 0** | **0 / 0** | **3 / 0** |

E-Unity: 33 BoxCollider, vier WheelCollider. A-Unity: ein BoxCollider, zwölf fehlende Skripte. Core: je zwei Culling-Sampler in E/C. Arena: je eine vorläufige pending-Endwarnung in E/C, später Restore/Persist bestätigt. Serverseitig drei Startupwarnungen und 0 Errors im ganzen frischen Lauf: Trader-Standardtimer und zwei StackObjectsCount-Korrekturen vor E; keine durch den Test ausgeführte Profilreparatur.

| BotDropDiag-Fenster | THROW / davon OnStartPeacefulMove | DROPPER / davon OnStartPeacefulMove | SURGERY-START | MEDS-FINISH | Info gesamt |
|---|---:|---:|---:|---:|---:|
| Erste drei E-Matchminuten | 0 / 0 | 0 / 0 | 0 | 0 | 0 |
| Ganz E | 0 / 0 | 0 / 0 | 0 | 8 | 8 |
| Ganz A | 0 / 0 | 0 / 0 | 861 | 17 | 878 |
| Ganz C | 0 / 0 | 0 / 0 | 0 | 4 | 4 |
| Frischer Gesamtlauf | 0 / 0 | 0 / 0 | 861 | 29 | 891 einschließlich einer Initialisierung |

Die 861 SURGERY-START-Zeilen in A liegen zwischen **10:38:23.322 und 10:38:45.287 MESZ**. Sie zählen Diagnoseaufrufe und sind keine 861 nachgewiesenen erfolgreichen Operationen. Info-Stacks sind keine Runtime-Exceptions und werden nicht als Warnungen gezählt.

Gesamter frischer Client: **0 Runtime-Exceptions, 0 erlaubte Meds-Familie, 0 MenuScreen-Errors**. Alle verbotenen Marker jeweils **0**: Respawn refused before any teardown; Core subscriber failed; Arena subscriber failed; GameEnd faulted before first await; Freecam compatibility patches could not be enabled; Arena … failed:; fail-closed incomplete; could not be installed. Info „cancelled with the raid“ ebenfalls 0; sie wäre nur bei tatsächlicher Endcancellation zu erwarten. Die zwei SAIN-Errors bleiben separat dokumentiert.

| Funktionsmarker | E | A | C | Gesamtlauf |
|---|---:|---:|---:|---:|
| Arena death / Respawntiming / Schutz aktiv / Schutzende / Startkit complete, jeweils | 1 | 0 | 1 | 2 |
| Arena Give up | 0 | 0 | 1 | 1 |
| Gewöhnliches Death intercepted / Respawn complete / Give up, jeweils | 0 | 0 | 0 | 0 |
| Arena round respawn complete | 0 | 0 | 0 | 0 |

## Rückstellung und Belege

Die nativen Einstellungen wurden zurückgestellt: **MaxRespawns 2 / Cooldown 5 / Schutz 10**, Deathmatch / Endless / Kills Off / Time Off / Whole map sowie TeamKillLimit 50 / TimeLimit 600. Alle **147 vorhandenen Client-Einstellungswerte und 149 bestehenden Arena-Konfigurationswerte sind gleich**; lediglich der neue Default NoBotGestures=true und der Speicherzeitstempel kommen hinzu bzw. ändern sich. **Sieben von neun Einstellungsdateien sind bytegleich**, einschließlich Redeploy und Graphics. Alle **64 Assets bytegleich, alle neun abschließenden DLL-Hashes passend**. Spiel PID 10008, Server PID 3000 und Launcher PID 9912 blieben mit unveränderten Startzeiten geöffnet und meldeten Responding=true; ein Prozessstopp wird nicht behauptet. Das native Grafik-Abschlussbild bestätigt **1920 × 1080 / Borderless / DLSS Balanced**.

Originalbilder unter test-evidence/respawn-stage0-r3/screenshots:

| Fall | Native Belege |
|---|---|
| E | [Zeitlimit Off](../test-evidence/respawn-stage0-r3/screenshots/E-time-limit-off-0.jpg), [Team-Startfläche](../test-evidence/respawn-stage0-r3/screenshots/E-team-starts-area-0.jpg), [erster Zustand](../test-evidence/respawn-stage0-r3/screenshots/E-first-state-0.jpg), [Teams live](../test-evidence/respawn-stage0-r3/screenshots/E-team-live-0.jpg), [Kampf/Tod/neues Kit](../test-evidence/respawn-stage0-r3/screenshots/E-resume-observation-0.jpg), [Ergebnisseite](../test-evidence/respawn-stage0-r3/screenshots/E-exit-progress-0.jpg), [Hauptmenü](../test-evidence/respawn-stage0-r3/screenshots/E-main-menu-loaded-0.jpg) |
| A | [MP5 ausgerüstet](../test-evidence/respawn-stage0-r3/screenshots/A-equip-check-0.jpg), [Schutz 30](../test-evidence/respawn-stage0-r3/screenshots/A-shield-thirty-0.jpg), [Spawn](../test-evidence/respawn-stage0-r3/screenshots/A-spawn-check-0.jpg), [Bewegung/Sprung vor Tod](../test-evidence/respawn-stage0-r3/screenshots/A-jump-forward-barrel-0.jpg), [Inventar/Karte](../test-evidence/respawn-stage0-r3/screenshots/A-map-inventory-0.jpg), [Position](../test-evidence/respawn-stage0-r3/screenshots/A-position-map-0.jpg), [Escape](../test-evidence/respawn-stage0-r3/screenshots/A-end-escape-0.jpg), [LEFT](../test-evidence/respawn-stage0-r3/screenshots/A-exit-results-0.jpg), [Behandlung](../test-evidence/respawn-stage0-r3/screenshots/A-free-treatment-0.jpg), [Charakter/Stash](../test-evidence/respawn-stage0-r3/screenshots/A-stash-check-0.jpg) |
| C | [Factory-Auswahl](../test-evidence/respawn-stage0-r3/screenshots/C-select-factory-0.jpg), [Spawn](../test-evidence/respawn-stage0-r3/screenshots/C-spawn-watch-0.jpg), [Positionierung](../test-evidence/respawn-stage0-r3/screenshots/C-step-clear-corner-0.jpg), [kein natürlicher Tod](../test-evidence/respawn-stage0-r3/screenshots/C-four-minute-no-death-0.jpg), [einmaliger Fallback](../test-evidence/respawn-stage0-r3/screenshots/C-once-give-up-fallback-0.jpg), [Exit](../test-evidence/respawn-stage0-r3/screenshots/cleanup-C-exit-menu-0.jpg), [Ergebnisseite](../test-evidence/respawn-stage0-r3/screenshots/cleanup-C-results-watch-0.jpg), [Hauptmenü](../test-evidence/respawn-stage0-r3/screenshots/cleanup-C-main-menu-confirmed-0.jpg) |
| Abschluss | [Schutz 10](../test-evidence/respawn-stage0-r3/screenshots/cleanup-shield-ten-0.jpg), [Grafik](../test-evidence/respawn-stage0-r3/screenshots/cleanup-graphics-proof-0.jpg), [abschließendes Hauptmenü](../test-evidence/respawn-stage0-r3/screenshots/cleanup-final-main-menu-0.jpg) |

Redigierte Prüfdaten: [Startup](../test-evidence/respawn-stage0-r3/audit/startup-gate.json), [E-Logs](../test-evidence/respawn-stage0-r3/audit/E-end-audit.json), [A-Logs](../test-evidence/respawn-stage0-r3/audit/A-end-audit.json), [C-Logs](../test-evidence/respawn-stage0-r3/audit/C-end-audit.json), [Gesamtlauf](../test-evidence/respawn-stage0-r3/audit/final-run-audit.json), [Zeitbilanz](../test-evidence/respawn-stage0-r3/audit/timing.json), [E-Profil](../test-evidence/respawn-stage0-r3/audit/E-profile-audit.json), [C-Profil](../test-evidence/respawn-stage0-r3/audit/C-profile-audit.json), [Einstellungen](../test-evidence/respawn-stage0-r3/audit/settings-final.json), [Endintegrität](../test-evidence/respawn-stage0-r3/audit/end-integrity.json). Gezielte Logbelege: [E](../test-evidence/respawn-stage0-r3/logs/E-end-excerpts.txt), [A](../test-evidence/respawn-stage0-r3/logs/A-end-excerpts.txt), [C](../test-evidence/respawn-stage0-r3/logs/C-end-excerpts.txt), [SAIN-Fehlerkontext](../test-evidence/respawn-stage0-r3/logs/C-SAIN-error-context.txt).

Die [Buildversionen](../test-evidence/respawn-stage0-r3/audit/build-versions.json) ergänzen die Hashbindung. Die 28 Originalbilder enthalten **JPEG/JFIF-Daten**; im privaten Aufnahmeordner trugen sie irrtümlich die Endung .png. Öffentlich wurde ausschließlich der Suffix zu **.jpg** korrigiert, ohne Transkodierung oder Byteänderung. Der Screenshotindex dokumentiert die Zuordnung und identischen SHA256-Werte.

Vollständige Logs und Profile bleiben privat unter artifacts/gpu-respawn-stage0/respawn-stage0-r3/2026-09-13T08-15-06-087Z/. Es wurden keine fehlenden Bilder erzeugt, aus älteren Läufen ersetzt oder nachgestellt. r1 und r2 bleiben unverändert.

[SHA256-Manifest](../test-evidence/respawn-stage0-r3/SHA256SUMS.txt) und [Belegindex](../test-evidence/respawn-stage0-r3/audit/screenshot-index.json).
