# Grafischer Arena-Starter

Stand: 2026-10-20. Additive Windows-Oberfläche in `tools/ArenaLauncher/`, auf
dem Branch `feature/arena-start-prototype`. Keine Änderung oder Bereitstellung
der normalen Arena-Client-/Server-DLLs und noch kein Bestandteil des Releases.

## Bedienung

1. `maschine-ArenaLauncher.exe` öffnen. Normales EFT, SPT-Server und SPT-Launcher
   vor dem eigentlichen Arena-Start beenden.
2. Unter **Existing SPT installation** den vorhandenen SPT-Ordner wählen,
   beispielsweise `C:\SPT41`. **Browse** öffnet den nativen Windows-Ordnerdialog.
3. **Check installation** liest die Auswahl. Bei einer neuen Umgebung wird nur
   der Kopier-/Hardlink-Plan berechnet; noch keine Umgebung angelegt.
   Bei einer vorhandenen Umgebung werden Besitznachweis und Dateikatalog gelesen.
   Die angezeigten Größen sind Katalogsummen, kein vollständiger Integritätscheck
   und keine Messung tatsächlich belegter NTFS-Cluster.
4. **Prepare & launch** legt beim ersten Start `<SPT-Ordner>-Arena` automatisch an;
   danach heißt der Knopf **Launch Arena**. Der Starter prüft alle privaten Dateien,
   Quellbindungen und freigegebenen Assets, startet den privaten Server und öffnet
   meldet das Konto `Arena-Internal` nativ an und startet EFT direkt.
   Ein zusätzliches SPT-Launcher-Fenster ist nicht mehr nötig. Versionsvergleich
   und Nachweis der nativen SPT-Patchausgabe bleiben vorgeschaltet; Details im
   [Direktstart-Bericht](ARENA-DIRECT-START-20261020.md).
   Der erste Spielstart benötigt weiterhin die native Charaktererstellung.
5. Zum Beenden EFT normal schließen. Der Arena-Starter
   wartet auf das Spiel und beendet dann seinen privaten Server. Erst danach lässt
   sich sein Fenster schließen oder erneut starten.

Das Fenster kann währenddessen minimiert werden. Während Prüfung, Vorbereitung
und Sitzung sind Ordnerwechsel und weitere Starts gesperrt. Ein Schließversuch
zeigt einen Hinweis, beendet aber keine Prozesse und gibt keine Asset-Sperren
frei. Es gibt in diesem Prototyp bewusst keinen Abbrechen-/Erzwingen-Knopf.
Ein Betriebssystem-Shutdown, Prozessabbruch oder Rechnerausfall lässt sich dadurch
nicht absichern; die bisherigen Grenzen des Thin-Runtime-Prototyps gelten weiter.

Ein eingeklappter **Activity log** enthält bis zu 200 Statuszeilen.
**Third-party licenses** öffnet die mitgelieferten Lizenztexte des Starters.
Serverdetails stehen weiterhin in `<Arena-Ordner>\.arena-start\server-start.log`.
Ein unerwarteter Serverexit wird angezeigt; solange Spiel oder Launcher noch
laufen, bleiben die gemeinsamen Assets gesperrt. Nach dem Schließen erscheint
ein Fehler mit Exitcode, kein erfolgreicher Sitzungsabschluss.

Gespeichert wird nur der zuletzt zum Start ausgewählte Quellordner unter
`%LOCALAPPDATA%\maschine\ArenaLauncher\settings.json`. Kein Profil, Passwort oder
SPT-Launcher-Zustand wird aus der normalen Installation übernommen.

Optional für Entwickler:

```powershell
.\maschine-ArenaLauncher.exe --source C:\SPT41
.\maschine-ArenaLauncher.exe --source C:\SPT41 --diagnostics
```

`--diagnostics` muss ausdrücklich angegeben werden und erlaubt nur die bereits
definierte UiBridge-Diagnosevariante. Ein bestehender Diagnose-Ordner wird nicht
stillschweigend als normale Umgebung übernommen oder umgekehrt.

## Aufbau und Build

Die Oberfläche verwendet WPF auf .NET 10 für Windows, ohne zusätzliche
UI-Bibliothek oder Grafiken. Sie ruft `ManagedEnvironment` und `RuntimeHost`
direkt auf; kein duplizierter Kopierer, Profilimport oder Shell-Kommandoaufbau.
Die gesamte Vorbereitung und Serversteuerung läuft außerhalb des UI-Threads.
Ausnahmen eines Status-Empfängers dürfen die Prozessbereinigung nicht verhindern.
Veraltete Meldungen früherer Sitzungen werden im Fenster verworfen.

Gezielter Entwicklerbuild; **nicht** die automatisch deployende Arena-Solution:

```powershell
dotnet build tools/ArenaLauncher -c Release
dotnet run --project tools/ArenaStartChecks -c Release
dotnet run --project tools/ArenaLauncherChecks -c Release
```

Dieser Build braucht zur Ausführung die Windows Desktop Runtime 10.
Ein eigenständiges Windows-x64-Programm einschließlich Runtime entsteht so:

```powershell
dotnet publish tools/ArenaLauncher -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o artifacts/arena-direct-start/portable
```

Die `maschine-ArenaLauncher.exe` aus diesem Ordner kann allein weitergegeben
werden; sie enthält auch die gemeinsamen Starter-Klassen. Die daneben vom
referenzierten CLI-Projekt erzeugte `maschine-ArenaStart.runtimeconfig.json`
ist für diese EXE nicht erforderlich. Die EXE enthält weder Spieldateien noch
SPT oder Mod-DLLs; diese werden erst aus der vom Benutzer gewählten Installation
für die private Umgebung bereitgestellt. Eine zusätzliche .NET-Installation
ist für diesen Publish-Build nicht nötig.

## Historische GUI-Prüfung vor dem Direktstart am 2026-10-20

Die folgenden Ergebnisse gelten für `27e990c` mit dem nativen Launcher-Fenster.
Aktuelle Direktstart-Prüfungen und der neue EXE-Hash stehen im
[Direktstart-Bericht](ARENA-DIRECT-START-20261020.md).

| Prüfung | Ergebnis |
|---|---|
| `ArenaStartChecks`, Release | 203 Assertions bestanden: bestehende Kopier-/Hardlink-/Profilgrenzen plus Vorschau ohne Anlage, Ablehnung fremder Kataloge, fehlerhafter Reporter und gehaltene Client-Lebensdauer bei Serverexit. |
| `ArenaLauncherChecks`, Release | 23 Assertions bestanden, mit echtem WPF-Fenster und kontrolliertem Backend: Fehlerzustände, Doppelklick, Schließschutz, Minimieren, Fortschritt vom Hintergrundthread, veraltete Meldungen und Hinweis-Lebensdauer. Ein ungültiger Ordner wird zusätzlich durch den echten Backend-Pfad geprüft. |
| Gezielter Build und Windows-x64-Publish | Erfolgreich. Keine normale Arena-Deployment-Pipeline ausgeführt. |
| Unabhängiger Code-Review | Serverexit-Erfolgsmeldung korrigiert und erneut geprüft; keine offenen Codebefunde. |
| SPT-GPU-2, native GUI | Ordnerdialog einschließlich Abbrechen, ungültiger Ordner, richtige Vorschau, gesperrte konkurrierende Aktionen, reaktionsfähige Statusansicht und verweigerter Schließversuch bestanden. |
| Finale eigenständige EXE auf SPT-GPU-2 | Allein in einem neuen Testordner gestartet. 10.290 Dateien und 8.239 gemeinsame Asset-Sperren geprüft, vorhandenes privates Konto im echten SPT-Launcher geöffnet; nach normalem Schließen Server beendet, Oberfläche wieder bereit und alter Schließhinweis entfernt. |
| Native Darstellung | Normales Fenster und 760 × 660 geprüft. Texte umbrechen, überlanger Inhalt ist scrollbar, Start bleibt erreichbar. |
| Abschluss | GUI und nativer Launcher normal geschlossen; frische Prozesskontrolle meldet weder Server noch Spiel. |

Insgesamt zwei echte GUI-/Launcher-Durchläufe, davon einer mit dem finalen
eigenständigen Publish-Artefakt. Ein zunächst nach Sitzungsende stehengebliebener
Schließhinweis wurde korrigiert und im zweiten Durchlauf nachgeprüft.
**Kein neuer Raid-Test und kein absichtlich herbeigeführter nativer Serverabsturz**:
Der neue Fehlerpfad ist durch den automatisierten Lebensdauertest abgedeckt.
Die vorherige native Raid-Prüfung bleibt im Thin-Runtime-Bericht separat dokumentiert.

Getestete finale EXE:

- Lokal: `artifacts/arena-launcher-ui/portable/maschine-ArenaLauncher.exe`
- GPU2: `C:\Temp\ArenaLauncher-UI-r2-20261020\maschine-ArenaLauncher.exe`
- GPU2-Aufruf: `--source C:\SPT41 --diagnostics`
- SHA-256: `09E6212CC8EE550C58D31AF29BA54C484C2DACAF5B2FA60A2A2266FAE71B1339`

Lokale, ignorierte Nachweise: `artifacts/arena-launcher-ui/fixture-*.png`
(kontrollierte Fensterprüfung) und `artifacts/arena-thin/gui-final-*.png`
sowie `gui-final-lifecycle.txt` (native GPU-Oberfläche).
Der bestehende private Diagnoseordner wurde wiederverwendet, kein Originalprofil
importiert und kein temporäres Drittanbieter-Testmod hinzugefügt.

## Weiterhin bestehende Grenzen

Die GUI führt keinen Updater, keine Reparatur, keinen Profilimport und keine
Umschaltung der normalen Mods ein. Die native Spieloberfläche ist noch nicht auf
Arena beschränkt. Optionale Mods wie PFT, SAIN, APBS, ArenaMapPort und Killcam
werden in dieser ersten isolierten Umgebung noch nicht übernommen.

Der ältere Vergleich der erfassten normalen Quelldateien bleibt ausdrücklich
nicht akzeptiert: während jener Messungen wurden Profile, Einstellungen und
Backups extern synchronisiert. Das damalige Messskript erfasst ausgewählte
Bereiche und Dateitypen, keinen vollständigen Baum. Dieser GUI-Test ersetzt
keine erneute Messung mit ruhender Quelle. Ebenso bleiben die vorher beobachteten PlayerBody-Fehler
beim Spielende und Factory-Pickup-Anker als separate Punkte offen.
Siehe [Thin-Runtime-Bericht](ARENA-START-THIN-20261020.md) und
[Sicherheitsgrenzen des Starters](ARENA-START-PROTOTYPE.md).
