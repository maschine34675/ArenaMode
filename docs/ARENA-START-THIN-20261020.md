# Separater Arena-Start: platzsparende Umgebung

Stand: 2026-10-20. Ergänzt [Verwendung und Schutzgrenzen](ARENA-START-PROTOTYPE.md).
Dies ist ein Entwicklungsprototyp, kein veröffentlichtes Launcherpaket.

**Historischer Nachweis:** Die folgenden Builds, Dateizahlen und nativen Tests
beschreiben die erste Thin-Runtime ohne die später ergänzten optionalen
SAIN-/APBS-/ABPS-/BigBrain-/Waypoints-Pakete. Diese werden inzwischen bei
vollständiger lokaler Installation aus einem festen Katalog kopiert und
aktualisiert, ohne Quell-Modkonfigurationen, Presets, Caches oder Profile.
Vorhandener privater Modzustand bleibt bei Updates/Rebuilds erhalten; fehlende
Pakete werden nicht heruntergeladen oder mit dem Starter ausgeliefert.
Bedienung und Umfang stehen im [Mod-Updatebericht](ARENA-START-UPDATES-20261020.md).
Ein neuer nativer Nachweis für diese Erweiterung steht noch aus.

## Implementierung und Offlineprüfung

`play <SPT-Verzeichnis>` erstellt oder verwendet automatisch den danebenliegenden
Ordner `<SPT-Verzeichnis>-Arena`. Nur der ausdrücklich freigegebene native
Assetkatalog wird über NTFS-Hardlinks geteilt. Programme, Assemblies, SPT-Daten,
Einstellungen und Profile bleiben privat. Ein fremder oder unvollständiger
Zielordner wird weder übernommen noch gelöscht. Die normale Installation erhält
keine geänderten DLLs oder Konfigurationen.

Der Release-Build und `ArenaStartChecks` bestehen mit **187 Assertions**.
Das schließt tatsächliche Schreib-, Lösch- und Umbenennungsversuche über beide
Hardlinknamen ein, bereits offene Schreibhandles, einen unabhängigen Ersatz mit
identischen Bytes, Quellversionswechsel und eine gemeinsame Datei an einer
veränderlichen Konfigurationsstelle. Unveränderte Lesezugriffe und legitime native
Profilbackups bleiben erlaubt.

Eine echte Negativkontrolle fand während der Entwicklung einen Fehler:
Nur den Quellnamen offenzuhalten verhinderte nicht zuverlässig das Löschen des
Zielnamens. Die fertige Fassung hält deshalb **beide Handles** bis zum Schließen
aller nativen Clients. Der Puffer pro dauerhaftem FileStream wurde auf ein Byte
begrenzt; SHA-256 verwendet einen eigenen begrenzten Lesepuffer. In der langen
lokalen Prüfung wurden etwa 77 MB Working Set beobachtet, keine vollständige
Speicherbenchmarkreihe.

Unabhängige adversariale Prüfung erfolgte zusätzlich zur Testsuite. Sie umfasst
Kataloggrenzen, native `.spt-bak`-Wiederherstellung, doppelte Dateisperren,
Konfigurationsbesitz, normale BepInEx-Normalisierung und die Grenzen des
Quellvergleichs.

## Tatsächlich angelegte Umgebungen

| Umgebung | Manifestdateien | Gemeinsame Assets | Gemeinsame Bytes | Private Bytes |
|---|---:|---:|---:|---:|
| VM: `D:\SPT41-Arena` | 10.290 | 8.240 | 57.371.516.680 | 4.292.073.004 |
| GPU2: `C:\SPT41-Arena` | 10.290 | 8.239 | 57.371.504.296 | 4.292.813.040 |

Das sind Dateikatalogsummen nach der NLog-Korrektur, ohne später erzeugte Profile,
Caches und Logs. Sie sind keine Messung belegter NTFS-Cluster. Die kleine
Katalogdifferenz stammt aus den getrennt erfassten Quellen; GPU2 enthält zusätzlich
die ausdrücklich zugelassene Test-Bridge. Rund **4,00 GiB** werden privat kopiert,
rund **53,43 GiB** werden wiederverwendet. Beide Zielordner liegen außerhalb der
normalen Installationssynchronisation. Die historische Vollkopie
`D:\ArenaMode-Prototype` bleibt unverändert.

## Durch den nativen Start gefundene Korrekturen

1. Der erste Spielstart blieb am Splash hängen. EFTs
   `LogConfigurator.Reconfig` erwartet die NLog-Variablen `clientId` und
   `gameVersion` zwingend; sie fehlten in der zunächst erzeugten Minimaldatei.
   Die neue private Vorlage enthält beide und schreibt ausschließlich unter
   `Logs/`. XML-Vertrag und das Entfernen jeder einzelnen Variablen sind geprüft.
2. Mit korrigiertem NLog wurde die Charakterauswahl erreicht, aber die
   Pluginobjekte überlebten den Szenenwechsel nicht. Das unveränderte
   BepInEx-Default `HideManagerGameObject=false` reicht für SPT nicht aus.
   Die neue private Minimaldatei setzt den in der installierten SPT-Basis und
   deren Referenzpaket enthaltenen Wert `true`. Der Starter prüft diesen Wert
   abschnittsbezogen; fehlende, falsche oder doppelte Definitionen werden
   abgewiesen. Andere von BepInEx ergänzte Defaults dürfen sich normalisieren.

Die bereits angelegten Entwicklungsumgebungen erhielten nur diese gezielten
Korrekturen. Für NLog wurden vorher alle Clients geschlossen, ReparsePoints und
Mehrfachverknüpfungen ausgeschlossen und alter Datei-/Manifesthash exakt geprüft.
Der alte Manifeststand blieb als Beleg erhalten; nur der bekannte generierte
NLog-Eintrag wurde angepasst. Die BepInEx-Datei ist eine private veränderliche
Konfiguration und benötigt keine Manifeständerung. Das ist **keine automatische
Reparatur- oder Hashübernahmefunktion** im ausgelieferten Code.

Schon nach dem ersten fehlgeschlagenen Spielstart bestand die unveränderte
Integritätsprüfung aller 10.290 GPU-Manifestdateien einschließlich 8.239 gemeinsamer
Assets. Der native Launcher hatte keine unerwartete Patchabweichung erzeugt.
Die Testfehler wurden nicht durch Aufheben der Dateisperren umgangen.

## Nativer GPU-Raid

Nach beiden Bootstrapkorrekturen:

- UiBridge überlebt den Szenenwechsel. Charaktererstellung vollständig über
  die native UI, USEC, vorhandenes internes Konto; keine Profil-JSON-Erzeugung
  außerhalb des Servers. Hauptmenü und Kontobeleg nennen dieselbe private ID.
- EFT, Launcher und Server laufen nachweislich aus `C:\SPT41-Arena`.
  Geladene Arena-Module wurden gegen die Dateien dieses privaten Verzeichnisses
  geprüft. Der korrigierte Starter hat SHA-256
  `403440B29032CF08EA2194611F11E7ED8BE30039687F1DF846420FAD21451134`.
- Factory-Deathmatch mit drei Teilnehmern: gestellte HK416, zwei aktive Gegner.
  Ein echter Tod durch einen Bot löst den automatischen Respawn mit einer
  VPO-209 aus. Danach tötet ein tatsächlicher Kopfschuss einen Gegner; 250 Punkte,
  ein Kill, ein Tod. Der Gegner wird ersetzt, zwei aktive Bots bleiben erhalten.
  Erst nach dem nachgewiesenen echten Tod wurde für den begrenzten Schusstest
  der Diagnose-Unverwundbarkeitsmodus eingeschaltet.
- Die Pickup-Platzierung meldet bei diesem äußeren Factory-Start fehlende
  geeignete NavMesh-/Ankerpositionen und überspringt die Pickups der Runde.
  Das bleibt als Gameplay-Befund bestehen;
  es ist kein Nachweis eines Dateisperren- oder Ladefehlers. Die Starterarbeit
  ändert keine Arena-Spawn- oder Pickup-Regeln.

Verwendete bestehende Arena-Nutzlast, nicht neu gebaut/deployt:

| Modul | Informational version | SHA-256 |
|---|---|---|
| Client | `0.1.1+a6919d3` | `F25CE88281107E946032194D46980389F6617CEA864667D3806B7624C37A6210` |
| Server | `0.1.1+235cc69` | `8123969B19243AE5A61524A1BB6839703A0AD80281620B21AB8FE8043DC6BB14` |
| Redeploy.Core | `1.0.8+d1d7240` | `218967C32C58F83C9BA2044888F12F595655D752172049DE57FA9C51FD4B231C` |

Das ist eine begrenzte Factory-Prüfung. Andere Modi, Karten, längere Matches,
optionale Modpakete und GPU1 wurden in diesem Schritt nicht getestet.

Der reguläre Ausstieg über Esc → Disconnect und die nativen Ergebnisse führte
zurück ins Hauptmenü. Nach vollständigem Schließen von Spiel, Launcher und
privatem Server startete die Umgebung erneut. Der zweite native Client erreichte
nach 37 Sekunden das Hauptmenü, ohne erneute Charakteranlage, mit genau demselben
internen Konto. Das ist die Zeit ab dem nativen Startknopf, **nicht** einschließlich
der vorherigen vollständigen Dateiprüfung. Abschließend: Verify mit Exitcode 0,
10.290 Dateien und 8.239 gemeinsamen Asset-Leases, unveränderte private
Client-/Server-/Core-Hashes. Alle Testprozesse und der Bridge-Listener sind beendet.

Auch lokal bestanden zwei Server-Smokes. Der abschließende Lauf mit der
getesteten Starterfassung prüfte 10.290 Dateien / 8.240 Asset-Leases, verwendete
das bereits angelegte interne Konto und beendete seinen eigenen Server.
Die angehängten Originaldateivergleiche bestanden dagegen nicht; deren
Exitcode darf nicht mit dem erfolgreichen Server-Smoke verwechselt werden.

### Vollständige Logauswertung

Es gab keine gefundenen Sharing-Verletzungen, AssetBundle-Öffnungsfehler oder
nativen Exceptions in den erfolgreichen Läufen. Die Logs sind trotzdem nicht
vollständig fehlerfrei:

- Sieben `SpawnPointMarker`-Korrekturen und `[Transit] Count:0` erscheinen
  absichtlich auf Error-Stufe. Derselbe Text mit denselben sieben Spawn-IDs steht
  in einer normalen Factory-Sitzung um 14:42:38, während der private Lauf diese
  Meldungen um 14:34:52 erzeugte. Die dekompilierten Implementierungen
  `SpawnPointMarker.FixParams` und `LocalTransitController` bestätigen das
  Loggingverhalten. Das sind keine spezifischen neuen Starterfehler.
- Beim regulären Beenden nach dem Raid erscheinen dreimal
  `PlayerBody destroyed without being disposed`. `PlayerBody.OnDestroy` prüft
  dabei einen tatsächlich noch aktiven Körper. Ein passender normaler
  Vergleichstreffer liegt nicht vor; die Ursache ist **offen**, nicht erwiesen
  harmlos oder bereits vorhanden. Der reine zweite Menüstart enthält diese
  Fehler nicht.
- Die optionale BigBrain-Abwesenheitswarnung ist erwartbar; außerdem erscheinen
  native SPT-Brain-Korrekturen und die beschriebene Pickup-Warnung.

Die vollständigen gespeicherten EFT-Traces haben Vorrang vor dem zunächst
unauffälligen Live-Merge. Die PlayerBody-Bereinigung und die Factory-Pickup-
Platzierung bleiben separate Nacharbeiten am Spielverhalten; in diesem Schritt
wurde dafür keine ungeprüfte Laufzeitänderung hinzugefügt.

## Vergleich mit der normalen Installation

`tools/ArenaStartChecks/Measure-Source.ps1` erfasst Dateimenge und SHA-256 von
normalen Profilen/Backups, Launcher-Einstellungen, BepInEx-Configs/Plugins/Patchern,
Servermods, SPT-Basiskonfigurationen, SPT-Settings-INIs, NLog, Doorstop und
Serverlogger. Es verweigert das Überschreiben eines vorhandenen Ausgangsbelegs.
Ein abweichender Vergleich bleibt ein fehlgeschlagener Vergleich.

Während früher GPU-Startversuche änderte sich genau ein normales Profil von
**SPT-GPU-1**. GPU1 lief unabhängig weiter und wurde nicht bedient oder beendet.
Der erste beobachtete Schreibzeitpunkt lag nach dem vollständigen Ende des
privaten Testlaufs; derselbe Stand erschien danach in der VM-Synchronisation.
Das interne Arena-Konto besitzt eine andere ID und liegt ausschließlich im
privaten Runtime-Verzeichnis. Das spricht für einen externen Profil-Save.
Es wäre trotzdem falsch, diese ganzen Zeitfenster als vollständig unverändert
zu melden. Im erweiterten GPU-Vergleich waren die übrigen 1.368 von 1.369
erfassten Dateien unverändert. Keine Originaldatei wurde zurückgesetzt.

Der abschließende vollständige GPU-Vergleich gegen einen frischen Beleg vor dem
zweiten Start scheiterte ebenfalls: **40 hinzugefügt / 40 entfernt** durch
Profil-Backup-Rotation und **7 geändert**. Betroffen waren zwei normale Profile,
ArenaMode-/DynamicMaps-Konfiguration, Arena-Clientsettings und zwei
APBS-Generierungslogs. Mehrere Zeitstempel liegen vor dem zweiten privaten
Serverstart; APBS, DynamicMaps und die normalen Profile existieren nicht in der
privaten Umgebung. Die beobachteten Quellstände stimmten mit der normalen
VM-Synchronisation überein. Der lokale Abschlussvergleich erfasste dasselbe
veränderte Siebener-Set sowie **60 hinzugefügte / 60 entfernte** Backups.
Das unterstützt die Erklärung paralleler Synchronisationsaktivität, ersetzt
aber keinen vollständigen Erhaltungsnachweis.

Ein zusätzlich versuchter später lokaler Beleg wurde noch während der
schreibgeschützten Dateiprüfung begonnen; der Server startete bereits während
seiner Erfassung. Die Kontrollabfrage wies ihn deshalb ausdrücklich als
Vorher-Beleg zurück. Auch daraus wird kein erfolgreicher Vergleich abgeleitet.

**Offener Abnahmenachweis:** Den vollständigen Originaldateivergleich bei
ruhender gemeinsamer Installation/Synchronisation wiederholen. GPU1 wurde
nicht dafür beendet; die Freigabe galt nur GPU2. Alle fehlgeschlagenen Belege
bleiben erhalten. Es wird weder „Original vollständig unverändert“ noch eine
vollständige Live-Abnahme der Isolation behauptet. Die Quellvergleichsklauseln
des Prüfplans wurden dafür sichtbar in einen eigenen zurückgestellten
Abnahmepunkt zusammengeführt; die funktionierenden lokalen und GPU-Läufe
bleiben davon getrennte Ergebnisse.

Rohbelege, Screenshots und vollständige Logs bleiben ignoriert unter
`artifacts/arena-thin/` sowie in den privaten Runtime-/Testverzeichnissen.
Sie gehören nicht in ein Release.
