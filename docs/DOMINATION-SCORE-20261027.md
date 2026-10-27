# Domination im Haupt-Scorepanel — 27.10.2026

Die Kontrollpunkte stehen als zusätzliche Zeile **innerhalb derselben Scorefläche**.
Das bisherige separate Panel samt Zwischenraum entfällt.

| Darstellung | Änderung |
|---|---|
| Scorefläche | Nur bei aktiver Domination von 84 auf 120 HUD-Pixel erweitert. |
| Kontrollpunkte | Eine 36-Pixel-Zeile unter den vorhandenen Scorezeilen; ein bis drei Punkte. |
| Zustände | Fraktion, Neutral, Umkämpft, Prozentfortschritt und Angriffssignal unverändert. |
| Lesbarkeit | Bestehende Farben; 20-Pixel-Schrift mit begrenzter Anpassung bis 16 Pixel, kein Zeilenumbruch. |
| Pickups | Bleiben mit 16 Pixel Abstand unter der vollständigen Scorefläche. |
| Andere Modi | Vorherige 84-Pixel-Höhe und Pickup-Position bleiben erhalten. |

`ScorePanel` besitzt und aktualisiert die Punktzeile. Ihre Sichtbarkeit und die
gemeinsame Höhe folgen derselben aktuellen Match-/Phasenprüfung; das gilt auch
zwischen den langsameren Textaktualisierungen. Weltmarker und Spielregeln ändern
sich nicht.

## Nachweis

Der erweiterte `DominationHudChecks` verknüpft jetzt auch den echten `ScorePanel`.
Er prüft Hierarchie, fehlende eigene Punktfläche, dynamische Höhe, ein bis drei
Punkttexte, Offsets, Matchwechsel, Sichtbarkeit und Pickup-Abstände. Root hat
**913 Assertions und 13 gezielte Negativkontrollen** erfolgreich ausgeführt;
die geänderten Quellen wurden unabhängig gegengelesen.

Release-Build von Client und Companion erfolgreich. Native Abnahme,
Schriftprüfung und geladene Identitäten stehen in der
[gemeinsamen Abnahme](UX-FOLLOWUP-20261027.md).
