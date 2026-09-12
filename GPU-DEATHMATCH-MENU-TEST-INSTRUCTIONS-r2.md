# GPU-Test: Deathmatch-Menü und Arena-Charakter, r2

Arbeitsauftrag für die Codex-Instanz auf dem Spiel-PC. Zuerst AGENTS.md und den
aktuellen Eintrag in HANDOFF.md lesen. Lokale Spiegelung: C:\SPT41; Repository:
C:\SPT41\Development\ArenaMode. Kein Produktionscode während dieser Tests ändern.

## Bindung an den Testbuild

Build-ID: **deathmatch-menu-preview-r2**.
Entwicklungsbranch: `feature/deathmatch-menu-preview`.
Implementierungscommit: `20669505459d90c212dc90806f24c12b3082870b`
Client SHA256: `60C2685951C4ECD17E29C80192EE26AA7CF7735C723091C89E8162BFAF4C6551`
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
20 Minuten aktive Testzeit. Abbruchregeln:

- Rein kosmetische Befunde (Text, Abstand, Farben ohne unklare Teamzuordnung):
  mit Originalbild als FAIL dokumentieren, dann unabhängige Fälle fortsetzen.
  Ein solcher Befund darf keine pauschale Funktionsfreigabe ergeben.
- Blockierte Bedienung oder falsche Modell-/Modusdarstellung: betroffenen Fall
  stoppen, sicher ins Menü zurück; unabhängige Menütests dürfen weitergehen.
  Keine Raids mit nicht eindeutig bestätigbarer Modus-/Regelauswahl starten.
- Absturz, Softlock, neue geworfene Exceptions/Fehlerflut, unerwartete Raid- oder
  Profiländerungen, falsche aktive Matchregeln oder DLL-Hashwechsel: gesamten
  Lauf beenden, Belege sichern und nur den eigenen Test regulär aufräumen.
  Bei unklarem Risiko ebenfalls stoppen. Schutzmechanismen niemals umgehen.
- Fehlende Aufnahme-/Bedienwerkzeuge: betroffenen Unterfall OFFEN dokumentieren;
  unabhängige Prüfungen fortsetzen. Keine Toolinstallation als Voraussetzung.

r1 (`05e88d0`) endete schon an einem kosmetischen 'Five modes'-Text. Keine
Kartenwahl und kein Raid wurden damit geprüft. Die offene Prüfmatrix jetzt
fortsetzen; Beschreibung erwartet: 'Solo and team arena combat.' und in der
zweiten Zeile 'Your profile and gear stay untouched.' (PFT-Zweig unverändert).

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

## B: Video nur bei erneutem Zittern oder auffälligen Folgescreens

Der Nutzer hat nach r1 bestätigt, dass das Charaktermodell nicht mehr zittert.
Diese Nutzerbeobachtung ist festgehalten; r1 enthält selbst keinen Videonachweis.
Keine erneute Videoabnahme als Pflicht vor den offenen Menü-/Raidtests verlangen.
Zunächst die Figur in A5 auf den Folgescreens beobachten. Nur wenn erneut
Zittern/Modellversatz auftritt, den folgenden Vergleich versuchen. Bei fehlendem
Recorder oder ffmpeg/ffprobe den Video-Unterfall OFFEN lassen, nicht den Lauf
abbrechen. Vorhandene Bilder beweisen Sichtbarkeit, aber keine Bewegungsruhe.

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

Konkrete Frame-/Zeitpaare nennen. Die genaue technische Ursache bleibt eine
Hypothese; die Verbesserung ist durch den Nutzer, nicht durch r1-Frames bestätigt. Ohne vorheriges Vergleichsvideo
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
3. **Optional bei verbleibender Zeit:** Deathmatch, Rounds Endless, beide Limits Off, FFA, ca.90s.
   Keine Rundenpause/automatisches Ergebnis; der bestehende Give-up/Respawn und
   native Menüausstieg bleiben möglich. Bei längerem Spiel auf wachsende
   Gruppen-/NRE-/Admissionsfehler achten;90s beweisen keinen Langzeitbetrieb.

Falls im gewählten Kit ein sichtbarer Waffenlaser vorhanden ist: kurz Punkt und
Strahl im Deathmatch prüfen; die vorhandenen Arena-Helligkeitsregler müssen wieder
wirken. Kein erfundenes Team-Tinting in Solo/FFA erwarten. Fehlt ein passendes
Kit oder eine klare Vergleichsszene, diesen Unterfall OFFEN lassen.

Danach Menü/Hideout-Weapon-Builder kurz öffnen: keine fremden Helm-/Körperreste.
Falls verfügbar normalen PMC-Start und Abbruch testen, ohne Arena-Status oder
überschriebene Profil-/Modellzustände. Fehlende Unterfälle ausdrücklich offen.

## Ergebnis und Belege

Bericht: GPU-DEATHMATCH-MENU-TEST-RESULTS-r2.md. Pro Unterfall PASS/FAIL/OFFEN,
Start/Endzeit, beobachtetes Ergebnis, Screenshot-/Clip-/Framepfade und relevante
Original-Logzeilen. Client- und Serverlog samt Zeitoffset sichern. Keine nur aus
Warnungsfreiheit abgeleitete Funktionsbestätigung. Die getesteten Hashes bleiben
maßgeblich, selbst wenn später eine andere DLL synchronisiert wird.

Nur Bericht und gezielte Testbelege committen, kein unbesehenes git add -A.
Commit-Trailer: Co-authored-by: Codex <codex@openai.com>.
