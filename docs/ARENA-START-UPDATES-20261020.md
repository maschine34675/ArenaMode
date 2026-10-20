# Arena-Starter: kontrollierte lokale Mod-Updates

Stand: 2026-10-20, Branch `feature/arena-start-prototype`. Änderung ausschließlich
an den Starter-Werkzeugen. Kein allgemeiner Modmanager, Download oder
Spiel-/SPT-Updater; normale Arena-Client-/Server-DLLs werden nicht neu gebaut.

## Bedienung

ArenaMode wie bisher in der **normalen SPT-Installation** aktualisieren, während
Spiel und Server geschlossen sind. Anschließend im Arena-Starter diese Installation
prüfen und **Launch Arena** wählen. Die Vorschau nennt neue, geänderte und entfernte
Paketdateien. Vor dem Start werden die privaten Dateien vollständig verifiziert
und die zugelassenen Änderungen übernommen. Auch bei gleicher Versionsnummer
werden neue Builds anhand ihrer SHA-256-Hashes erkannt.

`<SPT>-Arena` bleibt bestehen: Das interne Konto, sein Profil, private BepInEx-
und Mod-Konfigurationen werden nicht zurückgesetzt. Es gibt keine Rückübertragung
in normalem SPT. Unveränderte Mod-Dateien werden nicht neu geschrieben.
Die Größenanzeige zeigt den letzten geprüften Katalogstand; nach einem Update
kann **Check installation** sie auffrischen.

Die fünf nativen Spieleinstellungen werden weiterhin in einem **separaten**
Schritt vor jedem Start aus normalem SPT übernommen, wie im
[Einstellungsbericht](ARENA-START-SETTINGS-20261020.md) beschrieben. Dessen
Einzeldatei-Austausch ist nicht Teil des Modpaket-Rollbacks.

## Zulässiger Umfang

| Bestandteil | Auswahl |
|---|---|
| Arena | Client-DLL, BigBrainCompat-Bibliothek und Server-DLL an ihren bisherigen festen Pfaden |
| Redeploy | Ausschließlich `BepInEx/plugins/maschine-Redeploy.Core.dll` |
| Ansagen | Direkte `.ogg`-Dateien in `maschine-ArenaMode/announcements` |
| Effekte | Direkte `.wav`-Dateien in `maschine-ArenaMode/sounds` |
| Dokumentation | Unter `maschine-ArenaMode/docs`: `.md`, `.txt`, `.json`, `.png`, `.svg` und exakt `docs/LICENSE` |
| Kitdaten | Exakt `ArenaMode/config/kits.json` und `ladder.json` aus normalem SPT |
| Diagnose | UiBridge nur bei einer bereits ausdrücklich als Diagnoseumgebung angelegten Installation |

Keine `sounds/reserve`, AreaDrafts, fremden DLLs, zusätzlichen Mods, Profile,
Launcher-Zugangsdaten, BepInEx-Konfigurationen, `config.json`,
`client-settings.json` oder Sync-Konfliktdateien. Der gleiche enge Dateikatalog
gilt jetzt für die Erstvorbereitung. Unbekannte ausführbare Dateien in den
Audio-/Dokumentationsbäumen verweigern die Zusammenstellung.

Kitdaten sind die möglicherweise angepasste Auswahl aus der normalen Installation,
nicht zwingend unveränderte Release-Defaults. Beide müssen nichtleere JSON-Arrays
sein. Fehlende Pflicht-DLLs oder Kitdateien werden niemals als beabsichtigte
Deinstallation behandelt. Die Diagnoseoption lässt sich nicht durch ein Update
hinzufügen oder entfernen.

Die DLLs werden mit `PEReader` untersucht, **nicht im Starter geladen**:
verwaltete Assembly, genauer Name, gleiche Arena-Paketversionen und gültige
`RedeployCoreBuiltAgainst`-Mindestversion. Core darf neuer sein. Unterschiedliche
Commit-/InformationalVersion-Suffixe sind zulässig, ebenso neue Inhalte bei
gleicher Versionsnummer. Das beweist keine vollständige zukünftige API-/SPT-
Kompatibilität; native Serverprüfungen und Arena-Coreschutz bleiben aktiv.

## Austausch, Fehler und Wiederaufnahme

1. Unter der exklusiven `.arena-start/running`-Sperre wird zuerst ein eventuell
   offenes Update anhand seiner privaten Backups zurückgerollt.
2. Die bisherige Umgebung muss danach die vollständige Integritäts-, Quellen-,
   Asset- und Profilprüfung bestehen. Geänderte private Dateien werden nicht
   stillschweigend als neuer Sollzustand übernommen oder automatisch repariert.
3. Der vollständige neue Quellkatalog wird mit gehaltenen Lesehandles erfasst.
   Paketzusammenstellung, Metadaten, Kitdaten, Zielzustände und Platzreserve werden
   geprüft. Schreibzugriffe auf erfasste Quelldateien bleiben währenddessen gesperrt.
   Bei einem tatsächlichen Modupdate werden außerdem die nativen Quellenbindungen
   erneut geprüft und bis zum Abschluss mit Lesehandles gehalten: Ein SPT-Update
   während des vorherigen langen Assetchecks darf kein fremdes Basispaket liefern.
   Dies gilt auch für den älteren Vollkopie-Modus.
4. Neue Dateien, alte Sicherungskopien und beide Manifeststände werden unter
   `.arena-start/mod-updates/<Transaktions-ID>` als private `.bin`-Dateien vorbereitet.
   Keine zusätzlichen ausführbaren DLL-Kopien im Laufzeitbaum.
5. Erst nach abgeschlossener Vorbereitung wird das Journal
   `.arena-start/mod-update.json` atomar aktiviert. Ziele werden unmittelbar vor
   Austausch/Löschung erneut auf erlaubten Inhalt, Links und Schreibschutz geprüft.
   Austausch erfolgt pro Datei atomar; Entfernen betrifft nur eigene Katalogpfade.
6. Neue Inhalte und Abwesenheit entfernter Dateien werden geprüft. Das neue
   Manifest wird **zuletzt** atomar geschrieben; danach wird das Journal entfernt.
   Vorher startet weder Server noch Spiel.

Bei einem gewöhnlichen Fehler wird sofort der alte Paketstand wiederhergestellt.
Nach Prozessabbruch erfolgt derselbe Rücklauf beim nächsten Start – auch wenn
das neue Manifest bereits geschrieben war. Backups werden dabei nicht verbraucht,
damit ein erneut unterbrochener Rücklauf wiederholt werden kann.

Beschädigte Journale/Backups, fremde Zielinhalte, unbekannte Manifeststände oder
gesperrte Dateien stoppen die Wiederherstellung. Es wird nichts geraten oder
blind überschrieben. Bei einer vorübergehenden Dateisperre das betreffende Programm
schließen und erneut starten. Bei beschädigter Recovery nicht einfach Journal oder
Manifest löschen; Belege erhalten und die Ursache prüfen.

Die Recovery-Vorschau benötigt kein momentan vollständiges Quellpaket. Ein
anschließender neuer Updateversuch verlangt jedoch wieder alle Pflichtdateien.
Unvollständige Erstvorbereitungen ohne gültiges Manifest werden weiterhin nicht
übernommen. Ein Abbruch vor Journalaktivierung kann ungenutzte `.bin`-Dateien
hinterlassen; sie werden nicht als Paket oder Recovery-Autorität verwendet.

Schutzgrenzen: keine Betriebssystem-Sandbox gegen denselben Windows-Benutzer,
kein atomarer Gesamtverzeichniswechsel, keine zugesicherte Stromausfallsicherheit.
Die Quellinstallation muss fertig aktualisiert sein; ein während der Aufnahme
pausierter externer Installer kann nicht allgemein als solcher erkannt werden.
Spiel-/SPT-Änderungen und getauschte gemeinsame Assets bleiben durch die bisherigen
Versions-/Identitätsbindungen gesperrt und benötigen eine neue Umgebung.

## Prüfung

Die automatischen Prüfungen liegen in `tools/ArenaStartChecks/ModUpdateChecks.cs`,
der opt-in Vergleich einer echten privaten Umgebung in `NativeModUpdateChecks.cs`.
Letzterer startet selbst keine Prozesse und gibt keine Profilinhalte oder Konten-IDs
aus. `ArenaLauncherChecks` prüft das tatsächliche WPF-Fenster mit kontrolliertem
Backend; dies ist kein EFT-Ingame-Test.

```powershell
dotnet run --project tools/ArenaStartChecks -c Release
dotnet run --project tools/ArenaLauncherChecks -c Release
# Nur bewusst für eine vorhandene private Arena-Umgebung; übernimmt deren Updates:
dotnet run --project tools/ArenaStartChecks -c Release -- --native-mod-update D:/SPT41-Arena
dotnet run --project tools/ArenaStart -c Release -- smoke D:/SPT41-Arena
```

Abgedeckt: No-op und gleiche Versionsnummer/neuer Inhalt; neue/geänderte/entfernte
Dateien; fehlende Pflichtdateien; falsche Assembly/Version/Core-Minimum;
ungültige/leere Kitdaten; unbekannte Executables; Quelle/Target gesperrt oder
verlinkt; Schreibschutz; während Staging veränderte Dateimenge; fremde neue
Zieldateien; Wiederauftauchen gelöschter Dateien; Diagnosegrenze; byteidentische
geschützte Dateien; Abbruch an jeder Austauschphase; beschädigte Backups/Journale,
unerlaubte native Manifeständerungen und wiederholt unterbrochener Rollback.
Datenträger-Vollzustand und physischer Stromausfall wurden nicht erzwungen.

### Aktueller Offline- und Buildstand

- **519 Core-Assertions bestanden**, davon **130 Updateprüfungen**.
- **34 WPF-Assertions bestanden**; echte Fenster-Renderings bei 920 × 760 und
  700 × 600 geprüft. UiBridge-Fenstertitel, IDs, Ready-Status und Startbutton
  bleiben unverändert. Bilder unter `artifacts/arena-launcher-updates/fixture-*.png`.
- Unabhängiger adversarial Review: zwischenzeitliche Zieländerungen, erneute
  Backup-Prüfung beim Kopieren, kompletter Recovery-Abschluss und native
  Quellversionsbindung im Vollkopie-Modus sowie gehaltene native Quellhandles
  während eines tatsächlichen Updates ergänzt und mit Negativtests abgesichert.
  Abschließender Code-/UI-/Dokureview ohne offene Befunde.
- Windows-x64-Publish erfolgreich. Nur neue Starter-EXE bereitgestellt; keine
  normalen Arena-DLLs gebaut/ersetzt und kein GPU-Spiel gestartet.

Lokal:
`artifacts/arena-launcher-updates/portable-final/maschine-ArenaLauncher.exe`.

SPT-GPU-2:
`C:\Temp\ArenaLauncher-Updates-20261020-r2\maschine-ArenaLauncher.exe`.
Für den bestehenden Diagnoseordner dort mit `--source C:\SPT41 --diagnostics`
öffnen. Das Diagnoseflag ist für normale Benutzer nicht nötig.

Größe **140527580 Bytes**, lokaler und entfernter SHA-256 identisch:
`97A4CDA9657216D8301610204B2B15CE9282F00CB749AECD601EA99BDA196A7D`.

### Native Paketprüfung

`D:\SPT41-Arena` wurde unter Laufzeitsperre geprüft: **178 Quellenbindungen,
8.240 gemeinsame Assets, 10.290 Katalogdateien und 10.306 Laufzeit-Dateipfade**.
Das vorhandene Modpaket war bereits aktuell: **0 neue / 0 geänderte / 0 entfernte
Dateien**. Die echten DLL-Metadaten und Kitdaten wurden akzeptiert; alle
**69 zugelassenen Quelldateien** und **vier vorhandenen geschützten privaten
Dateien** blieben byteidentisch. Das interne Profil blieb gültig.

Dies ist ein nativer **No-op-Nachweis**, kein künstlich erzwungener DLL-Tausch.
Add/Change/Delete und Recovery sind mit echten Dateisystemoperationen und
synthetischen verwalteten Testassemblies im Harness geprüft. Der lange native
Vergleich lief bereits, als die letzten Recovery-/Vollkopie-Randfälle samt
Tests ergänzt wurden; auch der abschließende native Quellen-Leseschutz betrifft
nur einen tatsächlichen Austausch. Diese Änderungen beeinflussen den nativen
No-op-Fall nicht.
Sein damaliger Gesamtlauf meldete 584 Assertions einschließlich der nativen
Vergleiche; maßgeblicher finaler Offline-Stand ist die oben genannte Suite mit 519.

Beleg: `artifacts/arena-launcher-updates/native-update.log`.

### Privater Serverstart

Der anschließende CLI-Smoke-Test gegen `D:\SPT41-Arena` bestand die vollständige
Integritätsprüfung erneut, akzeptierte das bereits aktuelle Modpaket und übernahm
die fünf nativen Spieleinstellungen. Der private SPT-Server 4.1.6 startete mit
ArenaMode 0.1.1; Ping und Profilabfrage lieferten HTTP 200, anschließend wurde
ausschließlich der gestartete Server wieder beendet. Ergebnis:
`ARENA_SERVER_SMOKE_PASS` und
`SERVER_SMOKE_RECEIPT_PRESERVED_AND_PROCESSES_STOPPED`.

Die Konto-Zuordnung blieb byteidentisch, die Menge der privaten Profildateien
unverändert. Danach lief kein EFT-, Launcher- oder SPT-Serverprozess. Dies ist
kein Vergleich sämtlicher Profilinhalte nach einem Serverstart. Im neuen
Serverabschnitt keine Arena-Ausnahme; die bereits vorher vorhandenen Hinweise
zu 20 statt empfohlenen 32 GiB RAM und fehlenden ASP.NET-Action-Deskriptoren
erschienen erneut.

Dieser Smoke-Lauf begann vor dem letzten, nur bei Dateiaustausch aktiven
Quellen-Leseschutz. Die endgültige EXE wurde danach neu veröffentlicht und
hashgleich auf GPU2 bereitgestellt; deren nativer Spielstart wurde nicht erneut
ausgeführt. Es fand kein EFT- oder In-Raid-Test statt.

Belege: `artifacts/arena-launcher-updates/native-server-smoke.log`,
private `.arena-start/server-start.log` (letzter Start, 20.10.2026 ca. 19:32).
