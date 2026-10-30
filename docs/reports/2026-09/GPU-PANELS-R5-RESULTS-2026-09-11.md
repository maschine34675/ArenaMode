# GPU-Nachtest r5: vollständiger Killfeed — 2026-09-11

Der eine geplante Arena-Raid wurde mit der vorgeschriebenen DLL durchgeführt und nach 00:09:49 regulär verlassen. K01 ist für die beobachteten Meldungen bestanden. Zwei gleichzeitige Einträge, Menüausblendung, Panelrückkehr und gemeinsames natürliches Verschwinden sind belegt. Eine vollständige Abnahme von K02–K04 ist nicht möglich: eigener Kill, tatsächlicher Textumbruch/Größenwechsel und der exakt noch lebende Feed beim Menüeintritt fehlen. Kein beobachteter Killfeed-Renderingfehler wird behauptet oder als Ursache anderer Modfehler angenommen.

## Tatsächlicher Teststand

| Merkmal | Geprüfter Stand |
|---|---|
| Job | arena-panels-native-ui-20260911-r5 |
| Installation / Repository | C:/SPT41 / C:/SPT41/Development/ArenaMode |
| Quellcommit des DLL-Builds | 3ea19773dc63fec23d2b0e83ebb0a52c4792c772 |
| Repository-HEAD vor Start und nach Test, vor Berichtscommit | 97d52ca0e49fc51a72d3b8158ef8fa8d4de0f8a8 |
| Branch | fix/arena-panel-native-ui |
| Client-SHA256 vor/nach | A9D9A0E5D60E2BC56D9E419F5284745857D910C16B8E51496B5CAFBE995028F9 |
| Server-SHA256 vor/nach | 6DB3E0D01F8F06F5194D54B219B24ACB4E5A7293609D7F89D7B4B583D0E3B473 |
| Native Darstellung | 1920 × 1080, Borderless; während dieses Durchlaufs unverändert |
| Separates Testprofil | Im nativen Launcher geprüft; sichtbarer Spielname ArenaBEAR0908. Kein Hauptprofil verwendet. |
| Raid | Endless Deathmatch, factory4_day, volle Karte, solo ohne PFT-Gruppe; vorhandenes Zufallskit Enforcer mit MP-153/AP-20 |

Clientdatei: C:/SPT41/BepInEx/plugins/maschine-ArenaMode.Client.dll. Serverdatei: C:/SPT41/SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll. Clientstart 19:05:56 MESZ; Matchstart laut Client 19:10:10.088 MESZ; Verlassen bestätigt 19:19:59 MESZ. Ergebnis: 0 Kills, 0 Deaths, Score 0, EXP 0, Left the Action. Der Ergebnisbildschirm nennt 00:09:49. Kein weiterer Raid.

Vor dem frischen Start wurden eigenes Spiel und Server beendet und eine konsistente private Sicherung angelegt. Eine zweite private Profilaufnahme unmittelbar vor Ready um 17:09:15.186 UTC ist die alleinige Arena-Vergleichsbasis. [Teststand](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/test-stand.json) und [Abschlussprüfung](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/verification-final.json).

Der frühere Startfehler und die vom Benutzer separat autorisierte Grafikreparatur liegen vor diesem Lauf. Dort war eine gespeicherte aktive 1×1-Fensterauflösung aufgefallen; nach der Reparatur war der Hauptmenüstart wieder möglich. Dieser r5-Durchlauf begann mit der danach neu gesicherten Grafikbasis und benötigte weder Alt+Enter noch eine Grafikdateiänderung. Frühere abgebrochene Vorbereitungen und Reparaturstarts zählen nicht als r5-Raidbelege.

## Ergebnisse je Testfall

| Fall | Status | Konkrete Beobachtung und Grenze |
|---|---|---|
| K01 Andere Kills | **Bestanden, beobachteter Umfang** | Vollständige Angreifer- und Opfernamen mit Pfeil, Rollen und Waffe separat darunter. Aufnahmen vor dunkler Geometrie, blauem Himmel und heller Mauer. Keine Ellipsis, abgeschnittene Unterzeile oder übergroße leere Fläche in diesen Meldungen. HS und Entfernung waren in den ausgewerteten Meldungen nicht vorhanden und sind damit nicht zusätzlich geprüft. |
| K02 Eigener Kill | **Blockiert** | Native Schüsse, Nachladen, kurze Bewegung und grobe Blickänderungen gelangen; kein echter Gegner wurde eliminiert. Kein eigener Killfeed-Eintrag, kein Entfernungsbeleg. Abschlussscore und Audioereignisse bestätigen 0 eigene Kills. K01 wird nicht auf lokale Kills übertragen. |
| K03 Umbruch und Größenwechsel | **Blockiert als Gesamtfall; Teilfälle bestanden** | Unterschiedliche Namens-/Waffenlängen und zwei gleichzeitige Einträge sind aufgenommen. Beide Flächen im Zweierstapel mit Abstand, vollständigen Texten und ohne Überlappung; später wieder einzelne kurze Meldungen. Kein Text erforderte sichtbar zusätzlichen Umbruch. Wachsende/schrumpfende Zeilenhöhe, langer ungetrennter Name und eindeutige Wiederverwendung derselben Poolzeile bleiben offen. |
| K04 Ablauf und Menü | **Blockiert für den exakten Menüeintritt; übrige Teilfälle bestanden** | Inventar und Pause ohne Arena-HUD/Score/Feed; nach längeren Aufenthalten korrekte Panelwerte und unveränderte Positionen. Alte Meldungen kehrten nicht zurück; neue Meldungen waren nach Rückkehr möglich. Natürliches gemeinsames Verblassen/Verschwinden ohne leeren Balken belegt. Zwischen letzter sichtbarer Feedaufnahme und Menüaufnahme lagen jedoch mehr als sechs Sekunden; ein noch lebender Eintrag im exakten Öffnungsmoment ist nicht nachgewiesen. Keine Freigabe dieses Übergangs. |
| K05 Eigener Tod / volle Belegung | **Nicht ausgeführt, nicht natürlich aufgetreten** | Kein natürlicher eigener Tod/Respawn und kein Stapel aus sechs Einträgen beobachtet. Eigenname als Opfer, YOU-Vermeidung, volle Belegung und Duplikatfreiheit nach Respawn bleiben offen. Reguläres Aufgeben ist kein eigener Killfeed-Todesbeleg. |
| Optionaler Granaten-Restbestand | **Bestanden in diesem Raid** | Zwei RGD-5 vor dem Wurf, danach eine im Panel und eine im Inventar, anschließend weiterhin eine im Panel. Kein Tod, Kitwechsel oder Nachfüllen dazwischen. |

### Sichtbare Identitäten und Stapel

- Dunkler Hintergrund, 17:10:56.633 UTC: FlashMode → bitwarrior; darunter USEC → BEAR · MP5. [Aufnahme](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K01-bright-scene-wait-2026-09-11T17-10-56-633Z-0.jpg). Der Dateiname nennt die insgesamt helle Szene; die Fläche direkt hinter dem Feed ist hier dunkel.
- Dunkler Hintergrund, 17:12:05.156 UTC: Priv_P → Жорес Шрек; darunter BEAR → Scav · AK-74M. [Aufnahme](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K02-view-towards-courtyard-2026-09-11T17-12-05-156Z-0.jpg).
- Heller Himmel, 17:18:19.438 UTC: Ростислав Провинциал → Bepis; darunter Scav → BEAR · MP5. Beide Namen vollständig und gut lesbar. [Aufnahme](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K01-K03-bright-second-3-2026-09-11T17-18-19-438Z-0.jpg).
- Zwei gleichzeitige Einträge vor heller Mauer, 17:19:21.073 UTC: beide zweizeilig auf getrennten dunklen Flächen, ohne Kontakt zum Waffenpanel oder Score. [Zweierstapel und Granatenrest](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/G05-return-with-one-grenade-2026-09-11T17-19-21-073Z-0.jpg).
- 17:19:28.224 UTC wieder ein einzelner Eintrag FlashMode → Andrudis, darunter USEC → BEAR · MP5. [Nachfolgende kurze Meldung](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K03-two-row-followup-0-2026-09-11T17-19-28-224Z-0.jpg). Das belegt die kleinere sichtbare Belegung, nicht die Identität einer wiederverwendeten Poolzeile.

### Menü- und Zeitbelege

Alle folgenden Zeiten sind Aufnahmezeiten in UTC; sie messen keine exakten Tastendrücke. Inventaraufnahme 17:11:09.714 → Rückkehr 17:11:29.704: rund 20 Sekunden. Pause 17:12:13.184 → Rückkehr 17:12:36.351: rund 23 Sekunden. Zweites Inventar 17:13:56.523 → Rückkehr 17:17:13.337: rund 197 Sekunden. Der lange zweite Aufenthalt lag innerhalb der gesamten Raidzeit.

Belege: [Inventar](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-inventory-with-feed-2026-09-11T17-11-09-714Z-0.jpg), [erste Rückkehr](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-inventory-return-after-six-seconds-2026-09-11T17-11-29-704Z-0.jpg), [Pause](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-pause-from-visible-feed-2026-09-11T17-12-13-184Z-0.jpg), [Pause-Rückkehr](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-pause-return-2026-09-11T17-12-36-351Z-0.jpg), [zweites Inventar](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-second-inventory-from-visible-feed-2026-09-11T17-13-56-523Z-0.jpg), [zweite Rückkehr](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-second-inventory-return-2026-09-11T17-17-13-337Z-0.jpg). In der zweiten Rückkehr steht eine andere, neue Meldung; die zuvor beobachtete alte Meldung fehlt.

Die vorherigen Feedaufnahmen liegen 13.081, 8.028 beziehungsweise 9.630 Sekunden vor den zugehörigen Menüaufnahmen. Deshalb sind die Dateinamen mit „with-feed“/„from-visible-feed“ als Testabsicht zu lesen, nicht als lückenloser Nachweis eines beim Öffnen lebenden Eintrags.

Natürliches Auslaufen: Doge → Priv_P mit BEAR → BEAR · PP-91-01 Kedr-B ist um 17:13:08.971 sichtbar, um 17:13:10.056 verblasst und um 17:13:11.111 samt Fläche verschwunden. [Sichtbar](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K03-passive-sequence-0-2026-09-11T17-13-08-971Z-0.jpg), [verblassend](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K03-passive-sequence-1-2026-09-11T17-13-10-056Z-0.jpg), [verschwunden](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K03-passive-sequence-2-2026-09-11T17-13-11-111Z-0.jpg). Da der Entstehungszeitpunkt nicht aufgenommen wurde, folgt daraus keine genaue 4,5-/1,5-Sekunden-Messung.

Panelrückkehr: MP-153/AP-20, 440/440 HP, Gewicht zunächst 30,0 kg; nach dem einzelnen Granatenwurf 29,6 kg. Schüsse und Nachladen änderten die Munition. Score blieb bei 0 Kills / 0 Deaths, an derselben Position. Keine Positions-/Lesbarkeitseinstellung verändert.

Granatenbelege: [zwei RGD-5 im Inventar vor dem Wurf](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/K04-second-inventory-from-visible-feed-2026-09-11T17-13-56-523Z-0.jpg), [Panel vor dem Wurf: 2](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/G01-grenade-equipped-2026-09-11T17-18-36-657Z-0.jpg), [Wurfanimation](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/G02-grenade-thrown-2026-09-11T17-18-45-671Z-0.jpg), [Panel nach dem Wurf: 1](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/G03-after-throw-settled-2026-09-11T17-18-51-888Z-0.jpg), [Inventarrest: eine RGD-5](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/G04-inventory-remainder-2026-09-11T17-19-00-643Z-0.jpg), [Panel nach Rückkehr: 1](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/G05-return-with-one-grenade-2026-09-11T17-19-21-073Z-0.jpg). Der Vergleich stützt sich auf den beruhigten Zustand nach der Wurfanimation.

## Regulärer Abschluss und Profilschutz

Über Pause → Disconnect → Confirm Leave verlassen; [Ergebnisbildschirm](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/exit-result-screen-2026-09-11T17-20-11-463Z-0.jpg). Anschließend [Hauptmenü ohne Arena-HUD-Reste](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/exit-main-menu-settled-2026-09-11T17-21-08-380Z-0.jpg). Die früher benannte Aufnahme „exit-main-menu-clean“ zeigt noch den Ladehintergrund und wird nicht als fertige Menürückkehr gewertet.

Die Profilprüfung vergleicht nur die vor diesem Arena-Raid gesicherte Testprofilbasis mit dem Zustand nach Raidende und erneut nach beendetem Spiel/Server. Objekt-Schlüssel wurden kanonisch sortiert, Arrayreihenfolgen beibehalten. Es wurden keine Profile editiert oder Sicherungen zurückkopiert.

| Geprüfter Bereich | PMC | Scav |
|---|---|---|
| Inventory einschließlich kompletter Itemstrukturen | Gleich | Gleich |
| Info.Experience und Info.Level | Gleich | Gleich |
| Quests | Gleich | Gleich |
| TaskConditionCounters | Gleich | Gleich |
| RepeatableQuests | Gleich | Vorher/nachher nicht vorhanden |
| Skills | Gleich | Gleich |
| Health | Gleich | Gleich |

Ergebnisdateien: [erster Vergleich nach Raidende](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/profile-comparison-after-menu.json) und [abschließender Vergleich bei beendetem Spiel/Server](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/profile-comparison-final.json). Der erste Vergleich entstand noch während der Rückkehr-Ladeanzeige; maßgeblich ist zusätzlich der abschließende Vergleich. Dies ist kein Gleichheitsnachweis des gesamten Profils und kein Test eines normalen PMC-Raids.

Serverbelege (UTC): 17:09:37.553 Snapshot angelegt; 17:09:38.629 geschützter nativer Start; 17:19:59.782 Restore wegen noch ausstehendem Raidresultat zunächst verschoben; 17:20:00.100 Snapshot wiederhergestellt; 17:20:00.262 geschütztes Arena-Ende abgeschlossen, gewöhnliche Nachverarbeitung übersprungen und Snapshot gespeichert. Die Clientwarnung „raid-end remains pending“ ist damit im anschließenden Serverablauf aufgelöst. [Redigierte Abschlussmarker](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/logs/final/server-profile-markers-redacted.log).

## Logs und getrennte Auffälligkeiten

Im vollständigen frischen Clientlauf drei Error-Einträge aus zwei SAIN-Ereignissen:

| Clientzeit MESZ | Befund | Einordnung |
|---|---|---|
| 19:10:35.565 und .566 | SAIN verwirft die Initialisierung eines PlayerScav-Bots mit zugewiesenem Killa-Brain. | Zwei zusammenhängende SAIN-Fehlermeldungen; kein Exception-Stack mitgeliefert. Natürliche Bot-Kills liefen weiter. |
| 19:20:00.377 | NullReferenceException in SAIN.Components.BotComponent.OnDisable während Unity/EFT-Poolabbau beim Raidende. | Vollständiger vorhandener Stack gesichert. Hauptmenü wurde anschließend erreicht; keine Killfeed-Ursache daraus abgeleitet. |

Keine Error-Einträge aus Arena-HUD/TMP, QuestTracker, RenderTextureDesc oder BotWeaponSelector in diesem frischen Lauf. Harmony-Patchregistrierungen mit Wörtern wie Exception, TMPro oder BotWeaponSelector wurden nicht als Fehler gezählt. Im zeitlich abgegrenzten aktuellen Serverlog keine Error-/Fatal-Einträge. Kein zusätzlicher Raid zur Wiederholung der SAIN-Auffälligkeiten und kein Fix während dieses Tests.

Belege: [vollständiger Clientlog](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/logs/final/client.log), [Fehler samt vorhandenen Stacks](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/logs/final/client-errors-with-stacks.log), [aktueller Serverlog](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/logs/final/server-current.log). Rohlogs bleiben lokal und werden nicht in Git aufgenommen.

Bei der Abschlussprüfung wurde eine anfängliche falsche Serverlog-Auswahl erkannt: Frühere Unterordner mit server.log enthalten wegen lexikalischer Auswahl eine ältere sync-conflict-Datei. Sie sind ausdrücklich keine Belege dieses Raids. Maßgeblich ist ausschließlich final/server-current.log aus spt20260911.log, abgegrenzt auf diesen Lauf ab 17:05:28 UTC; [Zuordnungshinweis](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/logs/README.txt). Die Profil- und Clientbelege sind davon unabhängig.

## Wiederhergestellter Zustand

GameMode wurde ausschließlich nativ von tactical auf endless und am Ende zurück auf tactical gestellt; [sichtbare Wiederherstellung](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/restore-mode-tactical-2026-09-11T17-21-51-770Z-0.jpg). HUD, andere Bot-Kills und vorhandene Panelpositionen waren bereits passend aktiviert. Alle ausgewerteten Arena-Konfigurationswerte entsprechen danach der frischen Ausgangsbasis. Der Dateihash änderte sich ausschließlich durch acht automatisch neu formatierte Kommentarzeilen mit Dezimalpunkt statt Dezimalkomma, nicht durch Werte.

| Datei | SHA256 vorher = nachher, sofern nicht anders genannt |
|---|---|
| ArenaMode.cfg | Vorher 1A551A2934E15B2FDEA60CCA38EC3C32AB40681E0F687CABB291B92F712295BC; nachher E09B9D0B69BE97DDE52C8B603A2FADE74DF5E334E0F1A6BCE02E06344F7ED73E; Wertegleichheit, nur Kommentare anders |
| RaidInfoPanels.cfg | 4F5C762376FA4641AB2597A1FD4E09F0E22D0A3189F39ED43B6AB5C80D1C6E7C |
| Control.ini | 94DC134D702261334C0288CEFC5E26ADEC0A32A1923B25693F6EEDEE4143EB79 |
| Graphics.ini | 9371D45FA602DAA194169511E1A56930D0D9760E5386814D14B229AB893C401F |
| Arena-Server-config.json | 5A6965111C80861D3F888205E62C1E07137E9365F48733D969B4C0B058F1B90F |

Graphics.ini bleibt aktiv 1920×1080, 16:9, Borderless (FullScreenMode 1); das bereits vorher gespeicherte inaktive Fenstermaß 1904×993/19:10 bleibt ebenfalls erhalten. Alle sechs geprüften Sound-/Lizenzdateien unverändert, einschließlich aller drei WAVs mit SHA256 DF59AAA66C8727F33EA6E5C6C3C80A5E0BD43418C4ABB3C24A931D9282F0FE54. Keine Aussage zur tatsächlichen Hitmarker-Wiedergabe: im Raid 0 eigene Treffer-/Audioereignisse.

Eigene Prozesse: Spiel PID 7556 regulär über Exit/Yes beendet, Launcher PID 6452 geschlossen, Server PID 9276 über seine eigene gestartete Sitzung beendet. [Abschließende Prozessprüfung](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/processes-final.json) enthält keine dieser Anwendungen. Keine fremden Prozesse beendet.

## Belegablage und verbleibende Grenzen

Rohbelegwurzel: C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/ . Darin Screenshotindex mit UTC-Zeit und Auflösung, private Sicherungen, Einstellungen vorher/nachher, Logs und Vergleiche. [Screenshotindex](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/screenshots/index.ndjson); [Zeit- und Abdeckungsgrenzen](C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/2026-09-11T17-05-28-205Z/timing-and-coverage.json). Die verlinkten Raidbilder stammen aus echten nativen 1920×1080-Aufnahmen. Mehrere Bilder desselben Zustands werden nicht als unabhängige Ereignisse gezählt.

Offen bleiben eigener Kill mit Eigenname und Distanz, eigener natürlicher Tod und Respawn, sechs gleichzeitige Einträge, echter Umbruch mit dynamischer Höhe, langer ungetrennter Name, eindeutige Pool-Wiederverwendung und der exakt noch lebende Feed beim Menüeintritt. Doppel-O, tatsächlicher Extract, räumliche grüne Exit-Wegweiser, PFT, weitere Auflösungen und normale PMC-Wiederherstellung wurden hier nicht erneut getestet; die begrenzten r4-Belege bleiben auf r4 beschränkt.

Kein Build, DLL-/Mod-/Soundtausch, Profil-JSON-Edit, Recovery-Eingriff, Pull/Merge/Reset/Push, weitere Tasks oder Automationen. Bestehende Berichte und HANDOFF bleiben unverändert. Das Repository war vor Erstellung dieses Berichts frei von Änderungen. Der Abschlusscommit umfasst ausschließlich diese neue Berichtsdatei; private Belege bleiben außerhalb des Commits.
