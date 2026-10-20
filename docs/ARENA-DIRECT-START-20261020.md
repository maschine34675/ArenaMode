# Direkter Spielstart aus der Arena-Oberfläche

Stand: 2026-10-20. Ergänzung des grafischen Prototyps auf
`feature/arena-start-prototype`; keine Änderung der gewöhnlichen Mod-DLLs.

## Ablauf

**Check installation**, dann **Launch Arena**. Die Oberfläche prüft und hält
die bisherige private Umgebung, startet ihren Server, meldet das interne Konto
an und startet `EscapeFromTarkov.exe` direkt. Das SPT-Launcher-Fenster entfällt.
Beim ersten Profil bleibt die native Charaktererstellung bestehen. Nach
normalem Schließen des Spiels endet der private Server und die Oberfläche
wird wieder bedienbar.

Die bisherigen CLI-Befehle `play` und `start` bleiben als Referenz mit nativem
SPT-Launcher erhalten. Der neue Direktpfad wird von der grafischen Oberfläche
verwendet. Kein Download, Wechsel der normalen Mods oder Import eines normalen
Spielerprofils. Die Quelle muss eine bereits funktionierende SPT-4.1-Installation
mit ArenaMode und Redeploy.Core sein.

## Beibehaltene Prüfungen

- Der bestehende Manifest-, Eigentums-, Profil-, Dateihash- und Assetvergleich
  läuft vor jedem Start; die gemeinsamen Inhalte bleiben während der Sitzung
  gegen Schreiben und Löschen gesperrt.
- Aus privatem `Assembly-CSharp.dll.spt-bak` und dem erlaubten nativen
  `SPT-core`-Delta wird die erwartete Ausgabe in einer temporären `.bin`-Datei
  rekonstruiert. Länge und SHA-256 müssen der installierten privaten
  `Assembly-CSharp.dll` entsprechen. Die echte Spiel-DLL wird dabei nie geändert.
  Fehlende oder abweichende Daten führen zum Abbruch, nicht zur Reparatur.
- `/launcher/v2/version` muss zur Dateiversion von `spt-core.dll` passen.
  Der Direktpfad akzeptiert nur die unterstützte 4.1-Linie; kein
  Entwicklungsversions-Bypass.
- Native Anmeldung über `/launcher/v2/login`, anschließend Profilabfrage über
  `/launcher/v2/profile`. Name und Profil-ID müssen zum eigenen
  `Arena-Internal`-Beleg passen; ein gesetztes Fehlerflag blockiert den Start.
  SPT lässt das nullable Feld bei gesunden Profilen regulär weg.
  Ein frisches Konto darf `wipe=true` melden.
  Die Verbindung bleibt auf den privaten Loopback-Server und dessen Zertifikat
  begrenzt; Token/Profil-ID werden nicht ins GUI-Protokoll geschrieben.
- EFT erhält getrennte Argumente (`-force-gfx-jobs`, `native`, privates Token,
  native Backend-Konfiguration), den privaten Arbeitsordner und keine Shell.
  Native SPT-Prepatch-/Core-Prüfungen bleiben unverändert.
- Die bekannte ungenutzte `hwecho.dll` wird, falls vorhanden, nur nach Prüfung
  als private Datei entfernt, entsprechend dem nativen Startvertrag.
  Keine rekursive Wiederherstellung oder allgemeine DLL-Bereinigung.
- Die tatsächliche Spiel-Prozessinstanz bleibt bis zum Exit gehalten.
  Auch bei Serverfehlern werden gemeinsame Inhalte nicht vorzeitig freigegeben.
  Exit ungleich null oder ein Spielende innerhalb von 20 Sekunden meldet einen
  Startfehler. Eine längere Laufzeit allein ist **kein** Nachweis eines
  erfolgreich geladenen Hauptmenüs.

## Abhängigkeiten und Lizenzanzeige

Die Patchprüfung verwendet SharpHDiffPatch.Core 2.3.0. Die zusätzlichen
Bibliotheken gehören nur zum Starter. Die eigenständige EXE enthält die
Windows-.NET-Runtime und die zugänglichen Lizenztexte unter
**Third-party licenses**. Keine dieser Bibliotheken wird als BepInEx-Plugin
in die normale Installation deployt.

## Nachweise

| Prüfung | Ergebnis |
|---|---|
| `dotnet run --project tools/ArenaStartChecks -c Release -- --native-patch D:/SPT41-Arena` | 254 Assertions bestanden, darunter echter nativer Patchnachweis, Ablehnung einer gleich großen veränderten Ausgabe und das native optionale Profilfehlerfeld. Die Spieldaten werden nur in eine kurzlebige Testkopie gelesen. |
| `dotnet run --project tools/ArenaLauncherChecks -c Release` | 27 Assertions am echten WPF-Fenster mit kontrolliertem Backend: Direktstart-Status, Fehler-/Lebensdauerzustände und zugänglicher kopierbarer Lizenzdialog. |
| Windows-x64-Publish | Eigenständige EXE einschließlich Runtime und nativer Patchabhängigkeiten gebaut; keine normale Mod-Deployment-Pipeline ausgeführt. |
| Unabhängiger Review | `DirectGame`/`RuntimeHost` einschließlich Korrektur des nativen nullable Profilfehlerfelds ohne offenen Must-fix-Befund. Die vom Reviewer selbst implementierte Lizenzanzeige wurde separat durch den Hauptagenten und den WPF-Test geprüft. |
| Native GPU-Prüfung | Zwei direkte Starts der unten genannten EXE auf SPT-GPU-2 bis MainMenu bestanden (114 bzw. 115 Sekunden inklusive vollständiger Verifikation). Gleiche private Profil-ID, neue EFT-Prozessinstanz; kein SPT-Launcher-Prozess. |

Artefakt:

- Lokal: `artifacts/arena-direct-start/portable-r2/maschine-ArenaLauncher.exe`
- GPU2: `C:\Temp\ArenaLauncher-Direct-r2-20261020\maschine-ArenaLauncher.exe`
- SHA-256: `A161DFB2BF7CBD4030F74E602D137E9E11DE61C1A85333E6D735B047EE2210E6`
- 140.487.132 Bytes; nur diese EXE ist für den eigenständigen Starter nötig.

Der erste native Versuch mit dem vorherigen Build brach nach erfolgreicher
Datei-/Patchprüfung und nativer Anmeldung sicher vor dem EFT-Start ab:
Die neue Prüfung verlangte fälschlich ein explizites `false` im Profilfehlerfeld.
Der Server deklariert es als `bool?` und serialisiert `null` nicht
(`MiniProfile`, `ProfileController`, `JsonUtil.WhenWritingNull`).
Das wurde im Direktstart-Code und den Regressionstests korrigiert;
Profile oder Serverdaten wurden dafür nicht verändert.

Für die native Automation wurde im separaten UiBridge-Repository `gpu-run`
um `start --via arena --profile Arena-Internal` ergänzt (`20a9ed6`). Der Pfad
behält Host-/Display-/Belegungskontrollen, prüft den vorhandenen privaten
Kontobeleg und die tatsächlich sichtbare, startbereite WPF-Oberfläche und
vergleicht anschließend die UiBridge-Sitzungs-ID. Er verwendet nicht die
normalen Server-/Spielstarthilfen. Ein Zeitlimit widerruft seine Startfreigabe;
kein automatischer Wiederholungsversuch. `restart` und Scenario-Starts über
diesen neuen Pfad sind vorerst ausdrücklich ausgeschlossen.
Die vollständige `gpu-run`-Suite bestand 1.707 Tests, einschließlich der neuen
Fehlerpfade; PowerShell-Syntax und unabhängiger Review wurden ebenfalls geprüft.

Beide vollständigen Zyklen zeigten nach normalem EXIT wieder **Session
finished** und stoppten den privaten Server. Der zweite Start erfolgte aus
demselben Fenster. Beide tatsächlichen Prozesspfade lagen unter
`C:\SPT41-Arena`, das Starterprogramm im oben genannten separaten Temp-Ordner.
Im geprüften zweiten Start-/Menüfenster (16:50:11–16:51:16, lokale Zeit)
enthielten Client-, Server- und EFT-Logs keine Error-Einträge. Die erste
Client-Aufnahme enthält den erwarteten Hinweis der Serialization-Fix-Komponente,
dass BigBrainCompat wegen des in dieser Variante fehlenden optionalen BigBrain
nicht bearbeitet wird; kein daraus folgender Startfehler.
`Logging.Disk.WriteUnityLog` ist in der privaten Testkonfiguration deaktiviert;
die Logauswertung beweist daher nicht die Erfassung sämtlicher Unity-Meldungen.
Die Oberfläche wurde anschließend normal geschlossen. Die abschließende
Prozessaufnahme enthält weder EFT, SPT-Server, SPT-Launcher noch ArenaLauncher.

Der zusätzliche SHA-256-Vergleich vor/nach den beiden erfolgreichen Zyklen
meldete **1.369 ausgewählte Originaldateien unverändert**: erfasst wurden
die in `Measure-Source.ps1` aufgeführten Plugin-/Patcher-, Profil- und
Konfigurationsbereiche mit ihren festen Dateiendungen, nicht sämtliche
Dateien der Installation. Die Baseline entstand erst nach dem ersten
abgebrochenen Anmeldeversuch und nach dem autorisierten Beenden der vorher
laufenden normalen Sitzung. Keine Aussage über nicht erfasste Dateien oder
frühere Änderungen.

Lokale ignorierte Belege unter `artifacts/arena-direct-start/`: vollständige
erste MainMenu-/Finished-Aufnahmen, UI-Zustand `gui-final-state.txt`,
Prozessaufnahmen beider Starts und `final-processes.json`, getrennte
Client-/Serverlogs und der begrenzte Quellvergleich.
Die zweite Finished-PNG enthält nur Bildänderungen und wird nicht als
vollständige Bildschirmaufnahme gewertet.

Getestet wurde das bereits vorhandene private Konto. **Kein neuer Raid und
keine erneute frische Charakteranlage** in diesem Direktstart-Test. Die
native Erstprofil-/Raid-Prüfung des früheren Launcher-Pfads bleibt ein separater
Nachweis; `wipe=true` und das optionale Fehlerfeld sind hier zusätzlich durch
gezielte Protokolltests abgedeckt.

## Grenzen

Die native Spieloberfläche ist noch nicht auf Arena beschränkt. Optionale
SAIN/PFT/APBS/MapPort/Killcam-Installationen werden noch nicht übernommen.
Der frühere Vergleich der ausgewählten Quelldateien bleibt wegen gleichzeitiger
externer Synchronisierung nicht akzeptiert; dieser Direktstart-Nachweis ersetzt
keinen vollständigen Vergleich mit ruhender Quelle. Frühere PlayerBody-Fehler
beim Spielende und Factory-Pickup-Anker sind keine durch diese Änderung behobenen
Probleme. Siehe [Thin-Runtime-Nachweis](ARENA-START-THIN-20261020.md).

Es handelt sich weiterhin um einen Prototyp. Prozesszwangsabbruch, Windows-
Shutdown oder Rechnerausfall können die vom Starter gehaltenen Dateisperren
aufheben; die private EXE deshalb nicht außerhalb dieses Ablaufs starten.
