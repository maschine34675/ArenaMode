# GPU-Nachtest: Deathmatch-Ergebnis und Bot-Ausstieg, r4

Arbeitsauftrag für die Codex-Instanz auf dem Spiel-PC. Zuerst AGENTS.md und
den neuesten HANDOFF-Eintrag lesen. Repo `C:\SPT41\Development\ArenaMode`,
Installation `C:\SPT41`. Keine Produktionsänderungen, Builds oder DLL-Tausche.
Die Entwicklungsinstanz hat den Client unter D:\SPT41 eingespielt; die tatsächliche
Synchronisierung nach C:\SPT41 muss hier geprüft werden. Nur das Spiel benötigt
wegen dieses Clientwechsels einen Neustart; der Server wurde nicht verändert.

## Teststand

- Test-ID: **deathmatch-menu-exit-r4**
- Source-/Build-Commit: `39bd7882713da14afcc2432cad092a6c9e1919f6`
- Branch bei Lieferung: `fix/deathmatch-r3-exit`
- Client SHA256: `9D27F9A0EA9F76805E33F3F9FF5CBA2AF8821A4C210CFB6822EDA8EDD6090BD9`
- Client ProductVersion: `0.1.0+39bd7882713da14afcc2432cad092a6c9e1919f6`
- Unveränderter Server SHA256: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`
- Unveränderter Redeploy.Core SHA256: `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`
- Erwarteter SAIN 4.5.1.0 SHA256: `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`
- Staged Build/Manifest: `artifacts/double-jump-build/f51871e16b864fdeb72337aab1d0c367/`
- Lokale Lieferung/Backup: `artifacts/deathmatch-r4-delivery/529c55a9d656423692b4cdf1e1131d74/`

Vor Start, vor jedem Raid und am Ende die tatsächlichen installierten Dateien
hashen. Kein Neubau wegen eines späteren reinen Dokumentationscommits. Bei
Abweichung nicht testen und keine alte Abnahme auf einen anderen Client beziehen.
Spiel-PID/Startzeit, Server-PID/Startzeit, Git-HEAD, Auflösung und Hashes erfassen.
Server muss für diesen Test nicht eigens ersetzt oder neu gebaut werden.

## Was sich seit r3 geändert hat

`ee0a421` bleibt als r3-Bericht unverändert. Seine zwei Menüzyklen und die
beobachteten Rundengrenzen gelten weiterhin für den dortigen Teststand;
die Vorschau muss hier nur kurz geprüft werden, kein erneuter kompletter A-Test.
Das akzeptierte Ende des Modellzitterns bleibt bestehen.

R3-01: Das große Ergebnisbanner weicht jetzt dem sichtbaren F9-Ergebnisfenster.
Beim Schließen erscheint es wieder; Vorbereitungshinweise bleiben bestehen.
R3-02: Beim nativen Bot-Abbau werden nur nachweislich noch nicht ausgeführte
Arena-Pickup-Callbacks entfernt. Eine exakt zugeordnete SAIN-Komponente ohne
initialisierte Aktivierung behält ihre Coroutine-Bereinigung ohne Nullzugriff.
Vorhandene Aktivierungen und unbekannte Zustände laufen weiterhin unverändert.
Fehlender BotOwner während Unity-Poolabbau bleibt eine mögliche Schutzlücke;
deshalb ist ein fehlerfreier Build keine Abnahme der historischen NREs.

Der Client enthält außerdem die vier vorher vorbereiteten Verbesserungen
(DynamicMaps-Leichenmarker, Luftsprung nach Fallen, Gitter-Nähe, DLSS-Marker).
Diese sind **kein** Bestandteil der Deathmatch-Abnahme; dafür existiert die
separate `GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r2.md` auf demselben Build.

## Grenzen und Sicherung

Native Bedienung mit verfügbaren Automatisierungswerkzeugen; keine Speicher-
oder Harmony-Instrumentierung, Server-Arm-Aufrufe, erzwungenen Matchereignisse,
Profilbearbeitung oder native Leichenzerstörung. Andere Mods und Audio unverändert
lassen. Private, ausschließlich gelesene Profilkopien für den Vergleich sind erlaubt;
Profile niemals zurückkopieren. Vorher Einstellungen und relevante Dateihashes
sichern, danach nur eigene Testwerte regulär über das Spiel zurückstellen.

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

## A — zuerst den C2-Ausstieg nachstellen

Factory Tag, **Deathmatch / All vs player / 3 Runden / 10 Kills / 2 Minuten**.
Native Arena-Auswahl und Regeln kurz dokumentieren. Keine PFT-Gruppe.
Normal spielen; natürliche Tode/Respawns zulassen, aber nichts künstlich auslösen.
Alle drei Runden und den automatischen Ausstieg nach dem letzten Ergebnis beobachten.
Falls das Match vorher auf regulärem anderem Weg endet, den nicht erreichten
Runden-/Respawn-Unterfall OFFEN lassen; keinen dritten/fünften Anlauf anhängen.

Während einer sicheren Gelegenheit F9 öffnen, Munition dokumentieren, **Close**
bewusst einmal klicken und Munition unmittelbar danach vergleichen. Nicht auf
einen Bot zielen und dazwischen keine Schussbedienung. Wenn unauffällig, höchstens
zwei weitere Close-Klicks sowie Schließen per Hotkey/Escape prüfen. Der r3-Bericht
enthielt eine unbestätigte Schussbeobachtung; der Banner-Fix behauptet dafür
keine Korrektur. Bei eigenem/unabsichtlichem Schießen den Vergleich als unklar
kennzeichnen, nicht aus einem zeitlich entfernten Screenshot auf Close schließen.

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

`GPU-DEATHMATCH-MENU-TEST-RESULTS-r4.md` mit tatsächlichem Build, PASS/FAIL/OFFEN je
Unterfall, Raid-/Rundenanzahl, Settings, Zeitmarkern (Client MESZ, Server UTC),
vollständigen relevanten Stacks, Schutzmeldungszahlen und raidweisen Profilvergleichen.
Originalbilder samt SHA256-Manifest unter `test-evidence/deathmatch-menu-exit-r4/`.
Rohlogs und vollständige Profile ausschließlich unter ignoriertem `artifacts/`.
Private Kennungen in gezielten veröffentlichten Auszügen redigieren.
Nur eigenen Bericht/Belege committen; fremde Änderungen weder zurücksetzen noch
mitnehmen. Alte r1/r2/r3-Berichte und Belege bleiben unverändert.
