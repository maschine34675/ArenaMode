# Separater Arena-Start – Prototyp

Stand: 2026-10-20. Quellcode: `tools/ArenaStart/`; Prüfprogramm:
`tools/ArenaStartChecks/`. Dieser erste Prototyp ist ein Windows-Kommandozeilenprogramm,
kein neuer vollständiger Launcher und noch kein Bestandteil des Mod-Releases.

## Entscheidung und Umfang

Der normale Spielercharakter wird **nicht importiert und nicht angeboten**.
EFT benötigt technisch weiterhin einen Profildatensatz. Dafür legt der private
Server über seine originale Registrierungsroute genau ein neues Konto
`Arena-Internal` an. Die erste Charaktererstellung erfolgt im Spiel über den
nativen Ablauf. Es gibt keinen Profilimport, keinen Rückimport und keine
Übernahme von Stash, Begleitern oder Ausrüstung.

Die neue Umgebung erhält eigene Kopien von Spiel, SPT, ArenaMode und
Redeploy.Core. Fremde Plugins, zusätzliche Servermods, Prepatcher, normale
Profile, Launcher-Einstellungen, BepInEx-Konfigurationen und PFT-Daten werden
nicht übernommen. Die optionale Arena-BigBrain-Bibliothek gehört zu Arena,
bleibt ohne BigBrain aber inaktiv. Die erste Fassung enthält bewusst weder
SAIN/PFT/APBS noch ArenaMapPort/Killcam; verfügbar sind die eingebauten SPT-Karten.
Kit- und Waffenleiterdateien stammen aus der vorhandenen Arena-Installation.
Die übrigen SPT-Basiskonfigurationen werden ebenfalls übernommen; HTTP, Logging
und Backup erhalten dagegen neu erzeugte private Einstellungen. Vom Benutzer
direkt veränderte Balancewerte in den SPT-Basiskonfigurationen werden nicht auf
Werkseinstellungen zurückgesetzt.

**Die Quelle wird nicht umgeschaltet.** Keine Mod wird dort verschoben,
deaktiviert oder gelöscht. Es gibt keine Junctions oder Hardlinks zum Original.
Der native Launcher darf seine private Kopie normal prüfen und patchen.
Das kostet zunächst ungefähr die Größe der Spielinstallation zusätzlich.
Ein späteres Teilen großer unveränderlicher Assets ist noch nicht umgesetzt.

Die Grundlage muss eine funktionierende SPT-4.1-Installation sein. Dies ist kein
Reparaturprogramm für bereits auf Platte veränderte Spiel-/SPT-Dateien und kein
Nachweis, dass deren Inhalt dem offiziellen Original entspricht. Der feste
Dateikatalog ist auf den lokal geprüften SPT-4.1.6-Aufbau zugeschnitten; fehlende
benötigte Dateien führen zu einer Ablehnung.

## Verwendung

Voraussetzungen: Windows, .NET 10 für den Starter; zum Bauen das .NET-10-SDK.
Spiel, SPT-Server und SPT-Launcher müssen für `prepare`, `smoke` und `start`
geschlossen sein. Der reine `plan` verändert nichts.

Im Repository:

```powershell
dotnet build tools/ArenaStart -c Release
$starter = ".\tools\ArenaStart\bin\Release\net10.0\maschine-ArenaStart.exe"
& $starter plan    D:\SPT41 D:\ArenaMode-Prototype
& $starter prepare D:\SPT41 D:\ArenaMode-Prototype
& $starter verify  D:\ArenaMode-Prototype
& $starter smoke   D:\ArenaMode-Prototype
& $starter start   D:\ArenaMode-Prototype
```

Das Ziel darf noch nicht existieren und darf weder innerhalb noch oberhalb der
Quelle liegen. Die Vorbereitung kopiert nur den festen Katalog. Bei einem
Abbruch wird nichts automatisch gelöscht oder zusammengeführt; die unvollständige
Kopie startet ohne gültiges Manifest nicht. Für Änderungen an den installierten
Mod-DLLs oder SPT ist zunächst eine neue Zielumgebung nötig; ein Updateverfahren
ist absichtlich noch nicht enthalten.

`smoke` startet den echten privaten Server, erstellt oder prüft das interne Konto
und beendet ausschließlich diesen gestarteten Server. Es startet kein Spiel.

`start` prüft die Dateihashes und Profilgrenze, startet den privaten Server und
öffnet den **unveränderten SPT-Launcher**. Dieser wählt automatisch die Profilseite
des internen Kontos aus. Dort startet man das Spiel; beim ersten Mal folgen die
üblichen Charaktererstellungsschritte. Der Starter umgeht keine nativen
Versions-, Installations- oder Patchprüfungen.

Nach dem Spielen zuerst EFT und Launcher schließen, dann im Starter Enter
drücken. Erst dann beendet er den eigenen Server. Bei geschlossenem
Standardeingang wartet er weiter auf beide Anwendungen und hält seine Logpipes
offen. Das Starterfenster während des Spielens nicht zwangsweise schließen.
Nach einem Prozess-/Hostabsturz wird kein fremder/laufender Server automatisch
übernommen oder beendet; vor dem nächsten Start müssen die Anwendungen beendet
werden.

## Grenzen und Schutzmaßnahmen

- Eigener `SPT_Runtime/user`-Baum; keine normalen Profile im neuen Server.
- Ein nativ generierter Profil-ID-Beleg. Fremde oder zusätzliche aktive
  Profil-Dateien sowie eine geänderte interne Identität verhindern den Start.
- Native Profil-Backups bleiben eingeschaltet, liegen aber separat unter
  `SPT_Runtime/user/arena-profile-backups`.
- Eigene lokale Log-, HTTP-, Backup- und Launcher-Konfiguration. Pfade und
  Zugangsdaten aus der ursprünglichen Launcher-/HTTP-/Loggerkonfiguration werden
  nicht geerbt. Server bindet nur `127.0.0.1:6969`.
- Belegter Port oder laufender EFT/SPT/Launcher verhindert den Start. So zeigt
  auch der feste lokale Servereintrag des nativen Launchers auf den privaten
  Server. Parallelbetrieb mit normalem SPT ist in diesem Prototyp ausgeschlossen.
- Die Bootstrap-Anfragen akzeptieren nur das Zertifikat aus dem privaten
  Serververzeichnis, nur auf diesem Loopback-Endpunkt; keine Weiterleitungen,
  kein Proxy und keine globale Abschaltung der Zertifikatsprüfung.
- Manifest, SHA-256-Prüfung, vollständige Prüfung auf Verknüpfungen und Ablehnung
  zusätzlicher Moddateien/ausführbarer Dateien vor dem Start. Auch beschreibbare
  Log-/Konfigurationsdateien dürfen keine Hardlinks zum Original sein.
- Kein Download, kein Mod-Update, kein automatischer Import und kein automatisches
  Löschen alter Installationen. Kein Deployment in die normale Installation.

Das ist eine Trennung gegen versehentliche Vermischung, **keine Betriebssystem-
Sandbox gegen bösartige Mods oder manuelle Manipulation durch denselben
Windows-Benutzer**. Die Originalinstallation bleibt auf der Festplatte vorhanden.
Der native Launcher besitzt weiterhin seine allgemeinen Bedienmöglichkeiten;
der Starter setzt seine private Ziel- und Profilauswahl bei jedem Start zurück.
Auch allgemeine Windows-Benutzerdaten sind keine private Sandbox: der native
ASP.NET-Host benutzt weiterhin den gemeinsamen DPAPI-geschützten
`LocalAppData/ASP.NET/DataProtection-Keys`-Speicher. SPTs eigene Konten, Credentials,
Zertifikate und Spielprofile liegen dagegen im separaten Runtime-Verzeichnis.
Dieses Verhalten entspricht der
[ASP.NET-Schlüsselspeicherimplementierung](https://github.com/dotnet/aspnetcore/blob/main/src/DataProtection/DataProtection/src/Repositories/DefaultKeyStorageDirectories.cs)
und wurde im privaten Serverlog beobachtet.

## Noch nicht Bestandteil dieses Prototyps

- Eine eigene grafische Startoberfläche und vollständig auf Arena reduzierte
  Spielmenüs. Native PMC-/Scav-/Stash-Seiten sind in der privaten Kopie noch
  vorhanden; sie enthalten ausschließlich den neuen internen Charakter.
- Ein serverseitiger Arena-only-Raidvertrag und das Ausblenden von „eigenes Gear“.
- Optionale Modpakete einschließlich Abhängigkeitsauflösung, PFT-Begleiter und
  zusätzliche Karten. Keine fremden DLLs werden im Starter veröffentlicht.
- Automatisierte vollständige Charaktererstellung, verlinkte Spieldaten,
  Update/Migration einer bestehenden Arena-Kopie und ein Installer.

## Prüfung

Offline:

```powershell
dotnet run --project tools/ArenaStartChecks -c Release
```

Die Tests erzeugen ausschließlich eigene kleine Wegwerfverzeichnisse und prüfen
eine erfolgreiche Kopie sowie echte Ablehnungen: verschachtelte Ziele,
Pfadausbruch, Profilimport, zweite Identität, fremde DLLs, manipulierte Dateien,
Symlinks, Hardlinks einschließlich veränderlicher Logdateien, externe Logger-/
Backuppfade und Loader-Umgebungsvariablen. Sie prüfen außerdem die unveränderten
Quelldateien und dass legitime native Backups einen Folgestart nicht verhindern.

Abgeschlossene Prüfung am 20.10.2026:

- Release-Build ohne Warnungen/Fehler; **128 Offline-Assertions** einschließlich
  erfolgreicher Kontrollen und tatsächlicher negativer Dateisystemfälle.
- Echte unabhängige Kopie `D:\ArenaMode-Prototype`: **10.290 Manifestdateien**,
  etwa **57,43 GiB** kopierte Nutzdaten. Die vollständige Integritätsprüfung
  benötigt auf diesem Host mehrere Minuten; noch nicht auf schnellen Start optimiert.
- Zwei echte SPT-4.1.6-Serverstarts, ungefähr 10:59 und 11:06 Uhr Ortszeit.
  Jeweils genau eine Servermod geladen: ArenaMode 0.1.1. Erster Start erzeugt
  das Konto über `/launcher/v2/register`, zweiter Start übernimmt exakt dieselbe
  Identität. Eine aktive Profil-Datei; zwei native Backup-Dateien im separaten
  Backupbaum. Kein manueller Profil-JSON-Aufbau.
- Kontrollierter Vergleich um den zweiten Start: **1.438 Originaldateien** mit
  identischen SHA-256-Hashes und identischer Pfadmenge; kein hinzugefügter oder
  gelöschter Eintrag. Umfasst Originalprofile/Backups, Launcher-Dateien,
  BepInEx-Configs sowie DLL-/JSON-/CFG-Dateien der Mods und Prepatcher.
- Im ersten längeren Vergleich während der Kopierphase gab es fremde Änderungen:
  vier geänderte Dateien und 40 entfernte alte Backup-Dateien, zeitlich vor dem
  ersten privaten Serverstart. Parallel wurden auf dem Hauptbranch native
  Arena-Verfügbarkeitsprüfungen abgeschlossen. Dieser erste Vergleich wurde
  ausdrücklich **nicht** als Nachweis unveränderter Originaldaten gewertet.
  Keine dieser parallelen Änderungen wurde zurückgesetzt.
- Beide privaten Server beendet; keine EFT-/SPT-/Launcherprozesse nach dem Test.
  Keine Error/Critical-Ausgabe im privaten Serverlog; zweimal die native Warnung,
  dass dieser Host 20 statt der empfohlenen 32 GiB RAM hat.
- Unabhängige Codeprüfung: korrigierte externe Log-/Backuppfade, Backups getrennt
  vom aktiven Profilordner, Prozess-/Pipe-Lebensdauer bei EOF/Fehlern, Idle-Prüfung
  beim Vorbereiten sowie Hardlink-Prüfung auch für veränderliche Laufzeitdateien.
  Der native Logging-Enum heißt `Information`; die bereits während der Prüfung
  laufende Kopie wurde vor ihrem ersten Serverstart ausschließlich in diesem
  generierten Loggerwert und dessen Manifesthash an den korrigierten Code angepasst.

Rohbelege bleiben lokal unter `artifacts/arena-start-prototype/` sowie im privaten
`.arena-start`-/Serverlogverzeichnis. Sie werden nicht veröffentlicht.

**Noch nicht geprüft:** Start des nativen Launchers, vollständige
Charaktererstellung, EFT-Menü oder Raid in dieser privaten Kopie. Insbesondere
die erneute Manifestprüfung nach den nativen Launcher-Patches bleibt Teil des
GPU-Nachtests. Ein Server-Smoke ist kein Spielstart-/Raidnachweis. Es wurden
keine normalen Arena-DLLs aktualisiert und keine Dateien auf einen GPU-Host
ausgerollt.

## Lokale Vertragsquellen

- `../_reference/launcher-master/project/SPTarkov.Core/Helpers/GameHelper.cs`:
  `CheckGame`, `SetupGameFiles`, `PatchGame`, `LaunchGame`.
- Dort `Configuration/LauncherSettings.cs`, `Configuration/Server.cs`,
  `Helpers/HttpHelper.cs`, `Helpers/SessionHelper.cs`, `Spt/Urls.cs`.
- `../_reference/server-csharp-main/.../Controllers/LauncherV2Controller.cs`:
  native Kontoerstellung; `Helpers/Server/CertificateHelper.cs`: privates PEM.
- `ProfileValidatorHelper`, `ProfileMigrationService`, `ProfileFixerService`:
  fehlende Modinhalte können vor Arenas Raid-Snapshot bereinigt werden.
- Aktuelle Arena-Abhängigkeiten: [DEPENDENCIES.md](DEPENDENCIES.md).
