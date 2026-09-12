# Deathmatch r6 – vor dem Spielstart blockiert

**BLOCKIERT / keine Gameplay-Abnahme.** Am 12.09.2026 stimmen installierter Client und Redeploy.Core nicht mit dem verbindlichen Stand aus [r6](GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r6.md) überein; beim Abschlussread weicht zusätzlich die Server-DLL ab. Client und Server änderten sich während der ausschließlich lesenden Vorprüfung. Kein Spiel-/Server-/Launcherstart, kein Raid und kein Gameplay-Input dieses Auftrags. Alle nativen Unterfälle bleiben OFFEN.

## Bindung und Befund

Test-ID `deathmatch-menu-exit-r6`; Installation `C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`. AGENTS und aktueller HANDOFF gelesen. Vorgeschrieben sind Source-/Build-Commit `0b7f38d594b09644803f263a510543293b11ed63`, Branch `feature/arena-air-control` und Client ProductVersion `0.1.0+0b7f38d594b09644803f263a510543293b11ed63`.

Der [Root-Preflight](test-evidence/deathmatch-menu-exit-r6/2026-09-12T18-33-24-686Z/preflight.json) um **18:32:07.393 UTC / 20:32:07 MESZ** ergibt:

| Komponente | Soll-SHA256 | Beobachtete SHA256 | Bindung |
|---|---|---|---|
| ArenaMode.Client | `2042C558340877C85DAF0687A8AEBB4E7D54FD720AD467E770F5AEE7F0914ECD` | `50DAECC65F0EBE4FC43159063D83E7DEF829AF9ED83F74DB1FD47EA7463831AA` | FAIL |
| ArenaMode.Server | `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7` | `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7` | PASS, nur Dateibindung |
| Redeploy.Core | `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8` | `520BF61A0F6B52ACB8209B060B7739E88C3237712EABFBDE0DDB74E0E10ABF48` | FAIL |
| SAIN 4.5.1.0 | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` | PASS, nur Dateibindung |
| DynamicMaps 1.2.1.0 | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` | PASS, nur Dateibindung |

Beim Root-Read hat der Client ProductVersion `0.1.0+bb82ce8c81960f501fcf1b8312e9b267fad0480a`, LastWriteTime **18:31:48.3099819 UTC**. Redeploy hat ProductVersion `0.9.0+c938f5c43cb311a8a9ba1f81b2eb10573b99b936`, LastWriteTime **18:27:14.9352113 UTC**. Dies sind zeitlich gebundene Beobachtungen, keine Behauptung einer anschließend stabilen Installation.

Eine unabhängige Vorprüfung las zuvor Client `B40FD17FFC89229D7273A26D7194FD0C0FBF19A8FC850E99AA3419C1DF84629E` und um **18:31:42.168 UTC** `52A45F8E001F33AF2880B42EABE01CB0E56EA9CA1251AD1488E29BD6C7B3A5E4`. Alle drei beobachteten Clienthashes weichen vom Soll ab. [Abgegrenzte Beobachtungsfolge und Lieferbeleg](test-evidence/deathmatch-menu-exit-r6/2026-09-12T18-33-24-686Z/preflight-context.json).

Git stand anfangs auf `67d27cad87e9004c22e33df8e97a551b648d0fe0`, später auf `3a5123c70980a8074cf3c18d6c57dbac5385cc97`, jeweils Branch `feature/arena-air-control`. Die anfangs fremd geänderte `LiveKitRespawn.cs` wurde von diesem Auftrag nicht bearbeitet; ihr Status änderte sich ebenfalls während der Vorprüfung. Die fremde Syncthing-Datei bleibt unangetastet. Ursache und Urheber der parallel beobachteten Änderungen wurden nicht ermittelt.

Das referenzierte Build-Manifest und der Lieferbeleg sind vorhanden; die separate gestagte Client-DLL entspricht tatsächlich `2042C558…`. Der Lieferbeleg von **17:05:19.2001837 UTC** bestätigt jedoch nur die D:-Lieferung, mit `GpuMirrorVerified=false` und `LiveGpuTested=false`. Er belegt nicht den aktuellen C:-Stand. Kein DLL-Tausch oder Rückbau wurde vorgenommen. Beide Anweisungen wurden um **18:33:24 UTC** nochmals gelesen und binden weiterhin `0b7f38d…`; ihre SHA256 stehen im Preflight.

### Abschließende Bindungsprüfung und aktualisiertes Handoff

Der [Abschlussread um 18:37:28.417 UTC](test-evidence/deathmatch-menu-exit-r6/2026-09-12T18-33-24-686Z/final-binding-check.json) findet Client **`546EB7656D702C9B63A96C3408F34E871E5445108B9D9F69804C337DE4D0999C`**, Server **`04533957CB2618F2AEE3241CEB1157BC0CCD81FF5278C7DC39CC72E05434799A`** und weiterhin Redeploy **`520BF61A0F6B52ACB8209B060B7739E88C3237712EABFBDE0DDB74E0E10ABF48`**. Damit weichen nun **drei** gebundene Dateien ab. Die vorherige Server-PASS-Zeile gilt ausschließlich für den ausdrücklich datierten früheren Read. SAIN und DynamicMaps sind weiterhin hashgleich.

Das parallel aktualisierte Handoff beschreibt inzwischen Redeploy.Core stage 0 und Arena-Build `3a5123c70980a8074cf3c18d6c57dbac5385cc97` mit genau diesen drei neuen Hashes. Es fordert ausdrücklich die Neubindung der noch auf `2042C558…` bezogenen r6/r4-Anweisungen, bevor die Belege vergleichbar sind. Das ist ein dokumentierter neuer Entwicklungsstand; dessen Gameplay-Sicherheit wurde hier nicht geprüft. **Beide vom Nutzer genannten Anweisungsdateien sind beim Abschlussread bytegleich mit dem zuvor gelesenen alten Stand.** Sie wurden durch diesen Testauftrag nicht umgeschrieben. Das fremd geänderte HANDOFF wird ebenfalls nicht mitcommittet.

## Testfälle und Grenzen

| Unterfall | Status | Grund |
|---|---|---|
| Verbindlicher Client-/Server-/Redeploy-Stand | FAIL / Start blockiert | Anfangs zwei, abschließend drei Hashabweichungen; Client und Server wechselten während der Vorprüfung |
| Neue Startupmarker für native Menü-Rückkehr und Bot-Waffen-Callback | OFFEN | Kein neuer Prozess gestartet; alte Logs zählen nicht als r6-Nachweis |
| A: Customs morgens, unabhängiges TDM 5v5, Teamkontur; AirControl vorübergehend aus | OFFEN | Kein Raid und keine Testeinstellung vorgenommen |
| A: isolierter Resume-Vergleich mit Waffen-, Magazin- und Kammerbeleg | OFFEN | Kein Klickvergleich |
| A: einzeln belegte Settings-/Graphics-/DLSS-/Save-/Resume-Folge | OFFEN | Keine native Menüfolge; DLSS-Verfügbarkeit aktuell nicht geprüft |
| A: Bewegung, absichtlicher Schuss, Reload, Pause/Escape/Tab und F9-Smoke | OFFEN | Keine Gameplay-Eingaben |
| A: natürliche Bot-Ersatzaktivierungen, eigene Respawns, Teamvollständigkeit | OFFEN | Kein Raid |
| A: native Input-, retired-selector-, Pickup- und SAIN-Guard-Zählung | OFFEN | Keine neue Laufzeitbeobachtung; insbesondere nicht als null Aufrufe bestanden |
| A: regulärer Ausstieg, Logdelta bis Hauptmenü und vollständiger Profilvergleich | OFFEN | Kein Arena-Raid begonnen |
| B: FFA-Ergebnis, Banner/F9, Countdown, zeitlich getrennter Score-Freeze | OFFEN | A-Voraussetzung nicht erfüllt |
| B: rechtzeitiger Return-to-menu-Button gegenüber Autoexit | OFFEN | A-Voraussetzung nicht erfüllt |
| C: Continuous/Endless, Give up, Respawn und Ausstieg | OFFEN | A-Voraussetzung nicht erfüllt |
| Gewöhnlicher PMC-Menüweg, Hideout, Weapon Builder, Tactical-Kaufmenü | OFFEN | Nicht geprüft |

**0 Raids, 0 Runden, 0 Minuten aktive Gameplay-Testzeit.** Die 3-Raid-/20-Minuten-Grenze wurde nicht gestartet. Zeit dieser Vorprüfung ist Werkzeug-/Dokumentationszeit. Prozessprüfung um 18:32:07 UTC findet keinen EscapeFromTarkov-, SPT.Server- oder SPT.Launcher-Prozess; PID/Startzeit, aktuelle native Auflösung und DLSS-Modus bleiben deshalb ungemessen. Keine neuen Client-/Server-Logfenster, Fehler-/Warnungszahlen oder vollen Stacks dieses Laufs vorhanden; alte Fehler werden nicht erneut als r6-Befund gezählt.

Keine Profile gelesen, kopiert, bearbeitet oder zurückgespielt. Keine Einstellungen oder Assets durch diesen Auftrag geändert; damit keine eigenen Testwerte zurückzustellen. Während paralleler Dateiänderungen wird keine globale Unverändertheit fremder Dateien behauptet. Keine Builds, Produktionsänderungen, Laufzeitinstrumentierung, internen Arm-/Matchaufrufe oder zusätzlichen Softwareinstallationen.

Die Stopregel nennt ausdrücklich jeden Hashwechsel; die Anweisung untersagt Builds und DLL-Tausche. Der vorhandene gestagte Altstand wurde deshalb nicht eigenmächtig eingesetzt. Fortsetzung benötigt eine stabile Installation, die zu den verbindlichen Anweisungen passt, oder aktualisierte Anweisungen für den tatsächlich gelieferten Client, Server und Redeploy. Ein späterer korrigierter Nachtest muss diesen Vorprüfungsbefund erhalten.

## Belege

[Preflight](test-evidence/deathmatch-menu-exit-r6/2026-09-12T18-33-24-686Z/preflight.json), [Kontext](test-evidence/deathmatch-menu-exit-r6/2026-09-12T18-33-24-686Z/preflight-context.json), [SHA256-Manifest](test-evidence/deathmatch-menu-exit-r6/2026-09-12T18-33-24-686Z/SHA256SUMS.txt). Keine Bilder/Clips, weil kein Spiel gestartet wurde. Private Anweisungssicherungen liegen ausschließlich unter ignoriertem `artifacts/gpu-preflight/`. Der [separate Refinements-r4-Bericht](GPU-ARENA-REFINEMENTS-TEST-RESULTS-r4.md) bleibt ebenfalls blockiert. Historische Berichte unverändert.
