# GPU-Test: Deathmatch-Menü und Arena-Charakter, r1

Arbeitsauftrag für die Codex-Instanz auf dem Spiel-PC. Zuerst AGENTS.md und den
aktuellen Eintrag in HANDOFF.md lesen. Lokale Spiegelung: C:\SPT41; Repository:
C:\SPT41\Development\ArenaMode. Kein Produktionscode während dieser Tests ändern.

## Bindung an den Testbuild

Build-ID: **deathmatch-menu-preview-r1**.
Entwicklungsbranch: `feature/deathmatch-menu-preview`.
Implementierungscommit: `c8acf084b3eaf4e98455a70eacaa90ba56349207`
Client SHA256: `FBBF82C6993E16D3299B3081B8A53BD7770F3712CA3AB3E0A4B5F2D052A92D90`
Server SHA256: `8F9A56F574835FEAB73AB78C2EF4D5CF5734F12ABE63F384E2148CF319890B73`

Beide Prozesse neu starten. Vorher die DLLs unter BepInEx/plugins und
SPT_Runtime/user/mods/ArenaMode hashen und mit obigen Werten vergleichen.
Prozessstartzeit/PID, Git-HEAD, Auflösung und tatsächliche Hashes im Bericht
festhalten. Bei Abweichung keinen alten r6-Lauf als Nachweis verwenden; zuerst
Synchronisierung abwarten. Laufende Builds dürfen Test-DLLs nicht unbemerkt
wechseln: nach jedem Lauf erneut hashen. Keine DLLs oder Profile zurückkopieren.
Die mitgelieferten Audio-Dateien und fremde Mods unverändert lassen.

Native Bedienung über die vor Ort tatsächlich verfügbaren Automatisierungstools.
Kein Harmony-/Speicher-Instrumentieren, keine Profilmanipulation, keine direkte
Server-Arm-Ankündigung als Ersatz für die sichtbare Menübedienung. Wenn eine
Aktion nicht steuerbar ist, den konkreten Fall als offen dokumentieren; keinen
PASS aus Quellcode oder einem Build ableiten. Maximal drei begrenzte Raids und
20 Minuten aktive Testzeit; bei neuem Fehler Originalbelege sichern und stoppen.

## A: Menü und Figur ohne Raid

1. PMC, Scav, Arena auswählen. Für Arena bleiben Beschreibung und Figur sichtbar.
   Bei PMC/Scav kommen die unveränderten nativen Modelle zurück.
2. Arena-Kartenwahl: genau Deathmatch, Gun Game, Tactical und Team DM. Keine
   separaten Endless/Rounds-Buttons. Die nativen unteren Buttons bleiben frei.
3. Deathmatch: Rundenzahl, Kills/Runde, Zeit/Runde, FFA/All vs player sichtbar.
   Mehrfach wechseln, zurück/weiter navigieren. Markierung und F12-Werte stimmen
   überein. Bei beiden Limits Off darf keine endliche Rundenzahl gewählt werden.
   Erst Kill- oder Zeitlimit setzen, dann 3 Runden wählen. Ungewöhnliche F12-Werte
   (z.B. 37 Kills,75 Sekunden) werden exakt beschriftet, ohne falsche Markierung.
4. Insbesondere unterste Gegnerauswahl, Hinweis und PLAY AREA fotografieren:
   keine Überlappung. Deathmatch -> Gun Game -> Team DM ohne Kartenwechsel muss
   die Bereichsposition anpassen. Tactical zeigt solo 1v5 (bei anderer
   Squad-Einstellung tatsächliches 1vN) und den PitFireTeam-Hinweis. Nur mit einer
   real vorhandenen, akzeptierten Vierergruppe ist 5v5 (PFT) zu erwarten; keine
   Mitspieler erzeugen. Ohne passende Gruppe diesen Unterfall als nicht geprüft
   angeben. Team-DM-Limitwahl und Customs-Bereichsmenü bleiben bedienbar.
5. Die gleiche erzeugte Arena-Figur auf Kartenwahl, Offline-Einstellungen,
   Bestätigung und Laden prüfen. Auf den ersten beiden Screens unten rechts
   kompakt, auf Bestätigung/Laden im Modellbereich. Outfit/Waffe/Helm dürfen
   nicht zur echten PMC-Ausrüstung springen. Zurück zur Auswahl, zweimal wieder
   vor/zurück und auf PMC wechseln: keine zweite Figur, weiße Fläche oder Reste.
   Vorhandene PFT-Teamkollegen auf dem Ladescreen bleiben eigene native Vorschauen.

## B: Kurzer Clip und Einzelbilder

Je einen unveränderten 8-10-Sekunden-Clip der Arena-Figur in Charakterwahl und
Bestätigung aufnehmen; optional native PMC-Figur als Kontrollaufnahme. Gleiche
Auflösung, Bildrate und Grafikeinstellungen; Cursor außerhalb des Modells,
keine Drag-/Rotationsbewegung, zwei Sekunden Einpendeln. Originalvideo behalten.

Falls ffmpeg/ffprobe verfügbar sind, jeweils sechs direkt aufeinanderfolgende
Originalframes bei ca. Sekunde2 und5 extrahieren:

```powershell
pwsh -NoProfile -File tools/ArenaCharacterPreviewChecks/ReviewFrames.ps1 -VideoPath C:\ABSOLUTER-PFAD\arena-side.mp4 -StartSeconds 2 -FrameCount 6
```

Der Helfer schreibt neue Verzeichnisse, Quellvideo-Hash, echte Frameindizes und
Präsentationszeitstempel. Keine Interpolation, generierte Zwischenbilder oder
nachträglich erfundene Bildraten. Die tatsächlichen Originalframes ansehen und
Waffe/Handkontakt, Helmrand, Torso und unveränderte UI-Landmarke vergleichen:

- Ruhige UI, springende Figur: Pose/Modellbewegung.
- Ruhiger Körper, springender Hand-/Waffenkontakt: möglicher IK-Effekt.
- Ruhige Geometrie, wechselnde Randpixel: eher Rendering/Aliasing als Bewegung.
- Langsame durchgehende Bewegung kann die normale Idle-Animation sein.

Konkrete Frame-/Zeitpaare nennen. Die Verlagerung des Renderursprungs ist eine
Ursachenhypothese, kein bereits bewiesener Fix. Ohne vorheriges Vergleichsvideo
nur aktuellen Zustand bewerten, keine behauptete Verbesserung gegenüber r6.

## C: Begrenzte Deathmatch-Raids

Bevorzugt Factory Tag, unveränderte Profil-Schutzmechanismen und native Starts.
Vorher/nachher die vorhandene Profil-Diff-Methode aus früheren GPU-Aufträgen
verwenden; keine Profileinträge ändern. Selbst gespielte schwierige Aktionen
(z.B. bestimmte natürliche Kills) nicht erzwingen oder aus fehlenden Belegen
als bestanden ableiten.

1. **FFA, 1 Runde, Kills Off, Zeit2min.** Runde startet erst nach initialem
   Kit-/Spieleraufbau. Beobachten: Bots müssen auch untereinander Gegner sein,
   einschließlich gleicher Fraktion. Andere Kills im Killfeed/Scoreboard zählen
   pro individuellem Bot. Nach2min Ergebnis (Führender gewinnt, Gleichstand
   unentschieden); Punkte bleiben danach unverändert. Rückkehr über Button oder
   nach15s, kein Tod-/Respawn-Endloslauf. Falls das Limit während eines natürlichen
   eigenen Todes eintritt, dessen sicheren Ergebnisablauf gesondert belegen.
2. **All vs player, 3 Runden, 10 Kills oder2min.** Bots greifen nur den Spieler
   an. Einzelne Bot-Fehlschüsse sind kein Beweis für beabsichtigte Feindschaft;
   gezielte Bot-gegen-Bot-Kämpfe wären ein Fehler. Eigene Kills erreichen das Limit
   oder der Timer beendet die Runde als verfehltes Ziel. Pausen, frische lokale
   Rundenzähler und nach Runde3 genau ein Match-Ergebnis. Keine Map-Neuladung,
   keine zusätzliche Runde4. Eine absichtliche Änderung von F12-Limits während
   des Ladens/Matches darf dieses bereits angekündigte Match nicht ändern.
3. **Optional bei verbleibender Zeit:** Endless, beide Limits Off, FFA, ca.90s.
   Keine Rundenpause/automatisches Ergebnis; der bestehende Give-up/Respawn und
   native Menüausstieg bleiben möglich. Bei längerem Spiel auf wachsende
   Gruppen-/NRE-/Admissionsfehler achten;90s beweisen keinen Langzeitbetrieb.

Danach Menü/Hideout-Weapon-Builder kurz öffnen: keine fremden Helm-/Körperreste.
Falls verfügbar normalen PMC-Start und Abbruch testen, ohne Arena-Status oder
überschriebene Profil-/Modellzustände. Fehlende Unterfälle ausdrücklich offen.

## Ergebnis und Belege

Bericht: GPU-DEATHMATCH-MENU-TEST-RESULTS-r1.md. Pro Unterfall PASS/FAIL/OFFEN,
Start/Endzeit, beobachtetes Ergebnis, Screenshot-/Clip-/Framepfade und relevante
Original-Logzeilen. Client- und Serverlog samt Zeitoffset sichern. Keine nur aus
Warnungsfreiheit abgeleitete Funktionsbestätigung. Die getesteten Hashes bleiben
maßgeblich, selbst wenn später eine andere DLL synchronisiert wird.

Nur Bericht und gezielte Testbelege committen, kein unbesehenes git add -A.
Commit-Trailer: Co-authored-by: Codex <codex@openai.com>.
