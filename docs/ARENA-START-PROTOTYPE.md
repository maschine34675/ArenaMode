# Separater Arena-Start – Prototyp

Stand: 2026-10-20. Quellcode: `tools/ArenaStart/`; Prüfprogramm:
`tools/ArenaStartChecks/`. Zusätzlich bietet `tools/ArenaLauncher/` eine kleine
Windows-Oberfläche für denselben Ablauf: [Bedienung und GUI-Prüfung](ARENA-LAUNCHER-UI-20261020.md).
Der Prototyp ist noch kein Bestandteil des Mod-Releases. Die CLI behält den
originalen SPT-Launcher, die GUI startet EFT direkt. Die native
Charaktererstellung bleibt erhalten.

## Entscheidung und Umfang

Der normale Spielercharakter wird **nicht importiert und nicht angeboten**.
EFT benötigt technisch weiterhin einen Profildatensatz. Dafür legt der private
Server beim ersten Start über seine originale Registrierungsroute genau ein
neues Konto `Arena-Internal` an. Die erste Charaktererstellung erfolgt im Spiel über den
nativen Ablauf. Es gibt keinen Import normaler Spielerprofile, keinen Rückimport
und keine Übernahme ihres Stashs, ihrer Begleiter oder Ausrüstung. Ein späterer
ausdrücklicher Basis-Neuaufbau erhält dagegen das bereits vorhandene interne
Arena-Konto samt dessen Zustand; siehe [Rebuild-Vertrag](ARENA-BASE-REBUILD-20261020.md).

Die neue Umgebung erhält eigene Kopien der ausführbaren Spielbestandteile, SPT,
ArenaMode und Redeploy.Core. Große freigegebene native Assets werden auf demselben
NTFS-Laufwerk über dateiweise Hardlinks gemeinsam genutzt. Fremde Plugins,
zusätzliche Servermods, Prepatcher, normale
Profile, Launcher-Einstellungen, BepInEx-Konfigurationen und PFT-Daten werden
nicht übernommen. Die optionale Arena-BigBrain-Bibliothek gehört zu Arena,
bleibt ohne BigBrain aber inaktiv. Die erste Fassung enthält bewusst weder
SAIN/PFT/APBS noch ArenaMapPort/Killcam; verfügbar sind die eingebauten SPT-Karten.
Kit- und Waffenleiterdateien stammen aus der vorhandenen Arena-Installation.
Die fünf nativen Dateien unter `SPT_Runtime/user/sptSettings` werden vor jedem
Start als private Kopien übernommen, ohne Rückschreiben. Fehlende Quellen
lassen private Einstellungen bestehen. Mod-Konfigurationen aus normalem SPT
bleiben ausgeschlossen;
siehe [Einstellungsübernahme und Updateverhalten](ARENA-START-SETTINGS-20261020.md).
Die übrigen SPT-Basiskonfigurationen werden ebenfalls übernommen; HTTP, Logging
und Backup erhalten dagegen neu erzeugte private Einstellungen. Vom Benutzer
direkt veränderte Balancewerte in den SPT-Basiskonfigurationen werden nicht auf
Werkseinstellungen zurückgesetzt.
Eine minimale neue `BepInEx.cfg` setzt SPTs notwendiges
`[Chainloader] HideManagerGameObject = true`. BepInEx darf seine eigenen Defaults
und Kommentare ergänzen; der Starter prüft diesen Pflichtwert vor jedem Start
erneut, statt die normalisierte Datei gegen einen unveränderlichen Hash zu prüfen.
Das private NLog enthält die von EFT beim Start erwarteten Variablen `clientId`
und `gameVersion`; seine Dateiziele bleiben innerhalb der privaten Umgebung.

**Die Quelle wird nicht umgeschaltet.** Keine Mod wird dort verschoben,
deaktiviert oder gelöscht. Keine Quellinhalte, ACLs oder Read-only-Attribute
werden geändert. Hardlinks erhöhen jedoch die Verknüpfungszahl der betroffenen
Quelldateien; dies ist keine Behauptung unveränderter Dateisystem-Metadaten.
Der native Launcher darf seine privaten Programmdateien normal prüfen und patchen.
Die ausdrücklichen Befehle `plan-copy`/`prepare-copy` behalten die bisherige große
Referenzkopie ohne gemeinsame Dateien bei. Es gibt keinen stillen Rückfall darauf.

Für die untersuchte Installation benötigt die neue Auswahl rund **4,00 GiB**
private Dateien und nutzt **53,43 GiB** bereits vorhandene Assets. Weitere
Profile, Caches und Logs brauchen später zusätzlichen Platz. Explorer kann
bei Hardlinks die logische Größe doppelt zählen; die Summen stammen aus dem
Dateikatalog und sind keine Messung tatsächlich belegter NTFS-Cluster.

Die Grundlage muss eine funktionierende SPT-4.1-Installation sein. Dies ist kein
Reparaturprogramm für bereits auf Platte veränderte Spiel-/SPT-Dateien und kein
Nachweis, dass deren Inhalt dem offiziellen Original entspricht. Der feste
Dateikatalog ist auf den lokal geprüften SPT-4.1.6-Aufbau zugeschnitten; fehlende
benötigte Dateien führen zu einer Ablehnung. Normales SPT muss zuvor erfolgreich
gestartet worden sein. Der Starter verlangt die gepatchte `Assembly-CSharp.dll`
und eine davon abweichende native `.spt-bak`; das ist eine Voraussetzung, keine
Reparatur oder vollständige Verifikation des offiziellen Patchformats.

## Verwendung

Voraussetzungen: Windows, .NET 10 für den Starter; zum Bauen das .NET-10-SDK.
Spiel, SPT-Server und SPT-Launcher müssen für `prepare`, `rebuild`, `smoke` und `start`
geschlossen sein. Der reine `plan` verändert nichts.

Für die automatische Anlage und spätere Wiederverwendung:

```powershell
dotnet build tools/ArenaStart -c Release
$starter = ".\tools\ArenaStart\bin\Release\net10.0\maschine-ArenaStart.exe"
& $starter play D:\SPT41
```

`play` verwaltet den Nachbarordner `D:\SPT41-Arena` als festen Anker automatisch.
Die erste Runtime liegt dort, spätere neu aufgebaute Runtimes dauerhaft unter
`D:\SPT41-Arena.generations/<ID>`. Der Start über den Anker folgt seinem aktiven
Generationsverweis; Generationsordner nicht direkt starten. Bereits vorhandene
fremde, unvollständige oder anders zugeordnete Ordner werden nicht übernommen,
geleert oder repariert. Bei Folgestarts wird die vorhandene Umgebung geprüft.
ArenaMode, Redeploy.Core, Audio, Dokumentation und Kitdaten werden vor dem Start
kontrolliert aus der normalen Installation aufgefrischt; siehe
[Updatepfad und Rollback](ARENA-START-UPDATES-20261020.md). Ein Installer und ein
allgemeiner Spiel-/SPT-Updater sind nicht vorhanden. Die inzwischen ergänzte GUI
verwendet dieselben Schutzprüfungen.
Diese Prüfung liest weiterhin den gesamten geschützten Dateikatalog. Auch ein
Folgestart kann deshalb auf einem langsamen Laufwerk mehrere Minuten benötigen;
die gemeinsame Ablage spart Platz, ist noch keine Startzeitoptimierung.

Einzelne Schritte, insbesondere für Tests:

```powershell
& $starter plan    D:\SPT41 D:\SPT41-Arena
& $starter prepare D:\SPT41 D:\SPT41-Arena
& $starter verify  D:\SPT41-Arena
& $starter smoke   D:\SPT41-Arena
& $starter start   D:\SPT41-Arena
# Nach einem vollständig abgeschlossenen Update der normalen SPT-Basis:
& $starter rebuild D:\SPT41
```

Bei `plan` und `prepare` darf das Ziel noch nicht existieren und weder innerhalb
noch oberhalb der Quelle liegen. Die Vorbereitung kopiert nur den festen Katalog. Bei einem
Abbruch wird nichts automatisch gelöscht oder zusammengeführt; die unvollständige
Umgebung startet ohne gültiges Manifest nicht. Quell- und Zielordner müssen auf
demselben NTFS-Laufwerk liegen. Die Versionsbindung an Spielassemblies, Launcher,
Server und native Patchdateien verweigert das Mischen verschiedener Stände.
Nach einem Basisupdate erstellt `rebuild` beziehungsweise **Rebuild Arena environment**
in der GUI ausdrücklich eine neue Generation aus der fertig aktualisierten Quelle.
Das vorhandene interne Konto und zugelassene private Einstellungen werden erhalten.
Erst nach Integritäts-, Patch-, privater Server- und Loginprüfung wird der aktive
Verweis gewechselt. Der Rebuild startet kein Spiel und installiert keine SPT-Updates.

Alte und unvollständige Generationen werden nicht automatisch gelöscht. Sie brauchen
zusätzlichen privaten Speicher; die gemeinsamen Hardlinks sind keine garantierten
Backups einer alten Spielbasis. Eine Unterbrechung vor Aktivierung führt beim
ausdrücklichen Wiederholen zu einem neuen Kandidaten, eine Unterbrechung danach
nur zum Abschluss der schon erfolgten Aktivierung. Vorhandener Profilfortschritt
wird dabei nicht zurückgerollt. Vertrag und gesonderte Nachweise:
[Basis-Neuaufbau](ARENA-BASE-REBUILD-20261020.md). Fremde Mod-Updates werden weiterhin
nicht automatisch in Arena übernommen.

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
werden. Ein erzwungenes Beenden des Starters gibt auch seine Dateisperren frei.
Die Hardlinks bleiben bestehen und sind außerhalb dieser Laufzeitsperren nicht
schreibgeschützt. Die private Umgebung daher ausschließlich über den Starter
öffnen und nicht manuell modifizieren oder mit einer Synchronisation spiegeln.

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
- Manifest, SHA-256-Prüfung, Prüfung aller Pfade und Ablehnung zusätzlicher
  Moddateien/ausführbarer Dateien vor dem Start. Schema 1 ist die Referenzkopie,
  Schema 2 die neue gemeinsame Assetauswahl. Junctions/Symlinks bleiben verboten.
- Nur `SharedAssets.IsContent` darf Hardlinks freigeben: ausgewählte native
  Assetdateien, Windows-Bundles und Acoustic-/AudioBake-/Culling-Daten. DLLs,
  EXEs, `Managed`, native Plugins, Patches, Backups, Einstellungen, Serverdaten
  und Profile bleiben private Kopien. Auch veränderliche Logs/Configs werden
  auf unerlaubte Hardlinks geprüft.
- Vor jedem Start werden Quell- und Zielpfad jedes gemeinsamen Assets gleichzeitig
  geöffnet, Dateiidentität und Hash verglichen und **beide Handles** bis zum Ende
  aller nativen Clients gehalten. So werden normale Schreib-, Lösch- und
  Umbenennungsversuche über beide Namen blockiert. Ein bloßer Hardlink bietet
  diesen Schutz nicht. Der Starter hält die Sperren auch bei einem Serverfehler,
  solange EFT oder Launcher noch laufen.
- Unerwartete `.spt-bak` werden abgewiesen, weil der native Launcher diese im
  gesamten Spielbaum rekursiv wiederherstellt. Nur der geprüfte private
  Assembly-CSharp-Backup-/Patchpfad wird akzeptiert. Nach einem Start werden
  veränderte Manifesthashes niemals automatisch als neuer Sollzustand übernommen.
- Kein Download, kein allgemeiner Mod-Importer, kein Import normaler Profile/Fremdmods und kein automatisches
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

- Vollständig auf Arena reduzierte Spielmenüs. Eine eigene
  [grafische Startoberfläche](ARENA-LAUNCHER-UI-20261020.md) ist vorhanden.
  Native PMC-/Scav-/Stash-Seiten sind in der privaten Kopie noch
  vorhanden; sie enthalten ausschließlich den internen Arena-Charakter.
- Ein serverseitiger Arena-only-Raidvertrag und das Ausblenden von „eigenes Gear“.
- Optionale Modpakete einschließlich Abhängigkeitsauflösung, PFT-Begleiter und
  zusätzliche Karten. Keine fremden DLLs werden im Starter veröffentlicht.
- Automatisierte vollständige Charaktererstellung, ein SPT-Download/-Installer
  oder beliebige Versionsmigrationen ohne Prüfung. Der kontrollierte Neuaufbau
  aus einer funktionsfähigen lokalen Basis ist inzwischen vorhanden.

## Diagnose auf dem GPU-Host

`--diagnostics` am Ende von `play`, `plan`, `prepare` oder `rebuild` nimmt ausschließlich
die installierte `BepInEx/plugins/maschine-UiBridge.dll` zusätzlich in den
festen Katalog auf. Keine Diagnose-Konfiguration oder Zugangsdaten werden
automatisch importiert. Das Flag ist kein allgemeiner Fremdmod-Import.
Ein Rebuild erhält den bestehenden Diagnosemodus und dessen private Bridge-
Konfiguration; er übernimmt keine normale Quellkonfiguration.
Für den eigenen GPU-Test wird eine frische private Bridge-Konfiguration mit
gezieltem Host-Bind/Client-Allowlist, vorhandener Proxy-Authentifizierung und
privaten State-/Capture-Pfaden eingerichtet. Geheimnisse bleiben außerhalb Git.
Diese Testkomponente gehört nicht in eine öffentliche Starterauslieferung.

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

Historischer Stand der platzsparenden Variante vor GUI, Mod-Updater und Rebuild:
Sie ergänzte echte Hardlink-/Handle-Negativkontrollen,
Quellversionsbindung, verwaltete Wiederverwendung und native
Bootstrap-Konfigurationen: **187 Assertions**. Den getrennten Nachweis der
tatsächlich angelegten Umgebungen, der gefundenen Startfehler und des
Factory-GPU-Tests enthält
[ARENA-START-THIN-20261020.md](ARENA-START-THIN-20261020.md).
Die späteren Schritte besitzen eigene Berichte; insbesondere sind diese älteren
Resultate keine Abnahme eines Basis-Neuaufbaus.

Historische Prüfung der Vollkopie am 20.10.2026 (`f428c57`):

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

**Zum Stand der Vollkopie noch nicht geprüft:** Start des nativen Launchers, vollständige
Charaktererstellung, EFT-Menü oder Raid in dieser privaten Kopie. Insbesondere
die erneute Manifestprüfung nach den nativen Launcher-Patches bleibt Teil des
GPU-Nachtests. Ein Server-Smoke ist kein Spielstart-/Raidnachweis. Es wurden
keine normalen Arena-DLLs aktualisiert und keine Dateien auf einen GPU-Host
ausgerollt. Die Nachweise der anschließenden platzsparenden Variante werden
gesondert protokolliert.

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
- [Microsoft: Hard Links and Junctions](https://learn.microsoft.com/en-us/windows/win32/fileio/hard-links-and-junctions):
  gemeinsame Datei auf demselben Volume, keine private Kopie oder ACL.
- [Microsoft: CreateFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea):
  Share-Modi und Lebensdauer der geöffneten Handles. Die beiden Namen wurden
  zusätzlich mit realen Schreib-/Lösch-/Umbenennungsversuchen geprüft.
