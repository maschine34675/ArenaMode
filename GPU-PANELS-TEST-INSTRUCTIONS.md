# Auftrag: Arena-Panels und native UI auf dem Spiel-PC prüfen

Führe diesen begrenzten Test selbstständig auf dem GPU-PC durch. Installation:
`C:\SPT41`, Repository: `C:\SPT41\Development\ArenaMode`. Lies zuerst `AGENTS.md`,
den aktuellen Lieferabschnitt in `HANDOFF.md` und `RAID-INFO-PANELS-REVIEW.md`
einschließlich der Granaten-Ergänzung. Das ist ein **Testauftrag**, keine
Implementierung und kein Auftrag, andere Tasks zu starten oder zu benachrichtigen.

## Verbindlicher Teststand

Job-ID: `arena-panels-native-ui-20260911`.

- Quellcommit: `18267c86f85061d0603a70c6aecfe42fc6615000`.
- Client-SHA256: `D927D5B98D654BE63DDB25BD41727408FC86C1B488165C7666D283898F512ABE`.
- Enthält die absorbierten Panels, die Granatenanzeige aus `2b86e3a`, die
  Arena-Extract-Sperre und beide gekürzten Beschreibungen. Die parallel noch
  uncommittete Änderung am Granaten-Picker gehört nicht zu diesem Build.
- Clientänderung: neuer Spielprozess nötig; kein Serverupdate erforderlich.
  Die lokale Lieferung unter `D:\SPT41` beweist nicht den Spiegelstand auf
  dem GPU-PC. Dort unbedingt den folgenden Hashvergleich durchführen.

Prüfe vor dem ersten Spielstart den Hash von
`C:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll` und protokolliere zusätzlich
den tatsächlichen Repository-HEAD und Server-DLL-Hash. Ein Repository-Commit
allein beweist keinen installierten oder vom laufenden Prozess geladenen Build.
Bei abweichender DLL oder einem bereits laufenden Prozess mit unklarem Build
keine Ergebnisse diesem Job zuschreiben und nichts selbst überschreiben.

Nutze die vorhandene lokale Computer-Use-Schnittstelle für echte Launcher- und
Spieloberflächen. Prüfe ihre aktuellen Möglichkeiten und einen tatsächlichen
Screenshot, bevor du eine Bedienbeschränkung annimmst. Kein Browser-Nachbau als
UI-Nachweis, keine Remote-Desktop-Fähigkeit voraussetzen, keine undokumentierte
Eingabeschnittstelle ergänzen. Wenn native Darstellung oder nötige Eingaben
fehlen, alle noch möglichen Vorprüfungen erledigen und den betroffenen Test
konkret als blockiert ausweisen. Keine Hintergrund-Automation anlegen.

## Sicherheit und Vorbereitung

- Ausschließlich ein separates vorhandenes Testprofil benutzen, etwa das bei
  früheren GPU-Tests angelegte Profil; dessen Namen/Existenz im Launcher prüfen.
  Falls keines vorhanden ist, ein separates Standard-Testprofil über die native
  Oberfläche anlegen. Niemals mit dem Hauptprofil ins Spiel oder in einen Raid.
- Einen fremden laufenden Test nicht beenden. Spiel und Server nur regulär
  starten/beenden, soweit sie zu deinem Test gehören. Vor Veränderungen am
  Testprofil eine konsistente Sicherung bei beendetem Spiel und Server erstellen.
- Kein Build, DLL-Tausch, Mod-Update, Pull, Merge, Reset, Push, Profil-JSON-Edit
  oder Recovery-Manipulation. Die bestehende Spiegelung nicht umkonfigurieren.
  Den früheren eigenständigen RaidInfoPanels-Checkout nicht wiederherstellen.
- Aktuelle Arena-/Grafik-/Eingabeeinstellungen lokal sichern und später die
  absichtlich geänderten Testeinstellungen über die normale Oberfläche
  zurückstellen. Keine fremden Konfigurationsänderungen zurückrollen.
- Die alte `com.maschine.RaidInfoPanels.cfg` belassen: sie dient als Quelle für
  die erste Migration. Existiert die neue Arena-Konfiguration bereits, haben
  deren gespeicherte Werte Vorrang. Keinen vorhandenen Wert löschen, um eine
  künstliche Erstinstallation herzustellen.
- Für den Test einen frischen Spielprozess mit dem geprüften Client verwenden.
  Ein Serverneustart ist für diese Änderung nicht nötig, außer für eine
  konsistente Profilsicherung oder wenn der Server noch nicht läuft.

Belege unter `artifacts/gpu-panels/arena-panels-native-ui-20260911/<Zeitstempel>/`
ablegen: `test-stand.json`, `screenshots/`, `logs/`, gesicherte Einstellungen und
private Profilkopien. Diese Rohdaten bleiben Git-ignoriert. Screenshots mit
Prüffall und Zeitpunkt benennen. Tatsächliche Auflösung protokollieren; eine nur
aus Auflösung abgeleitete Skalierung nicht als gemessenen Canvas-Wert ausgeben.
Clientlog-Zeiten sind lokal, Serverlogs UTC.

## Prüffälle in dieser Reihenfolge

| ID | Aktion | Erwartetes Ergebnis / erforderlicher Beleg |
|---|---|---|
| P01 | Im nativen Startbildschirm ARENA wählen; dann PMC, Scav und wieder ARENA. Einmal zurück/vorwärts navigieren. | ARENA-Beschreibung höchstens zwei sichtbare Zeilen, vollständig lesbar, keine sichtbaren `<nobr>`-Tags, kein Abschneiden/Überlappen. PMC/Scav zeigen wieder ihren normalen Text. Screenshots von ARENA und PMC. |
| P02 | Optional: vorhandene gültige PitFireTeam-Auswahl mit vier Begleitern aufrufen. | Eigener Tactical-5v5-Text ebenfalls höchstens zwei sichtbare Zeilen. Keine fehlende PFT-Gruppe künstlich erzeugen; ohne vorhandene Voraussetzungen separat „nicht ausgeführt“. |
| P03 | Arena → Endless Deathmatch → Factory tagsüber, volle Karte, ohne PFT starten. Ein verfügbares Standardkit verwenden; keine Datenbank-/Kitdateien editieren. | Vom Übergang in die spielbare Szene an erscheint keine große native Extract-Liste. Kleine Raidzeit darf nativ sichtbar bleiben. Frühe Bilder/kurze Aufnahme bei Start und nach etwa 2 und 8 Sekunden sichern. Match-/Kitbereitschaft vor Inventarvergleichen abwarten. |
| P04 | Im lebenden Zustand die tatsächlich belegte Aktion „Timer und Exits“ auslösen (standardmäßig zweimal O). Anschließend die reine Timer-Aktion (einmal O) prüfen. | Exit-Liste bleibt weg; reine Raidzeit bleibt bedienbar. Pause/Esc und Inventar funktionieren. Eine zu langsame Tastensequenz nicht als erfolgreichen Doppel-O-Test werten: Belegung und tatsächliche Eingabemöglichkeit prüfen. |
| P05 | Beide Arena-Panels bei geladenem Kit und geöffnet/geschlossenem Inventar ansehen. | Links HP/Gewicht/Rüstung/Status, rechts Waffe/Munition/Magazinreserve/Granaten. Texte und Icons lesbar, keine doppelten Altpanels; keine Kollision mit Killfeed, Schnellzugriff, Stamina oder anderen nativen Anzeigen. Übersicht und lesbare Detailbilder. |
| P06 | Einige Schüsse abgeben und mit normaler Nachladetaste nachladen. Inventar vor/nachher vergleichen; wenn vorhanden ein gepanzertes Rig benutzen. | Magazinzähler, Kammer/Patronentyp und volle/angebrochene Reserve passen zum wirklichen Inhalt. AVS/TV-110 und andere TacticalVest-Rigs zählen auch mit Panzerung. Im Rucksack liegende Magazine getrennt ausgewiesen. Falls kein gepanzertes Rig verfügbar, diesen Teil ausdrücklich offenlassen. |
| P07 | Mit dem Standardkit die RGD-5-Anzahl vergleichen, per G ziehen und kontrolliert in freien Bereich werfen; anschließend ggf. zweites Exemplar werfen. | Frag-Zahl passt, sinkt nach Verbrauch, bleibt mit Granate in der Hand sichtbar und verschwindet bei null vorhandenen Granaten. Keine Waffenzeile mit veralteten Werten. Fehler beim Ziehen getrennt von falscher Anzeige protokollieren. |
| P08 | Soweit das Testinventar es nativ erlaubt: eine Granate ins Rucksack-Hauptraster, dann in eine geeignete verschachtelte Tasche legen; passende Flash/Smoke-Granaten ergänzend testen, wenn vorhanden. | „Ready“ folgt der nativen G-Kandidatenliste. Mit installiertem UseItemsAnywhere zählt eine lose Rucksackgranate als bereit; verschachtelte, nicht direkt erreichbare Reserve erscheint gedimmt als `+N`. Keine Launcherpatrone als Handgranate zählen. Nicht verfügbare Behälter/Granatentypen als nicht ausgeführt dokumentieren, keine Items per Code erzeugen. |
| P09 | Über F12 → ArenaMode → `HUD Panels - ...` Position/Skalierung/Deckkraft sowie Waffen- und Statuspanel einzeln ändern und zurückstellen. | Änderungen greifen, kein neues Duplikat, jedes Panel separat abschaltbar. Vorherige Werte wiederherstellen. Statusoffset `(24,245)` nur erwarten, wenn die tatsächliche alte CFG ihn enthielt und kein neuer Arena-Wert Vorrang hat. |
| P10 | Arena-HUD-Hauptschalter und Panel-Hauptschalter kurz abschalten; Doppel-O erneut prüfen. | Panels folgen ihren Schaltern, die native Exit-Liste bleibt unabhängig davon unterdrückt. Danach beide Schalter wieder auf die vorherigen Werte stellen. |
| P11 | In der laufenden Arena einen normalen Tod/Respawn zulassen; falls die sichere native Aufgeben-Funktion verwendet wird, dies gesondert nennen. Danach erneut Inventar und Panels prüfen. | Während Tod, Killcam und Ersatzkörper-Aufbau keine eingefrorenen eigenen HP-/Waffendaten. Nach Respawn frische HP-/Kit-/Magazin-/Granatenwerte; weiterhin nur ein Panelsatz und keine Exit-Liste. Natürliche Blutung/Fraktur/Rüstungsschäden auf passende Icons/Farben prüfen, falls beobachtet. Nicht beobachtete Zustände nicht als bestanden markieren. |
| P12 | Optional bei bestehendem funktionsfähigem PFT-Setup nach Tod zuschauen. | Eigene Panels im Zuschauerzustand verborgen; vorhandene Bot-HP-Anzeige bleibt separat funktionsfähig. Ohne Setup ist dies kein Pflicht-Blocker für die übrigen Paneltests. |
| P13 | Arena regulär verlassen, ins Menü/Hideout und einen zweiten kurzen Arena-Raid starten. | Keine übrig gebliebenen Panels im Menü, keine Verdoppelung beim zweiten Start; richtige neue Werte und erneut keine automatische Exit-Liste. |
| P14 | Mit demselben separaten Testprofil einen normalen PMC-Raid starten, keine Arena-Auswahl. Startliste und Doppel-O prüfen. Falls sicher erreichbar, kurz einen normalen Extract betreten und dessen Countdown beobachten. | Keine Arena-Panels. Native Start-/Doppel-O-Liste wieder sichtbar; Countdown bleibt normal. Native Extraktionsmechanik nicht durch Abkürzungen oder direkte Zustandsänderungen ersetzen. Countdown-Teil als nicht ausgeführt ausweisen, wenn nicht erreichbar. |

P01/P03–P07/P09–P11/P13/P14 sind Kernprüfungen; nicht verfügbare Unterfälle bleiben
sichtbar offen. Die genannten Tests geben keine allgemeine Freigabe für alle
Kits, Karten, Auflösungen oder PFT-Zustände. Bei normalem PMC-Raid dürfen
Testprofiländerungen entstehen; Arena-Tests müssen getrennt gegen ihre
gesicherte Ausgangslage geprüft werden.

Falls das Spiel eine weitere Auflösung ohne Eingriff in die Desktop-/RDP-Sitzung
anbietet, P01 und P05 dort zusätzlich wiederholen und anschließend zurückstellen.
Keine frei erfundene Auflösungsabdeckung behaupten.

## Fehlerbehandlung und Ergebnis

Bei Fehlern Beleg und zugehörigen Logausschnitt sofort sichern. Höchstens eine
gezielte Wiederholung mit unverändertem Build, um einen Bedienfehler abzugrenzen;
keine endlosen Raidstarts, keine Fehlerbehebung während dieser Abnahme. Wenn
ein vorgeschalteter Fehler einen Test verhindert, andere unabhängige Fälle
fortsetzen. Vor einem Neustart das aktuelle Log sichern.

Besonders unterscheiden: Ein bereits separat bearbeiteter Fehler mit einer
veralteten bevorzugten Granate kann G nach einem Wurf blockieren. Das ist nicht
automatisch ein Fehler des Zählers. Tatsächliches Ziehen, Inventarbestand und
Panelwert getrennt dokumentieren; mit dem installierten Build prüfen, ob der
Fehler überhaupt noch vorliegt. Keine andere Mod ohne Auftrag entfernen.

Abschließend Testeinstellungen zurückstellen und eigene Prozesse regulär beenden.
Clienthash erneut prüfen. Eine Änderung während des Tests macht die Zuordnung
der betroffenen Beobachtungen unklar und muss im Ergebnis stehen.

Schreibe `GPU-PANELS-TEST-RESULTS-2026-09-11.md` mit tatsächlichem Teststand,
einer Zeile pro Prüffall (**bestanden / fehlgeschlagen / nicht ausgeführt /
blockiert**), Beobachtung, Screenshot-/Logpfad und den offenen Grenzen.
Code-/Offlinechecks sind kein visueller Beleg. Keine Profil-IDs, Zugangsdaten,
vollen Profile oder komplette private Logs in den Bericht übernehmen.

Wenn das Test-Repository keine fremden Änderungen enthält, nur diesen Bericht
als Dokumentationscommit festhalten, mit
`Co-authored-by: Codex <codex@openai.com>`. Keine Binärdateien/Rohbelege committen,
keinen Push ausführen. Vorhandene fremde Änderungen nicht anfassen; in diesem
Fall Bericht schreiben und die Commit-Situation klar nennen.
