# Lokale KI-Pakete im Arena-Starter

Stand: 2026-10-20, `feature/arena-start-prototype`. Änderung ausschließlich an
Starter und Prüfwerkzeugen. Keine Änderung oder Auslieferung der fremden Mods.

## Umfang

Der Starter übernimmt bei der ersten Einrichtung und vor späteren Starts diese
bereits in normalem SPT installierten Pakete. Die Oberfläche zeigt vorhandene
und fehlende Pakete. Fehlend ist erlaubt; teilweise installiert führt vor dem
Spielstart zu einer Fehlermeldung. Keine automatischen Downloads.

| Paket | Geprüfte Installation | Kopierte Bestandteile |
|---|---|---|
| SAIN | 4.5.1 | Client, Shared-Library, Server, ServerInterop, Nickname-Personality-Daten, Web-CSS und vorhandene Lizenzen |
| APBS | Progressive Bot System 2.4.1 | Server, 44 feste Daten-JSONs, drei Webassets |
| ABPS | Bot Placement System 2.1.0 | Client und Server, sechs Defaults, drei Webassets und vorhandene Lizenzen |
| BigBrain | 1.5.0 | Plugin-DLL |
| Waypoints | 1.9.0 | Plugin, elf benannte Navmesh-Bundles und vorhandene Lizenz |

Das ergibt in der geprüften Installation **84 Dateien**. DLLs, Daten und Bundles
sind echte private Kopien, keine Links auf beschreibbare normale Moddateien.
Nur die schon zuvor freigegebenen nativen Spielassets nutzen NTFS-Hardlinks.

SAIN wird nur zusammen mit BigBrain und Waypoints ausgewählt. Client-/Server-
Komponenten müssen vollständig sein. Die PE-Prüfung lädt keine fremde Assembly
in den Starter. Sie prüft Namen, bei SAIN gleiche Assemblyversionen und identische
Shared-Library-Bytes, bei ABPS die unterschiedlichen Client-/Server-Typmarker.
APBS meldet Assemblyversion 2.0.0.0 trotz Modversion 2.4.1; ABPS-Client und -Server
haben ebenfalls unterschiedliche Assemblyversionen. Daraus wird keine falsche
Versionsgleichheit abgeleitet.

SAIN ab 4.5.1, BigBrain ab 1.5.0 und Waypoints ab 1.9.0 haben keine feste obere
Versionsgrenze. Neue Dateien/Abhängigkeiten benötigen trotzdem eine geprüfte
Katalogerweiterung. Zusätzliche DLLs/EXEs in ausgewählten Paketordnern werden
abgewiesen. Die eigenen SPT-/EFT-Versionsprüfungen der Mods bleiben unverändert;
SAIN 4.5.1 Server verlangt beispielsweise `~4.1.3`, nicht jede 4.1.x-Version.

## Einstellungen und Isolation

Normale Mod-Konfigurationen und Presets werden **nicht importiert**. SAIN, APBS
und ABPS erzeugen beim ersten privaten Serverstart ihre eigenen Defaults;
BepInEx erzeugt die Clientkonfigurationen. Die fünf nativen Spieleinstellungen
werden weiterhin separat aus normalem SPT übernommen.

Updates erhalten bereits private Einstellungen. Beim kontrollierten Basis-
Neuaufbau werden außerdem die privaten BepInEx-Konfigurationen der ausgewählten
Mods, SAIN-Editor-/Preset-/Servereinstellungen, APBS-Konfiguration/Blacklists/
Presets und ABPS-Konfiguration übernommen. ABPS-Fortschritt ist ausschließlich
für die bestätigte interne Arena-Konto-ID zulässig. Normale Profil-IDs, SAIN-
Freigabelisten und ABPS-Fortschrittsdateien werden nicht aus der Quelle importiert.

Logs, Caches, APBS-Vanilla-Mappings und automatisch erzeugte Release Notes werden
nicht importiert oder beim Rebuild wiederhergestellt. Bekannte `.bak`-Crashreste
darf die private Runtime enthalten; sie werden nicht als aktive Einstellungen
restauriert. Links in veränderlichen Dateien bleiben verboten. APBS-Presetnamen
dürfen keine externen oder verschachtelten Pfade sein. Das ist keine OS-Sandbox:
die lokal installierten Mods und ihre Weboberflächen bleiben vertrauenswürdiger
Code unter dem Benutzerkonto.

Vollständiges Entfernen optionaler Pakete in normalem SPT entfernt beim nächsten
privaten Update die zugehörigen Katalogdateien. Private Einstellungen bleiben für
eine spätere Neuinstallation erhalten. Teilweise Entfernung, fehlende SAIN-
Abhängigkeiten und abweichende Shared-Libraries werden nicht übernommen.

## Prüfung

Die finale Offline-Suite besteht aus **799 Core-Prüfungen**, darunter 131
Paketprüfungen, 39 Prüfungen privater Moddateien und 103 Rebuild-Prüfungen.
**45 WPF-Prüfungen** decken die echten Window-Controls mit kontrolliertem Backend
ab, einschließlich Paketanzeige und Zurücksetzen nach Quellenwechsel. Die lokalen
Renderdateien lieferten in dieser Sitzung kein brauchbares Bild; daraus wird kein
visueller Nachweis abgeleitet. Belege: `core-final.txt`, `wpf-final.txt` unter
`artifacts/arena-recommended-mods/` (ignorierte lokale Testdaten).

Unabhängige Gegenprüfung korrigierte zwei Randfälle: begrenzte native `.bak`-
Dateien von APBS/ABPS und eine Wiederherstellungsvorschau, die bei unvollständiger
Quelle nicht an der neuen Paketübersicht scheitern darf. Weitere Tests prüfen
ABPS-Client-/Server-Verwechslung, ungeeignete APBS-Defaults, zusätzliche DLLs,
fremde Fortschritts-IDs, Pfadausbrüche und beschreibbare Hardlinks. Echte
Transaktionstests decken Same-Version-Updates, Rollback, vollständige Entfernung
und Neuinstallation samt Erhalt privater Einstellungen ab.

Der erste native Lauf kopierte alle 84 Dateien in die bereits auf **SPT 4.1.6**
aktualisierte Testquelle unter `D:\arena-launcher-tests\SPT-4.1.1` und aktualisierte
deren bestehende private Arena-Generation. Alle drei Servermods wurden geladen,
SAIN erzeugte sechs Defaults, der private Login bestand. Das interne Konto/
Profil sowie 557 geschützte Dateien der normalen Quelle und 76 der Testquelle
blieben byteidentisch. Die normale Installation wurde nicht gestartet.

Dabei fiel ein APBS-Erststartproblem auf: sein Logger schreibt, bevor der Mod
den Ordner `logs` sicher anlegt. Der Starter legt jetzt `logs` und `Presets` vor
dem Prozessstart an. Das ist unabhängig gegengeprüft und im frischen Fixture
abgesichert. Im korrigierten Wiederholungslauf wurde ausschließlich der private
APBS-Mappingcache in die Testbelege verschoben, damit APBS ihn erneut erzeugt.
Die Mod-Konfigurationen und das Profil blieben erhalten.

Ergebnis: **ARENA_SERVER_SMOKE_PASS** und **NATIVE_RECOMMENDED_MODS_PASS**,
SAIN/APBS/ABPS erneut geladen, APBS-Datenbank und sechs SAIN-Standardpresets
bereit. Kein Error-/Exception-Treffer im neuen Serverlog, insbesondere kein
Logger-Pfadfehler mehr. Die nativen Hinweise zu 20 statt empfohlenen 32 GiB RAM
und fehlenden ASP.NET-Action-Deskriptoren bleiben unverändert. Alle 557 geschützten
normalen Quelldateien, 76 Testquelldateien, sechs privaten Profil-/Konfigurations-
dateien und das Konto-Receipt blieben byteidentisch. 10.369 Katalogdateien und
10.461 Runtime-Dateipfade bestanden die anschließende Prüfung; kein weiterer
Paketupdatebedarf und kein nativer Prozess blieb zurück.

Belege: `native/result.json`, `native/recommended-server.log` (Erstlauf),
`native/repeat-r2.txt`, `native/r2/result.json`, `native/r2/recommended-server.log`
und `native/r2/independent-audit.json` unter dem genannten Artifact-Verzeichnis.
Finale native CLI-DLL SHA256:
`8733A11F056E5723304710E52B2BA3D3D054D646F9377EB557E9E65AB2823195`.

Ein neuer GPU-Raidtest ist nicht Bestandteil dieser Änderung; dadurch ist
insbesondere kein neuer Kampf-/KI-Nachweis gegeben.

**Nachtrag 2026-10-21:** Der unveränderte finale Starter wurde inzwischen auf
SPT-GPU-2 geprüft: sichtbare Paketanzeige, zwei direkte private Starts, Factory-DM
mit echtem Kill/Ersatzbot und Spielertod/Respawn sowie geschütztes Raidende und
sauberes EXIT. 1.369 ausgewählte Quelldateien blieben unverändert. Einzelne
native Loghinweise und die Reichweite des Nachweises stehen im
[GPU-Nachtest](GPU-ARENA-RECOMMENDED-MODS-20261021.md).

## Artefakt

Eigenständiger Windows-Starter, Release / win-x64 / self-contained / SingleFile:
`artifacts/arena-recommended-mods/publish/maschine-ArenaLauncher.exe`, SHA256
`D0F8D0BC75C1EFAA39E4CD47A8FDBAF4C2E116A0CE6001564AA81606DABFA5E6`.
Keine fremden Mod-DLLs darin gebündelt. Kein neues Arena-Gameplay-DLL-Deployment.
SPT-GPU-2 war über den bisherigen SMB-Pfad in dieser Sitzung nicht erreichbar;
das Artefakt liegt lokal bereit und ersetzt dort noch nicht den vorherigen Starter.
Der Nachtest vom 2026-10-21 verwendete dieses Artefakt hashidentisch unter
`C:\Temp\ArenaLauncher-Recommended-20261021\maschine-ArenaLauncher.exe`.
