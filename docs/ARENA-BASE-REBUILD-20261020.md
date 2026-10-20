# Arena-Starter: kontrollierter Neuaufbau nach SPT-Updates

Stand: 20.10.2026, `feature/arena-start-prototype`. Ausschließlich Starter-Werkzeuge;
keine Änderung oder Bereitstellung normaler Arena-Client-/Server-DLLs.

## Bedienung

1. Spiel, Launcher und Server beenden. Die normale SPT-Installation wie üblich
   aktualisieren und ihren nativen Spielpatch vervollständigen.
2. Im Arena-Starter dieselbe Quellinstallation wählen und **Check installation**
   ausführen. Eine geänderte SPT-/Spielbasis zeigt **Arena rebuild required**;
   der normale Start bleibt dann gesperrt.
3. **Rebuild Arena environment** wählen. Der Starter bereitet eine neue private
   Runtime vor, übernimmt ausschließlich den eigenen internen Zustand und prüft
   den neuen privaten Server einschließlich Anmeldung. Dabei startet kein EFT.
4. Nach Erfolg erneut prüfen und **Launch Arena** wählen.

Der Starter installiert oder lädt kein SPT-Update herunter. Das Update muss in
der normalen Quelle vollständig vorliegen. Ein ungepatchtes oder gemischtes
Paket wird nicht durch eine neue Hash-Liste legitimiert: Die neue native
Patchausgabe, SPT-Client-/Serverversionen und Modmetadaten werden geprüft.

Modupdates innerhalb derselben Basis bleiben der kleinere automatische Vorgang
aus [ARENA-START-UPDATES-20261020.md](ARENA-START-UPDATES-20261020.md).
Der Neuaufbau steht auch bei unveränderter Quelle ausdrücklich zur Verfügung,
ersetzt aber keine Diagnose beschädigter Besitz-/Profilinformationen.

## Verzeichnisse und Aktivierung

`<SPT>-Arena` bleibt der Verwaltungsordner. Neue Runtimes liegen in
`<SPT>-Arena.generations/<zufällige ID>`; sie werden weder nachträglich verschoben
noch umbenannt. Das hält ihre absoluten Manifestpfade gültig. Ein kleiner
atomar ersetzter Verweis `.arena-start/active-generation.json` im Verwaltungsordner
wählt erst nach erfolgreicher Prüfung den neuen Stand aus. Die GUI und die
CLI-Befehle `play`, `start`, `smoke` und `verify` folgen diesem Verweis.

Der ursprüngliche Ordner und frühere Generationen bleiben bestehen. Sie enthalten
die vorherigen privaten Konten-/Profilbytes und Einstellungen. Nicht manuell aus
Generationsordnern starten; öffentliche Starter-Aufrufe direkt dorthin werden
abgewiesen. Den Verwaltungsordner nicht löschen: Er enthält Sperre, Besitznachweis
und aktiven Verweis. Eine automatische Löschung alter Generationen ist bewusst
noch nicht enthalten.

Jede Generation benötigt erneut Platz für private Dateien; große Spielassets
werden über NTFS-Hardlinks geteilt. Alte Generationen sind **keine vollständigen
Backups der alten Spielbasis**. Änderungen an gemeinsam benutzten Daten können
alle Namen derselben Datei betreffen. Es gibt keine zugesicherte spielbare
Rückkehr zur vorherigen SPT-Version.

## Übernommener privater Zustand

- Das bestehende `.arena-start/account.json` und ausschließlich dessen eine,
  bereits als `Arena-Internal` belegte Profildatei.
- `BepInEx/config/com.maschine.ArenaMode.cfg`.
- `ArenaMode/config/config.json` und `client-settings.json`.
- Die fünf nativen `sptSettings`-Dateien. Vor dem Serverstart gilt anschließend
  wie bisher die einseitige Aktualisierung aus normalem SPT; fehlende Quellen
  lassen private Werte bestehen.
- Nur in einer bereits ausdrücklich zugelassenen Diagnoseumgebung zusätzlich
  `BepInEx/config/com.maschine.UiBridge.cfg`.

Keine normalen Spielerprofile, fremden Modkonfigurationen, Caches, alten
Launcher-Einstellungen, Zertifikate oder Server-Zugangsdaten. HTTP-, Log-, Backup-
und BepInEx-Bootstrapkonfigurationen werden frisch erzeugt. Kit-/Ladderdaten kommen
aus dem zugelassenen Quellpaket. Es gibt weder einen Wipe noch einen erneuten
Register-Aufruf für ein vorhandenes internes Konto; native Profilmigration darf
nur die neue private Profilkopie verändern. Fehlende/fremde Profile führen zum
Abbruch, nicht zu einem stillen Reset.

## Abbruch, Sperren und Prüfgrenzen

Die Sperre im unverändert bleibenden Verwaltungsordner bleibt über den gesamten
Neuaufbau beziehungsweise eine spätere Spielsitzung offen. Beim Wiederaufbau einer
Generation wird zusätzlich deren Laufzeitsperre gehalten. Ein gültiger
unterbrochener Modaustausch wird zuerst aus seinen privaten Sicherungen
wiederhergestellt, auch wenn die SPT-Quelle inzwischen neuer ist.

Das Rebuild-Journal bindet Quell-/Verwaltungsidentität, Kandidat, bisheriges
Manifest und bisherigen aktiven Verweis. Vor Aktivierung bleiben native
Quellenbindungen, Kandidatenmanifest und bekannte unveränderliche Dateien
lesegesperrt. Die neue Besitzdatei und der abschließend geprüfte private Zustand
bleiben ebenfalls bis zur Umschaltung gehalten. Nach dem nativen Servertest wird
der gesamte Kandidatenbaum nochmals auf unerwartete Dateien/Links und Integrität
geprüft; kontinuierlich gehaltene Assethandles vermeiden einen dritten kompletten
Asset-Hashdurchlauf.

- **Abbruch vor Aktivierung:** Der alte aktive Verweis bleibt unverändert. Beim
  expliziten Wiederholen bleibt der unvollständige Kandidat zur Diagnose liegen;
  es entsteht ein neuer Kandidat. Keine automatische Löschung unbekannter Dateien.
- **Abbruch nach Aktivierung:** Ein erneuter Rebuild-Aufruf finalisiert nur die
  bekannte Umschaltung. Er kopiert kein altes Profil zurück und wiederholt den
  Servertest nicht.
- Beschädigte Journale, unerwartete Verweise, fremde Profile oder geänderte
  Besitzdaten blockieren den Vorgang. Belege nicht von Hand löschen, um einen
  Start zu erzwingen.

Schutz gegen versehentliche Parallelstarts und Dateischreiber, keine
Betriebssystem-Sandbox gegen denselben Windows-Benutzer und keine zugesicherte
Stromausfallsicherheit. Eine neue SPT-Version kann zusätzliche Änderungen an
Starter oder ArenaMode erfordern; ein erfolgreicher Test dieser Kombination ist
keine pauschale Freigabe aller künftigen Versionen.

## Automatisierte Prüfungen und Oberfläche

- `ArenaStartChecks`: **611 Assertions bestanden**, darin 130 Mod-Update-,
  85 Rebuild- und sieben Prozesszustandsprüfungen. Reale kleine Dateisystemumgebungen
  prüfen erste/weitere
  Generationen, private Zustandsübernahme, Quellen-/Schreibsperren, unterbrochene
  Modtransaktionen und Rebuilds vor/nach Aktivierung. Beschädigte Besitzdaten,
  zusätzliche Profile/DLLs und unerlaubte Links werden abgewiesen. Native
  Serverprozesse sind in diesen Offline-Prüfungen durch kontrollierte Probes ersetzt.
- `ArenaLauncherChecks`: **43 Assertions bestanden** mit echtem WPF-Fenster und
  kontrolliertem Backend. Start bleibt bei geänderter Basis gesperrt; Rebuild ist
  explizit, verhindert Doppeleingaben, meldet Fehler und startet nach Erfolg kein
  Spiel. Eine erneute Prüfung ist erforderlich.
- Der Rebuild-Zustand scrollt seine Aktionskarte sichtbar ins Fenster. Der Button,
  Hinweis auf zusätzlichen Speicher und der spätere separate Spielstart wurden
  im gerenderten 920 × 760-Fenster geprüft. Kleinfensterdarstellung des bestehenden
  Fortschrittsbereichs bei 700 × 600 bleibt durch die UI-Prüfung abgedeckt.
- Unabhängige Codeprüfung abgeschlossen; keine verbleibenden blockierenden
  Findings. Die realen nativen Ergebnisse stehen getrennt unten.

Rohbelege und WPF-Bilder: `artifacts/arena-base-rebuild/`; nicht als Spieldaten oder
Profile veröffentlichen. Die Prüfungen betreffen ausschließlich die Starter-Tools.

## Eigenständige Test-EXE

Lokal: `artifacts/arena-base-rebuild/portable-r2/maschine-ArenaLauncher.exe`.
Auf SPT-GPU-2:
`C:\Temp\ArenaLauncher-Rebuild-20261020-r2\maschine-ArenaLauncher.exe`.
Die dort vorhandene UiBridge-Testumgebung benötigt wie bisher
`--source C:\SPT41 --diagnostics`; der Diagnosemodus wird nicht automatisch gewechselt.
Beide Dateien sind hashgleich, 140.554.204 Bytes:

```text
SHA256 1936BF6C71D279116700406A71F2065170D88250A8F0EF773A9C434A3A70832E
```

Eigenständiger Windows-x64-Publish einschließlich .NET-Runtime erfolgreich.
Auf GPU2 wurde nur die neue EXE in einen neuen Testordner gestellt; keine dortige
Spielsitzung beendet, keine Umgebung neu aufgebaut und keine normalen Mod-DLLs
ausgetauscht. Der native Versionswechseltest verwendet die lokale Testinstallation.

## Lokaler Versionswechseltest

Testquelle: `D:\arena-launcher-tests\SPT-4.1.1`. Daneben liegt das offizielle
4.1.6-Paket für dieselbe EFT-Basis 40743. `D:\SPT41` dient lediglich als lesbare
Quelle des zugelassenen Arena-/Core-Pakets; keine normale Installation wird
aktualisiert oder gestartet.

Die sauberen 4.1.1- und 4.1.6-Pakete enthalten
`FixPluginTypesSerialization.dll` nicht. Der bisher zwingend ausgewählte
Zusatz-Patcher ist jetzt optional. Alle fest ausgewählten SPT-Runtime-Dateinamen
sind in beiden Paketen vorhanden. Das Spielassembly muss dagegen nach dem
4.1.6-Overlay mit dessen neuem nativen Delta erzeugt werden; die alte gepatchte
Assembly reicht nicht.

Die initialisierte 4.1.1-Baseline und der kontrollierte Overlay-/Ablehnungstest
sind unter `artifacts/arena-base-rebuild/native/` belegt. Das interne Konto wurde
einmalig über SPT registriert und über die native Charaktererstellung initialisiert;
keines der drei vorhandenen normalen Testprofile wurde übernommen. Das offizielle
4.1.6-Overlay wurde ausschließlich auf die Testquelle angewandt, einschließlich
neuer nativer Patchausgabe. Der bisherige Arena-Stand verweigerte anschließend
erwartungsgemäß die geänderte Quelle (`Source installation updated: ...spt-core.dll`).

Der erste vollständige Kandidat erreichte bereits `ARENA_SERVER_SMOKE_PASS`
mit 4.1.6 und demselben initialisierten Profil. Unmittelbar nach dem bestätigten
Serverende verweigerte `RequireIdle` jedoch die Aktivierung wegen eines noch
erfassten `SPT.Server`-Eintrags. Die anschließende Prozessprüfung war leer.
Der aktive Verweis blieb unverändert; 212 geschützte Quelldateien und alle 2.029
privaten Altdateien blieben byteidentisch. Auch das Profil im Kandidaten war
vollständig byteidentisch. Keine Profil- oder Migrationsfehler im Serverlog.

Die erste Version wertete jedes Ergebnis von `GetProcessesByName` als laufend.
Das Idle-Gate prüft jetzt zusätzlich `HasExited`; Abfragefehler führen weiterhin
zum Abbruch. Die konservative Überwachung während einer Spielsitzung bleibt
unverändert. Sieben neue Prüfungen erfassen einen echten lebenden Prozess,
beenden ihn und werten seinen zuvor aufgenommenen Snapshot erneut aus.
Das beweist die Behandlung veralteter Snapshots, keine deterministische
Reproduktion des kurzen nativen Ereignisses. Der folgende native Wiederholungstest
prüfte den kompletten Ablauf einschließlich unterbrochenem Rebuild-Journal.

### Ergebnis des finalen Wiederholungstests

- Finaler eingefrorener CLI-Build:
  `native-final-build-r2/bin/ArenaStart/release/maschine-ArenaStart.dll`, SHA256
  `C4E02A89E9FA7722D88D49F15F6E6CBD7922F0815E7B4547A9EAF2300BCA411A`.
- `ARENA_BASE_REBUILD_PASS`, Exit 0, anschließend unabhängiger
  `NATIVE_BASE_REBUILD_AUDIT_PASS`. Das alte unvollständige Journal wurde
  archiviert und ein frischer Kandidat erstellt.
- 10.285 Katalogdateien, 8.240 geteilte Assets und abschließend 10.305 Runtime-Pfade
  vollständig geprüft. 176 native Quellenbindungen stimmen. Native Patchprüfung,
  SPT-Server **4.1.6**, SPT-Core **4.1.6**, Login und Profilabfrage erfolgreich.
  Der tatsächlich gestartete Serverpfad wurde unabhängig über CIM erfasst.
- Aktiver Verweis zeigt auf Generation `f7075c519ab84846b5aa7e0180623151`;
  Besitzdatei und Manifest stimmen. Beide Rebuild-Journale liegen in der History;
  kein offenes Rebuild-Journal bleibt zurück.
- Dasselbe Konto/Receipt und **vollständig byteidentisches** initialisiertes
  Profil: Level 1, XP 0, 205 Inventargegenstände, unveränderter Inventarhash.
- **212** geschützte Quelldateien einschließlich ihrer Dateiliste unverändert.
  Alle **2.029** bestehenden privaten Altdateien und **2.064** privaten Dateien
  des ersten Kandidaten unverändert. Im ursprünglichen Anker kamen ausschließlich
  der aktive Verweis und zwei History-Dateien hinzu.
- Neuer Serverlog ohne Error-/Exception-Treffer. Native Hinweise zu 20 statt
  empfohlenen 32 GiB RAM und ASP.NETs fehlenden Action-Deskriptoren sind enthalten;
  keine Arena-Profil-/Migrationsfehler. Am Ende **keine nativen Prozesse** mehr.

Belege: `rebuild-4.1.1-to-4.1.6-r2.txt`, `native-rebuild-result.json`,
`audit-4.1.6-r2.txt`, `r2-native-server-process.json`, `r2-native-log-audit.json`
und `r2-rebuild-server-start.log` im genannten ignorierten Native-Verzeichnis.
Die fehlgeschlagene erste Generation bleibt zur Diagnose erhalten.

Das ist eine Abnahme von Vorbereitung, Versionswechsel, internem Profil,
Server/Login, Aktivierung und Wiederaufnahme auf derselben EFT-Basis 40743.
Kein neuer GPU-Spiel-/Raidtest und keine pauschale Freigabe anderer SPT- oder
EFT-Versionen. Die normale Installation und ihre Mod-DLLs wurden nicht aktualisiert.
