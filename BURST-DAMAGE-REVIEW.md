# Optionale Salven-Schadenszahlen

Stand: 2026-09-30. Die neue Option `HUD / CombineDamageNumbers` ist standardmäßig
aus und über F12 sowie als `Combine burst damage` im bestehenden HUD-Panel
schaltbar. Die Änderung wirkt sofort; `ShowDamageNumbers` bleibt der Hauptschalter.
Sie verändert ausschließlich schwebende Schadenszahlen im Arena-HUD. Treffer,
Headshot-/Killmarker, Audio, Assist-Erfassung und Punkte erhalten weiterhin jedes
ursprüngliche Ereignis unverändert.

## Verhalten

- Treffer desselben physischen Ziels werden bei höchstens 0,25 Sekunden Abstand
  zur letzten zusammengefassten Meldung addiert. Das Fenster gleitet pro Treffer;
  eine längere Feuerpause beginnt eine neue Zahl. Ein anderes Ziel bekommt stets
  eine eigene Zahl, auch bei abwechselnden Treffern auf mehrere Bots.
- `VictimBodyId` enthält nur `victim.GetInstanceID()`. Keine Spieler-/Botreferenz
  wird über das Damage-Ereignis hinaus gehalten. Eine unbekannte ID (0) gruppiert
  nie; negative Unity-IDs bleiben gültig. Neue physische Respawn-Körper haben ihre
  eigene Identität. Ein tödlicher Treffer schließt seine Gruppe sofort.
- HP- und Rüstungsschaden bleiben getrennte Float-Summen. Erst die angezeigten
  Summen werden gerundet; Rüstung bleibt blau in Klammern. Headshot-/Killfarbe
  bleibt innerhalb einer Salve erhalten. Die Anzahl von Markern oder Sounds wird
  nicht reduziert.
- Eine Salve bleibt am ersten Einschlagpunkt und behält ihre einmal gewählte
  vertikale Ausweichposition. Weitere Treffer verlängern nur Sichtbarkeit/Fade
  um die bisherigen 1,25 Sekunden. Der langsame Aufstieg beginnt weiterhin beim
  ersten Treffer und springt bei einem neuen Treffer nicht zurück.
- Für gruppierte Zahlen wird der maximale Platz zweier dargestellter
  Ganzzahlwerte vorher anhand des TMP-Fonts reserviert. So können Zahlen wachsen
  oder erstmals Rüstung zeigen, ohne Nachbarzeilen nachträglich zu verschieben.
  Die aktivierte Option erlaubt genügend Ausweichversuche für alle zwölf Zeilen.
- Die Kollisionsfläche reserviert zusätzlich den gesamten vertikalen Aufstieg,
  korrekt in Canvas-Einheiten umgerechnet. Andernfalls könnte eine später
  beginnende Zahl in eine bereits oben angekommene, weiter aufgefrischte Salve
  hineinsteigen. Sichtbares Rechteck und reservierter Bewegungsraum bleiben
  getrennt: kein Sprung und kein nachträgliches Verschieben einer bestehenden
  Zeile. Solange eine Salve sichtbar bleibt, gilt diese Reservierung auch nach
  Ausschalten der Option für neue Einzelzahlen. Ohne solche Salven behält der
  ausgeschaltete Modus seine bisherige Platzierung.
- Der Pool bleibt bei zwölf Objekten, ohne Ziel-Dictionary oder Körperreferenzen.
  Ablauf, Kameraausfall, Wiederverwendung und tödliche Treffer verhindern das
  Wiederverwenden alter Summen. Ein beobachteter Optionswechsel im Spawn-/HUD-Tick
  schließt alle bisherigen Gruppen, lässt deren sichtbare Zahlen aber auslaufen.
  Bei deaktivierter Option gilt weiterhin eine Zahl pro Ereignis mit bisheriger
  Platzierung, Rundung, Farbe und Dauer.

## Prüfung und Grenzen

Vier Durchgänge: Umsetzung; Prüfung von Zielidentität/Lebensdauer und native
Capture-Anbindung; Fehlersuche an Rundung, Pool-Wiederverwendung und Layout;
abschließende Bereinigung/Dokumentation. Dabei wurde verhindert, dass ein älteres
Fenster nach einer neuen Gruppe durch ein zeitlich rückwärts eintreffendes
Ereignis wieder angesprochen wird. Außerdem reichen die bisherigen zehn
Ausweichversuche nicht für zwölf gleichzeitig benachbarte Zeilen; die aktivierte
Salvenoption nutzt deshalb zusätzliche Versuche, ohne vorhandene Zeilen zu bewegen.
Das unabhängige Review fand außerdem die oben beschriebene Kollision zwischen
zeitversetzt aufsteigenden Zahlen. Der zusätzliche Bewegungsraum behebt sie;
Regressionen prüfen den gesamten überlappenden Sichtbarkeitszeitraum bei
Canvas-Skalierung 0,5/1/2 sowie nach Ausschalten der Option.

`tools/BurstDamageChecks/Verify.ps1` kompiliert die unveränderte Produktionsklasse
mit schmalen Unity-/TMP-/Konfigurationsgrenzen. Es prüft den Ausgangszustand, elf
kompilierte Fehlerkontrollen und wiederhergestellten Produktionscode. Die
Fehlerkontrollen entfernen gezielt Zieltrennung, Zeitfenster, getrennte
Rundung/Rüstungszuordnung, Todesabschluss, Optionsabschluss, Breitenreservierung,
genügend Layoutplätze, den Bewegungsraum, dessen Skalierung oder seinen Erhalt
nach Ausschalten der Option. Nur das Scheitern der dafür vorgesehenen
Laufzeitassertion zählt, keine Compilerfehler.

Lokaler Lauf am 30.09.2026 nach dem Layoutfix: **225 Assertions**, alle **elf Fehlerkontrollen** und
erneuter Produktionslauf erfolgreich (`BURST_DAMAGE_CHECKS_PASSED`). Belege:
`artifacts/burst-damage-checks/314f109dfb7d40dcb3ead3498f6f4aba/`.

Die Offline-Prüfung belegt keine native TMP-Pixelgeometrie oder Harmony-Ausführung.
Client-Build, unabhängiges Review, Deployment und ein echter Raid sind separate
Abnahmen. Im Spiel gezielt Automatikfeuer auf ein Ziel, schneller Zielwechsel,
eine Pause über 0,25 Sekunden, Rüstungstreffer, tödliche Treffer und Umschalten der
Option prüfen. Ohne aktivierte Option muss die bisherige Anzeige erhalten bleiben.
