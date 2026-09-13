# Unabhängige visuelle Prüfung – Area Map Preview r2

Geprüft am 13.09.2026 wurden die unten benannten, gespeicherten Originalbilder aus `artifacts/area-map-preview-r2/2026-09-13T10-58-42-871Z/screenshots/`. Die Bilder wurden in Originalauflösung betrachtet. Es erfolgten dabei keine UI-Eingaben, weiteren Testläufe oder Änderungen an Spiel, Einstellungen und Produktionsdateien.

**Ergebnis:** In diesen Bildern wurden keine materiellen visuellen Fehler festgestellt. PASS bezeichnet ausschließlich die jeweils sichtbare Darstellung und den ausdrücklich genannten Bildvergleich.

## Auswahl und Kartenansichten bei 1920×1080 Borderless

| Originalbelege | Beobachtung und Ergebnis |
| --- | --- |
| `13-center-regression-baseline-0.jpg`, `14-east-single-click-0.jpg`, `15-east-after-fifteen-seconds-0.jpg` | **PASS – sichtbarer Auswahlwechsel:** Bild 13 zeigt Factory Center mit Rechteck. Bild 14 zeigt anschließend East-Titel und Tankkontur; Bild 15 zeigt weiterhin East. Die Bilder belegen den geänderten und später erneut sichtbaren Zustand; Klickanzahl und exakte Wartezeit sind anhand der Eingabeaufzeichnung zu beurteilen, nicht allein anhand der Dateinamen. |
| `20-east-detail-0.jpg`, `21-east-overview-0.jpg`, `22-east-area-restored-0.jpg` | **PASS – East-Framing und Helligkeit:** Die AREA-Bilder 20/22 zeigen die vollständige blaue Kontur, einen helleren Innenbereich und gedimmtes Umfeld. Es sind keine sichtbaren Löcher oder abgeschnittenen Konturteile erkennbar. In Bild 21 passt die ganze Customs-Karte in OVERVIEW; graue Nachbarumrisse und das separate Legacyrechteck sind sichtbar. Bild 22 zeigt wieder AREA ohne graue Nachbarumrisse. In 21/22 ist East zusätzlich am Auswahlstreifen ohne Hover eindeutig erkennbar. |
| `20-east-detail-0.jpg`, `21-east-overview-0.jpg`, `22-east-area-restored-0.jpg` | **PASS – sichtbares East-Alignment:** Die vier Tanks in zwei Paaren, die schmale Halle daneben, die obere gerade Bahnlinie, die rechte Bahnkurve und die Straße unterhalb behalten über AREA → OVERVIEW → AREA ihre nachvollziehbare Lagebeziehung zur Kontur. Kein sichtbarer Versatz dieser Landmarken gegenüber der Grenze. |
| `23-center-detail-0.jpg`, `24-center-overview-0.jpg` | **PASS – Center-Framing, Helligkeit und Alignment:** Die AREA-Kontur ist vollständig sichtbar und innen heller. Die großen Hallen, die linke Bahnkurve, die rechte Straße und die untere Straßenkante behalten beim Wechsel zu OVERVIEW ihre Lagebeziehung zur Grenze. OVERVIEW zeigt die ganze Karte mit grauen anderen Gebieten und separatem Legacyrechteck. |
| `24-center-overview-0.jpg`, `37-team-starts-overview-0.jpg` | **PASS – gemeinsamer sichtbarer Footprint:** Center und Team-starts zeigen dieselbe blaue Kontur an derselben Stelle über Hallen und Straßen. Keine sichtbar versetzte oder dick gedoppelte Center-/Team-starts-Grenze. Bild 37 zeigt den vollständigen Team-starts-Titel, die Auswahl, alle Größen 2v2–5v5, ausgewähltes 5v5 und den zugehörigen Hinweis. Caption, Quellenangaben und native Bedienelemente bleiben sichtbar. |

## Fensterdarstellung

Für die Bilder 72–76 wurde laut nativem Test die tatsächliche Spielauflösung **1904×993 Windowed** verwendet. Die betrachteten Originalbilder messen **1906×1025 einschließlich Fensterrahmen und Titelleiste**. Diese Maße sind getrennt zu führen; die Bildgröße einschließlich Rahmen ist keine Spielauflösung.

| Originalbelege | Beobachtung und Ergebnis |
| --- | --- |
| `72-windowed-east-area-0.jpg`, `73-windowed-east-overview-0.jpg` | **PASS – East im Fenster:** Die vollständige Kontur passt in AREA, die ganze Karte in OVERVIEW. Tanks erscheinen weiterhin kreisförmig, Hallen nicht erkennbar gestreckt. Der helle Innenbereich und die Grenze sind vollständig sichtbar. Titel, AREA/OVERVIEW, Caption und beide Quellenzeilen sind ohne Anschnitt vorhanden. Die kleinen Quellenzeilen bleiben lesbar. |
| `74-windowed-center-area-0.jpg`, `75-windowed-center-overview-0.jpg` | **PASS – Center im Fenster:** Vollständige AREA-Kontur und vollständige OVERVIEW-Karte ohne sichtbare Verzerrung. Titel, Caption und Quellenangaben passen. Die grau dargestellten anderen Gebiete sind im OVERVIEW sichtbar. |
| `74-windowed-center-area-0.jpg`, `76-windowed-team-starts-controls-0.jpg` | **PASS – gemeinsamer Footprint und zusätzliche Controls:** Center und Team-starts verwenden auch in dieser AREA-Darstellung denselben sichtbaren Umriss. Alle vier Gebietseinträge, sämtliche Größen 2v2–5v5 sowie 5v5-Zusammenfassung und Hinweis passen in das linke Panel. Der längere Team-starts-Titel ist vollständig sichtbar. |
| `72-windowed-east-area-0.jpg`, `73-windowed-east-overview-0.jpg`, `74-windowed-center-area-0.jpg`, `75-windowed-center-overview-0.jpg`, `76-windowed-team-starts-controls-0.jpg` | **PASS – Layout im geprüften Fenster:** Keine erkennbare Überlappung der Vorschau oder Gebietsauswahl mit nativen Zeit-, Weiter-, Zurück-, Karten- oder Ready-Bedienelementen. Keine abgeschnittenen Paneltexte oder sichtbare Überlagerung des unteren Menüstreifens. Vorhandene deaktivierte Schaltflächen wurden nicht als Darstellungsfehler gewertet. |

## Ergänzender Dateibefund zur UI-Skalierung

Am 13.09.2026 um etwa **11:24:54 UTC** wurden ausschließlich die Schlüssel in `SPT_Runtime/user/sptSettings/Graphics.ini` und `Game.ini` lesend nach Scale/Scaling/Interface sowie Anzeige- und Auflösungsbezeichnungen durchsucht. In beiden Dateien wurde kein ausdrücklich benannter UI-Skalierungswert gefunden. Die zu diesem Zeitpunkt bereits gespeicherte Cleanup-Konfiguration enthielt aktive `DisplaySettings.Resolution` 1920×1080 und gespeicherte `WindowResolution` 1904×993. Dieser spätere Dateibefund ersetzt keinen Nachweis der aktiven Einstellung zum Aufnahmezeitpunkt und beweist nicht, dass das Spiel generell keine UI-Skalierung besitzt.

## Grenzen

- Die Prüfung umfasst die 14 verschiedenen oben benannten Originalbilder und die beiden belegten Darstellungsgrößen. Eine kleinere Auflösung oder veränderte UI-Skalierung wurde hiermit nicht abgenommen.
- Einzelbilder belegen keine durchgehende Stabilität zwischen Aufnahmen und keine zeitliche Flackerfreiheit. Es wurde kein Clip ausgewertet.
- Übereinstimmende sichtbare Center-/Team-starts-Konturen belegen den gemeinsamen dargestellten Footprint, nicht die Anzahl interner Renderinstanzen.
- Landmarken und Grenzen wurden im sichtbaren Kartenmaßstab verglichen. Daraus folgt keine pixelgenaue Vermessung, Weltkoordinaten-, Kollisions-, Höhen-, Navigations- oder Raid-Geometrieabnahme.
- Diese Prüfung enthält keine Aussage über Logfehler, Profilintegrität, Binärbindung oder die funktionale Wirkung der angezeigten Regeln. Solche Ergebnisse benötigen die jeweiligen separaten Belege.
