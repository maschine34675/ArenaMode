# Fokussierter GPU-Nachtest r4: Panels, Extract-Balken und HUD-Lesbarkeit

Führe diesen begrenzten Test auf dem Spiel-PC selbstständig durch. Installation
`C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`. Zuerst `AGENTS.md`,
den aktuellen Lieferabschnitt in `HANDOFF.md` und
`GPU-PANELS-RETEST-RESULTS-2026-09-11.md` und
`GPU-PANELS-R3-RESULTS-2026-09-11.md` lesen. Alle bisherigen Berichte bleiben
unverändert. r3 wurde vor Spielstart wegen der inzwischen neueren DLL blockiert;
dieser r4-Auftrag bindet ausdrücklich die neue Lieferung ein. Kein DLL-Rücktausch
nötig. Dieser Auftrag startet keine anderen Tasks/Automationen.

## Verbindlicher Teststand

- Job-ID: `arena-panels-native-ui-20260911-r4`.
- Quellcommit: `e37c3b69e236e5730d95dc86fa96270d8d7f4cdc`.
- Client-SHA256: `857206A4BF295607130B6F54A2A9CDECDB3F5A92B53784F9C644F7FC5199F1BB`.
- Clientdatei: `C:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll`.
- Enthält alle bisherigen Panel-/Granatenkorrekturen. Neu: Arena-HUD wird auf
  nativen Inventar-, Pausen- und Einstellungsseiten nicht gezeichnet; die Panels
  lesen nach der Rückkehr sofort frische Werte. Auch der grüne Extract-Balken
  wird verborgen, der numerische Raidtimer bleibt erhalten.
- Zusätzlich: Killfeed mit dunklen Zeilenflächen, 18px kräftiger Schrift und
  4,5 Sekunden voller Deckkraft vor 1,5 Sekunden Ausblenden. Maximal sechs Zeilen;
  lange Texte enden innerhalb der Fläche mit „…“. Mittige Statusleiste 110px
  unter dem oberen Rand, Team-Wartemeldung darunter bei 190px (1920×1080).

Vor dem ersten Spielstart und nach Abschluss den DLL-Hash vergleichen; zusätzlich
Repository-HEAD und Server-DLL-Hash protokollieren. Die Lieferung unter D:\SPT41
beweist keine erfolgte Spiegelung auf diesem PC. Bei abweichendem Hash oder
unklarem geladenem Build die betroffenen Tests blockiert lassen, nichts ersetzen.
Der Quellcommit bezeichnet den DLL-Build; spätere reine Dokumentationscommits
im Repository sind erlaubt und kein Hashkonflikt. Die DLL muss exakt passen.

## Grenzen und Vorbereitung

- Ausschließlich das vorhandene separate Testprofil verwenden und im nativen
  Launcher prüfen; niemals mit dem Hauptprofil testen. Falls keines verfügbar
  ist, nur über die normale Oberfläche ein separates Testprofil anlegen.
- Fremde Tests/Prozesse nicht beenden. Vor eigenen Änderungen konsistente
  Profil- und Einstellungssicherungen bei beendetem Spiel/Server erstellen.
  Für diesen Clienttest ist ein frischer Spielprozess nötig, kein Serverupdate.
- Kein Build, DLL-Tausch, Mod-Update, Pull/Merge/Reset/Push, Profil-JSON-Edit oder
  Recovery-Eingriff. Keine anderen Mods entfernen; keine Items per Code erzeugen.
- Die vorhandene native Computer-Use-Schnittstelle und echte Screenshots prüfen.
  Nur dokumentierte Eingaben verwenden. Nicht verfügbare Eingaben/Unterfälle
  offen ausweisen, keinen Browser-Nachbau oder erfundenen Sichtbeleg liefern.
- Vorhandene Panelpositionen, insbesondere Status (24,245), zunächst beibehalten.
  Absichtlich geänderte Einstellungen anschließend nativ zurückstellen, ohne
  fremde Änderungen zu überschreiben. Alte RaidInfoPanels-CFG nicht entfernen.

Rohbelege, private Sicherungen, Screenshots, Logs und `test-stand.json` unter
`artifacts/gpu-panels/arena-panels-native-ui-20260911-r4/<Zeitstempel>/` ablegen,
nicht committen. Tatsächliche Auflösung und Aufnahmezeiten angeben; Clientzeiten
sind lokal, Serverlogs UTC. Für jeden Arena-Raid eine eigene Profilbaseline vor
und nach dem regulären Ende vergleichen; normalen PMC separat behandeln.

## Prüffälle

| ID | Vorgehen | Erwartung / erforderlicher Beleg |
|---|---|---|
| R01 | Endless Deathmatch, Factory tagsüber, volle Karte, ohne PFT, vorhandenes Standardkit starten. Erste spielbare Bilder bis etwa 10 Sekunden aufnehmen. | Panels sichtbar, sobald eigener Körper und Kit bereit sind. Keine benannten Exit-Zeilen und kein grüner Balken mit „Find an extraction point“, Icon oder leerem Hintergrund. Kleine numerische Uhr erlaubt. Diskrete Screenshots nicht als lückenlose Aufnahme ausgeben. |
| R02 | Inventar per Tab öffnen, kurz ansehen und wieder schließen; mindestens zweimal. Ebenso Esc/Pause und eine native Einstellungsseite öffnen/zurückkehren. Vorhandenen Lootscreen nur zusätzlich prüfen, wenn zugänglich. | Im Inventar keine Status-/Waffenpanels und keine obere Arena-Punkteanzeige über nativen Slots/Tabs. Auch Pause/Settings frei von diesem Arena-Canvas. Im Spiel alle vorgesehenen Anzeigen wieder da, genau einmal und an unveränderten Positionen. Inventar- und Rückkehrbilder sichern. F12 ist ein separates Modfenster und darf die laufenden HUD-Einstellungen weiterhin sichtbar ändern. |
| R03 | Im Spiel einige Schüsse abgeben, nativ nachladen und den Bestand im Inventar ansehen. Danach sofort schließen und HUD aufnehmen. Bei vorhandener Granate G ziehen/werfen und erneut vergleichen. | Inventarvergleich erfolgt jetzt mit aufeinanderfolgenden Bildern, weil HUD dort absichtlich verborgen ist. Nach Schließen sofort richtige Patronen-/Reserve-/Granatenwerte, kein eingefrorener vorheriger Stand. Bereits in r2 geprüfte gepanzerte Rigs nicht als neue Vollabnahme aller Kits ausgeben. |
| R04 | Über native Arena-Aufgeben-Funktion einen Respawn auslösen, sofern sicher bedienbar. Danach Inventar öffnen/schließen. | Frische Körper-/Kitwerte und korrektes Wiedererscheinen der Panels. Fehlende Bilder der kurzen Tod-/Killcam-Phase ausdrücklich offenlassen. Kein PFT-Setup künstlich erzeugen. |
| R05 | Arena regulär verlassen, ins Menü und eine zweite kurze Arena mit gleicher Auswahl starten. | Menü ohne HUD-Reste. Zweiter Start ebenfalls ohne Liste und grünen Balken; frische Panels ohne Duplikate. R02 kurz wiederholen. |
| R06 | Danach normalen PMC-Raid mit demselben Testprofil starten. Einfaches O prüfen; tatsächlichen Extract nur bei sicher erreichbarem Zugang zusätzlich betreten. | Normale Startliste MIT grünem Balken wieder vorhanden; keine Arena-Panels/Punkteanzeige. Numerischer Timer bleibt bedienbar. Tatsächlichen Countdown nicht als getestet markieren, falls kein Extract erreicht wird. Normale PMC-Profiländerungen sind getrennt von Arena und erwartet. |
| R07 | Nur falls die dokumentierte Schnittstelle einen zuverlässigen Doppel-O-Druck erlaubt: in Arena und normalem PMC auslösen, in Arena zusätzlich mit abgeschaltetem HUD-Schalter. | Arena: weder Liste noch Balken; PMC: native Anzeige. Bei derselben bekannten Eingabegrenze wie r2 ausdrücklich nicht ausgeführt. Keine langsamen Einzeldrücke als Doppel-O werten und keine weiteren vergeblichen Umbelegungsserien. |
| R08 | Während der geplanten Arena-Raids Killfeed-Meldungen vor hellen und dunklen Flächen aufnehmen. Falls dafür nötig, andere Bot-Kills über die native F12-Einstellung vorübergehend einblenden. Bei natürlich auftretenden vielen Kills volle sechs Zeilen und deren Wiederverwendung beobachten; lange Namen und den eigenen Tod bei Gelegenheit mit aufnehmen. | Dunkle Fläche und kräftiger Text gemeinsam sichtbar; neue/wiederverwendete Meldungen sofort voll sichtbar. Erst nach etwa 4,5 Sekunden gemeinsames Ausblenden, nach sechs Sekunden entfernt; kein leerer Hintergrund bleibt stehen. Keine überlappenden Zeilen. „…“ bei langen Einträgen ist beabsichtigt; fehlende Enddetails als Lesbarkeitsgrenze festhalten. Genaue Fade-Zeit ohne hinreichend dichte Aufnahmen nicht als gemessen ausgeben. Nicht erreichte Vollbelegungs-/Langnamenfälle offenlassen. |
| R09 | Bei geschlossenen Menüs die obere Statusleiste zusammen mit sichtbaren Quickslots aufnehmen. Wenn im vorhandenen unabhängigen Team-DM ohne PFT möglich, für den zweiten geplanten Arena-Start diesen Modus auf derselben Karte nutzen und die Team-Wartemeldung aufnehmen; sonst R05 unverändert durchführen. | Statusleiste unter den Quickslots, keine Kollision ihrer beiden Textzeilen mit Schnellzugriff oder Killfeed. Team-Wartemeldung darunter mit Abstand, sofern gezeigt. Nach Inventar/Pause gleiche Position. Tatsächliche Auflösung nennen; weitere Seitenverhältnisse nur prüfen, wenn nativ verfügbar. Keine zusätzlichen Raids allein für optionale Banner-/Auflösungsfälle. |

R01/R02/R03/R05, Startliste/Timer-Teil von R06 sowie die grundlegende Lesbarkeit
in R08 und der Quickslot-Abstand in R09 sind die Kernnachweise.
R04 und optionale Unterfälle ergänzen sie. Der r4-Bericht ersetzt keine noch
offenen PFT-, Rucksack-/verschachtelten Granaten-, Auflösungs- oder Übergangstests
aus r2. Die Verschiebung der oberen Punktzahl wird jetzt ausdrücklich in R09
geprüft; sie ist noch kein Nachweis für beliebige Quickslot-Positionen oder
Seitenverhältnisse.

## Logs, Fehler und Abschluss

Pro Arena-Start werden je ein Panel-Aufbau- und Listenmaskierungsmarker sowie
`Arena extraction header hidden; numeric timer retained.` erwartet. Keine neuen
Panel-/Header-Ausnahmen oder CanvasGroup-Spam. Marker ergänzen Sichtbelege,
ersetzen sie aber nicht. Andere Bot-/SAIN-/AsyncUpload-Fehler separat mit Stack
und Zeitpunkt protokollieren; daraus keine UI-Ursache ableiten.

Bei einem reproduzierbaren Fehler sofort Bild und vollständigen relevanten
Exception-Stack sichern. Höchstens eine gezielte Wiederholung, keine Fixes oder
endlosen Raidstarts. Unabhängig mögliche Tests fortsetzen. Nach den geplanten
zwei Arena- und einem PMC-Raid keine zusätzlichen Versuche ohne konkreten Bedarf.
Eigene Prozesse regulär beenden, eigene Testeinstellungen zurückstellen und
abschließend DLL-/Einstellungsvergleiche durchführen.

Schreibe `GPU-PANELS-R4-RESULTS-2026-09-11.md`: tatsächlicher Teststand, je Fall
bestanden/fehlgeschlagen/blockiert/nicht ausgeführt, Beobachtung, Belegpfade und
verbleibende Grenzen. Keine Profilkennungen, Geheimnisse, kompletten Profile
oder privaten Rohlogs in den Bericht übernehmen. Ist das Repository frei von
fremden Änderungen, nur diesen neuen Bericht committen, mit
`Co-authored-by: Codex <codex@openai.com>`. Sonst Bericht belassen und die fremden
Änderungen unangetastet lassen. Kein Push, keine früheren Berichte überschreiben.
