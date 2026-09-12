# Deathmatch-Menü und Arena-Charakter — GPU r1

**Ergebnis: nach einem neuen kosmetischen Textfehler gemäß Abbruchregel gestoppt. Keine Raids gestartet.** Getesteter Build: **deathmatch-menu-preview-r1**, am **2026-09-12**. Die native Arena-Auswahl zeigt weiterhin **„Five modes“**, obwohl der Auftrag für diesen Build vier Modi vorgibt. Dies ist ein Beschreibungsfehler, kein Nachweis eines defekten Spielmodus oder einer defekten Figurendarstellung.

Die Testanweisung verlangt ausdrücklich **„bei neuem Fehler Originalbelege sichern und stoppen“** ([Anweisung:28](C:/SPT41/Development/ArenaMode/GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r1.md:28)). Sie nimmt kosmetische Fehler nicht aus. Deshalb wurden nach dem Befund nur Originalbelege, Hash- und Abschlussvergleiche gesichert und die eigenen Prozesse regulär beendet. Keine Produktionscodeänderung oder Fehlerkorrektur während dieses Laufs.

## Tatsächlicher Stand

| Merkmal | Wert |
|---|---|
| Installation / Repository | C:/SPT41 / C:/SPT41/Development/ArenaMode |
| Branch | feature/deathmatch-menu-preview |
| Implementierungscommit | c8acf084b3eaf4e98455a70eacaa90ba56349207 |
| HEAD vor Start und vor Bericht | 578e8022c8f1ba8f6f73493fab3750cb82cd6821 |
| Client SHA256 vor Start, am Befund und nach Abschluss | **FBBF82C6993E16D3299B3081B8A53BD7770F3712CA3AB3E0A4B5F2D052A92D90** — jeweils exakt passend |
| Server SHA256 vor Start, am Befund und nach Abschluss | **8F9A56F574835FEAB73AB78C2EF4D5CF5734F12ABE63F384E2148CF319890B73** — jeweils exakt passend |
| Eigener Server | PID **2260**, Start **10:23:05.155 UTC** |
| Eigener Launcher | PID **10740**, Start **10:23:25.484 UTC** |
| Beobachteter Spielprozess | PID **7244**, Start **10:24:21.144 UTC**; diese PID wurde nach nativem Start erfasst. Einschränkung zur Logzuordnung siehe unten. |
| Nativer Spiel-Fensterbezug | EscapeFromTarkov, zurückgegebene Fenster-ID 1180804; keine Eingaben in die BepInEx-Konsole |
| Auflösung | Original-Spielaufnahmen **1920×1080**, Grafik- und Steuerungseinstellungen unverändert |
| Profil | Vorhandenes separates BEAR-Testprofil nativ im Launcher ausgewählt, sichtbarer Spielername ArenaBEAR0908, Level 1; kein Hauptprofil |
| Testumfang | **0 von maximal 3 Raids**, **0 Minuten aktive Raidzeit**; kurze Charakterauswahl, keine Kartenwahl oder Arm-Ankündigung |

Vor Start liefen weder Spiel noch Launcher noch Server. Konsistente private Ausgangssicherung bei gestoppten Prozessen. Beide Komponenten wurden frisch gestartet. Keine DLLs/Profile zurückkopiert, keine Mods entfernt und keine Audio-Dateien verändert. Alte r6-Ergebnisse werden nicht auf diesen Build übertragen.

## Zeitablauf

Alle Zeiten hier UTC; Client-Logzeiten sind MESZ/UTC+2, Server-Logzeiten UTC.

| Zeitpunkt | Beobachtung |
|---|---|
| 10:22:48.595 | Ausgangssicherungen und Teststand begonnen |
| 10:24:21.144 | Nativer Spielstart über Launcher; Prozessstart erfasst |
| 10:26:26.229 | Charakterauswahl geöffnet |
| 10:26:27.255 | PMC initial ausgewählt, natives PMC-Modell und Beschreibung sichtbar; Scav noch im Ladezustand |
| 10:26:37.969 | Scav nativ ausgewählt, Scav-Modell und Beschreibung sichtbar |
| **10:26:49.065** | **Arena nativ ausgewählt; Figur/Beschreibung sichtbar, falscher Text „Five modes“ festgestellt** |
| 10:27:34.887 | Unveränderte Fehleransicht als zusätzlicher Originalbeleg gesichert; keine weitere Testnavigation |
| 10:27:45.165 | Back ins Hauptmenü zur Bereinigung |
| 10:27:58.379 | Spielende über Exit → Yes angefordert |
| 10:28:33.618 | Eigener Launcher normal geschlossen; danach eigener Server über Ctrl+C in seiner Sitzung beendet |
| 10:29:43.399 | Hash-/Datei-/Profilabschlussvergleich gesichert; alle drei Prozesse anschließend nicht mehr vorhanden |

## Befund DM-MENU-R1-01 — falsche Modusanzahl

**FAIL, P3 / kosmetischer Inhalt.** Reproduktion im frisch gestarteten passenden Build: Hauptmenü → Escape from Tarkov → Arena. Die Beschreibung lautet:

> Five modes. Solo and team combat.
> Your profile and gear stay untouched.

Der neue Sollumfang ist laut [Anweisung:34](C:/SPT41/Development/ArenaMode/GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r1.md:34) **Deathmatch, Gun Game, Tactical, Team DM**. Die vier tatsächlichen Kartenwahl-Buttons wurden wegen des Stopps noch nicht nativ geprüft. Der Fehlerbeleg ist der sichtbare falsche Beschreibungstext; keine daraus abgeleitete Funktionsstörung der Auswahl oder Raids.

[Original bei Arena-Auswahl, 10:26:49.065 UTC](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/03-arena-stale-five-modes.jpg) und [zusätzlicher Originalbeleg, 10:27:34.887 UTC](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/04-arena-failure-confirmed.jpg). Beide vollständig und unverändert, 1920×1080. Keine Ausschnitte, Bildbearbeitung oder nachgestellten Inhalte.

Read-only-Triage bestätigt denselben veralteten Text im Nicht-PFT-Zweig von [ArenaSideSelectionPatch.cs:209](C:/SPT41/Development/ArenaMode/ArenaMode.Client/Session/ArenaSideSelectionPatch.cs:209). Die Quellstelle erklärt den beobachteten Text; sie ersetzt keine native Prüfung. Eine unabhängige read-only-Gegenprüfung ordnet den Befund ebenfalls als kosmetisch ein. Es wurde nichts korrigiert.

## Unterfälle

PASS bezieht sich ausschließlich auf die hier beschriebene Beobachtung. **OFFEN** bedeutet nicht ausgeführt bzw. nicht ausreichend belegt; es ist kein FAIL der jeweiligen Funktion.

| Unterfall | Status | Beobachtung / fehlender Nachweis |
|---|---|---|
| A1 — PMC initial sichtbar | **PASS, begrenzt** | Initial ausgewählter PMC mit eigenständigem Modell und nativer Beschreibung. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/01-pmc-initial.jpg). Kein Vorbuild-Vergleich. |
| A1 — Scav auswählen | **PASS, begrenzt** | Scav-Modell, Markierung und Standardbeschreibung nach nativem Klick sichtbar. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/02-scav-selected.jpg). |
| A1 — Arena auswählen, Beschreibung/Figur erhalten | **PASS für Sichtbarkeit** | Arena markiert; ausgerüstete Arena-Figur und zweizeilige Beschreibung sichtbar. |
| A1 — Arena-Beschreibung sachlich korrekt | **FAIL** | DM-MENU-R1-01: „Five modes“ statt der vorgegebenen vier Modi. |
| A1 — Rückwechsel von Arena auf PMC/Scav | **OFFEN** | Nach dem Fehler nicht mehr getestet. Initial sichtbare Modelle beweisen keine korrekte Rückschaltung. |
| A2 — exakt vier Modusbuttons, keine separaten Endless/Rounds, native untere Buttons frei | **OFFEN** | Kartenwahl wegen Abbruch nicht betreten. |
| A3 — Rundenzahl, Kills/Runde, Zeit/Runde, FFA/All vs player sichtbar und schaltbar | **OFFEN** | Nicht ausgeführt. |
| A3 — mehrfaches Wechseln, Zurück/Weiter, Markierung ↔ F12 | **OFFEN** | Nicht ausgeführt. |
| A3 — beide Limits Off verhindern endliche Rundenzahl; danach Limit und 3 Runden | **OFFEN** | Nicht ausgeführt. |
| A3 — ungewöhnliche F12-Werte 37 Kills / 75 Sekunden exakt beschriftet | **OFFEN** | Nicht gesetzt oder geprüft. |
| A4 — Gegnerauswahl, Hinweis und PLAY AREA ohne Überlappung | **OFFEN** | Nicht ausgeführt. |
| A4 — Deathmatch → Gun Game → Team DM ohne Kartenwechsel, Bereichsposition | **OFFEN** | Nicht ausgeführt. |
| A4 — Tactical 1vN und PFT-Hinweis | **OFFEN** | Nicht ausgeführt. |
| A4 — echtes PFT-5v5 mit akzeptierter Vierergruppe | **OFFEN** | Keine solche Gruppe verifiziert oder erzeugt; keine Mitspieler hinzugefügt. |
| A4 — TDM-Limits / Customs-Bereichsmenü | **OFFEN** | Nicht ausgeführt. |
| A5 — gleiche Arena-Figur über Kartenwahl, Offline-Einstellungen, Bestätigung, Laden | **OFFEN** | Nur Charakterauswahl gesehen. Kein Kontinuitätsnachweis zu Folgescreens. |
| A5 — wiederholtes Vor/Zurück, PMC-Rückwechsel ohne zweite Figur/weiße Fläche/Reste | **OFFEN** | Keine Wiederholungsserie nach dem Befund. |
| A5 — native PFT-Teamkollegen-Vorschauen | **OFFEN** | Keine passende Gruppe und kein Ladescreen geprüft. |
| B — unveränderte 8–10-s-Clips, Charakterauswahl und Bestätigung | **OFFEN** | Wegen Stopps vor Aufnahme nicht erstellt. NVIDIA App/Overlay lokal gefunden, aber nicht gestartet oder auf Aufnahmefunktion getestet. |
| B — je sechs aufeinanderfolgende Originalframes bei 2 und 5 s, Pose/IK/Kanten/UI-Landmarke | **OFFEN** | Keine Videos; außerdem ffmpeg/ffprobe nicht gefunden. Keine installierte Alternative über PyAV/OpenCV/imageio_ffmpeg/moviepy im geprüften Python-Runtime. Keine Screenshotfolge als Videoersatz und keine künstlichen Zwischenbilder. |
| B — Vergleich/Verbesserung gegenüber r6 | **OFFEN** | Kein geeignetes Vorher-/Nachher-Video; keine Verbesserung oder Jitter-Ursache behauptet. |
| C1 — FFA, 1 Runde, Kills Off, Zeit 2 min | **OFFEN** | Kein Raid: Aufbau-/Timerstart, gegenseitige Botfeindschaft einschließlich gleicher Fraktion, individuelle Scores, Sieg/Unentschieden, eingefrorenes Ergebnis und sichere Rückkehr sämtlich ungetestet. |
| C1 — Limit während natürlichen eigenen Todes | **OFFEN** | Nicht aufgetreten, nicht erzwungen. |
| C2 — All vs player, 3 Runden, 10 Kills oder 2 min | **OFFEN** | Kein Raid: Feindschaft, Zielverfehlung, Pausen, frische Rundenzähler, genau ein Ergebnis nach Runde 3 und ausbleibende Runde 4 ungetestet. |
| C2 — F12-Änderung während Laden/Match ändert angekündigte Regeln nicht | **OFFEN** | Keine Regeländerung, keine Ankündigung und kein Match. |
| C3 — optional Endless/FFA, beide Limits Off, ca. 90 s | **OFFEN** | Nicht gestartet; keine Aussage zu Respawn, Fehlerwachstum oder Langzeitbetrieb. |
| Nachtest — Hideout/Weapon Builder ohne Modellreste | **OFFEN** | Wegen Stopp nicht geöffnet. Hauptmenü wurde nur zum regulären Beenden benutzt. |
| Nachtest — normaler PMC-Start und Abbruch ohne Arena-Einflüsse | **OFFEN** | Nicht ausgeführt. |
| Raidweiser Profilschutz | **OFFEN** | Kein Raid. Menü-Vorher/Nachher unten ist ausdrücklich kein Ersatz für eine Arena-Profilabnahme. |

## Dateien, Einstellungen und Profil

**Client und Server passen auch am Befund und abschließend exakt.** [Startstand](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/test-stand.json), [Hashprüfung am Befund](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/hash-failure.json), [Hashprüfung nach Ende](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/hash-final-stopped.json).

Alle **33 erfassten Dateien unter BepInEx/plugins/ArenaMode** sind unverändert, ohne neue oder fehlende Dateien. Die Zählung bezeichnet genau dieses lokale Inventar; sie wird nicht mit der breiteren 39-Dateien-Lieferprüfung aus dem Handoff gleichgesetzt. Audio-Auswahl und Audio-Dateien wurden nicht verändert. RaidInfoPanels.cfg, Control.ini, Graphics.ini und Server-config.json sind bytegleich zur Ausgangssicherung.

ArenaMode.cfg hat beim frischen Laden fünf neue Werte hinzugefügt: **Deathmatch/Rounds=0, KillLimit=0, TimeLimitSeconds=0, FreeForAll=true, LegacyRulesMigrated=true**. Vorhandene Werte blieben gleich; keine eigenen F12-/Regeländerungen vorgenommen. Die automatische Migration wurde dokumentiert und erhalten, nicht durch Rückkopieren der alten Datei entfernt. Dies ist eine Dateibeobachtung und noch kein PASS für die Bedienung dieser Werte.

Die private Menü-Vorher-/Nachhersicherung zeigt für PMC und Scav gleiche **Inventory, Info.Experience, Info.Level, Quests, TaskConditionCounters, RepeatableQuests und Skills**; Scav-RepeatableQuests fehlen beidseits. Scav-Health ist ebenfalls gleich. Bei **PMC-Health änderte sich ausschließlich UpdateTime** von **1789195580 auf 1789208714** (06:46:20 → 10:25:14 UTC), sämtliche übrigen verschachtelten Health-Werte blieben gleich. Deshalb wird nicht behauptet, das vollständige Health-Objekt oder das Gesamtprofil seien unverändert. Ohne Raid ist dies kein Nachweis eines Arena-Schutzfehlers und kein bestandener raidweiser Schutztest.

[Abschlussvergleich mit vollständigen Feldresultaten](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/final-comparison.json). Vollständige private Profile und Einstellungen liegen ausschließlich im ignorierten Rohbelegverzeichnis, nicht im Commit. Kein Profil wurde editiert oder zurückkopiert.

## Logs und Grenzen der Zuordnung

Originale Client-/Serverlogs liegen unter [Rohbelegverzeichnis](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/logs/final-stopped); Serverdatei ist das kanonische Tageslog **spt20260912.log**. Für Serverfehler wurde der Zeitraum ab eigenem Serverstart **10:23:05.155 UTC** ausgewertet, nicht frühere Sitzungen des Tages. Clientzeiten sind lokal UTC+2.

Relevante Original-Clientzeilen:

`[12:24:42.172] [Info   :maschine-ArenaMode] maschine-ArenaMode 0.1.0 loaded (Redeploy.Core 0.9.0.0)`

`[12:26:05.424] [Info   :maschine-ArenaMode] maschine-ArenaMode 0.1.0 loaded (Redeploy.Core 0.9.0.0)`

`[12:26:54.809] [Info   :maschine-ArenaMode] Arena character preview ready: side=Usec; separate equipment/camera; camera=(128.62, -128.60, 128.96); body=(128.00, -129.00, 133.00); visibleBounds=51/51.`

**Offene Logzuordnung:** Das Original-Clientlog enthält zwei unterschiedliche Startblöcke: LogTools/BepInEx um **12:24:29.862 lokal** (Zeile 1) und erneut um **12:26:00.918** (Zeile 12940); Chainloader-Abschluss um **12:24:56.952** bzw. **12:26:11.424**. Es handelt sich nicht um identische wiederholte Zeilen. Der genaue Mechanismus — erneute Initialisierung oder Zusammenführung/Wiedergabe von Logs — ist damit nicht geklärt. Die Datei hat keine ausreichende PID-Zuordnung, um einen zweiten Betriebssystemprozess nachzuweisen. Dokumentiert wird die tatsächlich erfasste Spiel-PID; es wird weder ein zusätzlicher absichtlicher Neustart behauptet noch eine einzige Initialisierung garantiert. Die Testbedienung hat Start Game genau einmal ausgelöst. Die sichtbaren Belege bleiben eigenständige native Aufnahmen, die DLL-Dateihashes stimmen an allen Kontrollpunkten.

Keine Error-/Fatal-Einträge oder tatsächlich geworfenen Exception-Zeilen im gesicherten Clientlog gefunden; keine Error-/Critical-/Fatal-Einträge im abgegrenzten Serverzeitraum. **49 Client-Warnungen** sind im vollständigen Original erhalten. Kein Arena-Session-/Matchstart gefunden. Die Warnungsfreiheit hinsichtlich Exceptions ist kein Funktionsbeweis; der Textfehler braucht keinen Exception-Stack. [Logauswertung](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/log-audit.json).

## Ablage und Commitumfang

Unveränderte, gezielt ausgewählte Originalbilder sowie Build-/Zeit-/Bildhash-Metadaten: [Belegmanifest](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/manifest.json). Nur diese vier Spielbilder, das Manifest und dieser Bericht sind für den Commit vorgesehen. Keine Launcherbilder mit privaten Profilkennungen, vollständigen Profile oder Rohlogs werden committet.

Weitere lokale Rohbelege: `C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r1/2026-09-12T10-22-48-595Z/`. Frühere Berichte/Belege bleiben unverändert. Keine Builds, Pushes, Produktionsänderungen, Profilmanipulation, Harmony-/Speicherinstrumentierung oder direkte Server-Arm-Ankündigung. Eigene Prozesse regulär beendet, keine fremden Prozesse beendet.
