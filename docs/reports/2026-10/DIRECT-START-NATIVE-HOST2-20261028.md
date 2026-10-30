# Direkter Arena-Start: Host 2, 28.10.2026

Host: `SPT-GPU-2` / `192.168.178.109`, Benutzer `Service`, Session 1. Originalinstallation `C:\SPT41`. Rohbelege liegen ausschließlich im ignorierten Verzeichnis `artifacts/direct-start-native-20261028/host2/` und enthalten vollständige Testprofile; nicht veröffentlichen.

## Umfang und Kandidaten

- Helper der Baseline-Tests: SHA-256 `2D8C7321367A4464A2DF08833A8080F53037EAFADE6214E2738E6B7B12EA9C09`.
- Wiederholte Starts und isolierte Crash-Tests: Server `19FDE5869D95CEDFD300A090748613C05F3B61CC53873B7E50801680E679052A`.
- Reproduzierter Rechtefehler: Server `CA15D61426CF11D3FB17F33AB12DCC8CFAB05523E94ED9FC0A4524354BA56918`.
- Nachprüfung des kombinierten Fixes: Server `2262F02036AA20F93FC87CF9222BE1C27A6007E4E7C0BF5C9B7ED59AE185363B`.
- Helper nach dem zweiten Rechtefix: `D2CF68D3E54957A5E827426CFFB1B15B96B927B1991C32C191409A6601A2A5B5`.

UiBridge dokumentiert in den gespeicherten Statusdateien die geladenen Client-MVIDs: 16/16 Plugins und beide Bibliotheken entsprechen Hostplatte und lokalem Build. Serverbelege verbinden Hash, konkreten Prozessstart, Installationspfad und beobachtetes Verhalten; sie sind kein separat ausgelesener Server-MVID.

## Native Abläufe

| Fall | Beobachtung | Belege |
|---|---|---|
| Normaler EFT-Start, reguläres Beenden, danach warmer Direktstart | Normalserver 16560 lief beim Start von Helper 14164 bereits 305,5 Sekunden. Arena-Hauptmenü und Rückkehr zum normalen Hauptmenü erreicht. Beide Prozesse High, gleicher Benutzer/Session. | `normal-start-loaded.json`, `warm-before.json`, `warm-A1/` |
| Profil A → B → A | A = SPT-GPU-2, B = SPT-GPU-1; jeweils tatsächlich separat gestartetes Arena- und normales Hauptmenü. A1 nur zwei native Zeitstempeldifferenzen, B und A2 zunächst komplett identisch. | `warm-A1/`, `B/`, `A2/`, `helper-after-ABA.log` |
| EFT läuft bereits | Zweiter Direktstart verweigert; bestehender normaler EFT-Prozess blieb erhalten. | `helper-after-duplicate.log`: 16:13:41.131Z |
| Zwei Helper | Zweiter Helper beendet sich nach Mutex-Prüfung; erster Profilpicker bleibt bedienbar. | `duplicate-after.json`, `helper-after-duplicate.log`: 16:20:40.190Z |
| EXIT, dann Direktstart | Application.Quit entspricht dem nativen EXIT-Pfad. Watchdog beendet privaten Server; neuer Direktstart erreicht wieder Arena-Hauptmenü. Vollständiges Originalprofil nach Wiederstart identisch. | `fixture-exit/` |
| Kontrollierter EFT-Absturz in Factory-DM | Tatsächlich laufende Runde: BattleUI, Timer gestartet, Arena aktiv, RD-704 in der Hand. Nur exakte isolierte EFT-PID 17340 beendet; keine Kill-/Reward-Hooks. | `fixture-crash/inraid-loaded.json`, `inraid-player.json`, `injection.json` |
| Gleichzeitiger Neustart und alter Watchdog | EFT endet 16:44:41.093Z; neuer Helper beginnt 41.167Z; alter Watchdog meldet Serverende erst 41.197Z. Anschließend genau ein neuer privater Server und EFT, danach normale Rückkehr erfolgreich. | `fixture-crash/helper-final.log`, `processes-after-restart.json`, `restarted.json`, `returned.json` |

Der erste EXIT-Neustart begann erst nach beendetem Watchdog. Die tatsächliche Überlappung wurde beim Crash-Neustart geprüft. Kein normaler Server wurde hart beendet.

## Profile und Isolation

Die Fehlerfälle liefen in einer unabhängigen Bytekopie `C:\ArenaMode-DirectNative-20261028`; keine Hardlinks für beschreibbare Daten. Vor Erststart wurden sämtliche kopierten `SPT_Runtime\user`-Verzeichnisse außer `mods` entfernt. Dadurch kamen auch alte Checkpoints, Snapshots, Credentials und Profile nicht in die Testfixture. UiBridge-State und Captures besitzen eigene Pfade. Reparse-Attribute der Kontrollpfade sind in `fixture-paths.json` festgehalten.

Das Profil `ArenaNative2810` wurde ausschließlich über die nativen SPT-4.1.6-Endpunkte registriert und initialisiert: `wipe=false`, Level 1, Create `err=0`. Zwei fehlerhafte Probeaufrufe ohne wirksam übertragenen Cookie ließen die Figur uninitialisiert; der erfolgreiche Aufruf verwendete eine echte CookieCollection. Keine Profilbindung wurde entfernt oder repariert.

Zwei Installationen desselben Windows-Benutzers teilen den Helper-Zustand. Der erste Fixture-Start wurde deshalb wegen der abgeschlossenen privaten Originalsession mit `OtherInstallation` abgewiesen. Für die Fixture wurde die nachweislich abgeschlossene Original-Session als ganzes Verzeichnis archiviert und später exakt zurückverschoben. Originalpointer war abwesend, `closed.json` und `completion.json` vorhanden, keine Original-/Privatprozesse aktiv. Das ist Testisolation, **kein akzeptierter Alltagsablauf für den Wechsel zwischen zwei Installationen**. Siehe `original-closure-before-fixture.json`, `session-isolate-result.json`, `session-restore-result.json`.

Nach Crash-Recovery und nach Erreichen des normalen Hauptmenüs war das vollständige Originalprofil exakt gleich dem eingefrorenen Vorherstand. Der erste anschließende normale Client-Save initialisierte 67 native Felder: 46 explizite Standardmengen `1`, 13 Mod-Metadaten, drei wiederholbare Questlisten, Dialog/Geschenk/Userbuilds sowie zwei Zeitstempel. Alle 205 Inventar-IDs, Templates, Plätze und Mengen blieben gleich, XP blieb 0. Die vollständigen unmaskierten Vergleiche bleiben in `fixture-crash/`; `final-profile-analysis.json` erläutert sie ohne Profile zu verändern.

Ein nativer Profilsnapshot wurde vor dem Kit im privaten Serverprotokoll bestätigt. Auf diesem alten Serverkandidaten entstanden keine dauerhaften Reward-Checkpointdateien in der Fixture. Der Test belegt deshalb den aktiven Kit-/Spielabsturz und die sichere Rückkehr, keine erfolgreiche Reward-Auszahlung und keine künstlich erzeugte Checkpoint-Reparatur. Alle drei Fixture-Offlinesessions besitzen `closed.json` und `completion.json`; finaler aktiver Pointer fehlt.

## Gemischte Rechte: reproduzierter Fehler

Windows-Ausführen startete den Normalserver ohne Erhöhung; der Helper wurde separat erhöht gestartet. Gemessen am 16:52:19Z:

| Prozess | PID | Integrity | Token | Benutzer/Session |
|---|---:|---|---|---|
| Normalserver | 14704 | Medium `S-1-16-8192` | Limited (3), elevated=0 | Service / 1 |
| Helper | 6728 | High `S-1-16-12288` | Full (2), elevated=1 | Service / 1 |

Um 16:52:44.255Z meldete der Helper `ProtocolError`. Das Server-Debugprotokoll nennt unmittelbar davor `helper-identity-mismatch` und `entry-not-authorized`. Der Server blieb erhalten, kein Profil wurde kopiert. Retry/Cancel wurde regulär abgebrochen, anschließend der Normalserver per Ctrl+C beendet. Belege: `final-mixed/`.

Ein lesender Probeprozess im Medium-Kontext bestätigt den Unterschied: `MainModule.FileName` und `HasExited` werfen in der verwendeten .NET-Framework-Getterprobe jeweils Win32-Fehler 5; `StartTime` funktioniert. Die begrenzten Windows-Abfragen liefern dagegen denselben Pfad und Startzeitpunkt, Session 1 und `WaitForSingleObject(0)=258` (läuft). Der echte Server verwendet .NET 9; die Probe belegt daher die Windows-Zugriffssituation, nicht welcher verwaltete Getter im Server zuerst fehlschlug. Keine Betriebssystem-Sicherheitsoption wurde geändert.

Die früheren, nicht ausreichend protokollierten Fehler werden dadurch nicht rückwirkend als identisch bewiesen. Die neue Konstellation ist jedoch reproduziert. `normal-start-processes.json` stammt von einer anfänglich fehlerhaften TokenElevation-Pufferabfrage und enthält `-24`: das ist **unavailable / Win32-Fehler 24**, kein Boolean. Spätere Messungen verwenden den korrekten Vier-Byte-Puffer.

Mit Server `2262…` wurde dieselbe Richtung erneut gemessen: Medium-Normalserver 2244, High-Helper 3028. Eintritt und normale Rückkehr gelangen, kompletter Profilvergleich unverändert (`fixed-mixed/`).

Die Gegenrichtung reproduzierte zunächst einen zweiten Fehler: High-Normalserver 3704, Medium-Helper 16372, `direct start failed: Zugriff verweigert` vor dem HTTP-Handoff (`reverse-mixed/`). Nach ausschließlich getauschtem Helper `D2CF…` gelang derselbe Ablauf gegen **denselben weiterhin laufenden High-Server 3704** mit Medium-Helper 9544. Eintritt und normale Rückkehr erreichten das Hauptmenü; kompletter Profilvergleich unverändert (`reverse-fixed/`). Seit dem Schließen des normalen EFT-Websockets um 17:03:17Z war dieser Server vor dem neuen Helperstart um 17:10:39Z mehr als sieben Minuten ohne Spiel. Damit ist zusätzlich der echte längere Leerlauf nach einer normalen EFT-Sitzung abgedeckt.

## Crash-Nachprüfung des finalen Kandidaten

Auch die unabhängige Fixture erhielt nach beendetem Betrieb Helper `D2CF…` und Server `2262…`; beide Hashes sind in `final-fixture/deployment.json` gespeichert. Die Originalsession war erneut nachweislich abgeschlossen, wurde vollständig archiviert und nach dem Test zurückgeschoben. Ein echter Factory-DM-Start führte zum aktiven Arena-Kit. Nur der anhand Pfad und Startzeit verifizierte Fixture-EFT-Prozess 4520 wurde beendet.

EFT endete um 17:17:58.309Z. Der neue Direktstart begann um 58.385Z, bevor der alte Watchdog um 58.424Z das Ende des alten privaten Servers meldete. Anschließend wurden Arena-Hauptmenü und normale Rückkehr erreicht. Nach dem Neustart war das gesamte eingefrorene Quellprofil identisch; nach normaler Rückkehr und regulärem Beenden unterschieden sich ausschließlich die zwei nativen Health-/Hideout-Zeitstempel. Alte Spiel-, Server- und Watchdog-PIDs waren verschwunden. Alle fünf Fixture-Offlinesessions sind geschlossen und abgeschlossen, aktiver Pointer fehlt. Belege: `final-fixture/`.

## Abschluss und Synchronisationsbefund

Die Originalsession wurde um 17:21:36Z exakt zurückgeschoben. Alle eigenen EFT-, Server- und Helperprozesse sind beendet. Originalkonfiguration und Launchereinstellungen entsprechen bytegenau dem Vorherstand. Zwei vom Spiel ausschließlich in Kommentaren umformatierte Konfigurationen wurden aus der Sicherung zurückgesetzt. Kein Originalprofil wurde manuell wiederhergestellt oder repariert. Die Fixture bleibt unter `C:\ArenaMode-DirectNative-20261028` erhalten; ihr abgeschlossener Helperzustand liegt unter `C:\Temp\ArenaMode-DirectNative-20261028\fixture-final-completed-session`.

Der vollständige Vorher-/Nachhervergleich der Originalprofile vor erneutem Synchronisieren ergab: A bleibt bei 9561 XP; Energy/Hydration regenerierten beim ersten normalen EFT-Start auf 100, zwei fehlende explizite Standardmengen wurden auf 1 initialisiert, dazu kommen zwei Zeitstempel. B bleibt bei 610 XP; ebenfalls zwei Standardmengen und zwei Zeitstempel sowie eine native `recoveries`-Markierung für den Abschluss `closed-without-payment`. Der Marker stimmt mit Review und Request-Fingerprint der gespeicherten Authority überein. Inventar-IDs, Templates, Plätze und Mengen sind bei beiden unverändert. Das unbenutzte dritte Originalprofil und die zuvor vorhandenen Konfliktkopien sind unverändert. Rohvergleich: `cleanup/full-profile-outcomes.json`.

SPT41-Synchronisation wurde zunächst entsprechend dem Vorzustand um 17:22:47Z fortgesetzt. Die anschließende Crosshost-Prüfung des übergeordneten Tests erkannte eine echte Divergenz: Host1 hatte B parallel mit zwei Auszahlungen auf 2810 XP entwickelt; der jüngere normale Save von Host2 mit 610 XP wurde beim Synchronisieren zum gemeinsamen Stand. Damit sind die auf Host1 vorher bewiesenen Auszahlungen **nicht im aktuell synchronisierten Normalprofil erhalten**. Das ist kein Beleg für dauerhafte Reward-Erhaltung zwischen parallel betriebenen Profilkopien. Auf Anweisung des übergeordneten Tests wurde ausschließlich SPT41 auf Host2 um 17:29:07Z erneut pausiert. Aktuelle Profile, Reward-Metadaten, Konfliktkopien, Hash-/Zeitmanifest und pausierter Syncthing-Status sind unter `post-sync-audit/` gesichert. Keine weitere Serverausführung oder Profilrestauration erfolgte.

Der sichere Endzustand wurde danach im übergeordneten Test festgelegt: Synchronisierung bleibt gemäß der Nutzeranweisung bei beobachtetem Eingriff pausiert. Der aktuelle Stand B mit 610 XP und null Auszahlungen bleibt erhalten; die früher auf Host1 bewiesenen optionalen Testrewards sind durch die externe Synchronisation zurückgerollt. Beide exakten Profilstände bleiben als Belege erhalten, ohne eine Profilkopie manuell zum aktuellen Stand zu machen.

Am 17:47:01Z wurden ausschließlich die 22 zuvor einzeln erkannten Reward-Metadaten-Konfliktkopien nach `C:\Temp\ArenaMode-sync-conflicts-20261028-host2` archiviert. Dryrun und Anwendung prüften getrennt Pfadgrenzen, fehlende Reparse-Pfade, vorhandene kanonische Geschwisterdateien und Abwesenheit von EFT/Server/Helper. Alle 608 regulären Reward-Metadatendateien und die drei Originalprofile blieben bytegleich. Die abschließende lesende Kontrolle um 17:52:16Z bestätigte die 22 Archivhashes, das Fehlen der entsprechenden Quellduplikate, unveränderte Profile, finale Binärhashes, null Prozesse und weiterhin pausiertes SPT41. Host2 startete anschließend keinen Server. Archivierung und unabhängiges Readback: `post-sync-audit/conflict-archive-*.json`, `final-safe-state.json`. Alte Profil-Konfliktkopien wurden nicht verschoben.

Eine zusätzliche lesende Prüfung der aktiven DLL-Suchpfade unter `BepInEx` und `SPT_Runtime\user\mods` fand genau eine alte Syncthing-DLL-Kopie: `maschine-ArenaMode.Server.sync-conflict-20261028-181231-W7YIU3M.dll`, SHA `CA15…`. Diese eindeutig bekannte Vorversion wurde um 17:52:00Z separat nach `C:\Temp\ArenaMode-sync-conflicts-20261028-host2\loader` archiviert. Die reguläre Server-DLL blieb unverändert `2262…`. Danach enthielt keiner der beiden untersuchten Suchbäume weitere `*.sync-conflict*.dll`; sämtliche Kopien bleiben im externen Archiv erhalten. Beleg: `post-sync-audit/loader-conflict-archived.json`, zusätzlich hashgleiche Belegkopie. Das tatsächliche Laden nach dieser letzten Bereinigung prüft Host1; Host2 führt entsprechend der Aufgabenabgrenzung keinen weiteren Serverstart aus.

`verify-evidence.mjs` prüft vierzehn gespeicherte Beleggruppen einschließlich einer Negativkontrolle gegen einen tatsächlich laufenden Prozesssatz. Der Verifier bestätigt die einzelnen Lebenszyklusabläufe und den gesicherten pausierten Abschlusszustand; er behauptet keine Wiederherstellung der extern zurückgerollten Testrewards. Die vier Abschlussdurchgänge umfassten Vollständigkeit, fachliche Rechte-/Profilprüfung, Gegenprüfung der unmaskierten Profile und Artefaktverknüpfungen sowie Berichtskorrektur nach dem Synchronisationsbefund. Die Abschlussanforderung entspricht der Contractrevision 4: Synchronisierung nur wiederaufnehmen, wenn dies nachweislich sicher ist; bei beobachtetem Eingriff ausdrücklich pausiert lassen.

## Grenzen

Der vorhandene gpu-run-Arenaablauf erwartet noch den alten Objective-Text „Most kills“ und brach bei „Endless combat“ vor READY ab. Factory/NEXT/READY wurden danach anhand frischer UiBridge-Klickpunkte bedient. Fehlerszenarien injizierten nur den Prozessabbruch, keine künstlichen Rewards oder Abschüsse. Leistungs-/Multiplayer- und Remote-Serverkompatibilität sind nicht Gegenstand dieser Nachprüfung.
