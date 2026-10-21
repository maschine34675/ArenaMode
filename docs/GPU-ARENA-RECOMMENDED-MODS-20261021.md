# GPU-Nachtest: KI-Pakete im privaten Arena-Starter

Stand: 2026-10-21. Nachtest des unveränderten Starter-Builds aus `6f12117`
auf SPT-GPU-2, nachdem der Host wieder erreichbar war. Keine Gameplay-Codeänderung,
kein Build/Deployment in die normale Installation und keine Veröffentlichung.

## Artefakt und Umgebung

- Gestartet: `C:\Temp\ArenaLauncher-Recommended-20261021\maschine-ArenaLauncher.exe`
  mit `--source C:\SPT41 --diagnostics`.
- SHA256: `D0F8D0BC75C1EFAA39E4CD47A8FDBAF4C2E116A0CE6001564AA81606DABFA5E6`,
  identisch zum lokalen SingleFile-Artefakt aus dem
  [Paketbericht](ARENA-START-RECOMMENDED-MODS-20261020.md).
- Quelle `C:\SPT41`, bestehende private Umgebung `C:\SPT41-Arena`, SPT 4.1.6,
  EFT 0.16.9.5.40743. Ausschließlich vorhandenes internes Profil `Arena-Internal`.
- Der erste Start übernahm **84 neue Paketdateien und drei aktualisierte
  Moddateien** aus der Quelle. Alle 84 KI-Paketdateien wurden anschließend einzeln
  gegen Quellhash und privates Manifest verglichen: identisch.
- SAIN 4.5.1, APBS 2.4.1, ABPS 2.1.0, BigBrain 1.5.0, Waypoints 1.9.0.
  APBS hat keine Clientkomponente. UiBridge bleibt ein bewusst zugelassenes
  Diagnose-Plugin; keine zusätzlichen Fremdmods wurden temporär installiert.

## Native Ergebnisse

| Prüfung | Ergebnis |
|---|---|
| Sichtbare WPF-Oberfläche | Alle fünf Pakete als installiert, keine fehlend; Quell-/Privatpfad, internes Profil, getrennte Kopien und gemeinsame Assets lesbar. Start erreichbar. Der Fortschritt zeigte beispielsweise `500 / 8239` und `8000 / 8239` geprüfte gemeinsame Dateien mit dem Hinweis `no copying`. |
| Erster Direktstart | Hauptmenü nach 189 Sekunden einschließlich Dateiprüfung und Modupdate. EFT und Server aus dem privaten Root; kein SPT-Launcher-Prozess. |
| KI-Initialisierung | Alle vier Clientplugins und drei Servermods geladen; APBS-Datenbank bereit, sechs SAIN-Defaults erzeugt, SAIN/ABPS-Anfragen erfolgreich. Arena bestätigt BigBrain-Bewegungsunterstützung. |
| Factory Tag, Deathmatch | Drei Teilnehmer, FFA, endlose Runde ohne Kill-/Zeitlimit. Exakt zwei aktive Bots. Bewegung durch die Karte sowie Wechsel zwischen SAIN Combat/Avoid Threat und Arena Combat Pickup beobachtet. |
| Kampf und Nachbesetzung | Echte RSASS-Schüsse, Brust-Kill an einem USEC, 200 Punkte und ein Kill. Neuer USEC als Ersatz; wieder zwei aktive Bots. Keine künstlich erzeugten Gegner und kein Kill-Hook. |
| Eigener Tod und Respawn | Nach Abschalten des anfänglichen Test-Godmodes durch BEAR/AKS74 getötet; automatischer Respawn mit Mosin und 440 HP, Zähler ein Kill/ein Tod. Der kurze Todeszustand wurde vom Polling verpasst, aber Todesursache und anschließender Körper-/Kitwechsel sind separat belegt. |
| Geschütztes Raidende | Normal über DISCONNECT und Ergebnisbildschirme ins Hauptmenü. Server bestätigt Wiederherstellung und Speicherung des Profil-Snapshots; gewöhnliche Nachbearbeitung übersprungen. Genau ein internes Profil, Level 1, 205 Inventargegenstände nach dem Raid. |
| Erstes EXIT | GUI zeigt `Session finished`; privater Server und EFT beendet. Starter bleibt bedienbar. |
| Zweiter Direktstart | Aus demselben GUI-Fenster bis Hauptmenü nach 116 Sekunden. Gleiche private Identität, neue EFT-Prozessinstanz. Kein SPT-Launcher. |
| Zusätzliche Startdiagnose | Nur für den zweiten Start privates `WriteUnityLog=true`: **11 Plugins erfolgreich initialisiert, 11 Plugin- und fünf Bibliotheks-MVIDs identisch** zwischen lokalem Build, Hostdatei und geladenem Code. Keine Warning-/Error-/Fatal-Zeile im zweiten Clientlog einschließlich EXIT. |
| Zweites EXIT | Wieder normal beendet; Server stoppt. Anschließend GUI geschlossen. Abschließende Prozessaufnahme ohne EFT, SPT-Server, SPT-Launcher oder ArenaLauncher. |

Die Testhilfen liefen nur im internen Einzelspielerprofil. Godmode diente dem
ersten Bewegungs-/Schusstest und war vor dem echten Spielertod ausgeschaltet.
Kein Infinite-Ammo-, GiveItem- oder SpawnBot-Hook. Kein Performancebenchmark:
die kleine Factory-Besetzung belegt keine Kapazität großer Zombie-Wellen.

## Logbefunde und Grenzen

Die vollständigen Logs wurden zusätzlich unabhängig gelesen. Kein neuer
Starter-/KI-Paketfehler, fehlgeschlagener HTTP-Request, APBS-Logger-Pfadfehler
oder PlayerBody-Bereinigungsfehler im geprüften Ablauf.

Der erste Raid ist dennoch **nicht vollständig ohne Fehlermeldungen**:

- Drei reguläre SPT-Brain-Korrektur-Warnungen beim initialen/ersetzten PMC.
- Sieben bekannte Factory-Spawnmarker-Korrekturen samt Zusammenfassung und eine
  Transit-Meldung mit null Transits in den EFT-Logs.
- Einmal `UtilityOperationEvent not found` am AKS74-Modell um 09:10:33, kurz vor
  dem Raidausstieg. EFTs `Player.FirearmController.UtilityOperation.Update`
  protokolliert dies nach 2,5 Sekunden ohne erwartetes Animationsereignis und
  geht selbst nach Ready/Idle. Kein Exception-Stack. Die konkrete Ursache ist
  nicht belegt; weder sicher harmlos noch einem KI-Mod oder Arena zugeordnet.
  Kein Hinweis auf eine fehlende Starterdatei. Im zweiten reinen Menü-/Exitlauf
  trat die Meldung nicht auf; dieser ersetzt keinen weiteren Kampfversuch.
- Der bekannte ASP.NET-Hinweis auf fehlende Action-Deskriptoren bleibt bestehen.

Im ersten Zyklus war die private Unity-Dateiprotokollierung aus; BepInEx allein
deckt deshalb nicht alle Unity-Meldungen ab. EFT-Traces wurden separat archiviert.
Für den zweiten Start war die Protokollierung an und der Startup-Nachweis damit
vollständiger. Die ursprüngliche private `BepInEx.cfg` wurde nach Prozessende
byteidentisch zurückgestellt. Die normale Konfiguration wurde nicht verändert.

## Erhalt und Belege

- **1.369 ausgewählte Quelldateien unverändert**, SHA256-Vergleich vor dem ersten
  Start und nach den Tests. Erfasst sind die in
  `tools/ArenaStartChecks/Measure-Source.ps1` festgelegten Plugin-/Patcher-,
  Profil-/Launcher- und Konfigurationsbereiche mit festen Dateiendungen.
  Das ist kein Vergleich sämtlicher Assets oder beliebiger Dateitypen.
- Privater Kontobeleg unverändert; keine normalen Profile importiert.
- **59 ausgewählte private Dateien** zwischen erstem und zweitem Zyklus
  byteidentisch, darunter 13 Konfigurationsdateien sowie Paketdaten und der
  private ABPS-Datensatz. Die absichtlich temporäre Unity-Einstellung war in
  beiden Vergleichsständen an; danach wurde der gesonderte Originalbeleg
  wiederhergestellt. Kein Anspruch, jede native Profildatei byteidentisch zu
  halten: Sessiondaten dürfen regulär aktualisiert werden.

Ignorierte lokale Belege unter `artifacts/arena-recommended-mods-gpu-20261021/`:
`start-1.txt`, `start-2.txt`, `raid-1.txt`, `leave-1.txt`, `status-second.json`,
Bot-/Schuss-/Respawn-Aufnahmen, getrennte vollständige `cycle-1/`- und
`cycle-2/`-Logs, Prozessaufnahmen, Paket-/Quellhashes und `native-result.json`.
`launcher-finished-1.png` zeigt einen vollständigen GUI-Stand. Die anfängliche
Paketanzeige wurde außerdem direkt in einer vollständigen Windows-MCP-Aufnahme
gesehen. `launcher-ready.png` und einzelne weitere Desktop-PNGs enthalten nur
Bildänderungen und gelten nicht als vollständige visuelle Abnahme. Die Factory-Aufnahme stammt aus dem nativen
Spiel-Backbuffer.

Dieser Nachtest schließt die offene GPU-Prüfung der Paketübernahme. Frische
Profilanlage, andere Karten/Modi, PFT und große Botzahlen wurden hier nicht
erneut getestet. Eine allgemeine Kompatibilitätsgarantie folgt daraus nicht.
