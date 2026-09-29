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
Menüprüfung werden nach dem finalen sauberen Build separat protokolliert.
