# GPU Deathmatch – F9-Close und Ausstieg r5

**Unterbrochener Testlauf vom 12.09.2026: Close und dreirundiger Autoexit bestanden; keine vollständige r5-Abnahme.** Vier einzelne Close-Klicks verursachten keinen Munitionsverlust. Danach funktionierten absichtliches Schießen, Nachladen und Bewegung. A endete nach drei Runden automatisch und ohne neue Exception. Alle 16 geschützten Profilfeldvergleiche sind gleich.

Vor B scheiterte die externe Windows-Bedienung. B/C, weitere Menüprüfungen und die getrennten Refinements wurden nicht gestartet. Die fünf eigenen Testwerte sind wegen dieses Zugriffsproblems **noch nicht zurückgestellt**. Spiel, Launcher und Server laufen weiter; letzter bestätigter Spielzustand ist die Charakterauswahl außerhalb eines Raids.

## Teststand

Auftrag: [r5-Anweisung](GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r5.md), Test-ID `deathmatch-menu-exit-r5`. Installation `C:\SPT41`, Repo `C:\SPT41\Development\ArenaMode`. AGENTS, aktueller HANDOFF und Eingabe-Review gelesen. Keine Produktionsänderung, kein Build, kein DLL-Tausch, keine internen Matchaufrufe und keine Profilrückkopie.

- Source/Build: `f64df5be499d06d29cc4e6b717dd52ea0931725a`.
- Git-HEAD beim Start: `1e64bc573cd5db0a2366908cf381596dfa078552`, Branch `fix/arena-panel-close-input`; Build ist dessen Vorfahr.
- Aufnahmen: 1920 × 1080; keine Behauptung über eine davon abweichende interne Renderauflösung.
- Spiel PID 11900, Start 15:10:36.900 UTC; Server PID 13660, Start 15:09:24.462 UTC; Launcher PID 4288, Start 15:09:58.818 UTC. Für diesen Lauf frisch gestartet.
- Fremde Datei `ArenaMode.Server/defaults/~syncthing~ladder.json.tmp` unberührt. Historische r1–r4-Berichte, Anweisungen und Belege unverändert.

| Installierte Datei, relativ zu C:\SPT41 | Tatsächliche SHA256 |
|---|---|
| BepInEx/plugins/maschine-ArenaMode.Client.dll | `7DCAA94CFDF5F0C7E6736796901BE4C3DCBADCEC6F74C76E1668C22B81201F11` |
| SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll | `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7` |
| BepInEx/plugins/maschine-Redeploy.Core.dll | `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8` |
| BepInEx/plugins/SAIN/SAIN.dll | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` |

Client ProductVersion `0.1.0+f64df5be499d06d29cc4e6b717dd52ea0931725a`; SAIN AssemblyVersion `4.5.1.0`. Alle vier Hashes stimmen beim Preflight 15:08:44 UTC, vor A 15:13:23 UTC und nach der Desktopblockade 15:28:18 UTC. Auch die abschließende Hashprüfung dieses unterbrochenen Laufs am 2026-09-12T15:36:05.769Z bestätigt alle vier Dateien. Eine spätere Prüfung nach regulärer Rückstellung/Programmende steht noch aus.

Startup, je einmal:
- Client L6107: **17:11:00.733 MESZ = 15:11:00.733 UTC**, `Arena input blocker ready: panel close waits for released-pointer input delivery.`
- Client L4968: **17:10:59.021 MESZ = 15:10:59.021 UTC**, `Arena: optional SAIN 4.5.1 uninitialized-component teardown safeguard ready.`
- Kein `Arena input blocker unavailable`. Die Startmarker belegen Registrierung; seltene Laufzeitpfade werden unten getrennt bewertet.

## Zeit und Umfang

**Ein Raid, drei Runden, vier Close-Versuche; keine weiteren Raids.** Die Anweisung erlaubt maximal drei Raids und 20 Minuten aktive Testzeit. Als konservative Planungsgrenze wurde 15:12:20.487 bis 15:32:20.487 UTC einschließlich Menü-, Prüf- und Werkzeugzeit angesetzt. Bis zum ersten extern gescheiterten Input um 15:27:34.500 vergingen 15 min 14,013 s Wanduhrzeit. Der Desktopblock und spätere reine Berichtserstellung sind keine weitere aktive Spielprüfung.

| Phase | UTC-Beobachtungsgrenzen | Einordnung |
|---|---|---|
| Startvorbereitung | Preflight 15:08:44; Hauptmenübild 15:12:13.691 | Programmstart/Laden und Werkzeuge |
| Arena-Menüs | Planungsstart 15:12:20.487; Ready-Eingabe 15:13:32.206 | Einstellungen/Navigation mit Werkzeugpausen |
| Laden bis erste Runde | Ready 15:13:32.206; Round 1 15:14:29.624 | Gemischtes Lade-/Werkzeugfenster, keine exakt gemessene Ladezeit |
| A laufend bis Autoexit-Commit | 15:14:29.624–15:21:23.000 | 6 min 53,376 s Wanduhr, inklusive Zwischenphasen und Respawns |
| Native Rückkehr | Commit 15:21:23.000; Hauptmenübild 15:22:49.140 | Endscreen, Eingabe und Lade-/Werkzeugfenster |
| Audit / Vorbereitung B | Hauptmenü 15:22:49.140; fehlgeschlagener Klick 15:27:34.500 | Profil-/Logprüfung, Charakterauswahl, längere Werkzeug-/Kontextpause; kein zweiter Raid |
| Externer Desktopblock | ab 15:27:34.500 | Keine weiteren UI-Eingaben nach gescheiterter Wiederherstellung |

Diese Grenzen sind keine lückenlose Stoppuhr aktiver Testzeit. Archivzeiten sind keine exakten Pixelaufnahmezeiten; aus ungenauen Fenstern wird keine sekundengenaue aktive Unter-20-Minuten-Abnahme abgeleitet.

## A – Factory Tag, All vs player

Native Auswahl: Deathmatch / All vs player / 3 Runden / 10 Kills / 120 Sekunden, Factory Tag, keine PFT-Gruppe. Kill-/Zeitlimit zuerst gesetzt, danach drei Runden. Server L7517 bestätigt um 15:13:53.702 UTC `all_vs_player, frozen Deathmatch selection`. [Regeln und Factory](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-factory.jpg), [native Bestätigung](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-native-confirmation.jpg).

| Unterfall | Status | Befund / Grenze |
|---|---|---|
| Arena-Auswahl / Regeln / keine PFT-Gruppe | PASS | Native Bilder und bestätigte serverseitige Regeln. |
| Charaktervorschau, kurze Sichtkontrolle | PASS für Einzelansicht | Separate Arena-Figur plausibel; keine neue zeitliche Jittermessung. |
| Drei einzelne Close-Vergleiche | PASS | VSS jeweils 10/10 +1 unverändert; gleiche Waffe, Gesundheit, Todeszahl und Wandansicht in jedem Paar. |
| Zusätzlicher Close nach natürlichem Respawn | PASS | MP-153 4/4 +1 unverändert; stabiler Zustand nach dem vierten natürlichen Tod, volle Gesundheit und gleiche Todeszahl vor/nach Close. |
| F9-Hotkey schließen | PASS | Eigenes Open-/Closed-Paar nach den ersten drei Close-Klicks. |
| Escape und reguläres Fortsetzen | PASS mit Grenze | Escape schließt F9 und zeigt das native Pausemenü; nächste Escape-Eingabe setzt fort. Keine Behauptung, Escape habe ausschließlich F9 verarbeitet. |
| Absichtlicher Schuss nach Freigabe | PASS | Getrennter HK416-Wandschuss: 30/30 +1 → 29/30 +1. |
| Nachladen / Bewegung nach Freigabe | PASS | Wieder 30/30 +1, abgeschlossene Nachladebewegung, danach gesonderter Schritt nach links. Kein dauerhafter Eingabelock. |
| Drei Runden bis Autoexit | PASS | Drei Start-/Endmarker, terminales Ergebnis, automatischer Result-Exit und native Rückkehr. |
| Terminales F9 offen, Ergebnis/Countdown lesbar | PASS im Einzelbild | DEFEAT, 0 Runden gewonnen / 3 verloren, drei abgeschlossen, Rückkehr in 1 s. Kein großes Banner verdeckt das Fenster. |
| Terminales F9 schließen und wieder öffnen / Banner nur bei geschlossenem F9 | OFFEN | Ergebnisaufnahme kam erst kurz vor Autoexit; kein vollständiger Wechselzyklus. |
| Kein Countdown-Neustart durch terminalen F9-Wechsel | OFFEN | Der erforderliche Wechselzyklus fehlt. |
| Score über Ergebnisdauer eingefroren | OFFEN | Zwei nahezu gleichzeitige Originale derselben Zustandsabfrage beweisen keine Dauer. Vorheriges Live-Bild ist kein zweites terminales Zeitfenster. |
| Vorbereitungsbanner nach F9 erhalten | OFFEN | Spezifischer Vorher-/Nachher-Unterfall nicht erfasst. |
| Tactical-Kaufmenü Close | OFFEN | In diesem Deathmatch nicht erreichbar; kein Zusatzraid dafür. |
| Exit ohne neue Exceptions | PASS für A | Vollständiges abgegrenztes Logfenster bis Hauptmenü unauffällig. |
| Tatsächlicher Pickup-Cancel | OFFEN / nicht beobachtet | 0 Laufzeitmarker. |
| Tatsächlicher SAIN-Uninitialized-Teardown | OFFEN / nicht beobachtet | 0 Laufzeitmarker; r4-Eingriff wird nicht r5 zugerechnet. |
| PMC-/Scav-Schutzfelder | PASS | Alle 16 Vergleiche gleich; vollständige JSON-Differenz separat ausgewiesen. |

### Close-Belege und Eingaben

Jedes Paar enthält genau einen bewussten Close-Klick bei (1042,906). Zwischen Vorherbild und Nachherbild gibt es keine andere aufgezeichnete Maus-, Schuss-, Nachlade- oder Waffenbedienung. Audio wurde nicht aufgezeichnet; Munitionsanzeige, sichtbarer Zustand, Eingabeprotokoll und begrenzte Laufzeitmarker bilden die Belege.

| Nr. | Vorher → Nachher, UTC-Archivmarker | Waffe / Munition | Input → Zustandsrückgabe UTC | Pending → Released UTC / Generation / blockedShotCommands |
|---|---|---|---|---|
| 1 | 15:14:48.541 → 15:14:59.518 | VSS 10/10 +1 → 10/10 +1 | 15:14:56.733 → 15:14:59.507 | 15:14:56.964 → 15:14:57.032 / 20934 / 1 |
| 2 | 15:15:10.616 → 15:15:21.077 | VSS 10/10 +1 → 10/10 +1 | 15:15:18.421 → 15:15:21.076 | 15:15:18.654 → 15:15:18.700 / 20935 / 1 |
| 3 | 15:15:37.408 → 15:15:49.443 | VSS 10/10 +1 → 10/10 +1 | 15:15:46.825 → 15:15:49.443 | 15:15:47.004 → 15:15:47.046 / 20936 / 1 |
| 4 | 15:19:49.123 → 15:19:58.760 | MP-153 4/4 +1 → 4/4 +1 | 15:19:56.374 → 15:19:58.760 | 15:19:56.609 → 15:19:56.658 / 20941 / 1 |

Originalpaare: [1 vorher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-1-before.jpg) / [nachher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-1-after.jpg), [2 vorher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-2-before.jpg) / [nachher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-2-after.jpg), [3 vorher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-3-before.jpg) / [nachher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-3-after.jpg), [4 stabiles Vorherbild](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-4-stable-before.jpg) / [nachher](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-4-after.jpg).

Der vierte Vergleich verwendet ausdrücklich das stabile Vorbild nach dem Waffenwechsel; die frühere Aufnahme während des Respawnübergangs ist nur Kontext. Es gab vier Close-Klicks, keinen fünften Versuch. Die unmittelbare Zustandsabfrage folgt jeweils ohne weitere Eingabe. Input→Rückgabe dauert 2,774 / 2,655 / 2,618 / 2,386 s; der jeweilige Archivabstand dagegen 10,977 / 10,461 / 12,035 / 9,637 s. Diese Größen dürfen nicht gleichgesetzt werden. Die Quellen liefern keine separaten exakten Pixelaufnahmezeitstempel. [Unabhängiger Timing-Audit](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/timing-audit-r5.json).

Getrennte Nachbedienung: bewusster Schuss 15:17:00.341→15:17:02.476, Reload 15:17:10.950→15:17:13.085, Bewegung 15:17:21.990→15:17:23.928 UTC. [Schuss](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-intentional-wall-shot.jpg), [Reload](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-deliberate-reload.jpg), [Bewegung](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-deliberate-move-left.jpg).

In A insgesamt **18 Input-State-Zeilen, neun vollständige Pending/Released-Paare**. Vier Released zählen je einen blockierten Schussbefehl, fünf zählen null. Die Kappe von 24 Zeilen wurde nicht erreicht. Die übrigen Paare gehören auch zu Hotkey-/automatischem Panel-Schließen; neun Paare sind keine neun Close-Versuche. Alle sample-Werte und Zeiten stehen im [Logaudit](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/A-log-audit.json). Ein Nullzähler ist nicht als Fehler gewertet.

### Runden und Rückkehr

| Ereignis | UTC | Quelle |
|---|---|---|
| Runde 1 Start / Ende Time limit | 15:14:29.624 / 15:16:30.718 | Client L13207 / L13275; 0 Kills / 0 Tode / 0 Punkte |
| Runde 2 Start / Ende Time limit | 15:16:46.951 / 15:18:50.074 | Client L13320 / L13404; 0 / 3 / 0 |
| Runde 3 Start / Ende Time limit | 15:19:05.886 / 15:21:07.779 | Client L13430 / L13512; 0 / 3 / 0 |
| Terminales F9 sichtbar | 15:21:22.573 Archivzeit | Ergebnisbild, Rückkehr in 1 s |
| Automatischer Result-Exit committed | 15:21:23.000 | Client L13520 |
| Session beendet / Matchsumme | 15:21:24.304 / 15:21:24.313 | Client L13535 / L13538; 0 Kills / 6 Tode / 0 Punkte |
| Native local-end / Snapshot restored / protected end persisted | 15:21:24.385 / 15:21:24.503 / 15:21:24.712 | Server L8012 / L8013 / L8016 |
| Native Endscreen / Hauptmenü bestätigt | 15:21:43.778 / 15:22:49.140 Archivzeit | Originalbilder |

[Terminales F9](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-terminal-f9-first.jpg), [nativer Endscreen](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-native-end-check.jpg), [Hauptmenü](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-mainmenu-confirm.jpg). Der Main-menu-Klick im nativen Endscreen ist kein vorzeitiger Raidabbruch und kein Nachweis des in B geforderten Ergebnisbuttons.

Sechs natürliche Tod-/Respawn-Paare, UTC: 15:17:39.820→15:17:40.597; 15:18:15.572→15:18:16.554; 15:18:38.215→15:18:38.946; 15:19:34.361→15:19:35.167; 15:20:18.743→15:20:19.512; 15:20:32.976→15:20:33.749. Keine erzwungenen Matchereignisse. Der zunächst geloggte pending raid-end wird in derselben Folge durch nativen local-end und serverseitiges Restore/Persistieren aufgelöst.

### Logs und vollständiger Profilvergleich

Client L1–13545 und Server L6832–8090 der archivierten A-Hauptmenüquellen vollständig abgegrenzt geprüft. Serverzeilen vor dem frischen Prozess gehören zu älteren Läufen und sind ausgeschlossen. Clientzeit ist **MESZ (UTC+2)**, Serverzeit **UTC**.

**0 Error/Fatal/Critical, 0 neue ausgelöste Exceptions, keine relevanten neuen Exception-Stacks.** Harmony-Finalizer-Signaturen und `Assert.raiseExceptions` sind keine ausgelösten Exceptions. 36 Client-Warnings enthalten bekannte Start-/Lade-/Culling-Hinweise und den anschließend aufgelösten deferred raid-end; serverseitig eine temporäre Händlerwarnung. Pickup-Cancel und SAIN-Uninitialized-Teardown jeweils **0**, daher keine Auslösezeiten. Auch kein BotItemTaker/CG_PickupAction-ArgumentException oder SAIN.Components.BotComponent.OnDisable-NRE. [118 gezielte, redigierte Originalzeilen](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/A-log-excerpts.txt). Das spätere Clientdelta bis 15:28:45 umfasst zwei weitere Zeilen ohne neue Fehler.

Private Profilbaseline **15:13:23.655 UTC**, Nachkopie **15:23:02.329 UTC**, nach bestätigtem Hauptmenü. Unabhängiger vollständiger, rekursiver JSON-Vergleich inklusive Typen und Arrayreihenfolge:

| Feld | PMC | Scav |
|---|---|---|
| Inventory | gleich | gleich |
| Quests | gleich | gleich |
| Skills | gleich | gleich |
| Health einschließlich UpdateTime | gleich | gleich |
| Info.Experience | gleich | gleich |
| Info.Level | gleich | gleich |
| TaskConditionCounters | gleich | gleich |
| RepeatableQuests | gleich | beidseits abwesend |

Der gesamte Scav-Teil ist gleich. **Genau eine vollständige JSON-Differenz**: `characters.pmc.Hideout.sptUpdateLastRunTimestamp`, `1789225953 → 1789226548`, also **15:12:33 → 15:22:28 UTC (+595 s)**. Das ist eine Hideout-Zeitstempeländerung; der Dateivergleich identifiziert ihren konkreten Schreiber nicht. Es wird weder ein vollständig identisches Gesamtprofil noch gespeicherte Endscreen-EXP behauptet. Die native Anzeige von 389 EXP änderte Info.Experience nicht.

Vorlauf vor der Raidbaseline separat: PMC.Health.UpdateTime 14:06:54→15:11:30 UTC sowie Hideout-Zeitstempel 14:21:23→15:12:33. Diese zwei Änderungen entstanden vor der A-Baseline; sie werden nicht A zugerechnet. [Vollständiger sanitiserter Profilaudit](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/A-profile-audit.json).

## Unterbrechung, B/C und Rückstellung

Letztes erfolgreiches Original: [Charakterauswahl](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-menu-resume.jpg), Archivzeit 15:27:20.498 UTC. Der Arena-Auswahlklick um 15:27:34.500 scheiterte mit `failed to activate captured window`. Frische Fensterliste und eindeutige Neuauswahl des Spielfensters, anschließend Aktivierungsfehler: `GetCursorPos failed: Zugriff verweigert (0x80070005)`. Darauf keine weiteren UI-Eingaben; Nutzer um Öffnen/Entsperren von RDP gebeten. Die Fehlermeldung belegt das externe Zugriffsproblem, nicht dessen genaue Windows-/RDP-Ursache und keinen Spiel-Softlock. B-Ready wurde nicht aufgerufen.

| Verbleibender Fall | Status / Grund |
|---|---|
| B: FFA / 1 Runde / Kills Off / 120 s, Regeln/Killfeed/Standings | OFFEN – kein Start wegen Desktopzugriff |
| B: plausibler Sieger und bewusster Return-to-menu-Ergebnisbutton | OFFEN – kein B-Raid |
| B: Exitlogs und geschützte/full-JSON Profile | OFFEN – kein B-Raid |
| C: FFA / Endless / beide Limits Off, ca. 90 s ohne Rundenpause | OFFEN – nicht gestartet |
| C: Give up, Respawn und normaler Disconnect | OFFEN – nicht gestartet |
| C: Exitlogs und geschützte/full-JSON Profile | OFFEN – kein C-Raid |
| Normaler PMC-Menüweg bis vor Ready und zurück | OFFEN – Charakterauswahl allein genügt nicht |
| Hideout / Weapon Builder, fremde Körperteile/Modelle | OFFEN – nicht geöffnet |
| Getrennter Refinements-r3-Lauf: Leichenmarker, Air Jump, Kontur, DLSS-Diamanten, normaler PMC-Raid | OFFEN – externer Desktopzugriff verhindert Start; keine Features aus A pauschal abgenommen |
| Rückstellung eigener Einstellungen / reguläres Programmende | OFFEN – UI nicht bedienbar |

Von 147 gesicherten Arena-Einstellungswerten sind **genau fünf eigene Testwerte noch geändert**:

| Wert | Aktuell | Wiederherzustellen |
|---|---|---|
| Match/GameMode | deathmatch | gungame |
| Deathmatch/Rounds | 3 | 0 / Endless |
| Deathmatch/KillLimit | 10 | 0 / Off |
| Deathmatch/TimeLimitSeconds | 120 | 0 / Off |
| Deathmatch/FreeForAll | false | true |

Rückstellung ausschließlich über native Arena-Menüs; beide Limits Off erzwingen korrekt Endless. Keine alten cfg-/client-settings-Dateien, Beschreibungstexte oder Profile zurückkopieren. Prüfung 15:29:31 UTC: andere 142 Arena-Werte gleich; RaidInfoPanels.cfg, Control.ini, Graphics.ini und Server-config.json bytegleich. client-settings.json und Arena.cfg spiegeln die eigenen Teständerungen; deren vollständige Bytegleichheit wird nicht behauptet. Alle **33 gesicherten Arena-Assetdateien einschließlich Audio unverändert**. [Rückstellungs- und Dateiaudit](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/settings-restoration-r5.json). Prozessprüfung 15:31 UTC: alle drei genannten Prozesse vorhanden und Responding; keine Zwangsbeendigung.

## Belege

[SHA256-Manifest](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/SHA256SUMS.txt) und [Bildindex mit UTC-Archivmarkern](test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/screenshots.json). 29 ausgewählte, unverändert kopierte Original-JPGs; pro Zustandsabfrage die erste Fläche. Zweite nahezu gleichzeitige Flächen dienen nicht als zeitlicher Dauerbeweis. Abgeleitete JSON-/Textauszüge sind mit LF gespeichert; Bildbytes unverändert.

Rohlogs, vollständige Profile, Launcherbilder und private Fortsetzungsdaten liegen ausschließlich im ignorierten Lauf `artifacts/gpu-deathmatch-menu/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/`. Veröffentlichte gezielte Logauszüge haben private Kennungen redigiert. Dieser Bericht dokumentiert den unterbrochenen Stand; er bescheinigt weder B/C noch ausstehende Rückstellung oder eine vollständige Freigabe.
