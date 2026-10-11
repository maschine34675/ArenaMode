# Arena Refinements und Air Control r4 – Vorprüfung blockiert

**BLOCKIERT / keine Feature-Abnahme.** Die [r4-Anweisung](GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r4.md) verlangt zunächst einen sicheren r6-Fall A am genau gebundenen Client. Der [r6-Nachtest](GPU-DEATHMATCH-MENU-TEST-RESULTS-r6.md) wurde bereits vor dem Spielstart wegen abweichender Client-/Redeploy-Hashes blockiert; beim Abschlussread weicht zusätzlich die Server-DLL ab. Damit ist die Voraussetzung dieses separaten Auftrags nicht erfüllt. Kein Featurefall oder Raid gestartet.

## Testbindung

Test-ID `arena-refinements-r4`, Installation `C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`. AGENTS, aktueller HANDOFF und beide neuen Testanweisungen gelesen. Vorgeschrieben: Branch `feature/arena-air-control`, Source-/Build-Commit `0b7f38d594b09644803f263a510543293b11ed63`, Client ProductVersion `0.1.0+0b7f38d594b09644803f263a510543293b11ed63`.

Die gemeinsame ausschließlich lesende Vorprüfung um **18:32:07.393 UTC / 20:32:07 MESZ am 12.09.2026** ist im [eigenen Beweisordner](../test-evidence/arena-refinements-r4/2026-09-12T18-33-24-686Z/preflight.json) abgelegt. Client Soll `2042C558340877C85DAF0687A8AEBB4E7D54FD720AD467E770F5AEE7F0914ECD`, beobachtet `50DAECC65F0EBE4FC43159063D83E7DEF829AF9ED83F74DB1FD47EA7463831AA`, ProductVersion `0.1.0+bb82ce8c81960f501fcf1b8312e9b267fad0480a`: **FAIL der Dateibindung**.

Redeploy Soll `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`, beobachtet `520BF61A0F6B52ACB8209B060B7739E88C3237712EABFBDE0DDB74E0E10ABF48`, ProductVersion `0.9.0+c938f5c43cb311a8a9ba1f81b2eb10573b99b936`: **FAIL der Dateibindung**.

Server `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`, SAIN 4.5.1.0 `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` und DynamicMaps 1.2.1.0 `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` stimmen mit der Vorgabe überein. Das belegt nur die Dateien, keine Laufzeitkompatibilität.

Die [Beobachtungsfolge](../test-evidence/arena-refinements-r4/2026-09-12T18-33-24-686Z/preflight-context.json) enthält die zwei zuvor unabhängig gelesenen anderen Clienthashes und wechselnden Git-HEADs. Der letzte für die Vorprüfung festgehaltene HEAD ist `3a5123c70980a8074cf3c18d6c57dbac5385cc97`; er ersetzt den vorgeschriebenen Build nicht. Der vorhandene ursprüngliche Lieferbeleg bestätigt D:, ausdrücklich nicht die aktuelle GPU-Spiegelung. Beide Anweisungen binden bei erneutem Lesen um 18:33:24 UTC weiterhin den ursprünglichen Stand.

Beim [Abschlussread um 18:37:28.417 UTC](../test-evidence/arena-refinements-r4/2026-09-12T18-33-24-686Z/final-binding-check.json) sind Client `546EB7656D702C9B63A96C3408F34E871E5445108B9D9F69804C337DE4D0999C`, Server `04533957CB2618F2AEE3241CEB1157BC0CCD81FF5278C7DC39CC72E05434799A` und Redeploy `520BF61A0F6B52ACB8209B060B7739E88C3237712EABFBDE0DDB74E0E10ABF48` installiert: **alle drei weichen von der Anweisung ab**. Der frühere Serververgleich oben ist ausschließlich eine Beobachtung von 18:32:07 UTC. Das inzwischen fremd aktualisierte Handoff dokumentiert diesen neuen Build `3a5123c…` und verlangt ausdrücklich, r6/r4 zuerst darauf neu zu binden. Beide Anweisungsdateien bleiben beim Abschlussread bytegleich mit der alten Bindung. Sie und das fremd geänderte HANDOFF wurden durch diesen Testauftrag nicht umgeschrieben.

## Unterfälle

| Fall | Status | Fehlender Nachweis |
|---|---|---|
| Sicheres r6 A am gebundenen Client ohne späteren Gesamtstopp | OFFEN / blockiert | r6 erreichte keinen Spielstart |
| Startupmarker für native Air-Control-Steuerung | OFFEN | Kein neuer Clientprozess |
| Air Control: gleichartige Sprünge off/on, 0/60 und optional 120 Grad/s | OFFEN | Keine Bewegungseingaben oder Vergleichsclips |
| Feste Blickrichtung, WASD-Kurve, Loslassen und erhaltene Flugrichtung | OFFEN | Nicht geprüft |
| Fallkante, Double Jump, Scheitel/Abstieg, Landung und natürlicher Respawn | OFFEN | Nicht geprüft |
| Vertikale Flugbahn, Geschwindigkeit, Stillstand ohne Seitenimpuls | OFFEN | Nicht geprüft; keine Weltgeschwindigkeitsbehauptung |
| Wand/Decke/Kontur-Kollision, konservatives Mantling | OFFEN | Nicht geprüft |
| Inventar/Pause verweigern neue Steuerung, Rückkehr und Loslassen ohne Lock | OFFEN | Nicht geprüft |
| Große Frame-Schritte, gewöhnliche PMC-Bewegung, Preview/Hideout | OFFEN | Nicht geprüft |
| DynamicMaps: Startup-/Versionskompatibilität zur Laufzeit | OFFEN | DLL-Version passend, aber kein Startupnachweis |
| Corpse-Trim mit zugeordnetem Full-Map-/Minimap-Marker | OFFEN | Kein Trim, keine Karte oder Minimap geprüft |
| Wiederöffnen/Corpse-Toggle sowie jüngste, eigene und geschützte Körper | OFFEN | Nicht geprüft |
| Air Jump nach kleiner/klarer Fallkante; stehendes/gehendes/sprintendes Fallen | OFFEN | Nicht geprüft |
| Boden-/Extrasprung, einmaliger Impuls, Landungsfreigabe und Luft-/Bodentoggle | OFFEN | Nicht geprüft |
| Stale-Request nach Inventar/Respawn, native Kollision und sichere Verweigerung | OFFEN | Nicht geprüft |
| Stamina-Debit und Fallfolgen mit geeignetem temporärem Vergleich | OFFEN | Keine Testwerte gesetzt; aktueller Zustand nicht als Kostenbeleg verwendet |
| Konturfade an langer/diagonaler Seite/Ecke, Annähern und Entfernen | OFFEN | Nicht geprüft |
| Endliche Wandsegmente, Etagenvergleich, Spieler-/Bot-Kollision, Teamspawns | OFFEN | Nicht geprüft |
| Friendly Diamond am selben Teammate mit DLSS Off/Quality/weiterer Qualität | OFFEN | DLSS und sichtbarer Körper in diesem Lauf nicht geprüft |
| Randposition/Haltungen, Respawn/Kamera, Tactical-Spectator und Negativfälle | OFFEN | Nicht geprüft |
| Geschützter Arena-Ausstieg, Logdelta und vollständiger PMC-/Scav-Vergleich | OFFEN | Kein Arena-Raid gestartet |
| Gewöhnlicher PMC-Raid ohne Arena-Funktionen, normale DynamicMaps-Marker | OFFEN | Nicht gestartet |

**0 Raids, 0 Runden, 0 Minuten aktive Testzeit.** Das separate 3-Raid-/20-Minuten-Budget wurde nicht begonnen. Werkzeug-/Dokumentationszeit ist keine aktive Gameplay-Zeit. Kein eigener Spiel-/Server-/Launcherprozess; die gemeinsame Prozessprüfung findet alle drei abwesend. Aktuelle native Auflösung, DLSS-Modus, Startupmarker, Fehler-/Warnungszahlen und Guard-Aufrufe sind ungemessen. Fehlende Laufzeitdaten werden weder als null Fehler noch als PASS ausgegeben.

Keine Profile gelesen, kopiert, verändert oder zurückgespielt; kein gewöhnlicher PMC-Raid mit legitimen Profileffekten. Keine eigenen Einstellungs-, Asset-, Audio-, Mod- oder Produktionsänderungen, kein Build und kein DLL-Tausch. Deshalb keine eigenen Testwerte zurückzustellen. Während der Vorprüfung parallel beobachtete fremde Änderungen werden nicht diesem Auftrag zugerechnet und nicht zurückgesetzt.

Fortsetzung erst mit stabiler, zur verbindlichen Anweisung passender Installation beziehungsweise aktualisierter Bindung von Client, Server und Redeploy; anschließend muss zunächst r6 A sicher abgeschlossen werden. Historische r5-Erfolge heben diese Voraussetzung nicht auf.

## Belege

[Preflight](../test-evidence/arena-refinements-r4/2026-09-12T18-33-24-686Z/preflight.json), [Kontext/Lieferbeleg](../test-evidence/arena-refinements-r4/2026-09-12T18-33-24-686Z/preflight-context.json), [SHA256-Manifest](../test-evidence/arena-refinements-r4/2026-09-12T18-33-24-686Z/SHA256SUMS.txt). Keine Originalbilder oder Clips, da kein Spiel gestartet wurde; vollständige historische Logs und Profile werden nicht veröffentlicht. Frühere Berichte und Belege unverändert.
