# EFT-Arena-Grafiken für Killfeed und Scoreboard

Stand: 2026-09-29. Darstellung des Killfeeds und der vollständigen F9-Tabelle;
das kompakte Score-Panel oben mittig bleibt unverändert.

## Darstellung und Grenzen

- Killfeed: dunklere Zeilenfläche, eigene Symbolspalte für Waffenklasse bzw.
  bekannte Todesursache, zusätzliches Kopftreffer-Symbol. Die sechs gepoolten
  Einträge behalten ihre vollständigen Namen, Rollen, Teamfarben, Entfernung,
  Waffenbezeichnung, Textmarkierung für Kopftreffer und bisherige Lebensdauer.
  Die Gesamtbreite steigt von 460 auf 520; die verfügbare Textbreite bleibt 436.
  Bestehende Positionsregler verschieben weiterhin die gesamte Anzeige.
- Scoreboard: eingefärbte Abschnittsleisten, abwechselnde Zeilenflächen,
  hervorgehobene eigene Zeile und feste Spalten für Name, Rolle, Status,
  Kills, Tode, Assists, Kopftreffer und Punkte. Lange Namen/Rollen vergrößern
  die Zeilenhöhe. Zahlen bleiben in ihren eigenen Spalten.
- Die bereits ermittelte Teamzuordnung entscheidet über Farben, nicht der
  Fraktionsname. Die vorhandenen konfigurierten Teamfarben werden berücksichtigt.
  Rangfolge, Archivzeilen, Summen, Assists, Punktevergabe, F9/Esc, Mausfreigabe
  und Scrollverhalten bleiben beim bisherigen Besitzer `ArenaPanel`.
- Keine neuen Einstellungen, Profiländerungen oder Änderungen an normalen Raids.

`ArenaKill` transportiert zusätzlich zwei einfache Metadatenfelder. Die
Waffenklasse wird am vorhandenen Kill-Ereignis aus `DamageInfo.Weapon` als
`Weapon.WeapClass` erfasst; ein Fehler beim Lesen darf das Ereignis nicht
unterdrücken. Maßgeblich ist der endgültige `lethalDamageType`. Blutung, Feuer,
Nahkampf und Granatensplitter haben Vorrang vor einer möglicherweise älteren
Waffenreferenz. Nur `Bullet` verwendet die Waffenklasse. Allgemeine Explosionen,
Landminen, unbekannte bzw. kombinierte Ursachen erhalten ein neutrales
Kill-Symbol; aus dem lokalisierten Waffennamen wird nichts geraten.

## Herkunft und Ressourcenbesitz

17 unverändert kopierte PNGs aus dem vom Nutzer bereitgestellten Export
`D:\ArenaMapPort\work\extract\arena-ui`. Einzelquellen, Objektidentität,
Abmessungen und SHA256 stehen in `ArenaMode.Client/Assets/Ui/SOURCES.json`.
`NOTICE.md` dokumentiert die Rechte: originale Spielgrafiken, keine nachgewiesene
freie Weiterverteilungslizenz. Nutzung für die autorisierte private Installation;
vor einer öffentlichen Veröffentlichung müssen die Rechte geklärt werden.
Nicht der Codelizenz oder den selbst erstellten RaidInfoPanels-Icons zurechnen.

PNGs und Herkunftshinweise werden in die Client-DLL eingebettet. Keine neuen
externen Installationsdateien oder fremden Bundles/DLLs. Jeder Anzeigenbesitzer
lädt eigene begrenzte Texturen einmalig; maximal 256 KiB pro Datei und exakt
erwartete PNG-Abmessungen werden vor dem Unity-Decode geprüft. Die ursprünglich
blaue Abschnittsgrafik wird nur in ihrer privaten Laufzeittextur neutralisiert;
Originaldatei und Alpha bleiben erhalten. Keine globalen Materialien verändern.

Fehler werden pro Asset gecacht und nur einmal geloggt. Text bleibt verfügbar.
Killfeed-Sprites sind passiv, nicht anklickbar, und werden bei Wiederverwendung
vollständig zurückgesetzt. Sprite und Text teilen ihre bestehende CanvasGroup.
Mit dem Canvas sterben eigene Sprites und Texturen, auch nach abgebrochener
HUD-Erstellung. Die Tabelle räumt ihre Ressourcen bei `ArenaPanel.OnDestroy` auf.
IMGUI-Farben werden auch bei Fehlern zurückgesetzt; GUILayout reserviert dieselben
Zeilen bei Layout und Repaint, ausschließlich Repaint zeichnet.

## Prüfung

`tools/ArenaUiAssetChecks/Verify.ps1` prüft die Originaldatei-Hashes und führt
143 Prüfungen gegen die eingebundenen Produktionsklassen aus, dazu sieben
kompilierte Fehlerkontrollen (Cache, Entsorgung, Todesursache, gepooltes HS-Symbol,
Textbreite, Assist-Spalte und globaler GUI-Farbzustand).

`Verify-Integration.ps1` führt die vollständige Server-Suite, bestehende
Panel-/HUD-/Killfeed-/Menü-/Kartenvorschau-/Tactical-Prüfungen und einen isolierten
Client-Release-Build ohne Deployment aus. Unabhängige adversariale Review:
keine offenen P0–P2. Native Typen und IMGUI-Aufrufkontrakte gegen die installierte
Version/dekompilierten Quellen geprüft.

Die SVG-/PNG-Vorschauen in `artifacts` entstehen aus aufgezeichneten Aufrufen
der Produktionsdarstellung, mit angenäherten Schriftmetriken. Sie wurden auf
Überlappungen und Lesbarkeit geprüft, sind **keine EFT-Screenshots**. Insbesondere
TMP-Teamfarben und Zeilenumbruch des Killfeeds werden darin nicht exakt gerendert.
Die Test-Doubles können weder Unitys PNG-Decoder noch tatsächliche TMP-/IMGUI-
Schriftmetriken oder GPU-Pixel bestätigen. Build, Dateideployment und native
Abnahme bleiben getrennte Aussagen.

## Noch im Spiel prüfen

Nach dem Einspielen EFT neu starten; Serveränderungen sind dafür nicht nötig.

1. Arena-Deathmatch/TDM: eigene, freundliche und gegnerische Kills mit langen
   Namen; Entfernung/Waffe/HS vollständig, Symbole zu plausiblen Waffenklassen.
2. Mehr als sechs schnelle Kills, abwechselnd mit/ohne Kopftreffer: kein altes
   Symbol in einer wiederverwendeten Zeile; Symbole und Text verblassen zusammen.
3. F9-Tabelle: Namen, LIVE/OUT, K/D/A/HS und Punkte gegen bekannte Ereignisse
   vergleichen; lange Zeilen, viele archivierte Bots, Scrollen und kleinere
   Auflösung/UI-Skalierung prüfen. F9/Esc und Mauseingaben unverändert.
4. Geänderte Teamfarben und Killfeed-Offsets prüfen; keine überlappenden
   Namen/Zahlen oder fehlenden Buchstaben in den Spaltenüberschriften.
5. Geschützt verlassen und erneut Arena starten; keine wachsenden Asset-Fehler,
   alten Symbole oder Änderungen an einer anschließenden normalen PMC-Runde.
6. Tactical Solo und möglichst PFT-5v5 über einen Rundenwechsel: aktuelle
   Teilnehmerzeilen und fortgeführte Teamsummen vergleichen. F9 über dem Kaufmenü
   öffnen und mit Esc schließen; Maus-/Tastaturfreigabe kontrollieren.

Die Deployment-Quittung und `HANDOFF.md` nennen den tatsächlich eingespielten
Quellstand und die Hashes. Eine native Raid-Abnahme ist hier nicht behauptet.
