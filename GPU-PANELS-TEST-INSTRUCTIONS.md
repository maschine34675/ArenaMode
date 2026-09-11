# Fokussierter GPU-Nachtest r5: vollständiger Killfeed

Führe diesen begrenzten Nachtest auf dem Spiel-PC selbstständig durch.
Installation `C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`.
Zuerst `AGENTS.md`, den aktuellen Lieferabschnitt in `HANDOFF.md` und
`GPU-PANELS-R4-RESULTS-2026-09-11.md` lesen. Alle bisherigen Berichte bleiben
unverändert. Dieser Auftrag startet keine anderen Tasks oder Automationen.

## Verbindlicher Teststand

- Job-ID: `arena-panels-native-ui-20260911-r5`.
- Quellcommit des DLL-Builds: `3ea19773dc63fec23d2b0e83ebb0a52c4792c772`.
- Client-SHA256: `A9D9A0E5D60E2BC56D9E419F5284745857D910C16B8E51496B5CAFBE995028F9`.
- Clientdatei: `C:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll`.
- Killfeed-Korrektur aus `4e49537`: stets beide Namen als Angreifer → Opfer;
  Rollen, Waffe, HS und gegebenenfalls Entfernung darunter. Keine Ellipsis;
  lange Texte umbrechen, Zeilenflächen und Abstände wachsen mit. Weiterhin
  maximal sechs Einträge, 4,5 Sekunden volle Deckkraft und 1,5 Sekunden Fade.
- Die separat gelieferten Hitmarker-Sounds bleiben erhalten. Im gemeinsamen
  Quellstand wurde dazu nur der Beschreibungstext der Audiooption geändert.
  Weder Sounddateien austauschen noch ihre Auswahl als Killfeed-Test behandeln.

Vor dem frischen Spielstart und nach Abschluss den tatsächlichen Client-Hash
prüfen; Repository-HEAD und Server-DLL-Hash ebenfalls protokollieren. Spätere
reine Dokumentationscommits dürfen den HEAD erhöhen; der Quellcommit bezeichnet
den DLL-Build. Die DLL muss exakt passen. Die Lieferung auf D:\SPT41 beweist
keine erfolgte Spiegelung auf C:\SPT41. Bei Abweichung blockieren und nichts
ersetzen. Insbesondere nicht den alten bereitgestellten `0DA0F70F...` verwenden.

## Grenzen und Vorbereitung

- Vorhandenes separates Testprofil im nativen Launcher prüfen und auswählen;
  niemals das Hauptprofil. Private Profil-/Einstellungssicherungen konsistent
  bei beendetem eigenem Spiel/Server erstellen. Fremde Prozesse nicht beenden.
- Kein Build, DLL-Tausch, Mod-Update, Pull/Merge/Reset/Push, Profil-JSON-Edit,
  Recovery-Eingriff oder Item-/Kill-Erzeugung per Code. Keine Mods entfernen.
- Native Computer-Use-Schnittstelle und echte Aufnahmen verwenden; nur tatsächlich
  dokumentierte Eingaben. Fehlende Bedienmöglichkeiten ausdrücklich offenlassen.
- Bestehende Panel-/Quickslotpositionen und Lesbarkeitseinstellungen beibehalten.
  Falls nötig andere Bot-Kills nativ über F12 einblenden; Änderungen am Ende
  zurückstellen. Bereits vorhandene Hitmarker-Sounds unverändert lassen.
- Falls das bekannte winzige Startfenster wieder auftritt: zunächst Zustand
  sichern, höchstens eine native Alt+Enter-Wiederherstellung wie in r4 versuchen.
  Keine Grafikdateien editieren und keine endlosen Umschalt-/Neustartserien.
  Tatsächlich verbleibende Grafikänderungen dokumentieren.

Rohbelege unter
`artifacts/gpu-panels/arena-panels-native-ui-20260911-r5/<Zeitstempel>/` ablegen:
Teststand, Screenshots mit Zeiten/Auflösung, relevante Logs, private Sicherungen
und Einstellungen-/Profilvergleich. Nichts davon committen. Clientzeiten sind
lokal, Serverzeiten UTC; Dateinamen können UTC sein. Keine doppelten Fensterbilder
als unabhängige Zeitpunkte zählen. Vorher-/Nachherbaseline nur für diesen Arena-Raid
vergleichen; kein Hauptprofil und keine normale PMC-Baseline damit vermischen.

## Ein geplanter Arena-Raid

Endless Deathmatch, Factory tagsüber, volle Karte, ohne PFT, vorhandene Kits.
Ein kurzer Raid genügt; höchstens zehn Minuten aktive Testzeit. Kein erneuter
vollständiger Zwei-Arena-plus-PMC-Durchlauf: r4 hat Menü-/Panel-Rückkehr,
Listen-/Header-Unterdrückung samt normaler Wiederherstellung und den Abstand
bei 1920×1080 bereits belegt. Diese Ergebnisse bleiben auf r4 begrenzt.

| Fall | Vorgehen | Erwartung und Beleg |
|---|---|---|
| K01 Andere Kills | Natürlich auftretende Bot-Kills vor heller und dunkler Szene aufnehmen. | Vollständiger Angreifer und vollständiges Opfer mit Pfeil dazwischen. Rollen/Waffe/HS, sofern vorhanden, darunter. Kein frühzeitiges „…“ auf weitgehend leerer Fläche. Beide Namen anhand echter sichtbarer Daten beurteilen, keine Namen erfinden. |
| K02 Eigener Kill | Wenn über dokumentierte native Eingaben möglich, einen echten Gegner eliminieren und Meldung aufnehmen. | Der tatsächliche eigene Profilname steht vor dem Opfer; keine namenlose Waffen-/Opferzeile. Waffen- und Entfernungsangaben separat lesbar. Ohne echten eigenen Kill bleibt dieser Fall blockiert, nicht aus K01 ableiten. |
| K03 Umbruch und Größenwechsel | Unterschiedlich lange Meldungen und mindestens zwei gleichzeitige Einträge beobachten. Bei natürlich auftretenden langen Namen/Details deren Umbruch und später kürzere Einträge aufnehmen. | Jeder Text vollständig auf seiner dunklen Fläche; Folgeeinträge darunter ohne Überlappung. Kein winziges Herunterskalieren, keine abgeschnittenen Unterzeilen. Kürzere wiederverwendete Einträge dürfen keine riesigen Leerflächen behalten. Nicht eindeutig beobachtete Wiederverwendung offenlassen. |
| K04 Ablauf und Menü | Bei sichtbarem Killfeed Inventar und Pause öffnen; einmal länger als sechs Sekunden offen halten und zurückkehren. Natürliches Verschwinden im Spiel ebenfalls aufnehmen. | Arena-HUD in nativen Menüs verborgen. Alte abgelaufene Einträge kehren nicht zurück; währenddessen neu entstandene Meldungen dürfen entsprechend ihrem eigenen Alter sichtbar sein. Text und Fläche verschwinden gemeinsam, kein leerer Balken. Nach Rückkehr korrekte Panelwerte und unveränderte Scoreposition. Zeitverhalten nur so genau behaupten, wie Aufnahmen es belegen. |
| K05 Eigener Tod / volle Belegung | Nur bei natürlichem Auftreten: eigene Todesmeldung und sechs gleichzeitig lebende Einträge aufnehmen. Nach Respawn kurz prüfen. | Eigener tatsächlicher Name als Opfer erhalten; keine bloße Ersetzung durch YOU. Keine überlappenden Flächen oder Duplikate nach Respawn. Den größeren Stapel auf Kollision mit Waffenpanel/anderen Anzeigen prüfen. Sechs Einträge, lange ungetrennte Namen und vollständige Pool-Wiederverwendung bleiben offen, falls nicht beobachtet. Aufgeben gilt nicht automatisch als Killfeed-Todesfall. |

K01–K04 sind die gezielten Kernfälle; fehlende echte lokale Kills oder nicht
beobachteter Umbruch bleiben einzeln offen. K05 und genaue Fade-Messung ergänzen
sie. Kein zusätzlicher Raid nur für seltene Unterfälle. Der in r4 unterbrochene
Granaten-Restbestandsvergleich darf im selben Raid nur dann ergänzt werden,
wenn einfach möglich; ein Tod/Kitwechsel beendet diesen Vergleich und frische
Kitgranaten gelten nicht als Beweis für den vorherigen Restbestand.

Räumliche grüne Exit-Wegweiser sind ein separates offenes UI-Thema. Die aktuelle
Korrektur entfernt sie nicht. Doppel-O, tatsächlichen Extract, PFT, weitere Kits
oder andere Auflösungen nicht neu erzwingen; bestehende offene Fälle nicht als
bestanden übernehmen.

## Abschluss

Bei einem reproduzierbaren Fehler Screenshot und vollständigen relevanten
Exception-Stack sichern, höchstens einmal gezielt wiederholen und unabhängig
mögliche Fälle fortsetzen. Keine Fixes während des Tests. Neue Arena-HUD-/TMP-
Fehler gezielt erfassen; QuestTracker-/RenderTexture-/SAIN-/BotWeaponSelector-
Fehler separat mit Zeitpunkt/Stack, ohne daraus eine UI-Ursache abzuleiten.

Arena regulär verlassen, Menü ohne HUD-Reste aufnehmen und die vorgesehenen
Profilfelder vor/nach vergleichen. Eigene Prozesse regulär beenden; eigene
Testeinstellungen zurückstellen, fremde Änderungen erhalten. Client-/Serverhash
und Einstellungssicherungen abschließend vergleichen.

Schreibe `GPU-PANELS-R5-RESULTS-2026-09-11.md`: tatsächlicher Stand, je Fall
bestanden/fehlgeschlagen/blockiert/nicht ausgeführt, konkrete Beobachtungen und
Belegpfade, verbleibende Grenzen. Keine privaten Profilkennungen, Geheimnisse,
kompletten Profile oder Rohlogs in den Bericht. Ist das Repository frei von
fremden Änderungen, ausschließlich diesen neuen Bericht committen mit
`Co-authored-by: Codex <codex@openai.com>`. Andernfalls Bericht belassen und fremde
Änderungen unangetastet lassen. Kein Push und keine alten Berichte überschreiben.
