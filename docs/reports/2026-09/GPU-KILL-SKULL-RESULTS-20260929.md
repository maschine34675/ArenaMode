# Kill-Totenkopf und Killfeed: autonomer GPU-Test

Stand: 29.09.2026. Native Funktionsprüfung bestanden; **keine vollständige
Pixel-Abnahme des kurz sichtbaren Totenkopfs**. Offline-Tests, Einspielen und
tatsächlich geladene Spielversion sind unten getrennt dokumentiert.

## Änderung und Identität

- Implementierung `838e9092b5ce4e2bc300ddb2296b1588a1d41f61`; anschließend nur die
  kompilierte Fehlerkontrolle für die Ausblenddauer in `e1d8975` präzisiert.
- Sauberer Build aus `e1d89753fecaf1740ebe38d4d9cf435d40586e98`, keine Quelländerungen
  beim Build; 0 Fehler, 4 bestehende CS8632-Warnungen.
- Client SHA256: `7BFADE51FFB2ABF1DE5FBEEA8EA927A10DD28C1A7939BB14D5C8A2E8BDAAA3BA`.
- Identisch lokal sowie auf GPU-Hosts `192.168.178.114` und `192.168.178.109`
  eingespielt. Alle drei Zieldateien vor dem Schreiben exklusiv gesperrt;
  Backups und Hashvergleich. Server, Redeploy.Core und BigBrainCompat unverändert.
  Beleg: `KILL-SKULL-DEPLOYMENT-20260929.json`.
- Test auf Host 2 (`192.168.178.109`), Profil `SPT-GPU-2`, EFT nach Einspielen
  neu gestartet. Prozessinstanz `b4ba3dbb`, Start um 18:07:20 Ortszeit.
  UiBridge bestätigte vor, während und nach dem Raid 24/24 übereinstimmende
  lokale, installierte und im Prozess geladene Mod-Builds.
- UiBridge `0.7.3+419df3c2ef81f2582a3d205b4b2af98b80b35712`, SHA256
  `EC325E81FD79ECBC0890AD6AE5B1D42897676791C68935940B6EAF1FADEF51B6`.
  EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`, Anzeige 1920 x 1080,
  Skalierung 100 %.

## Vorgehen und tatsächliche Ergebnisse

Über den unterstützten `gpu-run`-Start und die Arena-Raid-Rezeptur ins native
Menü und in Deathmatch FFA auf Equator gewechselt. Vier Teilnehmer insgesamt,
einschließlich drei Bots. Für reproduzierbare Treffer UiBridge-Godmode aktiviert,
Zielverfolgung auf Kopf/Brust, Bewegung beziehungsweise sicherheitsgeprüfte
Positionswechsel und echtes Feuer mit einer AK-12 benutzt. Kein künstliches
Kill-Ereignis injiziert. Die UiBridge-Kill-Funktion wurde nicht verwendet.

| Kill | Ziel | Native Todesursache | Live-Kill-Zeitstempel des Totenkopfs |
|---|---|---|---|
| 1 | Thekoenman | AK-12, Kopf | 143.818436 |
| 2 | November75 | AK-12, Brust | 391.577759 |
| 3 | Gatsu66 | AK-12, Kopf | 468.3282 |

Der schreibgeschützte Live-Inspector bestätigte das angelegte `ArenaKillSkull`-
Image, seine aktivierte Image-Komponente und das geladene Sprite mit Textur
`Arena UI kill.png` (Originalbreite 20 Pixel). Der Kill-Zeitstempel wechselte
von der Initialisierung `-10` zu den drei obigen Werten. Die eigene CanvasGroup
hatte vor dem ersten Kill und nach Ablauf der Einblendung jeweils Alpha 0.
Zusammen mit den ausführbaren Lebensdauerprüfungen bestätigt dies die native
Ereignisbindung und das spätere Ausblenden, nicht jeden gerenderten Zwischenframe.

Der erhaltene native Killfeed-Text zum Brusttreffer enthielt vollständige Namen,
Rollen, Entfernung und Waffe, aber keinen erzeugten HS-Zusatz:

```text
SPT-GPU-2 → November75
USEC → BEAR · 8 m · AK-12
```

Das separate Kopftreffer-Image dieser Zeile war deaktiviert. Beim zweiten
Kopftreffer war es aktiviert, während der Text ebenfalls ohne HS-Zusatz blieb:

```text
SPT-GPU-2 → Gatsu66
USEC → USEC · 8 m · AK-12
```

Der eigene Name blieb blau (`#209FFF`), beide Gegnernamen rot (`#FF1E1E`).
Auch der gleichfraktionelle Gegner wurde im FFA korrekt als Gegner eingefärbt.
Der native Matchabschluss meldete 3 Kills, 0 Tode, 700 Punkte, beste Serie 3
und 7,1 Minuten. Das Kopftreffer-Icon wurde als native Komponenteneigenschaft
geprüft; diese Feststellung ist keine Screenshot-Abnahme seiner Pixel.

## Grenzen der Bildprüfung

Die separate Windows-Screenshot-Abfrage nach einem bestätigten Kill traf erst
nach Ablauf der 0,8 Sekunden langen Totenkopf-Einblendung ein. Das neue UiBridge
ermöglicht echte Schüsse, Zielverfolgung und Objektprüfung, bietet in dieser
Version aber keine synchrone Bildserie am Kill-Ereignis. Deshalb liegt **kein
Bild des sichtbar eingeblendeten Totenkopfs** vor. Insbesondere dessen endgültige
Größe, Position unter dem Fadenkreuz und Lesbarkeit müssen noch kurz durch einen
Spieler oder eine ereignisnahe Aufnahme beurteilt werden. Keine UI-Zeit verlängert
und keinen Diagnosecode in den ausgelieferten Client eingebaut, um eine Aufnahme
zu erzwingen. Tactical, PFT, andere Auflösungen und normale Raids waren nicht
Bestandteil dieses nativen Laufs.

## Logs, Profilschutz und Aufräumen

- Im vollständigen kopierten Clientlog: **0 Error-Level-Zeilen**; keine Meldung
  `Arena HUD build/tick failed`, `Arena UI asset/sprite unavailable` oder
  `Arena kill capture failed`.
- Die Warnung `RespawnApiProbe.ProbeRuntime.AlwaysThrow` beim initialen
  Körperwechsel stammt aus der bereits installierten absichtlichen Fehlerprobe.
  Sie ist kein neuer Fehler des Totenkopfs.
- Zielverfolgung und Bewegung beendet, Godmode ausdrücklich ausgeschaltet.
  Über das native Pausenmenü `DISCONNECT` und die Ergebnis-Seiten zurück zum
  Hauptmenü. Kein erzwungenes Beenden, keine Konfigurationsänderung.
- Um 18:15:56.654 bestätigte Arena den geschützten nativen Raid-Abschluss.
  Im Hauptmenü war `_hitmarker` wieder null und der alte Unity-Skull-Handle
  ungültig. Alle 22 für die Untersuchung angelegten Inspector-Handles freigegeben.
  EFT blieb im Hauptmenü; der SPT-Server lief unverändert weiter.
- Vollständiger semantischer Vergleich des Serverprofils nach Login/vor Raid
  und nach Rückkehr: ausschließlich zwei Unterschiede:
  `profile.characters.pmc.Health.UpdateTime` und
  `profile.characters.pmc.Hideout.sptUpdateLastRunTimestamp`.
  Inventar, XP, Skills, Quests, Statistiken und übrige Werte unverändert.
  Kein manueller Eingriff in das Profil.

Lokale Rohbelege liegen bewusst im ignorierten Verzeichnis
`artifacts/kill-skull-gpu`; Profile werden nicht ins Repository übernommen.

| Datei | SHA256 |
|---|---|
| `client.log` | `2F4A612958BEF5DA1AC78DDB079B2C08C201DD11AA036C32C89B374C626CAF3F` |
| `profile-before.json` | `6DCB12387468FF765AF8289A527A6B6455174FD4682D585F8897B1CB0311EA5F` |
| `profile-after.json` | `F12E8F16AF8D74AD6B89B4094F9DB80C7A2E3B021423D0D8D7A681CDC3BC024A` |

Das Quelllog war
`\\192.168.178.109\c$\SPT41\BepInEx\LogOutput-SPT-GPU-2-2026-09-29_18-07-09.log`.
`start.log`, `raid.log`, `profile-diff-paths.json` und der Vergleichshelfer liegen
neben den Rohbelegen. Manifest des finalen Builds:
`artifacts/double-jump-build/124f45794a1a4f57ad64e50c7cef8b9a/manifest.json`.

## Ergänzende Offline-Absicherung

`ArenaUiAssetChecks` bestanden mit 160 Assertions und zehn kompilierten
Fehlerkontrollen, einschließlich falscher Kill-Zuordnung, Schadensereignis statt
bestätigtem Kill und falscher Lebensdauer. `KillfeedTextChecks` samt Fehlerkontrollen
bestanden; unter anderem bleibt ein wörtliches HS im Spieler-/Waffennamen erhalten.
Alle 17 eingebetteten PNGs und Herkunftsdokumente in der echten Client-DLL per
Hash geprüft. Unabhängige adversariale Review ohne offene P0-P2-Befunde.
