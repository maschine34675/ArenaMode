# Deathmatch-Menü und Arena-Charakter — GPU r2

**Ergebnis: Menüfunktionen teilweise bestanden; neuer Fehler beim Ablösen der Charaktervorschau. Vor dem ersten Raid gestoppt.** Build **deathmatch-menu-preview-r2**, Test am **2026-09-12**. Die korrigierte Beschreibung, vier Modi, endliche-Runden-Sperre und exakte Sonderwerte wurden nativ geprüft. Die gleiche Arena-Figur war auf Kartenwahl, Offline-Seite und Bestätigung sichtbar. Beim Verlassen von Kartenwahl und Offline-Seite lehnt Unity jedoch das Ablösen von `ArenaCharacterPreview` ab.

Der Abbruch folgt der ausdrücklichen Klausel **„Bei unklarem Risiko ebenfalls stoppen“** in der [r2-Anweisung](C:/SPT41/Development/ArenaMode/GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r2.md). Das abgewiesene Ablösen ist ein Laufzeitfehler der Vorschau, keine reine Text-/Abstandsabweichung. Seine Folgen beim Laden und Beenden sind ungeklärt. **Kein beobachteter Absturz, Softlock, Exception-Stack, Fehlerflut oder nachgewiesener Profilschaden.** Nach der Entscheidung wurden nur Belege gesichert und eigene Teständerungen regulär aufgeräumt. Keine pauschale Funktionsfreigabe; keine Codeänderung während des Tests.

## Teststand und Zeiten

Alle folgenden Zeiten UTC; Clientlog **MESZ/UTC+2**, Server-Tageslog **UTC**.

| Merkmal | Tatsächlicher Wert |
|---|---|
| Repository / Branch | C:/SPT41/Development/ArenaMode / feature/deathmatch-menu-preview |
| Implementierungscommit | 20669505459d90c212dc90806f24c12b3082870b |
| HEAD bei Beginn und vor Bericht | 60966bfc3e854a7667888acfa4d6cef7c25d50cd |
| Client SHA256, Start / vor geplantem C1 / Ende | **60C2685951C4ECD17E29C80192EE26AA7CF7735C723091C89E8162BFAF4C6551**, jeweils passend |
| Server SHA256, Start / vor geplantem C1 / Ende | **8F9A56F574835FEAB73AB78C2EF4D5CF5734F12ABE63F384E2148CF319890B73**, jeweils passend |
| Eigener Server | PID **12540**, Start **10:55:43.753** |
| Eigener Launcher | PID **10428**, Start **10:56:02.539** |
| Spiel | PID **12280**, Start **10:57:18.797**, nativ über Start Game; bei späterer Kontrolle dieselbe PID und Startzeit |
| Native Fensterbindung | EscapeFromTarkov, zurückgegebene Fenster-ID **3736726**; keine Eingaben in die BepInEx-Konsole |
| Auflösung / Bedienung | **1920×1080** Originalbilder; native Eingaben über Computer Use / sky; keine Grafikänderung |
| Profil | Vorhandenes separates BEAR-Testprofil; sichtbarer Spielername ArenaBEAR0908, Level 1 |
| Umfang | **0 von maximal 3 Raids**, **0 Minuten Raidzeit**; keine Ready-Betätigung in der Bestätigung |
| Vorprüfung | **10:55:26.176**, beide Komponenten zuvor gestoppt; private Ausgangssicherung |
| Menütestfenster | **10:59:08.779–11:10:28.766**, ca. **11 min 20 s**, einschließlich Diagnose-/Werkzeugzeit |
| Erster Fehler / Entscheidung | **11:08:05.625** / **11:10:28.766** |
| Aufräumen | Spielbeenden angefordert **11:12:28.724**, Launcherbeenden angefordert **11:12:53.162**, danach eigener Server per Ctrl+C |
| Abschlussvergleich | **11:13:17.155**; alle drei Prozesse anschließend nicht vorhanden |

Selbst das gesamte Zeitfenster von Vorprüfung bis Abschlussvergleich beträgt nur rund **17 min 51 s**. Der Test blieb unter 20 Minuten. Keine DLLs oder Profile zurückkopiert, keine Builds/Installationen, keine fremden Mods/Audio-Dateien geändert, keine direkte Arm-Ankündigung und keine Speicher-/Harmony-Instrumentierung. r1-Belege bleiben unverändert.

## Befund DM-MENU-R2-01: Unity lehnt Vorschau-Ablösung ab

**FAIL — Vorschau-Lebenszyklus, native Laufzeitbelege.** Reproduktion: frischer r2-Start → Arena → Factory Tag → Deathmatch **FFA / 1 Runde / Kills Off / 2 min** → Next zu Offline → Next zur Versicherung → Next zur Bestätigung. Versicherung ohne Änderung durchlaufen; kein Raidstart.

Originale Clientzeilen, lokale Zeit UTC+2:

`[13:08:05.625] [Error  : Unity Log] Cannot set the parent of the GameObject 'ArenaCharacterPreview' while activating or deactivating the parent GameObject 'Matchmaker Location Selection'.`

`[13:08:13.928] [Error  : Unity Log] Cannot set the parent of the GameObject 'ArenaCharacterPreview' while activating or deactivating the parent GameObject 'Matchmaker Offline Raid Screen'.`

Beim regulären Rückweg nach dem Stopp nochmals derselbe Fehler: **13:10:46.477** für Offline und **13:11:17.023** für Kartenwahl. Insgesamt vier isolierte Error-Einträge, jeweils an Seitenwechseln; keine Fehlerflut. [Unveränderte relevante Logzeilen](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/preview-errors.log).

Die ausgerüstete Figur bleibt sichtbar: [Kartenwahl, 11:07:57.984](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/c1-prestart-rules.jpg), [Offline, 11:08:06.557](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a5-offline.jpg), [Bestätigung, 11:08:22.077](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a5-confirmation-lobby.jpg). Helm, grüne Kleidung, Weste und Gewehr stimmen in diesen Ansichten überein; der echte PMC war vorher ohne Helm/Gewehr sichtbar. Auf Karten-/Offline-Seite steht die Vorschau kompakt unten rechts, auf der Bestätigung im Modellbereich. Bilder beweisen Sichtbarkeit und Ausrüstung, keine lückenlose Bewegungsruhe.

Unabhängige Read-only-Triage stützt als Ursache den Pfad `ArenaPreviewHostLifetime.OnDisable → SuspendHost → Suspend → ArenaCharacterPreview.Suspend → SetParent(null,false)`: das Ablösen läuft während der Deaktivierung des Elternobjekts. Siehe [ArenaCharacterPreview.cs:209](C:/SPT41/Development/ArenaMode/ArenaMode.Client/Session/ArenaCharacterPreview.cs:209) und [ArenaPreviewFlow.cs:271](C:/SPT41/Development/ArenaMode/ArenaMode.Client/Session/Preview/ArenaPreviewFlow.cs:271). Unity kann dies nur loggen, während die Verwaltung danach ihren Zustand freigibt. Ein späteres erneutes Anhängen passt zur sichtbaren Figur. **Diese technische Einordnung ersetzt keinen Lade-/Raidtest und beweist keinen Profileingriff.**

## Prüfmatrix

PASS gilt nur für die beschriebene native Beobachtung. OFFEN bedeutet ungetestet oder unzureichend belegt, nicht Funktionsfehler.

| Unterfall | Status | Beobachtung / Grenze |
|---|---|---|
| A1 — PMC, Scav, Arena auswählen | **PASS** | Markierung, native PMC-/Scav-Beschreibung und Modelle sichtbar; Arena-Figur ausgerüstet. [Scav](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a1-scav.jpg), [Arena](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a1-arena-description.jpg). |
| A1 — korrigierte Arena-Beschreibung | **PASS** | Exakt „Solo and team arena combat.“ / „Your profile and gear stay untouched.“; r1-Textfehler behoben. |
| A1 — Arena zurück auf PMC | **PASS, begrenzt** | Natives PMC-Modell/Standardbeschreibung wieder ausgewählt. [Rückwechsel](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a1-return-pmc.jpg). Kein zweiter Durchgang Arena → Scav: dieser Teil **OFFEN**. |
| A2 — genau vier Modi, keine separaten Endless/Rounds-Modi | **PASS** | Deathmatch, Gun Game, Tactical / 1v5, Team DM. [Kartenmenü](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a2-map-menu.jpg). |
| A2 — native untere Buttons frei | **PASS** | Auf Factory Kartenwahl Next/Map/Back/Ready räumlich frei; Next nativ benutzt. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/c1-prestart-rules.jpg). |
| A3 — Rounds, Kills/round, Time/round, FFA/All vs player | **PASS** | Alle sichtbar und geändert; FFA → All vs player → FFA; Runden 0 → 3 → 1; Kills 0 → 37 → 0; Zeit 0 → 120 → 75 → 120. |
| A3 — Limits Off sperren endliche Runden | **PASS** | Klick auf 3 bei beiden Limits Off bleibt Endless; nach 2-min-Limit lässt sich 3 wählen. [Gesperrt](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a3-finite-blocked-both-off.jpg), [3 gewählt](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a3-three-rounds.jpg). |
| A3 — Markierung und F12-Werte | **PASS für Stichproben** | F12 Deathmatch FreeForAll disabled, KillLimit 0, Rounds 3, TimeLimitSeconds 120 stimmen mit Menü überein. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a3-f12-deathmatch.jpg). |
| A3 — ungewöhnliche F12-Werte exakt, kein falsches Preset | **PASS** | Native Eingabe **37 / 75**, Menü **37 / 1:15**; bei Kill-/Zeitpresets keine Auswahlmarkierung. [F12](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a3-custom-f12.jpg), [Menü](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a3-custom-menu.jpg). |
| A3 — mehrfaches Zurück/Weiter mit Regelpersistenz | **OFFEN für volle Wiederholungsserie** | Ein Vorweg bis Bestätigung und regulärer Rückweg beobachtet; auf zurückgekehrter Karte C1-Regeln erhalten. Weitere Testzyklen nach Stopp nicht ausgeführt. |
| A4 — Gegnerauswahl, Hinweis und PLAY AREA ohne Überlappung | **PASS** | Gegnerzeile, Hinweis darunter und abgesetzter PLAY-AREA-Block vollständig. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/c1-prestart-rules.jpg). |
| A4 — Bereichsposition bei Moduswechsel | **PASS, begrenzt** | Vor Kartenwahl bei DM tiefer als bei GG/Tactical/TDM; beim Aufräumen DM → GG auf Factory ebenfalls angepasst. Exakte Serie DM → GG → TDM ohne Kartenwechsel **OFFEN**. |
| A4 — Tactical solo 1v5 / PitFireTeam-Hinweis | **PASS** | „TACTICAL / 1v5“, „1v5 / Solo“, Hinweis „5v5 requires PitFireTeam + 4 teammates.“ [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a4-tactical-solo.jpg). |
| A4 — reale PFT-5v5-Gruppe | **OFFEN** | Keine akzeptierte Vierergruppe vorhanden/verifiziert oder erzeugt. |
| A4 — Team-DM-Limits | **OFFEN für Bedienung** | Team kills 50 und Time limit 10 min mit Markierungen sichtbar, aber diese Limitbuttons nicht geändert. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a4-team-dm.jpg). |
| A4 — Customs-Bereichsmenü | **PASS** | Customs gewählt, „Customs contour (team starts)“ nativ aktiviert; 2v2–5v5-Auswahl und Hinweis erscheinen. [Vorher](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a4-customs-area.jpg), [Ausgewählter Bereich](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/a4-customs-contour.jpg). Kein Customs-Raid. |
| A5 — Modellkontinuität Karte / Offline / Bestätigung | **PASS nur für Sichtbarkeit/Ausrüstung** | Gleiche Helm-/Waffen-/Outfitkombination an den vorgesehenen Positionen. |
| A5 — sicherer Vorschau-Lebenszyklus | **FAIL** | DM-MENU-R2-01, natives Reparenting beim Seitenwechsel zurückgewiesen. |
| A5 — Ladescreen und zwei weitere Vor-/Zurückzyklen | **OFFEN** | Nach neuem Laufzeitbefund gestoppt, kein Ladescreen betreten. |
| A5 — Rückwechsel auf PMC ohne Reste | **PASS für einfachen Aufräum-Rückweg** | Kein zusätzliches Duplikat oder weiße Fläche in der Auswahl; PMC mit ursprünglicher Ausrüstung. [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/cleanup-pmc.jpg). Wiederholungsserie bleibt offen. |
| A5 — native PFT-Teamkollegen auf Ladescreen | **OFFEN** | Keine entsprechende Gruppe und kein Raidstart. |
| B — Jitter-/Framevergleich | **OFFEN / nicht erforderlich ausgelöst** | Nutzerbeobachtung „Zittern behoben“ wird übernommen; keine eigene Videoverbesserung behauptet. Keine erneute auffällige Sprungbewegung belegt; Screenshotposen allein sind kein Jitter-Nachweis. Keine Clips/Frames erstellt; vorhandene Recorder-Funktion in r2 nicht geprüft, keine Installation. |
| C1 — FFA, 1 Runde, Kills Off, Zeit 2 min | **OFFEN** | Regeln nativ vorbereitet, aber nicht gestartet. Timerbeginn, Bot-gegen-Bot einschließlich gleicher Fraktion, individuelle Scores, Sieger/Draw, Ergebnisfreeze und 15-s-Rückkehr ungetestet. |
| C1 — natürlich eigener Tod am Zeitlimit | **OFFEN** | Kein Raid, kein entsprechender Fall aufgetreten/erzwungen. |
| C2 — All vs player, 3 Runden, 10 Kills oder 2 min | **OFFEN** | Keine KI-/Runden-/Pausen-/Zähler-/Endergebnisprüfung; keine Aussage zu zusätzlicher Runde 4 oder Map-Neuladung. |
| C2 — F12-Änderung beeinflusst angekündigtes Match nicht | **OFFEN** | Sonderwerte nur im unarmed Menü gesetzt; kein Frozen-Rules-Nachweis. |
| C3 — optional Endless/FFA 90 s | **OFFEN** | Nicht gestartet. Keine Respawn-/Ausstiegs-/Langzeitaussage. |
| Waffenlaser Punkt / Strahl / Helligkeit | **OFFEN** | Kein Raid oder geeignetes Vergleichskit geprüft. |
| Nachtest — Hideout / Weapon Builder ohne Modellreste | **OFFEN** | Nach Stopp nicht geöffnet. |
| Nachtest — normaler PMC-Start und Abbruch ohne Arena-Einfluss | **OFFEN** | Kein normaler Raid gestartet. |
| Raidweiser Profilschutz | **OFFEN** | Null Raids. Menüvergleich unten ist kein raidweiser Schutztest. |

## Wiederherstellung, Profil und Logs

Eigene Menü-/F12-Änderungen regulär zurückgesetzt: **Deathmatch Rounds=0, KillLimit=0, TimeLimitSeconds=0, FreeForAll=true; GameMode=gungame; SkipPreRaidScreens=true; ExperimentalArea=full-map**. Die bereits vorhandene Migration blieb erhalten. Alle ArenaMode-Konfigurationswerte stimmen beim Abschluss mit dem Ausgang überein. Alle **147** Abschnitt/Schlüssel-Werte sind gleich. Der rohe CFG-Dateihash unterscheidet sich ausschließlich durch **acht Kommentarzeilen** mit akzeptablen Wertebereichen: Dezimalkommas wurden beim Speichern zu Dezimalpunkten. Keine dieser Differenzen betrifft einen Einstellungswert. RaidInfoPanels.cfg, Control.ini, Graphics.ini und Server-config.json sind bytegleich. Alle **33 zuvor inventarisierten Dateien unter BepInEx/plugins/ArenaMode** sind hashgleich; diese lokale Inventarliste ist nicht mit der 40-Dateien-Lieferprüfung aus dem Handoff gleichzusetzen.

Private Vorher-/Nachherprofile wurden ausschließlich gelesen. Inventory, Experience, Level, Quests, TaskConditionCounters, RepeatableQuests und Skills sind bei PMC/Scav gleich; Scav-RepeatableQuests fehlen auf beiden Seiten, Scav-Health ist gleich. **PMC-Health ist als vollständiges Objekt nicht wertgleich**: ausschließlich **UpdateTime 1789208714 → 1789210691** (**10:25:14 → 10:58:11 UTC**), also zeitlich vor der Arena-Auswahl. Sämtliche übrigen verschachtelten Gesundheitswerte einschließlich Körperteilen, Energie und Hydration sind gleich. Ohne Raid darf dies weder als erfolgreicher Arena-Schutztest noch als nachgewiesener Arena-Profilschaden ausgegeben werden. Die unmittelbar vor dem geplanten C1 angelegte Baseline wurde nicht als Raidpaar ausgewertet, da Start ausblieb.

[Abschlussprüfungen](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/final-checks.json) enthalten Feldvergleich, Datei- und DLL-Hashes. Vollständige private Profile und Rohlogs bleiben im ignorierten [Rohbelegverzeichnis](C:/SPT41/Development/ArenaMode/artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z). Original-Clientlog und kanonisches Server-Tageslog `spt20260912.log` liegen dort unter `logs/final/client.log` und `logs/final/server-current.log`; Originalhashes stehen im Manifest. Server-Tageslog nur ab eigenem Start **10:55:43.753 UTC** auswerten; frühere Tagesläufe sind keine r2-Belege.

Das aktuelle und gesicherte Clientlog sind bytegleich, mit **13.269 Zeilen** und genau den vier oben beschriebenen Unity-Errors. Keine Arena-Arm-/Session-/Match-/Local-Start-Markierungen und keine gefundenen geworfenen Exception-Zeilen. Im vollständigen eigenen Serverfenster ab **10:55:43.753 UTC** wurden **744 zeitgestempelte Zeilen** geprüft: keine Arena-Arm-/Native-Start-Markierungen und keine Error-/Fatal-/Critical-/Exception-Treffer. Die Logprüfung liefert keinen Hinweis auf einen Matchstart und ist kein Funktions-PASS.

## Belege und Commitumfang

[Belegmanifest](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r2/2026-09-12T10-55-26-176Z/manifest.json) mit **18 unveränderten Originalbildern**, Aufnahmezeiten und SHA256. Originaldateien wurden ohne Zuschneiden, Skalieren, Nachstellen oder Bildbearbeitung kopiert und per Hash abgeglichen. Keine Screenshotfolge als Videoersatz.

Nur dieser Bericht, diese gezielten Spielbilder, die vier unveränderten Fehlerzeilen und sachbezogene Prüfmetadaten werden committet. Keine Launcherbilder mit privater Profilkennung, vollständigen Profile, vollständigen Rohlogs oder Produktionsänderungen. Alle eigenen Prozesse regulär beendet; kein fremder Prozess beendet.
