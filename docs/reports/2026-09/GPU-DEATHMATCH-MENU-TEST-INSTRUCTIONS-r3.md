# GPU-Nachtest: Vorschau-Lebenszyklus und Deathmatch, r3

Arbeitsauftrag für die Codex-Instanz auf dem Spiel-PC. Zuerst AGENTS.md und den
neuesten HANDOFF-Eintrag lesen. Repository: C:\SPT41\Development\ArenaMode;
Installation: C:\SPT41. Keine Produktionsänderungen während des Tests.

## Exakter Teststand

- Build-ID: **deathmatch-menu-preview-r3**
- Branch: `feature/deathmatch-menu-preview`
- Client-Implementierungscommit: `3010346e23f802ae40d58669d12b4327fa86b1fe`
- Client SHA256: `4F33B337108987C5471E42FDD706F57D61421CB05D9AAD25186A592366130473`
- Server-Implementierungscommit / Quellstand beim Abgleich: `939d56454e7479a7d49f2ac9a03f475c02d59a29`
- Eingebettete Buildrevision beider DLLs: `f887090f5b04569a6f35fd19bf8a3ac63c25e0e2`
- Server SHA256: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`

Nach Abschluss der parallelen Arbeiten wurden beide installierten DLLs erneut
abgeglichen. Client und Shared-Quellen sind seit der Vorschaukorrektur unverändert;
der Server wurde für den Waffenfilter weiterbearbeitet. Installiert ist aktuell
der Debug-Serverbuild, nicht die anders gehashte Release-Datei. Die DLLs wurden
vor dem letzten Quellcommit gebaut, daher unterscheiden sich eingebettete
Buildrevision und Repository-HEAD. Die zur Server-DLL passenden Debugsymbole
bestätigen den aktuellen LadderPicker-Quellstand aus `939d564` im Build.
Für diesen Test gelten die obigen tatsächlichen
DLL-Hashes; keine Umstellung auf einen anderen Build und kein Neubau nur wegen
dieses Unterschieds. Build-ID r3 und Testumfang bleiben unverändert.

Für diesen kombinierten Teststand Spiel und Server nach der Synchronisierung
neu starten; tatsächliche PIDs/Startzeiten festhalten. Vor Start,
vor jedem Raid und am Ende beide DLLs hashen. Bei Abweichung nicht testen und
keine alten Ergebnisse auf diese DLL übertragen. Git-HEAD, Prozessstartzeit/PID,
Auflösung und tatsächliche Hashes gehören in den Bericht. Keine DLLs, Profile,
Audio-Dateien oder fremden Mods in der Installation austauschen/verändern;
private, ausschließlich gelesene Sicherungskopien für Vergleiche sind erlaubt,
Profile niemals zurückkopieren. Keinen Build starten.

r2 (`c5c8945`) hat die vier Modi, Beschreibung, Rundensperre bei beiden Limits
Off, ungewöhnliche 37/75-Werte und Teile der Platzierung nativ bestätigt. Gleiche
Arena-Ausrüstung war auf Karte, Offline-Seite und Bestätigung sichtbar. Vier
Unity-Fehler beim Ablösen führten vor dem ersten Raid zum Stopp. Es gab weder
Raid- noch Lade- oder wiederholte Navigationsabnahme. Diese neue Anweisung
prüft zuerst genau diesen Fehler; nicht wieder alle F12-Menüfälle abarbeiten.
Die Nutzerbeobachtung „Zittern behoben“ bleibt bestehen.

## Grenzen und Sicherung

Native Bedienung mit tatsächlich verfügbaren Automatisierungswerkzeugen.
Keine Harmony-/Speicher-Instrumentierung, direkte Server-Arm-Ankündigung,
Profilbearbeitung oder erzwungenen Matchereignisse. Nicht steuerbare Unterfälle
als OFFEN dokumentieren, keinen PASS aus Quellcode oder fehlenden Fehlern ableiten.
Maximal drei Raids und insgesamt 20 Minuten aktive Testzeit; Lade-/Werkzeugzeiten
getrennt erfassen. Bei Zeitende sicher aussteigen und Restfälle OFFEN lassen.

Vorher Einstellungswerte, Arena-Dateihashes und private Profilvergleichsbaselines
sichern. Nur eigene Menü-/F12-Testwerte anschließend regulär wiederherstellen.
Keine Profile zurückkopieren. Profilvergleiche vor/nach jedem tatsächlich
gestarteten Raid mit der vorhandenen Vergleichsmethode durchführen; Inventory,
Quests, Skills, Erfahrung und Gesundheit getrennt ausweisen. Zeitstempeländerungen
konkret benennen, keine pauschale Gesamtgleichheit behaupten.

- Kosmetische Abweichungen mit Originalbild als FAIL festhalten; unabhängige
  Prüfungen dürfen weitergehen.
- Falsche/fehlende Modellanzeige oder blockierte Bedienung: betroffenen Fall
  stoppen, sicher zurück. Keine Raids mit unklarer Modus-/Regelauswahl starten.
- Erneutes `Cannot set the parent ... ArenaCharacterPreview`, neue Exceptions,
  Absturz, Softlock, Fehlerflut, unerwartete Profiländerung, falsche Matchregeln,
  Hashwechsel oder unklares Risiko: gesamten Lauf stoppen und Belege sichern.
- Fehlender Recorder ist kein Grund, unabhängige Tests abzubrechen. Nur bei
  erneutem Zittern optional einen Originalclip aufnehmen und die vorhandene
  `tools/ArenaCharacterPreviewChecks/ReviewFrames.ps1`-Anleitung aus r2 nutzen.
  Keine Werkzeuge installieren und keine Screenshots als Videobeweis ausgeben.

## A: Seitenwechsel vor dem ersten Raid

1. Arena wählen, Factory Tag / Deathmatch / FFA / 1 Runde / Kills Off / Zeit 2 min.
   Bei Bedarf `SkipPreRaidScreens=false` über F12 setzen und Ausgangswert notieren.
   Karte -> Offline -> Versicherung -> Bestätigung, zunächst **kein Ready**.
   Versicherung nur durchlaufen; dort wird keine Arena-Figur erwartet.
2. Nach jedem Seitenwechsel die seit dem letzten Marker hinzugekommenen
   Clientlogzeilen prüfen. Kein abgewiesenes Reparenting, keine neue
   Arena-preview-/CameraImage-/PlayerModelLoader-Exception. Karte und Offline
   zeigen die kompakte Figur unten rechts, Bestätigung dieselbe Ausrüstung im
   Modellbereich. Keine weiße Fläche, doppelte Figur oder verdeckten Buttons.
   Originalbilder mit Zeitangabe sichern.
3. Zur Karte zurück und denselben Weg zur Bestätigung erneut durchlaufen;
   mindestens zwei vollständige Vor-/Zurückzyklen. Einen Weg zügig durchklicken,
   soweit native Buttons freigegeben sind; keine verdeckten Aktionen erzwingen.
   Nach Rückkehr müssen Outfit und Regeln stimmen, die Figur darf nicht beim
   zweiten Aufruf auf die echte PMC-Ausrüstung umspringen.
4. Zur Charakterauswahl, PMC und Scav anwählen: ihre nativen Modelle ohne
   Arena-Reste. Arena erneut wählen und C1 vorbereiten. Die vorher akzeptierten
   Regeln/Labels kurz bestätigen, kein vollständiger Sonderwerte-Retest nötig.
   Sind A1–A4 ohne Laufzeitfehler gelungen, mit B fortfahren.

## B: Laden und bislang ungetestete Raids

**C1 – FFA, 1 Runde, Kills Off, Zeit 2 min.** Native Ready-Betätigung und Laden
beobachten: gleiche Arena-Figur, keine echte PMC-Ausrüstung oder Duplikate.
Sofern bedienbar, einmal bewusst drehen; keine ungewollte Dauerrotation.
Im Raid beginnt der Rundentimer nach dem initialen Kit-/Spieleraufbau.
Bots kämpfen auch untereinander einschließlich gleicher Fraktion; Scores
zählen individuelle Teilnehmer. Nur tatsächlich beobachtete Kämpfe bewerten.
Nach Zeitablauf gewinnt der Führende oder es gibt ein Unentschieden. Ergebnis
und Punkte frieren ein; Rückkehr per Ergebnisbutton oder nach 15 Sekunden,
kein endloser Tod-/Respawnablauf. Natürlichen eigenen Tod am Zeitlimit nur
bewerten, wenn er wirklich auftritt; nicht künstlich auslösen.

**C2 – All vs player, 3 Runden, 10 Kills oder 2 min.** Alle Bots sind gegen den
Spieler, nicht absichtlich gegeneinander. Einzelne Fehlschüsse belegen keine
fehlerhafte Feindschaft. Spieler erreicht Killziel oder Zeitablauf beendet die
Runde mit verfehltem Ziel. Pausen und zurückgesetzte lokale Rundenzähler prüfen.
Nach Runde 3 genau ein Match-Ergebnis, keine Runde 4 und keine Map-Neuladung.
Eine reguläre F12-Änderung eines Limits während Laden/Match darf die vorher
bestätigten Regeln dieses Matches nicht ändern; alten/neuen Wert und Anzeige
belegen und den Testwert anschließend wiederherstellen.

**C3 – optional bei verbleibendem Budget:** FFA, Rounds Endless, beide Limits
Off, ca. 90 Sekunden. Keine automatische Rundenpause/Ergebnisanzeige. Bestehender
Give-up/Respawn und regulärer Menüausstieg bleiben möglich. Dieser kurze Lauf
beweist keine Langzeitstabilität. Bei neuen Gruppen-/NRE-/Admissionsfehlern stoppen.

Der PFT-Teammate-Wechsel bleibt in dieser Deathmatch-Testserie OFFEN: Die
Arena-PFT-Integration gehört zu Tactical, nicht zu diesen drei Raids. Keine
Gruppe oder zusätzlichen Tactical-Test dafür starten. Ein Laservergleich ist
optional: nur mit vorhandenem geeignetem Kit Punkt/Strahl und Helligkeit kurz
kontrollieren, keine Teamfärbung in Solo/FFA erwarten.

## C: Rückkehr und Bericht

Nach sicher beendetem Raid Hauptmenü und, wenn zugänglich, Hideout-Weapon-Builder
öffnen: keine fremden Helme/Körper oder weiße Fläche. Normalen PMC-Menüweg
ansteuern und vor Ready abbrechen: native Vorschau, keine Arena-Modusreste.
Einen Ladeabbruch nur regulär prüfen, falls ein verfügbarer Abbruchknopf und das
Raidbudget es erlauben; andernfalls diesen Unterfall ausdrücklich OFFEN lassen.

Bericht: **GPU-DEATHMATCH-MENU-TEST-RESULTS-r3.md**. r1/r2-Berichte und Belege
unverändert lassen. Pro Unterfall PASS/FAIL/OFFEN mit tatsächlicher Beobachtung,
Zeitfenster, Originalbildern und relevanten Original-Logzeilen. Raidanzahl und
aktive Zeit getrennt von Laden/Toolzeit nennen. Clientlog (lokale Zeit) und
Serverlog (UTC) nur im eigenen Testfenster zuordnen. Start-/Endhashes, wieder
hergestellte Konfigurationswerte und raidweise Profilvergleiche dokumentieren.
Vollständige Profile/Rohlogs bleiben privat im ignorierten artifacts-Verzeichnis.

Nur Bericht und gezielte Belege committen, keine pauschale Aufnahme aller
Änderungen. Trailer: `Co-authored-by: Codex <codex@openai.com>`.
