# GPU-Nachtest: native Menü-Rückkehr und Bot-Waffenwechsel, r6

Arbeitsauftrag auf dem Spiel-PC: zuerst AGENTS.md und den neuesten HANDOFF-Eintrag
lesen. Repo `C:\SPT41\Development\ArenaMode`, Installation `C:\SPT41`. Ausschließlich
native Bedienung und Beobachtung; keine Produktionsänderungen, Builds, DLL-Tausche,
Harmony-/Speicherinstrumentierung, Arm-Aufrufe oder erzwungenen Matchereignisse.

## Verbindlicher Teststand

- Test-ID: `deathmatch-menu-exit-r6`.
- Source-/Build-Commit: `0b7f38d594b09644803f263a510543293b11ed63` (clean beim Build), Branch `feature/arena-air-control`.
- Client SHA256: `2042C558340877C85DAF0687A8AEBB4E7D54FD720AD467E770F5AEE7F0914ECD`.
- ProductVersion: `0.1.0+0b7f38d594b09644803f263a510543293b11ed63`.
- Server unverändert: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
- Redeploy.Core unverändert: `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`.
- SAIN 4.5.1.0: `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.
- Build-Manifest: `artifacts/double-jump-build/f4f38f3505ba4d9ca1976ebccf2a4a5a/manifest.json`.
- Lokale Lieferung/Backup: `artifacts/air-control-delivery/2d2989b50ded4bcd93571978283a7754/`.

GPU-Synchronisierung und tatsächliche Hashes vor Start, vor jedem Raid und am Ende
prüfen. Spiel nach Clientwechsel neu starten; Serverwechsel/-neustart ist für
diesen Client nicht nötig. PID/Startzeit beider Prozesse, Git-HEAD, Auflösung und
DLSS-Modus festhalten. Kein Neubau wegen eines späteren Dokumentationscommits.

Startup benötigt beide Marker:
`Arena input blocker: native menu resume release safeguard ready.`
und `Arena: retired bot weapon callback safeguard ready.`
Fehlender Marker oder passende unavailable-Meldung: vor dem Raid blockiert berichten.
Air Control ist enthalten, wird aber separat unter Refinements r4 geprüft.

## Was bereits belegt ist

Deathmatch r5 (`0a49898`) hat vier isolierte F9-Close-Vergleiche ohne Schuss,
anschließende normale Bedienung, dreirundigen Autoexit sowie einen FFA-Autoexit
bestanden. Vollständiges terminales F9/Banner-Toggling, Score-Freeze über Zeit,
rechtzeitiger Ergebnisbutton, Continuous/Give up und einige Normal-/Tactical-
Unterfälle blieben offen. Diese Befunde am alten Client bleiben unverändert.

Refinements r3 (`bd95b72`) stoppte an einem neuen BotWeaponSelector.OnWeaponTaken-
NRE. Ein verspäteter Rückruf nach Bot-/Waffendienst-Abbau ist jetzt gezielt
abgesichert; der historisch genaue Bot und Null-Zugriff sind nicht bewiesen.
Zusätzlich verlor die AK-12 über eine 64-Sekunden-Folge mit Escape und sechs
Grafik-/Resume-Klicks eine Patrone. Der einzelne auslösende Klick ist unbekannt.
Die neue Absicherung erweitert den vorhandenen Eingabe-Drain auf native
Menü-Rückkehr. Keine Behauptung, DLSS oder der F9-Fix hätten den Fehler verursacht.
Siehe NATIVE-MENU-INPUT-REVIEW.md und BOT-WEAPON-CALLBACK-REVIEW.md.

## Schutz, Budget und Belege

Diese Anweisung erlaubt ausschließlich den beschriebenen Nachtest am korrigierten
Build nach dem r3-Stopp. **Jede neue Exception, Softlock, Fehlerflut, Profilrisiko,
Hashwechsel oder Schuss durch UI-Bedienung stoppt den gesamten Lauf erneut.**
Regulär aussteigen, keine weiteren Testfälle/Raids; Belege sichern. Bekannte reine
Lade-/Culling-Warnungen separat zählen. Kosmetischer FAIL darf unabhängig weiter
geprüft werden. Keine neue Exception wegen ihrer Herkunft für harmlos erklären.

Maximal **3 Raids / 20 Minuten aktive Testzeit**; Lade-/Werkzeugzeiten separat
ausweisen. Budgetende: sicher aussteigen, Rest OFFEN. Keine Zusatzraids zum
Erzwingen seltener Callback-/Cleanup-Pfade. Andere Mods und Audio unangetastet.
Aktuelle Einstellungen vorher sichern, nur eigene Testwerte regulär zurückstellen;
keine alten cfg/client-settings-Dateien oder Metadaten wiederherstellen.
Neue Air-Control-Schlüssel können beim Erststart regulär ergänzt werden.

Profile privat nur lesen/kopieren, niemals zurückkopieren oder bearbeiten.
Nach bestätigter Hauptmenürückkehr jedes Arena-Raids: PMC und Scav jeweils
Inventory, Quests, Skills, Health, Experience, Level, TaskConditionCounters,
RepeatableQuests vergleichen und vollständige JSON-Differenzen konkret nennen
(z.B. Hideout-Heartbeat). Native Endscreen-EXP beweist keine Profiländerung.

## A — zuerst native Menüklicks isolieren, danach natürliche Bot-Wechsel

Customs morgens / unabhängiges **Team Deathmatch 5v5**, Contour
`customs-factory-center-teams-v1`, 50 Kills / 10 Minuten. Keine PFT-Gruppe.
Für dieses Diagnose-Raid AirControlEnabled vorübergehend aus; Rückstellung
dokumentieren. Natürliche Bot-Tode/Respawns zulassen, nicht intern erzwingen.

Bei sicherer Gelegenheit zur Wand blicken und Waffenidentität, Magazin UND
Kammer dokumentieren. Escape öffnen, dann **genau einen Resume-Klick**: unmittelbare
Vorher-/Nachherbilder oder Originalclip samt Eingabezeiten. Keine weiteren
Schuss-, Reload- oder Waffenaktionen innerhalb dieses Vergleichs. Danach separat
Escape -> Settings -> Graphics -> DLSS wählen -> Save -> Resume. **Jeden einzelnen
Klick separat mit Originalbild des sichtbaren Menüs und Eingabezeit dokumentieren**;
Munition und Waffenidentität jeweils dort erfassen, wo sie tatsächlich sichtbar
sind. Im Grafikmenü ist der Waffen-HUD verborgen: diese Segmentgrenze offenhalten,
keine Einzelklick-Kausalität behaupten und keine Zusatzschleifen erzwingen.
Der isolierte Resume-Vergleich bleibt die wichtigste Einzelklick-Prüfung. Keine
weit auseinanderliegenden Gesamtbilder als eindeutige Klickursache werten.
DLSS nur ändern, wenn verfügbar; ansonsten SKIP und native Settings/Resume prüfen.
Nach Save prüfen, ob wirklich BattleUI oder weiter ein natives Menü aktiv ist.

Tod/Kitwechsel/unklare Munition unterbricht den Vergleich: UNKLAR, höchstens
3 Versuche insgesamt im selben Raid. Jede beobachtete UI-Schussabgabe stoppt.
Nach sauberen Vergleichen separat Bewegung, bewussten Schuss, Reload und erneute
Pause/Escape/Tab-Navigation prüfen: kein dauerhafter Input-Lock. Ein einzelner
F9-Close-Smoke-Test genügt; die vier alten Vergleiche nicht als neue zählen.

Inputmarker mit Zeit, Generation, `sample` und `blockedShotCommands` sichern:
`Arena panel input: native menu released gameplay input; pointer drain pending`
sowie vorhandene Pending/Released-Zeilen. Maximal 24 Zeilen pro Prozess; danach
fehlende Zeilen nicht als fehlende Funktion werten. 0 abgefangene Schusskommandos
kann bei einem sauberen Klick korrekt sein.

Danach im Restbudget natürliche Bot-Ersatzaktivierungen und eigenes Respawn
beobachten: Teams weiter vollständig, Bots bewegen sich und schießen. Zeitpunkte
und volle Stacks jedes Fehlers sichern. Eventuelle
`Arena bot weapon callback: ignored retired selector; state=..., dead=..., managerDisposed=...`
zählen. Keine solche Meldung heißt **Pfad unbewiesen**, auch bei fehlerfreiem Raid.
Ebenso Pickup-/SAIN-Teardownmarker zählen, ohne sie erzwingen zu wollen.
Regulärer Ausstieg, Logdelta bis Hauptmenü und Profilvergleich vor B.

## B — noch offene Ergebnisanzeige und Ergebnisbutton

Nur nach sicherem A und mit Restbudget: Factory Tag / Deathmatch FFA /
1 Runde / Kills Off / 2 Minuten. Schon vor Ergebnis F9 vorbereiten. Ergebnis,
Sieger/Grund und Countdown lesbar, großes Banner nur bei geschlossenem F9.
Schließen/Öffnen darf Countdown nicht neu starten oder verlängern. Score anhand
zeitlich getrennter Originalbilder über die Ergebnisdauer prüfen.
**Return to menu vor Ablauf** drücken und Eingabezeit gegen Autoexit-Commit im
Log abgleichen. Gewinnt Autoexit das Rennen, Button bleibt OFFEN. Nicht die
Ergebnisdauer künstlich verändern. Drei-Runden-Autoexit wurde bereits r5 belegt;
dieser kurze Fall ist keine erneute Drei-Runden-Abnahme.
Logdelta und Profilvergleich nach Hauptmenü.

## C — Continuous / Give up, nur bei Restbudget

Factory / Deathmatch FFA / Endless / beide Limits Off, etwa 90 Sekunden.
Keine unbeabsichtigte Rundenpause; einmal regulär Give up, Respawn prüfen,
anschließend regulär verlassen. Separater Log-/Profilvergleich.
Ohne Zusatzraid ggf. gewöhnlichen PMC-Menüweg bis vor Ready und zurück sowie
Hideout/Weapon Builder kontrollieren. Tactical-Kaufmenü bleibt ohne erreichten
Tactical-Raid OFFEN. Für nichts davon einen vierten Raid starten.

## Bericht

`GPU-DEATHMATCH-MENU-TEST-RESULTS-r6.md`, Originalbilder/-clips und SHA256-Manifest
unter `test-evidence/deathmatch-menu-exit-r6/`; vollständige Logs/Profile nur im
ignorierten `artifacts/`. Private Kennungen in gezielten Auszügen redigieren.
Pro Unterfall PASS/FAIL/OFFEN/SKIP, tatsächliche Raids/Runden, Zeitbudget,
Buildidentitäten, Steuerhandlungen, Warn-/Fehlerzahlen, volle relevante Stacks,
Schutzmarkerzahlen und raidweise Profilvergleiche nennen. R5 und Refinements r3
nicht überschreiben. Nur eigenen Bericht/Belege committen.

Nach sicherem A darf der separate Auftrag
`GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r4.md` folgen, sofern auch danach kein
weiterer Gesamtstopp eintrat. Ein früheres A-PASS hebt keinen späteren Stopp auf.
