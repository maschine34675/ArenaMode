# Totenkopf, Kartenauswahl und Blutungstod

Stand: 29.09.2026. Reine Darstellung; keine Änderungen an Schaden, Kill-Zuordnung,
Punkten, Profil, Konfiguration oder Assets.

- `ArenaKillSkull` liegt jetzt 42 HUD-Einheiten **oberhalb** des Fadenkreuzes,
  weiterhin 32 x 32 und 0,8 Sekunden sichtbar. Der Nutzer hat die vorherige
  Darstellung abgesehen von der Position akzeptiert.
- Die Arena-Kartenauswahl suspendiert die eigene Charaktervorschau, statt sie
  unten rechts anzuhängen. `HideForMap` bindet sich an exakt dieselben
  RaidSettings und die bestehende Arena-Auswahl. Modell und Bundles bleiben
  für Offline-/Bestätigungs-/Ladebildschirm erhalten. Vorherige native Bilder
  werden wiederhergestellt; verspätete Accept-/Loading-Anfragen werden verworfen.
  PMC/Scav, fremde Settings und laufende Raids werden nicht verändert.
- Ein natives Blutungstod-Symbol war bereits vorhanden. Es wird nun klar rot
  angezeigt. Leichte und schwere Blutung verwenden das vorhandene Arena-
  Blutstropfen-Asset im Todesursachenfeld. Ein geerbtes Kopftreffer-Icon wird
  dabei unterdrückt. Beim nächsten direkten Kill erhält dieselbe gepoolte
  Zeile wieder ihr weißes Waffen-Symbol und gegebenenfalls das Kopftreffer-Icon.

## Native Ursache statt Vermutung

`../_decompiled/EFT.HealthSystem/ActiveHealthController.cs` liefert in
`LightBleeding.DamageInfo` und `HeavyBleeding.DamageInfo` die entsprechenden
Blutungstypen. `Bleeding.BleedHealth` reicht diese an `ApplyDamage` weiter.
`../_decompiled/EFT/Player.cs`, `OnDead`, übergibt den tatsächlichen letalen Typ
separat an `OnBeenKilledByAggressor`, neben `LastDamageInfo` und `LastBodyPart`.
Diese beiden letzten Werte können noch vom vorherigen Schuss stammen.

Arena übernimmt den letalen Typ schon in `ArenaKill.DamageKind`. Die vorhandene
exakte Zuordnung `LightBleeding`/`HeavyBleeding` bleibt erhalten. Nicht aus einer
beliebigen aktiven Blutung, einem Waffennamen oder unbekannten kombinierten
Flags ableiten. `ArenaKill.Headshot` wird nicht geändert, weil andere Verbraucher
dieses Feld für bestehende Statistik-/Punkteregeln verwenden. Hier wird nur das
irreführende Badge im Killfeed ausgeblendet. Ohne nativen Angreifer entsteht
weiterhin kein künstliches Kill-Ereignis.

## Prüfungen

- `tools/ArenaUiAssetChecks/Verify.ps1`: 193 Assertions, zwölf kompilierte
  Fehlerkontrollen. Beide Blutungstypen, geerbter Kopftreffer, alle sechs
  wiederverwendeten Zeilen, Zurücksetzen von Farbe/Icon und unveränderte
  Kampfmetadaten sind abgedeckt.
- `tools/ArenaCharacterPreviewChecks/Verify.ps1`: Zustandsmodell, Hook-Kompilierung,
  Fehlerkontrollen und echte Produktions-Hostwechsel gegen schmale Unity-Doubles
  bestanden. Map-Ersteintritt, Accept-/Loading-Rückweg, fremde Settings,
  wiederholtes Anzeigen, alte Callbacks und erneutes Offline-/Accept-Anzeigen
  geprüft. Zusätzliche kompilierte Defekte für Entsorgung beziehungsweise
  fehlendes Ausblenden werden zurückgewiesen.
- Isolierter Client-Release-Build bestanden, keine automatische Änderung der
  Installation. Kein Server-/Core-Build erforderlich.
- Zwei unabhängige lesende Reviews (HUD/Kampfmetadaten und Preview-Lebensdauer):
  keine offenen P0-P2-Befunde.

Diese Offline-Prüfungen sind keine gerenderten EFT-Pixel. Native Lieferung und
Menüprüfung sind separat unten protokolliert.

## Lieferung und native Menüprüfung

Finaler sauberer Quellstand `2dfc1e7dd802134c98d988669538c3d293083179`, Client
SHA256 `05A017E181049D48EC0332D19EFB88FE08727630C2EFA7CCA5DE2B552B5DE303`.
Build: 0 Fehler, 4 bestehende CS8632-Warnungen.
Manifest: `artifacts/double-jump-build/e17e9adf1291462b82cc73a91f0f2b2e/manifest.json`.
Lokal und auf GPU-Host 1 (.114) mit exklusiven Dateisperren, Backups und
Hashvergleich eingespielt; Server/Core/BigBrainCompat unverändert. Host 2 (.109)
war durch eine andere Sitzung belegt und wurde nicht beschrieben oder gesteuert.
Bei der abschließenden lesenden Kontrolle lag auch dort derselbe neue Clienthash
vor; UiBridge bestätigte in einem inzwischen neu gestarteten Prozess auch
den übereinstimmenden geladenen ArenaMode-Build. Beleg:
`HUD-BLEED-POLISH-DEPLOYMENT-20260929.json`.

Host 1 war zunächst ohne Spiel und SPT-Server. Über den unterstützten gpu-run-
Start mit Profil `SPT-GPU-1` gestartet: EFT-Prozess `3344be26`, Start 18:48:01.
UiBridge `0.7.3+419df3c2ef81f2582a3d205b4b2af98b80b35712`, SHA256
`EC325E81FD79ECBC0890AD6AE5B1D42897676791C68935940B6EAF1FADEF51B6`.
EFT `0.16.9.5-40743-f137e819`, 1920 x 1080, Skalierung 100 %.
ArenaMode lokal/installiert/geladen identisch. Die einzige Abweichung unter
24 maschine-Mods betraf ArenaMapPort.Client aus parallelen Arbeiten; dieser
Build wurde weder ersetzt noch als identisch behauptet.

Die folgenden echten Screenshots wurden geöffnet und visuell geprüft:

| Zustand | Ergebnis | Lokaler Rohbeleg unter `artifacts/hud-polish-gpu/` |
|---|---|---|
| Arena → Karte | Kein Avatar unten rechts | `map-first/step-8-screenshot.png` |
| Equator → Bestätigung | Arena-Modell sichtbar | `accept/step-8-screenshot.png` |
| Bestätigung → Karte | Erneut kein Avatar | `return-safe-back/step-4-screenshot.png` |
| Karte → Charakterauswahl | Scav-, Arena- und PMC-Modelle sichtbar | `return-safe-back/step-7-screenshot.png` |
| PMC beziehungsweise Scav → Karte | Normale Kartenauswahl ohne Arena-Bedienfeld | `return-safe-back/step-11-screenshot.png`, `step-17-screenshot.png` |

39 erfolgreiche Szenarioschritte über drei Läufe; kein Raid gestartet, kein
Godmode, keine Test-Hooks, keine Inspector-Handles, keine Konfigurationsänderung.
Ein vorheriger Lauf verweigerte den nativen BACK-Klick durch die allgemeine
Sicherheitssperre des Helfers; auf der überprüften Menüseite anschließend mit
dem vorgesehenen `allowDangerous`-Parameter wiederholt. Keine Spielsperre.
Zurück im Hauptmenü, Steuerung frei. Spiel und Server auf Host 1 dort belassen.

Im kopierten Start-/Menülog keine Preview-Restore-/Cleanup-Fehler. Genau eine
Error-Zeile: Unity-Mikrofoninitialisierung um 18:48:42, bereits vor der ersten
Arena-Menünavigation um 18:49:04. Kein Anspruch auf ein insgesamt fehlerfreies
Log. Nebenbefund: zusätzliche ArenaMapPort-Ortsbeschriftungen überlagern sich
oben in der gewöhnlichen PMC-/Scav-Karte; nicht Gegenstand dieser Änderung und
nicht auf die Avatar-Suspendierung zurückgeführt.

Diese native Abnahme betrifft **Menü und Avatar**. Der nach oben verschobene
Kill-Totenkopf und ein tatsächlicher Verblutungstod wurden in diesem Menütest
nicht erneut im Raid dargestellt. Blutungs-Pooling/Metadaten sind offline
geprüft; die vorige Nutzerabnahme bestätigt die übrige Totenkopf-Darstellung.

Rohlog `artifacts/hud-polish-gpu/client.log`, SHA256
`AA8B196C69CF96DB07FAD3BF54DAF8E686A4E166357DF95058DFB7D2457185FE`.
Quelle: `\\192.168.178.114\c$\SPT41\BepInEx\LogOutput-SPT-GPU-1-2026-09-29_18-47-38.log`.
Szenario-JSON/Markdown-Berichte liegen neben den Screenshots im ignorierten
Artefaktverzeichnis. Bildhashes: Map-Ersteintritt
`EBD3CEE5C3EA21D2B6202FED997E7E42CC583959B6CD0CD1B804C617AF7376A8`,
Accept `8F1BFC9DED7763F957E8E4003376FCC8B07A2F08BB19940C426C846B3F06F044`,
Map-Rückkehr `75B328EC443D5C85D18A8A570F55D763C027AF7E382514ACF5010387E0FE56EB`.
