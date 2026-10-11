# GPU-Nachtest r3 — 2026-09-11

Job-ID: `arena-panels-native-ui-20260911-r3`.

## Ergebnis: vor Spielstart blockiert

Der installierte Client entspricht nicht dem verbindlichen Hash aus `GPU-PANELS-TEST-INSTRUCTIONS.md`. Er entspricht stattdessen der im aktuellen HANDOFF beschriebenen neueren Lieferung für Killfeed-Lesbarkeit und Schnellzugriffsabstand. Der HANDOFF-Lieferabschnitt hält ausdrücklich fest, dass der r3-Auftrag auf seinem ursprünglichen Build bleibt und die neuere DLL diesem exakt gebundenen Testjob nicht stillschweigend zugeordnet werden darf.

Deshalb kein Spiel-, Launcher- oder Serverstart, keine Arena und kein normaler PMC-Raid. Keine Laufzeitbeobachtung aus r2 als neues Ergebnis übernommen. Dies ist ein dokumentierter Vorprüfungsblocker, kein Nachweis eines UI-Fehlers im installierten neueren Build.

## Tatsächlicher Teststand

Vorprüfung und Sicherung am 11.09.2026, ab 17:10:39 MESZ (UTC+2). Rohbeleg-Zeitstempel sind UTC. Keine neuen Client-/Server-Laufzeitlogs entstanden.

| Merkmal | Stand |
|---|---|
| Angeforderter Quellcommit | `d35d8785be236cf447a98b99e49f65d13e5e48dd` |
| Tatsächlicher Repository-HEAD vor Prüfung und Bericht | `46905467b714b24a89c19cd8fdcd7ec6e38ee42f` |
| Branch | `fix/arena-panel-native-ui` |
| Clientdatei | `C:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll` |
| Erwarteter Client-SHA256 | `218F176B15D007DB451893368EC754DDA5669F45C8E96B4F0EE1E67E9BA58C7C` |
| Tatsächlicher Client-SHA256 vor/nach Vorprüfung | `857206A4BF295607130B6F54A2A9CDECDB3F5A92B53784F9C644F7FC5199F1BB` — unverändert, abweichend |
| Serverdatei | `C:\SPT41\SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll` |
| Server-SHA256 vor/nach Vorprüfung | `6DB3E0D01F8F06F5194D54B219B24ACB4E5A7293609D7F89D7B4B583D0E3B473` — unverändert |
| Prozesse | Spiel, Launcher und Server zu Beginn und am Ende nicht vorhanden; keine fremden Prozesse beendet. |
| Grafik | Gespeicherte Spielauflösung 1920 × 1080; in diesem Versuch keine gerenderte Auflösung und kein Canvas-Wert gemessen. |
| Native Schnittstelle | Dokumentation und aktuelle Fensterliste geprüft; Abfrage erfolgreich, kein Spiel-/Launcherfenster vorhanden. Keine Behauptung einer gesperrten RDP-Sitzung oder fehlenden Computer-Use-Fähigkeit. |
| Profil | Vorhandenes separates BEAR-Testprofil bei gestopptem Spiel/Server lokal gesichert; in diesem Versuch wegen Build-Blocker kein Launcher-Aufruf, keine native Profilauswahl und kein Raid. |

Belegordner (Git-ignoriert, nur lokal):

`C:/SPT41/Development/ArenaMode/artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z`

Belege: [test-stand.json](../artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z/test-stand.json), [Einstellungsvergleich](../artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z/settings-comparison.json), [Eingabemöglichkeiten](../artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z/input-capabilities.json), [native Fensterprüfung](../artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z/preflight/native-window-check.json). Die bei der Prüfung gültigen [Anweisungen](../artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z/preflight/GPU-PANELS-TEST-INSTRUCTIONS.md) und der [HANDOFF-Lieferstand](../artifacts/gpu-panels/arena-panels-native-ui-20260911-r3/2026-09-11T15-10-39-445Z/preflight/HANDOFF.md) sind ebenfalls lokal gesichert.

## Prüffälle

| ID | Status | Beobachtung / verbleibende Grenze | Beleg |
|---|---|---|---|
| R01 | blockiert | Erster Arena-Start, sichtbare Panels sowie Unterdrückung von Exit-Liste und grünem Balken wegen abweichender Client-DLL nicht ausgeführt. Keine Startbilder. | `test-stand.json`, `clientMatches: false` |
| R02 | blockiert | Inventar zweimal, Pause, native Settings und HUD-Rückkehr nicht ausgeführt. Der r2-Inventarfehler wird weder als erneut reproduziert noch als behoben behauptet. Lootscreen ebenfalls nicht geprüft. | `test-stand.json`, `gameStarted: false` |
| R03 | blockiert | Keine neuen Schuss-, Nachlade-, Inventar-/HUD- oder Granatenvergleiche. Keine neue Kit-Abnahme. | `test-stand.json`, 0 Arena-Raids |
| R04 | blockiert | Kein Aufgeben/Respawn und kein neuer Körper-/Kitvergleich. Tod-/Killcam-Übergang bleibt ohne neuen Sichtbeleg. | `test-stand.json`, 0 Arena-Raids |
| R05 | blockiert | Keine Menürückkehr aus Arena und kein zweiter Arena-Start; keine Wiederholungsaufnahme von R02. | `test-stand.json`, 0 Arena-Raids |
| R06 | blockiert | Normaler PMC-Start, native Liste mit grünem Balken und einfacher O-Timer nicht ausgeführt. Optionaler tatsächlicher Extract-Countdown nicht ausgeführt; kein normaler PMC-Profilfortschritt durch diesen Versuch. | `test-stand.json`, 0 PMC-Raids |
| R07 | nicht ausgeführt | Die dokumentierte Tastaturfunktion bietet weiterhin nur einen Tastendruck bzw. Akkord, keinen zuverlässig zeitgesteuerten Doppel-O-Druck. Gespeicherte Belegung: WatchTime = O / Press, WatchTimerAndExits = O / DoubleClick. Keine langsamen Einzeldrücke als Doppel-O gewertet und keine Umbelegungsversuche. Zusätzlich verhindert der Build-Blocker jeden Raid. | `input-capabilities.json` |

Die Belegnamen in der Tabelle beziehen sich auf den oben angegebenen Belegordner. 6 Fälle blockiert, 1 nicht ausgeführt, keine bestandenen oder fehlgeschlagenen Laufzeitfälle. Es gibt keine neuen Screenshots oder Exception-Stacks, weil kein Spielprozess gestartet wurde. Panel-/Listen-/Header-Marker und CanvasGroup-Spam wurden für r3 nicht zur Laufzeit geprüft; alte Logs ersetzen diesen Nachweis nicht.

## Sicherung und Abschluss

Die fünf Einstellungsdateien und das vorhandene separate BEAR-Testprofil wurden bei gestopptem Spiel/Server lokal gesichert. Keine Einstellung absichtlich geändert und kein Profil bearbeitet. Der abschließende Vergleich bestätigt alle fünf Dateien sowie das gesicherte Testprofil bytegleich zur Vorprüfung:

- `BepInEx/config/com.maschine.ArenaMode.cfg`
- `BepInEx/config/com.maschine.RaidInfoPanels.cfg`
- `SPT_Runtime/user/sptSettings/Control.ini`
- `SPT_Runtime/user/sptSettings/Graphics.ini`
- `SPT_Runtime/user/mods/ArenaMode/config/config.json`

Pfade relativ zu `C:\SPT41`. Private Profilkopie bleibt im ignorierten Belegordner; keine Profilkennung oder privaten Rohdaten im Bericht. Da kein Raid stattfand, existieren keine Arena-Vorher-/Nachher-Raidvergleiche für diesen Versuch.

Client und Server blieben unverändert. Keine Prozesse zu beenden und keine Einstellungen zurückzustellen. Kein Build, DLL-Tausch, Mod-Update, Pull/Merge/Reset/Push oder Recovery-Eingriff; keine andere Task oder Automation gestartet.

Das Repository war vor der Dokumentation frei von fremden Änderungen. Nur dieser neue Bericht wird als Dokumentationscommit festgehalten. Die beiden früheren GPU-Berichte und HANDOFF bleiben unverändert, Rohbelege werden nicht committet.

## Voraussetzung für Fortsetzung

Der verbindliche Testauftrag und die installierte Client-DLL müssen wieder denselben Build bezeichnen: entweder aktualisierter Auftrag für die bereits installierte neuere Lieferung oder separat bereitgestellter, weiterhin angeforderter r3-Build. Dieser Testauftrag erlaubt keinen eigenständigen DLL-Tausch und keine stillschweigende Änderung seiner Hashbindung. Die offenen PFT-, Rucksack-/verschachtelten Granaten-, Auflösungs- und Übergangstests aus r2 bleiben unverändert offen.
