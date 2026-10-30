# Thumbnail- und Marker-Revision — Prüfstand / nächste native Abnahme

Quellcommit `a8b8dd1`, Client-SHA256
`1E7A0EE9D8584B15038961B60FBCFCB1093312646A09EAA50AE70E5D3E5E036D`.
Nur Client geändert, lokal und auf GPU-Host 1 (.114) unter exklusiven Dateisperren
eingespielt und nachgehasht. Server/Core/BigBrain unverändert. Auslieferungsbeleg:
`MAP-PRESENTATION-DEPLOYMENT-20260930.json`. Host 2 wurde nicht bedient.

## Bereits geprüft

- 191 Menü-Assertions + 14 kompilierte Fehlerkontrollen bestanden.
- 4419 Karten-/Darstellungs-Assertions + 5 kompilierte Fehlerkontrollen bestanden.
  Echte Produktionsansichten prüfen feste Mutatorenposition, aktuelle/verspätete/
  gelöschte Thumbnail-Referenzen, fehlende Beschreibung, keine Zerstörung fremder
  Sprites, Markertext/-farbe/-position und vollständige Restaurierung.
- Isolierter Release-Build erfolgreich, kein unbeabsichtigtes Deployment während
  des Builds. Anschließend ausschließlich die neue Client-DLL ausgeliefert.
- Unabhängiges adversarial Review ohne konkrete offene P0–P2-Befunde.

## Aktuelle Testgrenze

Kein neuer nativer Sichttest: Windows-MCP auf GPU-Host 1 war über
`192.168.178.114:8000/mcp` wiederholt nicht erreichbar; auch die direkte
Snapshot-Anfrage scheiterte. UiBridge beantwortete keine Anfrage. SMB war
erreichbar und die Client-DLL nicht gesperrt, deshalb ist die Disk-Auslieferung
bestätigt, **nicht** der geladene Build oder die neue Darstellung. Es wurde kein
Spiel/Server gestartet, keine Testeinstellung geändert und kein Raid betreten.
Der vorherige 1080p-Bericht `GPU-INLINE-MUTATORS-RESULTS-20260930.md` gilt für
die vorige Version und darf nicht als Abnahme dieser Revision verwendet werden.

## Native Prüffolge nach Wiederherstellung der Bildschirmsteuerung

1. EFT neu starten. UiBridge-Status muss Client-MVID lokal = Host-Disk = geladen
   bestätigen; SHA256 oben vergleichen. Fremde aktive Tests nicht unterbrechen.
2. Arena > Kartenauswahl: oberes Kartenfeld bleibt auch ohne Auswahl 320
   Referenzpixel hoch, Mutatoren beginnen immer bei Y=508 in 1080p.
3. Factory wählen: natives Thumbnail mit unverzerrtem Seitenverhältnis und Name,
   keine Beschreibung oder Raidstatistiken. Tag/Nacht und Next bleiben erreichbar.
4. Bay 5 und Equator wählen, auch durch Klick auf die neue Schrift: kurzer Name
   ohne ARENA-Präfix, Gold im Grundzustand, dunkleres Gold mit gutem Kontrast im
   hellen Auswahlzustand. Native Auswahl, Hover und angezeigtes Thumbnail folgen
   der gewählten Karte. Bay 5 oben bei den Hafenanlagen, Equator südöstlich der
   normalen Interchange-Position. Keine Überschneidung mit anderen Markern.
5. Team DM > Customs: AREA/OVERVIEW funktionieren, Arealvorschau ersetzt das
   Thumbnail, der Mutatoren-Kasten springt nicht. Zurück zu Factory und
   Deathmatch: Position unverändert, kein altes Bild nach Deselection.
6. Escape > PMC > Factory: ursprüngliche Karte samt Text/Banner/Statistiken,
   keine eigenen Mutatoren/Goldlabels. Native Markerpositionen unverändert.
7. Ausgangseinstellungen (insbesondere GameMode) restaurieren, Screenshot und
   Log sichern. Menütest genügt, kein Raid erforderlich. Spiel/Server nur wieder
   beenden, wenn sie für diesen Test gestartet wurden.
