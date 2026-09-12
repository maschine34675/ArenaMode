# Vorschau-Lebenszyklus und Deathmatch — GPU r3

**Ergebnis: Vorschau-Nachtest A bestanden; zwei Raids durchgeführt; Gesamtfreigabe FAIL wegen drei neuer Exceptions beim Ausstieg aus C2.** Der bisherige Reparenting-Fehler aus r2 trat in den beiden vollständigen Menüzyklen und den geprüften Lade-/Raidwegen nicht erneut auf. C1 endete nach Zeitlimit mit passendem FFA-Sieger. C2 lief durch drei Runden; eine native F12-Änderung von 120 auf 60 Sekunden verkürzte die laufende dritte Runde nicht. Beim anschließenden automatischen Ausstieg entstanden jedoch eine ArgumentException und zwei NullReferenceExceptions. Zusätzlich ist die überlagerte C1-Ergebnisdarstellung bei geöffnetem F9 ein kosmetischer FAIL.

Der Lauf wurde beim Erkennen der neuen Exceptions um **12:48:50.386 UTC** gemäß [r3-Anweisung](C:/SPT41/Development/ArenaMode/GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r3.md) vollständig gestoppt. Danach ausschließlich reguläre Rückkehr, Profilvergleich, Wiederherstellung eigener Testwerte und Beweissicherung. Kein weiterer Raid, kein Hideout-/PMC-Nachtest. Kein beobachteter Absturz oder Softlock; kein nachgewiesener persistierter Profilschaden. Die Fehlerorte allein beweisen nicht die endgültige Ursache.

## Teststand und Abgrenzung

Testdatum **12.09.2026**. Alle Bericht-/Bildzeiten **UTC**; Clientlog **lokal MESZ = UTC+2**, Serverlog **UTC**. Native Bedienung und Originalaufnahmen über Computer Use / sky, **1920 × 1080**, Grafik-/Steuerungseinstellungen unverändert. Keine neue Videoabnahme; vorhandene Nutzerbeobachtung „Zittern behoben“ bleibt bestehen.

| Merkmal | Tatsächlicher Stand |
|---|---|
| Build-ID / Branch | deathmatch-menu-preview-r3 / feature/deathmatch-menu-preview |
| HEAD bei Vorprüfung 12:22:55.448 | fa438b5fa6f3ec7f3b0fb71f58bc967208f65bb2 |
| Client-Implementierung | 3010346e23f802ae40d58669d12b4327fa86b1fe |
| Server-Quellstand | 939d56454e7479a7d49f2ac9a03f475c02d59a29 |
| Eingebettete Revision beider DLLs | f887090f5b04569a6f35fd19bf8a3ac63c25e0e2 |
| Client-SHA256 | **4F33B337108987C5471E42FDD706F57D61421CB05D9AAD25186A592366130473** |
| Server-SHA256, installierter Debugbuild | **294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7** |
| Hashprüfungen, jeweils beide passend | 12:23:42.074; vor C1 12:33:17.736; vor C2 12:40:25.978; nach Stopp 12:49:50.273; Ende 12:53:36.581 |
| Frischer Server | PID **3644**, Start **12:23:50.4389074** |
| Eigener Launcher | PID **12864**, Start **12:24:00.4610416** |
| Frisches Spiel | PID **12460**, Start **12:25:17.9513515**, nativ über Launcher gestartet |
| Profil | Vorhandenes separates BEAR-Testprofil, sichtbarer Name ArenaBEAR0908, Level 1 |
| Raidanzahl | **2 von maximal 3**, C1 und C2; C3 nicht gestartet |
| Umfang der Live-Runden | C1 eine FFA-Runde; C2 drei All-vs-player-Runden |
| Ende eigener Prozesse | Spielende angefordert 12:52:52.600; Launcher 12:54:11.228; eigener Server per Ctrl+C; alle drei um 12:54:28 nicht mehr vorhanden |

Die eingebettete Revision wurde wie angewiesen nicht als Anlass für Neubau oder DLL-Tausch verwendet. **Kein Build, keine Profilbearbeitung/-rückkopie, keine direkte Arm-Ankündigung, keine Speicher-/Harmony-Instrumentierung und keine erzwungenen Matchereignisse.** Audio und fremde Mods wurden nicht verändert.

Während des Laufs erschienen fremde Entwicklungsänderungen und neue Commits im gemeinsamen Checkout. HEAD bei Abschlusskontrolle war **0be7f7aa5473625fe82cc4d63dd475bd5217d6a4**, mit weiteren fremden uncommitteten Änderungen; diese wurden nicht zurückgesetzt oder in den Testbericht-Commit aufgenommen. Die installierten DLLs blieben bei allen Hashmarkern auf dem oben gebundenen Stand. Diese Abnahme gilt ausdrücklich nicht für die inzwischen weiterentwickelten Quellen. Bereits bei Beginn vorhanden: fremde ungetrackte Datei ArenaMode.Server/defaults/~syncthing~ladder.json.tmp. [Abschlussnachweise](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/final-checks.json).

### Zeitbudget

Nach Beobachtungs- und Logmarkern: **16 min 18,869 s Test-/Bedienfenster**, zusätzlich **3 min 02,232 s** für eigene Einstellungen und reguläres Spielende, zusammen **19 min 21,101 s**. Darin sind sämtliche Werkzeug-, Kontroll- und Kontextpausen während der laufenden Runden sowie der übrigen erfassten Menüfenster konservativ enthalten. **4 min 19,033 s als Lade-/Rückkehrwartefenster eingeordnete Abschnitte** sind getrennt erfasst. Die Eingaben in F12 während des C2-Ladens liegen im Ladefenster; die längere spätere F12-Reparatur liegt vollständig im gezählten Livefenster.

**Zeitbudget-Nachweis: OFFEN in sekundengenauer Abgrenzung.** Dies ist eine Bilanz der protokollierten Zustandsfenster, keine gesicherte Obergrenze der aktiven Zeit und keine reine Eingabe-Stoppuhr. Die Ladegrenzen sind teilweise Beobachtungsgrenzen: Beispielsweise ist C1 von 12:36:42.421 bis zur ersten beobachteten Raid-Ende-Seite um 12:37:49.115 eingeordnet; die Seite kann schon früher fertig gewesen sein. Reine Ladezeit und passive Wartezeit auf einer bereits fertigen Seite sind nicht unabhängig getrennt. **Aus 19:21.101 folgt deshalb kein sekundengenau nachgewiesener Unter-20-Minuten-PASS.** Eine vollständige Netto-Werkzeugzeit wurde nicht unabhängig gemessen und nicht zusätzlich vom aktiven Budget abgezogen. Vorprüfung, reine Dateiauswertung, Bericht und Commit zählen nicht als aktive GPU-Prüfung. C3 blieb bereits wegen des knappen Restbudgets vorgesehen offen und entfiel endgültig durch den Exception-Stopp. [Zeitbilanz mit Intervallen](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/timing.json).

## A — wiederholte Seitenwechsel

**PASS im beobachteten Umfang.** Zwei vollständige Wege Karte → Offline → Versicherung → Bestätigung → Versicherung → Offline → Karte. Auf Karte und Offline stand die kompakte Figur unten rechts, auf der Bestätigung dieselbe grüne Ausrüstung mit Helm, Weste und Waffe im Modellbereich. Auf Versicherung erwartungsgemäß keine Arena-Figur. Keine weiße Fläche, doppelte Figur, verdeckten Buttons oder Umstellung auf echte PMC-Ausrüstung beobachtet.

| Unterfall | Status | Beobachtung |
|---|---|---|
| A1: Factory Tag / Deathmatch / FFA / 1 Runde / Kills Off / 2 min | PASS | Regeln um 12:30:30.233 bestätigt. SkipPreRaidScreens nativ true → false für beide Wege. |
| A2: Vorschauplatzierung und Fehlerprüfung je Seite | PASS | Alle zwölf Vor-/Zurück-Seitenwechsel mit frischer Logdelta-Prüfung; keine Reparenting-/Preview-/CameraImage-/PlayerModelLoader-Exception. |
| A3: Wiederholter, zügiger Weg und Regel-/Outfit-Erhalt | PASS | Zyklus 1 12:30:30.233–12:31:13.760; Zyklus 2 12:31:21.289–12:31:55.447, anschließend Regeln und Outfit gleich. Zweiter Weg ca. 6–8 s je Schritt einschließlich Werkzeug/Kontrolle. |
| A4: PMC, Scav, Arena erneut | PASS | [PMC 12:32:14.284](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/after-cycles-pmc-2026-09-12T12-32-14-284Z-0.jpg) nativ unbewaffnet; [Scav 12:32:22.752](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/after-cycles-scav-2026-09-12T12-32-22-752Z-0.jpg) mit eigenem Modell; [Arena 12:32:31.322](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-arena-reselected-2026-09-12T12-32-31-322Z-0.jpg) erneut korrekt. Separate Arena-Auswahlfigur auf der Charakterseite ist beabsichtigt. |
| A4: Bereits akzeptierte Regeln/Labels kurz bestätigen | PASS, begrenzt | Vier Modi, Beschreibung sowie C1-Regeln kurz bestätigt; [erneute C1-Regeln 12:32:49.978](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-factory-day-rules-2026-09-12T12-32-49-978Z-0.jpg). Kein erneuter Sonderwerte-Test 37/75. |
| Kontinuierlicher Jitter-/Rotationsnachweis | OFFEN als neue Videoabnahme | Nur Einzelbeobachtungen und Originalbilder; kein Video und keine Ableitung von Bewegungsruhe aus Screenshots. |

| Originalzustand | UTC | Beleg |
|---|---|---|
| cycle1-map-rules-day | 12:30:30.233 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-map-rules-day-2026-09-12T12-30-30-233Z-0.jpg) |
| cycle1-offline | 12:30:31.127 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-offline-2026-09-12T12-30-31-127Z-0.jpg) |
| cycle1-insurance | 12:30:40.945 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-insurance-2026-09-12T12-30-40-945Z-0.jpg) |
| cycle1-confirmation | 12:30:48.251 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-confirmation-2026-09-12T12-30-48-251Z-0.jpg) |
| cycle1-back-insurance | 12:30:59.727 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-back-insurance-2026-09-12T12-30-59-727Z-0.jpg) |
| cycle1-back-offline | 12:31:07.474 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-back-offline-2026-09-12T12-31-07-474Z-0.jpg) |
| cycle1-back-map | 12:31:13.760 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle1-back-map-2026-09-12T12-31-13-760Z-0.jpg) |
| cycle2-offline | 12:31:21.289 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle2-offline-2026-09-12T12-31-21-289Z-0.jpg) |
| cycle2-insurance | 12:31:28.034 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle2-insurance-2026-09-12T12-31-28-034Z-0.jpg) |
| cycle2-confirmation | 12:31:35.071 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle2-confirmation-2026-09-12T12-31-35-071Z-0.jpg) |
| cycle2-back-insurance | 12:31:41.883 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle2-back-insurance-2026-09-12T12-31-41-883Z-0.jpg) |
| cycle2-back-offline | 12:31:48.825 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle2-back-offline-2026-09-12T12-31-48-825Z-0.jpg) |
| cycle2-back-map | 12:31:55.447 | [Original](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/cycle2-back-map-2026-09-12T12-31-55-447Z-0.jpg) |

Die zwölf Seitenwechsel wurden über Markerstände 12982–13002 des frischen Clientlogs geprüft, danach die Charakterwechsel/C1-Vorbereitung bis 13014. Alle zugehörigen Fehlerlisten leer. [Sequentielle Logmarker](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/transition-log-checks.json). Relevante Originalzeilen aus dem C1-Archiv, Clientzeit UTC+2:

~~~text
12984: [14:30:00.339] [Info   :maschine-ArenaMode] Arena mode selected in map screen: deathmatch.
12995: [14:31:13.046] [Info   :maschine-ArenaMode] Arena map view: buttons=4, visible=3, sharedContainer=True, mode=deathmatch
13002: [14:31:54.724] [Info   :maschine-ArenaMode] Arena map view: buttons=4, visible=3, sharedContainer=True, mode=deathmatch
13004: [14:32:03.901] [Info   :maschine-ArenaMode] Arena character preview ready: side=Bear; separate equipment/camera; camera=(128.62, -128.60, 128.96); body=(128.00, -129.00, 133.00); visibleBounds=47/47.
~~~

Der interne Logwert visible=3 ist keine visuelle Modizählung; diese beruht auf der tatsächlichen Menüansicht.

## C1 — FFA, eine Runde, Kills Off, 2 Minuten

Native Ready-Betätigung **12:33:29.593**. Kit-Swap fertig **12:34:26.145**, Rundentimer gestartet **12:34:26.199**, also 54 ms danach. Lokaler Endstand **0 Kills / 0 Tode / 0 Punkte**.

| Unterfall | Status | Tatsächliche Beobachtung / Grenze |
|---|---|---|
| Gleiche Arena-Figur beim Laden | PASS, beobachtete Ansichten | [Laden 12:33:31.207](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-ready-2026-09-12T12-33-31-207Z-0.jpg), [12:33:41.040](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-loading-deliberate-rotation-2026-09-12T12-33-41-040Z-0.jpg) und [12:34:19.112](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-loading-progress-2026-09-12T12-34-19-112Z-0.jpg): gleiche Ausrüstung, keine echte PMC-Ausrüstung, Duplikate oder weiße Fläche. |
| Einmal bewusst drehen / keine Dauerrotation | OFFEN | Ein nativer Drag über die Ladefigur wurde versucht; eine eindeutige Drehung wurde nicht belegt. Keine Dauerrotation in den Einzelbeobachtungen gesehen. |
| Timer nach initialem Kit-/Spieleraufbau | PASS mit HUD und Log | Erstes [Livebild 12:34:45.351](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-loading-kit-2026-09-12T12-34-45-351Z-0.jpg) zeigt AKM-Kit und ca. 00:19 / 1:41. Die exakte Startsekunde ist durch die Original-Logreihenfolge belegt, nicht durch ein Bild bei 00:00. |
| Bot-Abschüsse untereinander, einschließlich gleicher Fraktion | PASS für Killfeed-Ereignisse | Beobachtet: reider123 → LunnayaLuna, BEAR → USEC; später [igorir3 → DanCam38, USEC → USEC](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-usec-vs-usec-killfeed-2026-09-12T12-35-31-931Z-0.jpg), 1 m, MP5K-N. Letzteres Bild archiviert 12:35:31.931 einen kurz zuvor zurückgegebenen Zustand; kein exakter Abschusszeitstempel daraus. |
| Direkt beobachteter Bot-Schusswechsel / allgemeine Feindschaft | OFFEN | Killfeed gesehen, keine unmittelbare Kampfszene zwischen Bots. Kein umfassender PASS für sämtliche Beziehungen oder Botbewegungen. |
| Individuelle Scores | PASS | [F9 12:35:09.761](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-scoreboard-2026-09-12T12-35-09-761Z-0.jpg) und [12:36:01.211](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-late-scoreboard-2026-09-12T12-36-01-211Z-0.jpg) mit getrennten Teilnehmerzeilen/Punkten. |
| Zeitablauf und richtiger Führender | PASS | Ende 12:36:27.222; crustyselenium_ gewinnt per Time limit. [Ergebnis 12:36:39.961](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-terminal-result-2026-09-12T12-36-39-961Z-0.jpg) mit Sieger 300 Punkte und genau einer abgeschlossenen Runde. Log-Wanduhrdauer 121.023 s; kein Anspruch auf exakt 120.000 s. |
| Ergebnislayout mit offenem F9 | **FAIL, kosmetisch — R3-01** | Großes DEFEAT-Banner überlagert Text des gleichzeitig geöffneten F9-Ergebnisbereichs. Original unverändert gesichert. Nach der kosmetischen Ausnahme durften unabhängige Tests folgen. |
| Ergebnis-/Punktefreeze über die gesamte Ergebnisphase | OFFEN | Zwei nahezu gleichzeitige Originalframes zeigen dieselbe Endwertung; sie beweisen keinen 15-s-Freeze oder Verhalten bei einem weiteren wertbaren Ereignis. Beide Frames im Manifest. |
| Automatische Rückkehr nach 15 s, kein endloser Tod-/Respawnablauf | PASS | Ergebnis 12:36:27.222 → Exit-Commit 12:36:42.421 = **15.199 s**. Bild zeigte noch Returning to menu in 3s. Reguläre Raid-Ende-Seite, danach Hauptmenübutton. |
| Ergebnisbutton als Alternative | OFFEN | Sichtbar, nicht geklickt; Autoexit verwendet. |
| Eigener natürlicher Tod am Zeitlimit | OFFEN | Nicht aufgetreten; keine künstliche Auslösung. |
| Hauptmenü nach C1 | PASS | [12:38:38.087](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c1-main-menu-settled-2026-09-12T12-38-38-087Z-0.jpg) ohne fremden Körper/Helm oder weiße Fläche. |
| Laufzeitfehler in C1 | PASS für C1-Fenster | Keine neuen Exceptions oder Reparenting-Meldungen bis zur bestätigten Hauptmenürückkehr. Einzelne Collider-/PerfectCulling-Warnungen; keine Fehlerflut. |

Beim Schließen des frühen F9-Fensters über dessen Button wurde eine um eins verringerte Patronenzahl bzw. ein einzelner Schuss beobachtet. Kein Treffer/Kill; nicht auf einen Bot gerichtet. Dies war kein gezielter Bot-Aktivierungstest. Der mögliche Eingabe-Nebeneffekt wurde nicht separat reproduziert.

Die vollständigen einschlägigen Originalzeilen stehen in [Original-Logauszüge und vollständige Fehlerstacks](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/targeted-log-excerpts-r3.txt). C1-Server: zunächst aufgeschobener Restore **12:36:43.684**, Snapshot-Restore **12:36:44.476**, geschütztes Raidende abgeschlossen **12:36:44.792**. Die normale Nach-Raid-Verarbeitung wurde laut Log übersprungen.

## C2 — All vs player, drei Runden, 10 Kills oder 2 Minuten

[Bestätigte Regeln 12:39:57.617](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-factory-day-rules-2026-09-12T12-39-57-617Z-0.jpg): Factory Tag, 3 Runden, 10 Kills, 2 min, All vs player. Native Ready **12:41:05.397**; [frühe Ladeansicht 12:41:06.880](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-ready-2026-09-12T12-41-06-880Z-0.jpg) mit korrekter Arena-Figur. Kit fertig **12:41:46.428**, erste Runde **12:41:46.472**. Kein weiterer Raidstart innerhalb von C2.

| Runde | Beginn UTC | Ende UTC | Wanduhrdauer | Rundenergebnis lokal |
|---|---|---|---|---|
| 1 | 12:41:46.472 | 12:43:47.190 | 120.718 s | target missed — Time limit; 0 Kills / 0 Tode / 0 Punkte |
| 2 | 12:44:03.296 | 12:46:04.050 | 120.754 s | target missed — Time limit; 0 Kills / 1 Tod / 0 Punkte |
| 3 | 12:46:19.926 | 12:48:21.156 | 121.230 s | target missed — Time limit; 0 Kills / 2 Tode / 0 Punkte |

| Unterfall | Status | Tatsächliche Beobachtung / Grenze |
|---|---|---|
| Richtiger Modus, Rundenzahl, Kill-/Zeitlimit | PASS | Menüregeln und [HUD Runde 3/3](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-resume-round-status-2026-09-12T12-46-30-047Z-0.jpg) All vs player / 0 von 10; alle drei Runden endeten protokolliert wegen Zeitlimit. |
| Killziel verfehlt bei Zeitablauf | PASS | Je Runde ausdrücklich target missed; lokaler Matchendstand 0 Kills / 3 Tode / 0 Punkte. |
| Spieler erreicht 10 Kills | OFFEN | Nicht erreicht, keine Ereignisse erzwungen. |
| Bots gegen Spieler | PASS für tatsächlich erhaltenen Schaden/Tode | Live Schaden, natürliche Tode und anschließende neue lokale Körper/Kits beobachtet; z. B. 395/440 im F12-Änderungsbild, später wieder 440/440. Der erste Tod ist um 12:44:57.163 protokolliert, Respawn nach 0.80 s. |
| Alle Bots richten sich nur gegen Spieler, keine beabsichtigten Bot-Duelle | OFFEN | Kein hinreichender unmittelbarer Überblick über die Kämpfe aller Bots. Kein Beweis einer fehlerhaften Feindschaft; aus einzelnen Killfeed-/Schadensereignissen keine allgemeine Freigabe. |
| Pausen zwischen Runden | PASS für Logablauf; UI offen | Zwischen Ende/Start 16.106 s und 15.876 s, nur ein geladener Raid. Die sichtbaren Pausenanzeigen wurden wegen des überbreiten F12-Fensters und Beobachtungslücken nicht gesichert. |
| Lokale Rundenzähler zurückgesetzt | PASS für protokollierte Todeszähler; Killreset offen | Runde 2 hat 1 Tod, Runde 3 separat 2, Matchgesamt 3. Nicht kumulativ als Rundenwert. Killwerte stets 0; Reset eines zuvor positiven Killzählers nicht beobachtet. Keine vollständige UI-Resetabnahme. |
| Nach Runde 3 genau ein Matchende, keine Runde 4/Map-Neuladung | PASS für protokollierten Lifecycle | Genau drei Start-/Endmarker, ein Exit-Commit und ein Matchende. HUD Runde 3/3 bis Schluss, danach reguläre Raid-Ende-Seite. |
| Finale Arena-Ergebnisansicht und Punktefreeze | OFFEN | Die Aufnahme nach dem letzten Countdown kam erst auf der nativen Raid-Ende-Seite an. Der Arena-Ergebnisbildschirm und die ganze 15-s-Phase wurden nicht visuell erfasst. |
| F12-Limit ändert laufende Regeln nicht | **PASS für die laufende dritte Runde** | Ablauf und Originale unten. Verhalten einer später gestarteten Runde nach Änderung nicht gesondert geprüft. |
| Regulärer Exit ohne neue Laufzeitfehler | **FAIL — R3-02, Gesamtstopp** | Drei neue Exceptions unmittelbar nach automatischem Exit-Commit. Hauptmenü später trotzdem regulär erreicht. |

### F12-Änderung und Bedienproblem

Beim ersten Eingeben der Suche während des Ladens erschien unerwartet langer, nicht beabsichtigter Text. Vermutlich wurde Zwischenablageinhalt verzögert übernommen; diese Ursache wurde nicht technisch nachgewiesen. Betroffen war zunächst nur Search. Das Fenster blieb nach Korrektur der Suche überbreit, sein rechter Rand außerhalb des Bildes. Deshalb wurden vorerst keine mehrdeutigen Regler geändert. Zwei native Titel-Drags machten Beschriftung/Werte/Close erreichbar; Schließen und erneutes Öffnen stellte später die normale Breite wieder her. Kein Konfigurationsdatei-Workaround, kein geänderter fremder Mod. Das Bedienproblem und Werkzeug-/Kontextpausen begrenzen die visuelle C2-Abnahme.

Der eigentliche Test erfolgte erst mit eindeutig zugeordnetem Deathmatch-Feld:

1. [12:47:08.992: Deathmatch-Zeitlimit 120](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-f12-right-edge-recovery-2026-09-12T12-47-08-992Z-0.jpg) sichtbar; die anderen beiden Treffer waren Team Deathmatch 600 und Match 300 und blieben unverändert.
2. Native Texteingabe **12:47:33.798**, [12:47:35.375: Deathmatch 60, HUD noch 0:45](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-frozen-limit-60-2026-09-12T12-47-35-375Z-0.jpg). Die dritte Runde lief bereits länger als 60 Sekunden und wurde nicht beendet.
3. F12 geschlossen; [12:47:53.626: unverändert Runde 3/3, noch 0:28](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-frozen-live-proof-2026-09-12T12-47-53-626Z-0.jpg); [12:48:13.136: noch 0:08](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-final-countdown-2026-09-12T12-48-13-136Z-0.jpg). Rein lesender Dateiabgleich um **12:48:12.786** bestätigt gespeicherten F12-Wert **60**.
4. Runde endet erst **12:48:21.156**, nach **121.230 s**. Der geänderte Konfigurationswert verkürzte diese laufende Runde nicht.
5. Nach sicherem Ausstieg [F12 wieder normal breit, weiterhin 60](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/restore-f12-open-2026-09-12T12-50-01-020Z-0.jpg); anschließend regulär auf den ursprünglichen Vor-Test-Wert **0 / Off** zurückgestellt, zusammen mit den übrigen eigenen Testwerten.

### R3-02: neue Exceptions beim Ausstieg

Ergebnis-/Exit-Reihenfolge: Runde 3 Ende **12:48:21.156** → automatischer Exit-Commit **12:48:36.259**, Abstand **15.103 s** → erster Fehler **414 ms später**. Die folgenden Originalzeilen sind Clientzeit UTC+2, Zeilennummern des frischen r3-Logs:

~~~text
13589: [14:48:36.259] [Info   :maschine-ArenaMode] Arena match result exit committed (Survived); protected native end follows.
13592: [14:48:36.673] [Error  : Unity Log] ArgumentException: Value does not fall within the expected range.
13594: [14:48:36.673] BotItemTaker+CG_PickupAction.method_0 () (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13595: [14:48:36.673] EFT.PickUpState.ManualAnimatorMoveUpdate (System.Single deltaTime) (at <cc2d80b06d5b4cb1a5816d2cc901d4c7>:0)
13617: [14:48:37.769] [Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object
13619: [14:48:37.769] SAIN.Components.BotComponent.OnDisable () (at <fa495b69dd2a4d3ea7473d47d3c53a22>:0)
13637: [14:48:37.781] [Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object
13639: [14:48:37.781] SAIN.Components.BotComponent.OnDisable () (at <fa495b69dd2a4d3ea7473d47d3c53a22>:0)
~~~

Die SAIN-Stacks führen über DestroyImmediate → Pools.UnloadPools → GameWorld.Dispose zum nativen Spielende. Der erste Stack betrifft eine Bot-Aufhebeanimation. **Keine dieser drei Meldungen ist der frühere ArenaCharacterPreview-Reparenting-Fehler.** Die Stacks belegen Fehlerort und Ausstiegskontext, keine abschließende Verursacherzuordnung. Vollständige Stacks: [Original-Logauszüge und vollständige Fehlerstacks](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/targeted-log-excerpts-r3.txt).

Der Server meldet Snapshot-Restore **12:48:37.560** und geschützten Abschluss **12:48:37.699**, noch vor den beiden SAIN-NREs. [12:48:50.363](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-terminal-early-2026-09-12T12-48-50-363Z-0.jpg) zeigt trotz irreführendem Archivnamen bereits die native Seite RAID ENDED / Survived / 00:06:52 / EXP 389, keinen Arena-Endbildschirm. Regulärer MAIN MENU-Klick **12:49:10.741**, [Hauptmenü 12:49:50.368](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/c2-safe-return-progress-2026-09-12T12-49-50-368Z-0.jpg) erreicht. Neue Teiltests wurden danach nicht gestartet.

Bis zum C2-Exit keine neuen Exceptions. Isolierte Warnungen zu fehlenden extractor_smoke-Scriptreferenzen beim Laden, Collider und PerfectCulling-Aufräumen bleiben von geworfenen Exceptions getrennt. Die frischen Logdeltas nach Stopp/Wiederherstellung bis zum Spielende enthalten keine weiteren Exceptions (letzter Markerstand 13934). Keine wiederholte ArenaPreview-/Gruppen-Fehlerflut. Ein generischer Servertext mit PitFireTeam im gemeinsamen Schutzpfad belegt keine tatsächlich gestartete PFT-Gruppe.

## Raidweise Profilvergleiche

Nur lesende private Vor-/Nachkopien, JSON-Vergleich rekursiv nach Schlüsseln und mit erhaltener Arrayreihenfolge. Nachkopien erst nach vollständig bestätigter Hauptmenürückkehr. Zusätzlich unabhängiger Vergleich des gesamten JSON-Dokuments. [Unabhängiger Lifecycle-/Profilabgleich](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/independent-lifecycle-audit-r3.json); [Abschlussnachweise](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/final-checks.json).

| Bereich | C1 PMC | C1 Scav | C2 PMC | C2 Scav |
|---|---|---|---|---|
| Inventory | PASS gleich | PASS gleich | PASS gleich | PASS gleich |
| Quests | PASS gleich | PASS gleich | PASS gleich | PASS gleich |
| Skills | PASS gleich | PASS gleich | PASS gleich | PASS gleich |
| Experience | PASS gleich | PASS gleich | PASS gleich | PASS gleich |
| Health | PASS gleich | PASS gleich | PASS gleich | PASS gleich |
| Level / TaskConditionCounters | PASS gleich | PASS gleich | PASS gleich | PASS gleich |
| RepeatableQuests | PASS gleich | vorher/nachher nicht vorhanden | PASS gleich | vorher/nachher nicht vorhanden |

| Raid | Vorherkopie UTC | Nachherkopie UTC | Einzige vollständige JSON-Differenz |
|---|---|---|---|
| C1 | 12:33:17.739 | 12:38:47.111 | characters.pmc.Hideout.sptUpdateLastRunTimestamp: **1789216379 → 1789216679**, entsprechend **12:32:59 → 12:37:59**, +300 s |
| C2 | 12:40:25.984 | 12:50:00.122 | derselbe Pfad: **1789216799 → 1789217399**, entsprechend **12:39:59 → 12:49:59**, +600 s |

Alle übrigen JSON-Werte gleich, das gesamte Scav-Objekt jeweils unverändert. **Keine pauschale Gesamtgleichheit des PMC-Profils.** Die native Raid-Ende-Seite zeigte jeweils EXP 389; gespeicherte PMC-Experience blieb bei beiden Vergleichen **0 → 0**. Die C2-Exit-Fehler haben in diesen Vergleichen keinen persistierten Schaden hinterlassen.

## Rückkehr, offene Fälle und Wiederherstellung

| Unterfall | Status | Grund / tatsächliche Reichweite |
|---|---|---|
| Hauptmenü ohne fremde Körper/weiße Fläche | PASS, beide Rückkehransichten | Belegte C1-/C2-Hauptmenüs; funktionierende Rückkehr hebt C2-Exceptions nicht auf. |
| Hideout-Weapon-Builder | OFFEN | Wegen Gesamtstopp nicht geöffnet. |
| Normaler PMC-Menüweg nach Raid bis vor Ready, dann Abbruch | OFFEN | Nach Stopp nicht als Test durchgeführt. Charakterauswahl wurde später nur zur Wiederherstellung eigener Arena-Werte durchlaufen; daraus keine vollständige PMC-Abnahme. |
| Regulärer Ladeabbruch | OFFEN | Kein zusätzlicher Ready-/Abbruchlauf; kein verfügbarer Abbruchknopf abgenommen. |
| C3 Endless / beide Limits Off / ca. 90 s | OFFEN | Gesamtstopp; kein dritter Raid. Wiederhergestellte Menüwerte sind kein Endless-Raidtest. |
| C3 Give-up/Respawn und Menüausstieg | OFFEN | Nicht durchgeführt; natürliche Respawns in C2 ersetzen diesen Unterfall nicht. |
| PFT-Teammate-Wechsel | OFFEN, ausdrücklich außerhalb dieser Serie | Keine Gruppe oder zusätzlicher Tactical-Test. |
| Optionaler Laservergleich | OFFEN | Kein gezielter Punkt-/Strahl-/Helligkeitsvergleich durchgeführt. |

Alle eigenen Änderungen nativ zurückgestellt, zuletzt **12:52:31.027** nachgerechnet: **147 Konfigurationswerte verglichen, null Unterschiede**, Client-Arena-Konfigurationsdatei wieder bytegleich. [SkipPreRaidScreens wieder true](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/restore-skip-true-2026-09-12T12-50-26-944Z-0.jpg), [Deathmatch wieder Endless / Off / Off / FFA](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/restore-deathmatch-ffa-2026-09-12T12-52-09-964Z-0.jpg), [ursprünglicher Modus Gun Game](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/restore-gungame-2026-09-12T12-52-18-333Z-0.jpg).

| Eigener Testwert | Vorher | Test | Wiederhergestellt |
|---|---|---|---|
| General/SkipPreRaidScreens | true | false | true |
| Match/GameMode | gungame | deathmatch | gungame |
| Deathmatch/Rounds | 0 | 1, dann 3 | 0 |
| Deathmatch/KillLimit | 0 | 10 in C2 | 0 |
| Deathmatch/TimeLimitSeconds | 0 | 120, dann 60 in C2 | 0 |
| Deathmatch/FreeForAll | true | false in C2 | true |

LegacyRulesMigrated, FastDeploy und sämtliche übrigen Werte unverändert. Beim Rückstellen ließ sich das letzte Zeitlimit erst nach Wahl von Endless ausschalten; reguläre Bedienung verwendet. Search geleert, F12 normal breit und geschlossen. RaidInfoPanels.cfg, Control.ini und Graphics.ini sind ebenfalls bytegleich. Arena-Dateibestand **33 vorher / 33 nachher**, keine hinzugefügten/fehlenden Dateien und alle Hashes gleich; insbesondere keine Audioänderung.

### Serverkonfigurations-Hashwechsel beim Start

Die Serverkonfiguration ist **nicht bytegleich** zur ausgeschalteten Ausgangsbaseline: SHA256 vorher **5A6965111C80861D3F888205E62C1E07137E9365F48733D969B4C0B058F1B90F**, nachher **E5D22ABA7DECE559F6E1100E6D64D5FCD4D8D57C1875A9AFC6794D1ED1CFB58B**. Einzige semantische Differenz: neu ergänztes excludedTemplates mit acht Standardausschlüssen; alle bestehenden Werte gleich. Änderungszeit **12:23:58.450720 UTC**, also nach Serverstart, vor Clientstart und beiden Raids. Direkt danach Config loaded **12:23:58.458** und acht Ausschlüsse aus der Konfiguration **12:23:58.562**.

Die acht Werte entsprechen in gleicher Reihenfolge den Defaults des gebundenen Serverquellstands 939d564; dessen Startpfad ergänzt fehlende Konfigurationsschlüssel. Daraus wird eine automatische Startmigration abgeleitet. Es gibt keinen Dateisystem-Audittrace, der den schreibenden Prozess unmittelbar identifiziert. Kein belegter Konfigurationswechsel während der Raids. Beide Raids liefen nach diesem Start mit der ergänzten Liste. Die Datei wurde von dieser Testinstanz nicht editiert oder zurückkopiert; fremde bzw. vom Server ergänzte Werte nicht zurückgesetzt. [Serverkonfigurations-Abgleich](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/server-config-audit.json).

## Belege und Übergabe

[Originalbild-Manifest mit SHA256](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/manifest.json) enthält **40 gezielt ausgewählte unveränderte Originalbilder**, Auflösung, Archivzeit und Hash. Die beiden C1-Endframes sind Momentaufnahmen, kein Video. [Original-Logauszüge und vollständige Fehlerstacks](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/targeted-log-excerpts-r3.txt), [Sequentielle Logmarker](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/transition-log-checks.json) und [Abschlussnachweise](C:/SPT41/Development/ArenaMode/test-evidence/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z/final-checks.json) ergänzen die visuellen Belege. Vollständige Profile und Rohlogs bleiben ausschließlich im ignorierten artifacts/gpu-deathmatch-menu/deathmatch-menu-preview-r3/2026-09-12T12-22-55-448Z; keine vollständigen Profile/Rohlogs im Commit. Private Kennungen in den gezielten Serverauszügen sind ausdrücklich mit [private-id] redigiert; sonst Originalzeilen samt Zeitstempeln und ursprünglichen Zeilennummern.

r1/r2-Berichte und deren Belege bleiben unverändert. Zu bearbeiten sind zunächst **R3-02 (Bot-Pickup-/SAIN-Teardown-Exceptions beim C2-Autoexit)** sowie **R3-01 (Ergebnis/F9-Überlagerung)**. Dieser Bericht enthält keine Produktionskorrektur und keine Freigabe der offenen Unterfälle.
