# Arena-Starter: Spieleinstellungen und Updateverhalten

Stand: 2026-10-20, `feature/arena-start-prototype`. Nur Starter-Werkzeuge,
keine Änderung der normalen Arena-Client-/Server-DLLs.

## Übernahme der Spieleinstellungen

Vor jedem verwalteten Start übernimmt `NativeGameSettings.CopyFromSource` die
vorhandenen Dateien aus `<normales SPT>/SPT_Runtime/user/sptSettings` in den
gleichnamigen privaten Ordner:

| Datei | Inhalt |
|---|---|
| `Control.ini` | Tastenbelegungen, Maus-/Zielempfindlichkeit |
| `Game.ini` | Sprache, FOV und sonstige Spieleinstellungen |
| `Graphics.ini` | Anzeige- und Grafikeinstellungen |
| `PostFx.ini` | PostFX |
| `Sound.ini` | Lautstärken, Audio-/Mikrofonoptionen |

Die Quelle ist der im geprüften Manifest gespeicherte SPT-Ordner desselben
Rechners; keine feste D:-Adresse und keine Übernahme zwischen PCs. Die `.ini`-
Dateien enthalten JSON. Ihre Bytes einschließlich BOM bleiben unverändert;
native Migrationen und Ergänzungen bleiben EFT überlassen.

Die Übernahme erfolgt **bei jedem Start**, auch bei bereits bestehenden privaten
Umgebungen. Vorhandene Quellen ersetzen die entsprechenden Arena-Einstellungen.
Änderungen während einer Arena-Sitzung schreiben **niemals zurück** in die normale
Installation. Sie werden beim nächsten Start wieder durch die dann vorhandenen
Quellen ersetzt. Fehlende Quellen lassen private Einstellungen unverändert oder
überlassen die Initialisierung den nativen Defaults.

Die feste Liste schließt unbekannte Dateien, Backups, Legacy-Dateien, Profile,
Launcher-Zugangsdaten, BepInEx- und Mod-Konfigurationen aus. Kein Hardlink,
keine Verzeichnisverknüpfung und keine rekursive Übernahme.

## Schutz und Fehlerverhalten

Der Aufruf liegt in `RuntimeHost.RunCore` nach Besitz-/Integritätsprüfung und
gegebenenfalls nativer Patchprüfung, unter der exklusiven Start-Sperre und den
gehaltenen Asset-Sperren. Erst anschließend darf der private Server starten.
Die Idle-Prüfung bleibt vor und nach der Übernahme erhalten. Reine Vorschau,
`plan`, `prepare` und `verify` importieren keine Einstellungen.

Alle vorhandenen Quellen werden vor der ersten Ersetzung eingelesen und als
JSON-Objekt bis maximal 1 MiB pro Datei geprüft. Quell-Lesehandles bleiben bis
zum Abschluss gehalten. Alle vorhandenen Ziele werden vorab exklusiv geöffnet
und gegen Hardlinks und Schreibschutz geprüft; Reparse Points sind ebenfalls
ausgeschlossen. Erst nach vollständiger Vorprüfung werden private temporäre
Dateien geschrieben und **pro Datei atomar** umbenannt.

Fehler verweigern den Start, ohne Quellen zu verändern. Ein Fehler während der
Ersetzung kann bereits vollständig ersetzte private Dateien zurücklassen:
es gibt keinen atomaren Gesamt-Rollback über fünf Dateien. Verbliebene eigene
Tempdateien werden aufgeräumt; ein erneuter Start versucht die Übernahme erneut.
Ein Prozess-/Rechnerabbruch kann Tempdateien hinterlassen. Diese werden nicht als
native Einstellungen geladen.

Einstellungen bleiben außerhalb des unveränderlichen Manifestkatalogs, weil EFT
sie selbst normalisiert und speichert. Die Prüfung aller privaten Laufzeitpfade
auf unerlaubte Links bleibt unverändert.

Native Quellen: `../_reference/modules-master/project/SPT.Custom/Patches/SaveSettingsToSptFolderPatch.cs`
setzt beide `SettingsManager`-Pfade relativ zum Arbeitsverzeichnis;
`../_decompiled/EFT.Settings/SettingsManager.cs` erzeugt genau die fünf Provider.
`Bsg.GameSettings.Json/JsonFileSettingsProvider.cs` liest und schreibt diese Dateien.

## Mod- und SPT-Updates

Die Einstellungsübernahme bleibt vom inzwischen ergänzten
[kontrollierten Mod-Updatepfad](ARENA-START-UPDATES-20261020.md) getrennt:

| Änderung in normalem SPT | Verhalten der bestehenden Arena-Umgebung |
|---|---|
| ArenaMode-DLLs, Audio, `kits.json`, `ladder.json`, Redeploy.Core | Geänderte zugelassene Dateien werden vor dem Start aus normalem SPT übernommen; vorhandenes internes Konto bleibt bestehen. Die Vorschau nennt neue/geänderte/entfernte Dateien. |
| Optionale/fremde Mods | Nicht im festen Katalog, daher weiterhin nicht übernommen. |
| Gebundene Spiel-/SPT-Dateien, etwa EFT.exe, UnityPlayer, SPT.Server, Launcher, spt-core, Managed, native Patches | Geänderter Quellenhash verweigert den nächsten Start; die Vorschau bietet den ausdrücklichen Basis-Neuaufbau an. |
| Bereits katalogisierte gemeinsame Assets | Geänderte Daten oder eine ausgetauschte Dateiidentität verweigern den Start. Neue Quellassets werden erst beim ausdrücklichen Neuaufbau katalogisiert. |
| Andere private SPT-/Serverdaten ohne Quellenbindung | Bleiben zwischen Starts alte Kopien; der ausdrückliche Neuaufbau erstellt auch diese Kopien neu. Eine allgemeine automatische Updateerkennung existiert nicht. |

Für Spiel-/SPT-Updates erstellt **Rebuild Arena environment** ausdrücklich eine
neue private Generation. Zuvor muss die normale SPT-Installation vollständig
aktualisiert und funktionsfähig sein; Spiel, Launcher und Server müssen beendet
sein. Der Starter installiert oder lädt keine SPT-Updates herunter.

`<SPT>-Arena` bleibt als Steuerungsanker erhalten. Die neue Runtime liegt dauerhaft
unter `<SPT>-Arena.generations/<ID>` und wird erst nach Prüfung und privatem
Server-Smoke aktiviert. Das vorhandene interne Konto samt Profil sowie die
zugelassenen privaten Arena-Einstellungen werden kopiert, nicht zurückgesetzt.
Native Profilmigrationen dürfen nur die neue Kopie ändern. Beim Server-Smoke gilt
bereits die oben beschriebene Spieleinstellungsübernahme aus normalem SPT:
vorhandene Quellen ersetzen die kopierten privaten Werte; fehlende Quellen lassen
sie bestehen. Es wird kein normales Spielerprofil importiert.

Alte und unvollständige Generationen bleiben erhalten. Sie brauchen zusätzlichen
privaten Speicher; ihre Hardlinks sind keine unveränderlichen Asset-Snapshots.
Keine manuelle Verschiebung des Ankers oder Anpassung der Manifestpfade ist nötig.
Details und gesonderte Nachweise stehen im
[Rebuild-Bericht](ARENA-BASE-REBUILD-20261020.md).

Keine einzelnen DLLs von Hand überschreiben und keine Manifesthashes nachziehen:
das umgeht die vorgesehene Zusammenstellung bzw. verletzt deren Integritätsprüfung.
Der Mod-Updater ersetzt nur vorher verifizierte, eigene Paketdateien und aktualisiert
deren Manifesteinträge nach erfolgreichem Austausch.

## Historische Prüfung und Artefakt vor dem Mod-Updater

- `ArenaStartChecks`: **389 Assertions bestanden**, darunter 77 neue Import-
  Prüfungen sowie vier Prüfungen mit echtem vorbereitetem Manifest und
  gehaltenen Laufzeit-/Asset-Sperren. Native private Einstellungsänderungen bleiben
  danach verifizierbar; Manifest, Quellen und Profilgrenze bleiben erhalten.
- Byteidentische UTF-8-/UTF-16-Kopien, Wiederholungsübernahme, fehlende/unbekannte
  Dateien, ungültiges JSON, Übergröße, Hardlinks, Datei-/Verzeichnis-Symlinks,
  gesperrte Quellen/Ziele und Schreibschutz geprüft.
- Absichtlicher Fehler während der Ersetzung: keine falsche N/N-Meldung,
  verbliebene Temps bereinigt, vollständige private JSON-Dateien und unveränderte
  Quellen; Quell-Schreibsperren bis zum Fehler gehalten.
- `ArenaLauncherChecks`: **29 Assertions bestanden**. Importstatus aus dem
  Hintergrundthread sichtbar; bestehende UI-/Lifecycle-Prüfungen grün.
- WPF-Renderbilder bei 920 × 760 und 700 × 600 visuell geprüft:
  `artifacts/arena-launcher-settings/fixture-*.png`.
- Unabhängiger Code- und Renderreview ohne offene Befunde. Dabei korrigierte
  Exceptionfilter anschließend erneut geprüft. Windows-x64-Publish erfolgreich.
- Nur neue Starter-EXE auf GPU2 bereitgestellt, lokaler/entfernter Hash identisch.
  Keine normalen Mod-DLLs ersetzt und keine tatsächlichen Spieleinstellungen
  übernommen. **Kein neuer EFT-/Serverstart oder Ingame-Test** in diesem Schritt.
  Die Übernahme in der echten Installation erfolgt beim nächsten verwalteten Start.

Lokal: `artifacts/arena-launcher-settings/portable/maschine-ArenaLauncher.exe`.
GPU2: `C:\Temp\ArenaLauncher-Settings-20261020\maschine-ArenaLauncher.exe`.
Für den dort vorhandenen Diagnoseordner mit `--source C:\SPT41 --diagnostics`
starten; normale Benutzer benötigen das Diagnoseflag nicht.

SHA-256: `C5799EC5C57C42428141A00613B63BDB38513ECA1C5DFC997B8E4F6E81F93486`.
