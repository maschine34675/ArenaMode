# GPU Deathmatch – F9-Close und Ausstieg r5

**Am 12.09.2026 nach einer externen Desktopunterbrechung fortgesetzt und im begrenzten Testumfang abgeschlossen: zwei Raids, vier Runden; keine vollständige r5-Abnahme.** Vier einzelne Close-Klicks in A verursachten keinen Munitionsverlust. Danach funktionierten absichtliches Schießen, Nachladen und Bewegung. A endete nach drei Runden automatisch; B bestätigte FFA, einen plausiblen Sieger und einen weiteren fehlerfreien nativen Ausstieg. Für jeden Raid sind alle 16 geschützten Profilfeldvergleiche gleich; vollständige JSON-Nebenfeldänderungen stehen separat im Bericht.

Der bewusste Ergebnisbutton in B bleibt **OFFEN**: Autoexit war bereits 0,996 s vor Klickbeginn committed. C, die übrigen Dauer-/Bannerprüfungen und die ergänzenden normalen Menüprüfungen bleiben wegen fehlender Belege bzw. Zeitgrenze offen. Alle fünf eigenen Testwerte wurden anschließend regulär über das Spiel zurückgestellt; 147/147 Arena-Werte stimmen wieder. Spielprozess 10268, Launcher und Server bleiben für den getrennten, bereits autorisierten Refinementsauftrag geöffnet. Dessen Ergebnisse gehören nicht zu diesem r5-Bericht.

## Teststand

Auftrag: [r5-Anweisung](GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r5.md), Test-ID `deathmatch-menu-exit-r5`. Installation `C:\SPT41`, Repo `C:\SPT41\Development\ArenaMode`. AGENTS, aktueller HANDOFF und Eingabe-Review gelesen. Keine Produktionsänderung, kein Build, kein DLL-Tausch, keine internen Matchaufrufe und keine Profilrückkopie.

- Source/Build: `f64df5be499d06d29cc4e6b717dd52ea0931725a`.
- Git-HEAD beim Start: `1e64bc573cd5db0a2366908cf381596dfa078552`, Branch `fix/arena-panel-close-input`; Build ist dessen Vorfahr.
- Aufnahmen: 1920 × 1080; keine Behauptung über eine davon abweichende interne Renderauflösung.
- A: Spiel PID 11900, Start 15:10:36.900 UTC. Nach der Pause B: neuer Spielprozess PID 10268, Start 15:45:36.5518531 UTC. Server PID 13660 (Start 15:09:24.462 UTC) und Launcher PID 4288 (Start 15:09:58.818 UTC) liefen durch; Prozess-/Inputzählungen bleiben getrennt.
- Fremde Datei `ArenaMode.Server/defaults/~syncthing~ladder.json.tmp` unberührt. Historische r1–r4-Berichte, Anweisungen und Belege unverändert.

| Installierte Datei, relativ zu C:\SPT41 | Tatsächliche SHA256 |
|---|---|
| BepInEx/plugins/maschine-ArenaMode.Client.dll | `7DCAA94CFDF5F0C7E6736796901BE4C3DCBADCEC6F74C76E1668C22B81201F11` |
| SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll | `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7` |
| BepInEx/plugins/maschine-Redeploy.Core.dll | `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8` |
| BepInEx/plugins/SAIN/SAIN.dll | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` |

Client ProductVersion `0.1.0+f64df5be499d06d29cc4e6b717dd52ea0931725a`; SAIN AssemblyVersion `4.5.1.0`. Alle vier Hashes stimmen beim Preflight 15:08:44 UTC, vor A 15:13:23, während der Unterbrechung 15:28:18/15:36:05, vor der Fortsetzung 15:44:36.061, vor B 15:48:25.319 und nach der nativen Rückstellung 15:56:34.015 UTC. [Abschließende r5-Dateibindung](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/hash-restored-r5-end.json). Es wurde kein Programmende des zweiten Spielprozesses behauptet oder für r5 erzwungen.

Startup des A-Prozesses, je einmal:

- Client L6107: **17:11:00.733 MESZ = 15:11:00.733 UTC**, `Arena input blocker ready: panel close waits for released-pointer input delivery.`
- Client L4968: **17:10:59.021 MESZ = 15:10:59.021 UTC**, `Arena: optional SAIN 4.5.1 uninitialized-component teardown safeguard ready.`
- Kein `Arena input blocker unavailable`. Die Startmarker belegen Registrierung; seltene Laufzeitpfade werden unten getrennt bewertet.

## Zeit und Umfang

**Zwei Raids, vier Runden; vier kontrollierte Close-Versuche ausschließlich in A.** Die Anweisung erlaubt höchstens drei Raids und 20 Minuten aktive Testzeit. Zunächst wurde eine konservative Planungsgrenze 15:12:20.487–15:32:20.487 UTC einschließlich Menü-/Werkzeugzeit angesetzt. Bis zum ersten extern gescheiterten Input 15:27:34.500 vergingen 15 min 14,013 s Wanduhrzeit. Dieses verbrauchte Fenster wurde bei der Fortsetzung beibehalten; dokumentierte Desktoppause und erforderlicher Neustart wurden gesondert behandelt.

Die Fortsetzung begann 15:47:21.993 UTC mit konservativ verbleibenden 4 min 45,987 s; daraus folgt die r5-Deadline **15:52:07.980 UTC**. B erreichte den Autoexit-Commit 15:51:54.413 und den nativen Endscreen im Archivbild 15:52:07.431. Sichere Hauptmenürückkehr, Profil-/Logkontrolle und Rückstellung liefen anschließend weiter. **C und weitere r5-Abnahmefälle wurden nach der Zeitgrenze nicht gestartet.** Die Deadline wird nicht als bereits vollständig beendete Bereinigung ausgegeben. [Fortsetzungsbudget](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/resume-budget.json).

| Phase | UTC-Beobachtungsgrenzen | Einordnung |
|---|---|---|
| Erster Start / Arena-Menüs | Preflight 15:08:44; Hauptmenübild 15:12:13.691; Planung ab 15:12:20.487 | Programmstart/Laden, dann Navigation mit Werkzeugpausen |
| Laden A | Ready-Eingabe 15:13:32.206; Runde 1 ab 15:14:29.624 | Gemischtes Lade-/Werkzeugfenster, keine exakt gemessene Ladezeit |
| A bis Autoexit-Commit | 15:14:29.624–15:21:23.000 | 6 min 53,376 s Wanduhr einschließlich Zwischenphasen/Respawns |
| A native Rückkehr / Audit | Commit 15:21:23.000; Hauptmenübild 15:22:49.140; gescheiterter Input 15:27:34.500 | Endscreen, sichere Rückkehr, Profil-/Logkontrolle, Vorbereitung B; noch kein B-Raid |
| Desktoppause / erforderlicher Neustart | ab 15:27:34.500; alter Client endet mit Cleanup 15:43:49.415; neuer PID ab 15:45:36.552 | Externe Unterbrechung und Neustart; getrennt von aktiver r5-Prüfung |
| B-Menüs / Laden | Budgetfortsetzung 15:47:21.993; Ready-Archiv 15:48:42.200; Runde 1 ab 15:49:37.869 | Konservativ einschließlich Werkzeug-/Ladezeit gezählt |
| B bis Autoexit | Runde 1 15:49:37.869–15:51:39.158; Result-Exit 15:51:54.413 | 121,289 s Runden-Wanduhr; anschließender Ergebniscountdown |
| Deadline / sichere Bereinigung | Deadline 15:52:07.980; Hauptmenüarchiv 15:53:10.433; Rückstellung bestätigt 15:56:33.994 | Nach Deadline nur sichere Rückkehr, Kontrollen und Rückstellung; kein C-Raid |

Diese Grenzen sind keine lückenlose Stoppuhr aktiver Testzeit. Archivzeiten sind keine separaten exakten Pixelaufnahmezeiten. Aus den Fenstern wird keine sekundengenaue aktive Unter-20-Minuten-Abnahme abgeleitet; insbesondere werden sichere Rückkehr und Bereinigung nach der Planungsdeadline ausdrücklich ausgewiesen. Das Budget eines nachfolgenden separaten Refinementsauftrags ist kein Bestandteil dieses r5-Laufs.

## A – Factory Tag, All vs player

Native Auswahl: Deathmatch / All vs player / 3 Runden / 10 Kills / 120 Sekunden, Factory Tag, keine PFT-Gruppe. Kill-/Zeitlimit zuerst gesetzt, danach drei Runden. Server L7517 bestätigt um 15:13:53.702 UTC `all_vs_player, frozen Deathmatch selection`. [Regeln und Factory](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-factory.jpg), [native Bestätigung](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-native-confirmation.jpg).

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

Originalpaare: [1 vorher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-1-before.jpg) / [nachher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-1-after.jpg), [2 vorher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-2-before.jpg) / [nachher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-2-after.jpg), [3 vorher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-3-before.jpg) / [nachher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-3-after.jpg), [4 stabiles Vorherbild](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-4-stable-before.jpg) / [nachher](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-close-4-after.jpg).

Der vierte Vergleich verwendet ausdrücklich das stabile Vorbild nach dem Waffenwechsel; die frühere Aufnahme während des Respawnübergangs ist nur Kontext. Es gab vier Close-Klicks, keinen fünften Versuch. Die unmittelbare Zustandsabfrage folgt jeweils ohne weitere Eingabe. Input→Rückgabe dauert 2,774 / 2,655 / 2,618 / 2,386 s; der jeweilige Archivabstand dagegen 10,977 / 10,461 / 12,035 / 9,637 s. Diese Größen dürfen nicht gleichgesetzt werden. Die Quellen liefern keine separaten exakten Pixelaufnahmezeitstempel. [Unabhängiger Timing-Audit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/timing-audit-r5.json).

Getrennte Nachbedienung: bewusster Schuss 15:17:00.341→15:17:02.476, Reload 15:17:10.950→15:17:13.085, Bewegung 15:17:21.990→15:17:23.928 UTC. [Schuss](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-intentional-wall-shot.jpg), [Reload](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-deliberate-reload.jpg), [Bewegung](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-deliberate-move-left.jpg).

In A insgesamt **18 Input-State-Zeilen, neun vollständige Pending/Released-Paare**. Vier Released zählen je einen blockierten Schussbefehl, fünf zählen null. Die Kappe von 24 Zeilen wurde nicht erreicht. Die übrigen Paare gehören auch zu Hotkey-/automatischem Panel-Schließen; neun Paare sind keine neun Close-Versuche. Alle sample-Werte und Zeiten stehen im [Logaudit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/A-log-audit.json). Ein Nullzähler ist nicht als Fehler gewertet.

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

[Terminales F9](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-terminal-f9-first.jpg), [nativer Endscreen](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-native-end-check.jpg), [Hauptmenü](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/a-mainmenu-confirm.jpg). Der Main-menu-Klick im nativen Endscreen ist kein vorzeitiger Raidabbruch und kein Nachweis des in B geforderten Ergebnisbuttons.

Sechs natürliche Tod-/Respawn-Paare, UTC: 15:17:39.820→15:17:40.597; 15:18:15.572→15:18:16.554; 15:18:38.215→15:18:38.946; 15:19:34.361→15:19:35.167; 15:20:18.743→15:20:19.512; 15:20:32.976→15:20:33.749. Keine erzwungenen Matchereignisse. Der zunächst geloggte pending raid-end wird in derselben Folge durch nativen local-end und serverseitiges Restore/Persistieren aufgelöst.

### Logs und vollständiger Profilvergleich

Client L1–13545 und Server L6832–8090 der archivierten A-Hauptmenüquellen vollständig abgegrenzt geprüft. Serverzeilen vor dem frischen Prozess gehören zu älteren Läufen und sind ausgeschlossen. Clientzeit ist **MESZ (UTC+2)**, Serverzeit **UTC**.

**0 Error/Fatal/Critical, 0 neue ausgelöste Exceptions, keine relevanten neuen Exception-Stacks.** Harmony-Finalizer-Signaturen und `Assert.raiseExceptions` sind keine ausgelösten Exceptions. 36 Client-Warnings enthalten bekannte Start-/Lade-/Culling-Hinweise und den anschließend aufgelösten deferred raid-end; serverseitig eine temporäre Händlerwarnung. Pickup-Cancel und SAIN-Uninitialized-Teardown jeweils **0**, daher keine Auslösezeiten. Auch kein BotItemTaker/CG_PickupAction-ArgumentException oder SAIN.Components.BotComponent.OnDisable-NRE. [118 gezielte, redigierte Originalzeilen](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/A-log-excerpts.txt). Das spätere Clientdelta bis 15:28:45 umfasst zwei weitere Zeilen ohne neue Fehler.

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

Vorlauf vor der Raidbaseline separat: PMC.Health.UpdateTime 14:06:54→15:11:30 UTC sowie Hideout-Zeitstempel 14:21:23→15:12:33. Diese zwei Änderungen entstanden vor der A-Baseline; sie werden nicht A zugerechnet. [Vollständiger sanitiserter Profilaudit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/A-profile-audit.json).

## Desktopunterbrechung und frischer Start für B

Das letzte erfolgreiche Bild vor dem Zugriffsverlust war die [Charakterauswahl](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-menu-resume.jpg), Archiv 15:27:20.498 UTC. Der Arena-Auswahlklick 15:27:34.500 scheiterte mit `failed to activate captured window`; erneute Fenstererfassung/Aktivierung anschließend mit `GetCursorPos failed: Zugriff verweigert (0x80070005)`. Bis zur Nutzerbestätigung „ist wieder offen“ wurden keine weiteren Spieltests durchgeführt. Die Meldungen beweisen den externen Zugriffsausfall, nicht dessen genaue Windows-/RDP-Ursache oder einen Spiel-Softlock. Vor der Pause gab es keinen B-Ready und keinen B-Raid.

Bei Wiederaufnahme war Spielprozess 11900 beendet, Server 13660 und Launcher 4288 liefen weiter. Die archivierte Fortsetzung des ersten Clientlogs stimmt bis A-Hauptmenü exakt mit der alten Quelle überein. Clientdelta L13546–13823 und Serverdelta L8091–8277 enthalten 0 Error/Fatal/Critical, 0 Warnings und 0 neue ausgelöste Exceptions; drei `Exception`-Texttreffer sind Harmony-Finalizer-Signaturen beim Unpatching. Letzte Clientzeile 15:43:49.415 UTC meldet die wiederhergestellte ursprüngliche AssemblyNames-Liste. Es gibt keine neue Arena-Arm-/Raidfolge im Pausenfenster. Die Cleanup-Zeilen identifizieren weder Verursacher noch genaue Art des Programmendes. [Pausen-/Shutdown-Audit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/resume-game-absent-audit-r5.json).

Der gleiche gebundene Client wurde neu gestartet: **PID 10268, Start 15:45:36.5518531 UTC**. Sein frisches Startup-Archiv reicht bis Client L13019; das fortlaufende Serverdelta L8278–8344 ist ebenfalls geprüft. Keine neue Exception/Error; neun bekannte Client-Startupwarnungen, keine Warnung im Serverdelta. Neue Ready-Marker je einmal:

- L4968,17:45:57.268 MESZ = 15:45:57.268 UTC: SAIN-Uninitialized-Teardown-Safeguard ready.
- L6107,17:45:58.830 MESZ = 15:45:58.830 UTC: Input blocker ready; kein unavailable.

Die Prozessgrenzen und die Diagnosekappe von 24 Input-State-Zeilen werden getrennt behandelt; A-Zählungen werden nicht zu B addiert. [Prozesse](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/resume-processes.json), [neuer Startupaudit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/resume-startup-audit-r5.json).

## B – Factory Tag, FFA, eine Runde

Native Auswahl: **Deathmatch / FFA /1 Runde / Kills Off /120 Sekunden**, Factory Tag. Die vorherigen A-Regeln wurden regulär umgestellt und vor Ready dokumentiert. Server L8437 bestätigt 15:49:01.750 UTC `ffa, frozen Deathmatch selection`. [Factory/Regeln](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-factory-rules.jpg), [F9-Regeln](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-f9-rules.jpg).

| Unterfall | Status | Befund / Grenze |
|---|---|---|
| FFA-Regeln, eine Runde, Kills Off /120 s | PASS | Native Auswahl/F9 und bestätigte gefrorene Serverregel. |
| Individueller Killfeed-/Standings-Smoke-Test | PASS im beobachteten Umfang | Killfeed und getrennte Spielerzeilen sichtbar; kein vollständiger neuer Gegnerkatalogtest. |
| Plausibler Sieger / Grund | PASS | Quik gewinnt bei Time limit; terminale Standings zeigen Quik 650 vor CWX 450. |
| Ergebnis im offenen F9 | PASS im Einzelbild | DEFEAT, Quik wins, eine Runde abgeschlossen, Rückkehr in10 s sichtbar. Kein Dauernachweis für Score-Freeze. |
| Bewusster Ergebnisbutton Return to menu | **OFFEN** | Autoexit-Commit 15:51:54.413 liegt 0,996 s vor dem erst 15:51:55.409 begonnenen Klick. Der Klick kann diesen bereits laufenden Ausstieg nicht als Buttonroute belegen. |
| Native Rückkehr / keine neue Exit-Exception | PASS für B | Autoexit und bestätigtes Hauptmenü, vollständiges abgegrenztes Logfenster ohne neue Exception. |
| Tatsächlicher SAIN-Uninitialized-Teardown | BEOBACHTET, einmal ohne neue Exception | L13349 um15:51:56.358 UTC; keine pauschale Garantie für alle Teardownvarianten. |
| Tatsächlicher Pickup-Cancel | OFFEN / nicht beobachtet | 0 Laufzeitmarker. |
| PMC-/Scav-Schutzfelder | PASS | 16/16 Vergleiche gleich, ganze Scav-Hälfte gleich; alleinige vollständige JSON-Nebenfeldänderung unten. |

[Killfeed](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-live-killfeed-view.jpg), [Standings während der Runde](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-ffa-scoreboard-mid.jpg), [terminales Ergebnis](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-terminal-result.jpg). Das terminale Original vom Archivmarker 15:51:44.869 UTC wurde unabhängig gelesen: Quik 3 Kills/0 Tode/1 Headshot/650 Punkte; CWX 2/1/1/450. Das passt zum geloggten Sieger. Die lokale Zeile endet mit 0 Kills/0 Toden/0 Punkten. Die nahezu gleichzeitige zweite Fläche derselben Zustandsabfrage beweist keinen Score-Freeze über die Ergebnisdauer.

### B – Ereignisse, Button-Grenze und Logs

| Ereignis | UTC | Quelle |
|---|---|---|
| Geschützter Arm / FFA eingefroren / nativer Start | 15:49:01.049 /15:49:01.750 /15:49:01.984 | Server L8418 / L8437 / L8439 |
| Session aktiv / Match gestartet | 15:49:35.370 /15:49:35.464 | Client L13130 / L13139 |
| Kit fertig / Runde 1 gestartet | 15:49:37.798 /15:49:37.869 | Client L13197 / L13199; 71 ms nach Kit |
| Runde 1: Quik wins — Time limit | 15:51:39.158 | Client L13315; 121,289 s Runden-Wanduhr |
| Terminalbild, Rückkehr in10 s | 15:51:44.869 Archivmarker | Originalb-terminal-result |
| Autoexit committed | 15:51:54.413 | Client L13328; 15,255 s nach Rundenergebnis |
| Verspäteter Return-to-menu-Klick → Rückgabe | 15:51:55.409 →15:51:56.381 | Native Eingabeaufzeichnung; erst nach Commit |
| Session beendet / Matchsumme | 15:51:55.898 /15:51:55.909 | Client L13344 / L13347; 0/0/0 |
| Native local-end / Snapshot restored / protected end persisted | 15:51:56.192 /15:51:56.202 /15:51:56.302 | Server L8714 / L8715 / L8718 |
| SAIN-Uninitialized-Teardown-Guard | 15:51:56.358 | Client L13349, einmal |
| Nativer Endscreen / Hauptmenü | 15:52:07.431 /15:53:10.433 Archivmarker | Originalbilder |

Der Versuch des [Return-to-menu-Klicks](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-return-to-menu-button.jpg) traf bereits die Ausstiegsphase. Der [native Endscreen](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-native-end-check.jpg) zeigte 2:20 und 389 EXP; anschließend wurde regulär zum [Hauptmenü](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/b-mainmenu-check.jpg) weitergeschaltet. Weder dieses Weiterklicken noch die native EXP-Anzeige beweist den noch offenen Ergebnisbutton oder gespeicherte Erfahrung.

Der komplette zweite Clientprozess ist bisL 13355 des B-Hauptmenüarchivs geprüft, einschließlich seines Startups. Der fortlaufende ServerabschnittL 8345–8791 schließt unmittelbar an den separat geprüften Resume-Startupabschnitt an. **0 Error/Fatal/Critical,0 neue ausgelöste Exceptions; keine neuen vollständigen Exception-Stacks auszulassen.** Client: 28 Warning-Zeilen, Serverdelta 0. Insbesondere kein BotItemTaker/CG_PickupAction-ArgumentException und kein SAIN-OnDisable-NRE. Der zunächst deferred raid-end ist durch den nativen local-end und das persistierte Protected-End aufgelöst. SAIN-Guard einmal; Pickup-Abbruch nicht beobachtet. [B-Logaudit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/B-log-audit.json), [82 geprüfte redigierte Originalzeilen](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/B-log-excerpts.txt).

Im **zweiten** Spielprozess stehen vier Input-State-Zeilen = zwei vollständige Paare; beide Released-Zeilen haben `blockedShotCommands=0`. Generation 22509: Pending 15:50:58.277, sample 2262 → Released 15:50:58.373, sample 2263. Generation 22510: Pending 15:51:54.429, sample 3358 → Released 15:51:54.526, sample 3359. Die Kappe 24 wurde nicht erreicht. Diese Marker sind keine zusätzlichen kontrollierten Munitionspaare. In B gibt es keinen natürlichen Tod und keinen Respawn-Timing-Marker.

### B – vollständiger Profilvergleich und Vorlauf

Private Baseline 15:48:25.338 UTC, Nachkopie 15:53:18.246 nach bestätigtem Hauptmenü. Inventory, Quests, Skills, Health einschließlich UpdateTime, Info.Experience, Info.Level, TaskConditionCounters und RepeatableQuests stimmen für PMC und Scav überein; Scav RepeatableQuests bleibt beidseits abwesend. Die ganze Scav-Hälfte ist identisch. Genau eine vollständige JSON-Differenz: `characters.pmc.Hideout.sptUpdateLastRunTimestamp`, **1789228048 →1789228348**, also **15:47:28 →15:52:28 UTC (+300 s)**. Der konkrete Schreiber ist durch Dateivergleich nicht identifiziert. Die 389 EXP des nativen Endscreens änderten Info.Experience nicht. [B-Profil-Gesamtaudit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/B-profile-audit.json).

Pausen-/Neustartänderungen bleiben getrennt von beiden Raidvergleichen: A-after → Resume-prestartup ändert allein den Hideout-Zeitstempel 15:22:28→15:45:28 UTC (+1380 s), alle Schutzfelder und die ganze Scav-Hälfte gleich. Resume-prestartup → B-before ändert PMC.Health.UpdateTime 15:11:30→15:46:30 sowie Hideout 15:45:28→15:47:28; sonstige Health-Werte bleiben gleich. Diese Änderungen liegen vor der B-Baseline und werden B nicht zugerechnet. [Pausengrenzen-Audit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/resume-boundary-profile-audit-r5.json).

## Offene r5-Unterfälle und native Rückstellung

| Verbleibender Fall | Abschlussstatus / Grund |
|---|---|
| A/B Score-Freeze über vollständige Ergebnisdauer, terminaler F9/Banner-Wechsel, Countdown bei Wechsel, Prep-Erhalt | OFFEN – die jeweils erforderlichen zeitlich getrennten/wechselnden Belege fehlen |
| B bewusster Return-to-menu-Ergebnisbutton | OFFEN – Autoexit 0,996 s vor Klickbeginn committed |
| C FFA/Endless/beide Limits Off, ca. 90 s ohne Rundenpause | OFFEN – wegen konservativer r5-Zeitgrenze nicht gestartet |
| C Give up, Respawn, normaler Disconnect, Exit-/Profilkontrolle | OFFEN – kein C-Raid |
| Normaler PMC-Menüweg bis vor Ready und zurück | OFFEN – Zeitgrenze; Charakterauswahl und Rückstellungsnavigation reichen nicht |
| Hideout/Weapon Builder, fremde Körperteile/Modelle | OFFEN – im r5-Auftrag nicht geöffnet |
| Tactical-Kaufmenü Close | OFFEN – in Deathmatch nicht erreichbar, kein Zusatzraid |
| Pickup-Cancel bei tatsächlich laufendem Pickup | OFFEN / nicht beobachtet – A und B je 0 Marker |

Die zwischenzeitlich offenen fünf eigenen Testwerte wurden **vollständig durch native Menübedienung zurückgestellt**. Der erste Versuch, Time Off bei noch endlicher Rundenzahl zu setzen, wurde nicht als Erfolg gewertet; erst nach Endless wurde Time Off wirksam übernommen und anschließend Gun Game wieder gewählt. Das ist die dokumentierte Rückstellungsreihenfolge, kein nachträglich behaupteter PASS für Continuous/Give up.

| Eigener Testwert | Abschließend wiederhergestellt |
|---|---|
| Match/GameMode | gungame |
| Deathmatch/Rounds | 0 / Endless |
| Deathmatch/KillLimit | 0 / Off |
| Deathmatch/TimeLimitSeconds | 0 / Off |
| Deathmatch/FreeForAll |true |

Abschlusskontrolle **15:56:33.994 UTC**: 147/147 geparste Arena-Werte stimmen mit der Baseline, keine verbleibende Wertänderung. Arena.cfg, RaidInfoPanels.cfg, Control.ini, Graphics.ini und Server-config.json sind bytegleich zur gesicherten Ausgangslage. Alle 33 gesicherten Arena-Assetdateien einschließlich Audio blieben unverändert. [Abgeschlossene native Rückstellung](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/settings-restored-final-r5.json), [Endless vor Time Off](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/restore-endless-first.jpg), [übernommenes Time Off](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/restore-time-off-after-endless.jpg), [Gun Game wiederhergestellt](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/restore-gungame.jpg).

`client-settings.json` ist im Rohhash verschieden: `E6B709DF09C26223C0C7A54DE1FD1066B87A1A3BEA2F4329933025C6E9A03DC1` → `C4767CB3330430BE7A9AC530484C45BFF7EC4B9CE77839CF189CCBA2FF9B0D5F`. Der unabhängige vollständige JSON-Vergleich findet jedoch **exakt eine Differenz: UpdatedAt, 16:19:36 → 17:55:56**. Das Feld enthält keine Zeitzone. Alle Entrywerte, Beschreibungen und sonstigen Metadaten stimmen; keine Behauptung einer vollständigen Bytegleichheit oder eines aus dem Vergleich identifizierten Schreibers. [Semantischer client-settings-Abschlussaudit](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/client-settings-final-audit-r5.json).

Die r5-Abschlussdatei von 15:57:02.931 UTC erfasst zwei Raids/vier Runden und abgeschlossene Rückstellung. **Spielprozess 10268, Launcher 4288 und Server 13660 bleiben für den getrennten autorisierten Refinementsauftrag geöffnet; es wird kein reguläres Programmende des B-Prozesses bescheinigt.** Nachfolgende Featureprüfungen und deren Profile/Settings gehören in einen eigenen Bericht und überschreiben diese r5-Abschlussbelege nicht. [r5-Abschlusszustand](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/r5-completed-state.json).

## Belege

[SHA256-Manifest](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/SHA256SUMS.txt) und [Bildindex mit UTC-Archivmarkern](../test-evidence/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/screenshots.json) benennen die 46 ausgewählten, unverändert kopierten Original-JPGs aus A, Fortsetzung/B und Rückstellung. Alle 46 Bildhashes wurden unabhängig gegen Index und private Originaldatei geprüft. Pro Zustandsabfrage ist die erste Fläche als Beleg ausgewählt; zweite nahezu gleichzeitige Flächen dienen nicht als zeitlicher Dauerbeweis. Abgeleitete JSON-/Textauszüge sind mit LF gespeichert; Bildbytes unverändert.

Rohlogs, vollständige Profile, Launcherbilder und private Fortsetzungsdaten liegen ausschließlich im ignorierten Lauf `artifacts/gpu-deathmatch-menu/deathmatch-menu-exit-r5/2026-09-12T15-08-44-047Z/`. Veröffentlichte gezielte Logauszüge haben private Kennungen redigiert. Der Bericht schließt den r5-Lauf nach zwei Raids und nativer Rückstellung ab; die ausdrücklich offenen Unterfälle und die getrennten Refinements erhalten daraus keine Freigabe.
