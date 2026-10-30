# GPU-Nachtest: F9-Close-Eingabe und Deathmatch-Ausstieg, r5

Arbeitsauftrag für die Codex-Instanz auf dem Spiel-PC. Zuerst AGENTS.md und
den neuesten HANDOFF-Eintrag lesen. Repo `C:\SPT41\Development\ArenaMode`,
Installation `C:\SPT41`. Keine Produktionsänderungen, Builds oder DLL-Tausche.
Die Entwicklungsinstanz hat den Client unter D:\SPT41 eingespielt; die tatsächliche
Synchronisierung nach C:\SPT41 muss hier geprüft werden. Nur das Spiel benötigt
wegen dieses Clientwechsels einen Neustart; der Server wurde nicht verändert.

## Teststand

- Test-ID: **deathmatch-menu-exit-r5**
- Source-/Build-Commit: `f64df5be499d06d29cc4e6b717dd52ea0931725a`
- Branch bei Lieferung: `fix/arena-panel-close-input`
- Client SHA256: `7DCAA94CFDF5F0C7E6736796901BE4C3DCBADCEC6F74C76E1668C22B81201F11`
- Client ProductVersion: `0.1.0+f64df5be499d06d29cc4e6b717dd52ea0931725a`
- Unveränderter Server SHA256: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`
- Unveränderter Redeploy.Core SHA256: `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`
- Erwarteter SAIN 4.5.1.0 SHA256: `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`
- Staged Build/Manifest: `artifacts/double-jump-build/54da549e8e144e76beabdac7d9b6b080/`
- Lokale Lieferung/Backup: `artifacts/deathmatch-r5-delivery/1c5a3a84724f483c8aa3a79f1402eb10/`

Vor Start, vor jedem Raid und am Ende die tatsächlichen installierten Dateien
hashen. Kein Neubau wegen eines späteren reinen Dokumentationscommits. Bei
Abweichung nicht testen und keine alte Abnahme auf einen anderen Client beziehen.
Spiel-PID/Startzeit, Server-PID/Startzeit, Git-HEAD, Auflösung und Hashes erfassen.
Server muss für diesen Test nicht eigens ersetzt oder neu gebaut werden.

## Was r4 tatsächlich gezeigt hat

`228d892` bleibt als r4-Bericht unverändert. Der zweite bewusste F9-Close-Klick
schoss mit der MP-153: `4/4 +1` -> `3/4 +1`, neuer Einschlag an der Wand.
Der Gesamtstopp war richtig. Der folgende manuelle Ausstieg blieb fehlerfrei;
der SAIN-Schutz griff einmal, der Pickup-Cancel-Pfad wurde nicht beobachtet.
Dreirundiger Autoexit, terminales F9/Banner, Score-Freeze und B/C bleiben offen.
Die bestätigte ruhige Charaktervorschau muss nur kurz kontrolliert werden.

R5 schützt die Eingabefreigabe beim Schließen von F9 und Tactical-Kaufmenü.
Die Sperre endet erst nach einem neuen Eingabedurchlauf mit losgelassenen
Maustasten. Andere offene Panels behalten ihre Sperre; Escape/EndShooting
bleiben zugelassen. Kein künstliches Zeitfenster, keine geänderten Keybinds.
Details und Offline-Grenzen: `ARENA-PANEL-INPUT-REVIEW.md`.
Startup muss melden:
`Arena input blocker ready: panel close waits for released-pointer input delivery.`
Fehlt der Marker oder erscheint `Arena input blocker unavailable`, nicht in den
Raid starten, sondern als Blockade berichten. Die begrenzten Laufzeitmarker
`Arena panel input: close ... pending` und `... released` samt `sample` und
`blockedShotCommands` sichern. Höchstens 24 Zeilen pro Prozess; danach darf
fehlendes Logging nicht als fehlende Funktion gewertet werden. Ein Filterzähler
von 0 kann bei einem sauberen Klick korrekt sein. Erneutes Öffnen kann eine
ältere Pending-Generation ersetzen, ohne dass diese eine Released-Zeile erhält.

R3-Banner-/Ausstiegsschutz und die vier Refinements bleiben enthalten.
Die vier Features werden getrennt nach erfolgreichem A geprüft:
`GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r3.md`, gleicher Client. R1/r2 dieser
Feature-Anweisung und Deathmatch r1-r4 sind historische Teststände.

## Grenzen und Sicherung

Native Bedienung mit verfügbaren Automatisierungswerkzeugen; keine Speicher-
oder Harmony-Instrumentierung, Server-Arm-Aufrufe, erzwungenen Matchereignisse,
Profilbearbeitung oder native Leichenzerstörung. Andere Mods und Audio unverändert
lassen. Private, ausschließlich gelesene Profilkopien für den Vergleich sind erlaubt;
Profile niemals zurückkopieren. Die Beschreibungstexte in cfg/client-settings wurden
durch r4 aktualisiert; keine alten Dateien oder Hashes wiederherstellen. Vorher
Einstellungen und relevante Dateihashes sichern, danach nur eigene Testwerte
regulär über das Spiel zurückstellen.

Maximal drei Raids und 20 Minuten aktive Testzeit; Lade-/Werkzeugfenster getrennt
und ehrlich erfassen. Nicht aus ungenauen Beobachtungsgrenzen eine sekundengenaue
Unter-20-Minuten-Abnahme ableiten. Bei Budgetende sicher aussteigen, Rest OFFEN.
Keine zusätzlichen Raids nur um einen seltenen Fehler zu provozieren.

Nach jedem Raid private Profilvergleiche nach bestätigter Hauptmenürückkehr:
PMC/Scav Inventory, Quests, Skills, Health, Experience, Level, TaskConditionCounters
und RepeatableQuests getrennt. Zusätzlich vollständigen JSON-Vergleich durchführen;
beispielsweise einen Hideout-Heartbeat-Zeitstempel als konkrete Differenz ausweisen.
Native Endscreen-EXP ist kein Nachweis gespeicherter Erfahrung.

- Kosmetische Abweichungen als FAIL mit Originalbild sichern; unabhängige
  Prüfungen dürfen weitergehen.
- Neue Exceptions, falsche Regeln, Softlock, Fehlerflut, unerwartete Profiländerung,
  Hashwechsel oder reproduzierter Schuss durch einen UI-Klick: gesamten Lauf
  stoppen, regulär zurückkehren und Belege sichern. Keine weiteren Abnahmeraids.
- Bereits bekannte reine Lade-/Culling-Warnungen von Exceptions unterscheiden.
  Keine neue Exception als harmlos abtun, nur weil sie aus einer anderen Mod kommt.

## A — zuerst Close absichern, anschließend drei Runden bis Autoexit

Factory Tag, **Deathmatch / All vs player / 3 Runden / 10 Kills / 2 Minuten**.
Kill-/Zeitlimit zuerst setzen, danach die endliche Rundenzahl; bei beiden
Limits Off wird korrekt Endless verwendet. Native Arena-Auswahl und Regeln
kurz dokumentieren. Keine PFT-Gruppe. Natürliche Tode/Respawns zulassen,
keine erzwungenen Ereignisse oder zusätzlichen Anläufe zum Provozieren.

Bei erster sicherer Gelegenheit zur Wand schauen, F9 öffnen und geladene
Munition einschließlich Kammer sowie Waffenidentität dokumentieren. **Close**
bewusst genau einmal klicken und unmittelbar ein Nachbild aufnehmen. Zwischen
diesen Bildern keine weitere Maus-, Schuss-, Nachlade- oder Waffenbedienung.
Originalbilder oder kurzer Originalclip plus Eingabe-/Rückgabezeitpunkte sichern.
Ein Schuss oder Eingabelock bedeutet sofortigen Gesamtstopp. Wird der Vergleich
durch Tod, Respawn oder Kitwechsel unterbrochen, UNKLAR und keine Fehlursache
behaupten. Nicht aus weit auseinanderliegenden Bildern auf Close schließen.

Wenn unauffällig: insgesamt drei gültige einzelne Close-Vergleiche, höchstens
fünf Versuche innerhalb desselben Raids. Nach Möglichkeit einen nach natürlichem
Respawn; fehlt er, Unterfall OFFEN. Danach separat F9/Hotkey und Escape-Schließen,
Bewegung, Nachladen und einen bewusst protokollierten Schuss an der sicheren
Wand prüfen. Nach Loslassen der Maus muss die nächste absichtliche Bedienung
normal funktionieren; keine dauerhafte Sperre. Diese Nachbedienungen strikt
von den Close-Vorher/Nachher-Vergleichen trennen. Falls die Zeit nicht für alle
Vergleiche reicht, Rest OFFEN; keinen Zusatzraid starten.

Nach unauffälligen Vergleichen die verbleibenden Runden und den automatischen
Ausstieg nach dem letzten Ergebnis beobachten. Die native Uhr läuft während
der Close-Prüfung weiter. Frühzeitig auf anderem regulärem Weg beendete Matches
sind kein Nachweis des dreirundigen Ausstiegs. Das Tactical-Kaufmenü ist in diesem
Deathmatch-Raid nicht erreichbar: dessen separaten Live-Unterfall OFFEN lassen,
keinen vierten Raid allein dafür starten.

Beim letzten Ergebnis F9 offen beobachten: Ergebnis, Sieger/Grund und Countdown
müssen lesbar sein, kein großes Banner darüber. Schließen und wieder öffnen:
Banner allein bei geschlossenem F9, Ergebnis im Fenster bei offenem F9.
Ein früheres Vorbereitungsbanner darf durch F9 nicht verlorengehen. Keine
Verlängerung/Neustart des Autoexit-Countdowns durch Öffnen/Schließen.
Zeitlich getrennte Belege des eingefrorenen Scores aufnehmen; zwei nahezu
gleichzeitige Bilder gelten nicht als Nachweis über die Ergebnisdauer.

Nach dem nativen Ausstieg Logdelta bis einschließlich Hauptmenürückkehr prüfen:

- Startmeldung `Arena: optional SAIN 4.5.1 uninitialized-component teardown safeguard ready.`
- Eventuell `Arena bot teardown: cancelled pending native pickup before disposal.`
- Eventuell `Arena SAIN teardown: uninitialized Arena component; coroutine cleanup retained.`
- **Kein** BotItemTaker/CG_PickupAction-ArgumentException und **kein**
  SAIN.Components.BotComponent.OnDisable-NRE; auch andere neue Exceptions erfassen.

Anzahl und Zeit der Schutzmeldungen berichten. Keine Schutzmeldung bedeutet
„Pfad in diesem Lauf nicht beobachtet“, nicht „Schutz nachgewiesen“. Bei weiterem
OnDisable-NRE kompletten Stack und Zeitpunkt sichern; keine pauschale Freigabe.
Beide Profilhälften vergleichen, bevor B gestartet wird.

## B — FFA und bewusster Ergebnisausstieg

Nur nach unauffälligem A und mit Restbudget: Factory Tag, Deathmatch / FFA /
1 Runde / Kills Off / 2 Minuten. Kurzer Regel-/Killfeed-/Scoreboard-Smoke-Test,
kein vollständiger erneuter Katalogtest. Sieger/Grund am Ende plausibel gegen
Standings abgleichen. Diesmal den Ergebnisbutton **Return to menu** benutzen;
native Rückkehr, Logdelta und Profilvergleich wie in A prüfen. Wird vorher
automatisch beendet, bleibt der Button-Unterfall OFFEN.

## C — offene Continuous-/Give-up-Fälle

Nur nach unauffälligem A/B und mit Restbudget: Deathmatch / FFA / Endless /
Kills Off / Zeit Off, ungefähr 90 Sekunden. Keine unerwartete Rundenpause.
Einmal regulär **Give up**, geglückten Respawn prüfen, danach über das normale
Raid-Menü verlassen. Kein Matchende durch internen Aufruf erzwingen.
Logdelta, Hauptmenü und Profilvergleich wieder separat dokumentieren.

Abschließend, soweit Budget vorhanden, normaler PMC-Menüweg bis vor Ready und
zurück; kein vierter Raid. Hideout/Weapon Builder öffnen und auf fremde Körperteile
oder Modelle prüfen. Fehlende Zeit oder fehlender Zugang ergibt OFFEN, keinen PASS.

## Bericht

`GPU-DEATHMATCH-MENU-TEST-RESULTS-r5.md` mit tatsächlichem Build, PASS/FAIL/OFFEN je
Unterfall, Raid-/Rundenanzahl, Settings, Zeitmarkern (Client MESZ, Server UTC),
vollständigen relevanten Stacks, Eingabe-/Schutzmeldungszahlen und raidweisen Profilvergleichen.
Originalbilder samt SHA256-Manifest unter `test-evidence/deathmatch-menu-exit-r5/`.
Rohlogs und vollständige Profile ausschließlich unter ignoriertem `artifacts/`.
Private Kennungen in gezielten veröffentlichten Auszügen redigieren.
Nur eigenen Bericht/Belege committen; fremde Änderungen weder zurücksetzen noch
mitnehmen. Alte r1/r2/r3/r4-Berichte, Anweisungen und Belege bleiben unverändert.
