# Fokussierter GPU-Nachtest r6: Killfeed-Distanzen und Teamfarben

Führe diesen begrenzten Nachtest auf dem Spiel-PC selbstständig durch.
Installation `C:\SPT41`, Repository `C:\SPT41\Development\ArenaMode`.
Zuerst `AGENTS.md`, den aktuellen Lieferabschnitt in `HANDOFF.md` und
`GPU-PANELS-R5-RESULTS-2026-09-11.md` lesen. Die Berichte r1–r5 bleiben
unverändert; ein bereits begonnener r6-Lauf bleibt als eigener Stand erhalten.
Dieser Auftrag startet keine anderen Tasks oder Automationen.

## Verbindlicher Teststand

- Job-ID: `arena-panels-native-ui-20260911-r6`.
- Quellcommit des DLL-Builds: `41bb6e32d9018e38cd3e65520ce1bb938216f8c0`.
- Client-SHA256: `D9BB7FA07D05FF4BB6909E9C3ED885D0D41C96F8BD385FBB33B7C7281461626C`.
- Clientdatei: `C:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll`.
- Killfeed-Korrektur aus `4e49537`: stets beide Namen als Angreifer → Opfer;
  Rollen, Entfernung, Waffe und HS darunter. Keine Ellipsis;
  lange Texte umbrechen, Zeilenflächen und Abstände wachsen mit. Weiterhin
  maximal sechs Einträge, 4,5 Sekunden volle Deckkraft und 1,5 Sekunden Fade.
- Neu in r6: Entfernung bei allen Kill-Richtungen, auch anderen Bot-Kills und
  eigenem Tod, beispielsweise `USEC → BEAR · 42 m · MP5`. Unbekannte/ungültige
  Messung erscheint als `? m`; echte null Meter als `0 m`. Gemeint ist der direkte
  Abstand zwischen Angreifer und Opfer beim Tod, nicht die Flugstrecke der Kugel.
- Neu in diesem r6-Stand: Beide Killfeed-Namen verwenden dieselben Einstellungen
  `Effects/FriendlyTracerColor` und `Effects/OpponentTracerColor` wie Tracer und
  Teamlaser, standardmäßig Cyan und Rot. Maßgeblich ist die bestätigte Zugehörigkeit
  im aktuellen Match, nicht der Rollen-/Fraktionstext: solo ist nur der lokale
  Spieler freundlich, alle anderen bestätigten Arena-Akteure sind Gegner; im
  unabhängigen TDM gehören eigene Bots zum freundlichen Team. Im PFT-Pfad zählen
  die für das Match eingefrorenen exakten Teams; unbekannte Zugehörigkeit bleibt
  neutral grau. Die Namen bleiben auch bei ausgeschalteten `TeamTracers` und
  `TeamLasers` farbig. Jeder Eintrag hält die bei seinem Ereignis erfassten Farben;
  Einstellungsänderungen gelten nur für neue Einträge. Scoring ist unverändert.
- Zusätzlich in diesem Build: Die Granatenzahlen stehen in der Magazinzeile des
  Waffenpanels, Magazine links, Granaten rechts (Splitter ganz außen). Das
  Panel ist wieder 280×132; nur eine zu volle Zeile schiebt die Granaten in
  eine zweite Zeile (Panel 160 hoch) und behält das bis zum nächsten Respawn.
  Die Zählung selbst ist unverändert.
- Die separat gelieferten Hitmarker-Sounds bleiben erhalten. Im gemeinsamen
  Quellstand wurde dazu nur der Beschreibungstext der Audiooption geändert.
  Weder Sounddateien austauschen noch ihre Auswahl als Killfeed-Test behandeln.

Vor dem frischen Spielstart und nach Abschluss den tatsächlichen Client-Hash
prüfen; Repository-HEAD und Server-DLL-Hash ebenfalls protokollieren. Spätere
reine Dokumentationscommits dürfen den HEAD erhöhen; der Quellcommit bezeichnet
den DLL-Build. Die DLL muss exakt passen. Die Lieferung auf D:\SPT41 beweist
keine erfolgte Spiegelung auf C:\SPT41. Bei Abweichung blockieren und nichts
ersetzen. Insbesondere nicht den alten bereitgestellten `0DA0F70F...` und nicht
den ersten r6-Build `41F26B5D...` oder den Granatenzeilen-Build `DD25D2AE...` verwenden.

Falls r6 bereits unter einem früheren Hash begonnen wurde: bisherigen Lauf samt
Rohbelegen erhalten und den neuen Build nach frischem Spielstart als getrennten
Durchlauf mit eigenem Zeitstempel führen. Im r6-Bericht beide Stände klar getrennt
ausweisen; kein früheres Bestehen oder Bild dem neuen Hash zuordnen.

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
  zurückstellen. Aktuelle Werte von `FriendlyTracerColor`, `OpponentTracerColor`,
  `TeamTracers` und `TeamLasers` vor dem Farbtest sichern und danach wiederherstellen.
  Bereits vorhandene Hitmarker-Sounds unverändert lassen.
- Falls das bekannte winzige Startfenster wieder auftritt: zunächst Zustand
  sichern, höchstens eine native Alt+Enter-Wiederherstellung wie in r4 versuchen.
  Keine Grafikdateien editieren und keine endlosen Umschalt-/Neustartserien.
  Tatsächlich verbleibende Grafikänderungen dokumentieren.

Rohbelege unter
`artifacts/gpu-panels/arena-panels-native-ui-20260911-r6/<Zeitstempel>/` ablegen:
Teststand, Screenshots mit Zeiten/Auflösung, relevante Logs, private Sicherungen
und Einstellungen-/Profilvergleich. Nichts davon committen. Clientzeiten sind
lokal, Serverzeiten UTC; Dateinamen können UTC sein. Keine doppelten Fensterbilder
als unabhängige Zeitpunkte zählen. Vorher-/Nachherbaseline nur für den jeweiligen
Arena-Raid vergleichen und Belege je Raid getrennt kennzeichnen; kein Hauptprofil und keine
normale PMC-Baseline damit vermischen.

## Raid 1: Distanz und Solo-Zuordnung

Endless Deathmatch, Factory tagsüber, volle Karte, ohne PFT, vorhandene Kits.
Höchstens fünf Minuten aktive Testzeit. Der zweite kurze Raid unten prüft die
Teamfarben an zwei wirklichen Teams. Kein erneuter vollständiger
Zwei-Arena-plus-PMC-Durchlauf: r4 hat Menü-/Panel-Rückkehr,
Listen-/Header-Unterdrückung samt normaler Wiederherstellung und den Abstand
bei 1920×1080 bereits belegt. r5 belegt zusätzlich vollständige andere-Kill-
Meldungen, zwei getrennte Zeilenflächen und den ununterbrochenen Granatenbestand
2 → 1. Diese früheren Ergebnisse bleiben auf ihren jeweiligen Build begrenzt.

| Fall | Vorgehen | Erwartung und Beleg |
|---|---|---|
| K01 Andere Kills mit Distanz | Mehrere natürlich auftretende Bot-Kills vor heller und dunkler Szene aufnehmen. | Vollständiger Angreifer und vollständiges Opfer mit Pfeil, darunter Rollen und eine numerische Meterangabe vor der Waffe. Kein generelles Fehlen der Entfernung oder ausschließlich `? m` trotz normaler lebender Beteiligter. Kein „…“ oder abgeschnittenes Ende. Ohne unabhängige Positionsmessung nur Sichtbarkeit/Plausibilität der Zahl belegen, keine exakte geometrische Messgenauigkeit behaupten. |
| C01 Solo-Farben mitbeobachten | In den K01-Aufnahmen beide Namen mit den protokollierten Farbeinstellungen vergleichen. | Bestätigte andere Arena-Akteure tragen beide die Gegnerfarbe (Standard Rot), auch wenn ihre Rollen USEC/BEAR lauten oder der eigenen Fraktion entsprechen. Der lokale Name trägt die freundliche Farbe (Standard Cyan), aber nur ein tatsächlich beobachteter eigener Kill/Tod belegt diesen Unterfall. |
| K02 Eigener Kill, zusätzlich | Wenn über dokumentierte native Eingaben möglich, einen echten Gegner eliminieren und Meldung aufnehmen. | Der tatsächliche eigene Profilname steht vor dem Opfer; Entfernung mit Einheit vor der Waffe lesbar. Ohne echten eigenen Kill bleibt dieser Fall blockiert, nicht aus K01 ableiten und keine längere Schuss-/Bewegungsserie nur dafür erzwingen. |
| K03 Umbruch und Größenwechsel | Unterschiedlich lange Meldungen und mindestens zwei gleichzeitige Einträge beobachten. Bei natürlich auftretenden langen Namen/Details deren Umbruch und später kürzere Einträge aufnehmen. | Jeder Text vollständig auf seiner dunklen Fläche; Folgeeinträge darunter ohne Überlappung. Kein winziges Herunterskalieren, keine abgeschnittenen Unterzeilen. Kürzere wiederverwendete Einträge dürfen keine riesigen Leerflächen behalten. Nicht eindeutig beobachtete Wiederverwendung offenlassen. |
| K04 Ablauf und Menü | Bei sichtbarem Killfeed Inventar und Pause öffnen; einmal länger als sechs Sekunden offen halten und zurückkehren. Natürliches Verschwinden im Spiel ebenfalls aufnehmen. | Arena-HUD in nativen Menüs verborgen. Alte abgelaufene Einträge kehren nicht zurück; währenddessen neu entstandene Meldungen dürfen entsprechend ihrem eigenen Alter sichtbar sein. Text und Fläche verschwinden gemeinsam, kein leerer Balken. Nach Rückkehr korrekte Panelwerte und unveränderte Scoreposition. Zeitverhalten nur so genau behaupten, wie Aufnahmen es belegen. |
| P01 Magazin- und Granatenzeile | Nur in den ohnehin entstehenden Aufnahmen mitprüfen; keinen eigenen Granatenwurf und keine Kitsuche dafür erzwingen. | Magazinsymbole links, Granatensymbol(e) mit Zahl rechts in derselben Zeile, ohne Berührung oder Überlappung; Splitter ganz rechts. Waffenpanel niedriger als in r5, Abstand zum Killfeed gleich oder größer. Beim Halten einer Granate bleiben die Granatenzahlen rechts, das Panel springt nicht. Eine umgebrochene zweite Granatenzeile nur protokollieren, wenn sie natürlich auftritt. |
| K05 Eigener Tod / unbekannte Distanz / volle Belegung | Nur bei natürlichem Auftreten: eigene Todesmeldung, unbekannte Distanz oder sechs gleichzeitig lebende Einträge aufnehmen. Nach natürlichem Respawn kurz prüfen. | Eigener tatsächlicher Name als Opfer und Distanz mit Einheit erhalten. Bei nicht mehr ermittelbarer Position darf `? m` erscheinen, keine erfundene Zahl. Keine überlappenden Flächen oder Duplikate nach Respawn. Keine künstliche Bot-/Körperbereinigung für den unbekannten Fall. Nicht aufgetretene Unterfälle bleiben offen; Aufgeben ist kein automatischer Killfeed-Todesbeleg. |

K01 ist das Distanz-Hauptziel; C01 nutzt dieselben Aufnahmen. K02–K05 ergänzen es
nur, soweit im selben kurzen Raid einfach möglich; die offenen r5-Unterfälle
bleiben bei fehlendem Beleg ausdrücklich
offen. Keine zusätzlichen Raids für seltene Unterfälle und keine erneute komplette
Granatenprüfung. Nach K01 noch einen kurzen Inventar-/Rückkehrblick aus K04 als
Regressionstest durchführen; exaktes Menüeintritts-/Fade-Timing nicht erzwingen.

## Raid 2: Teamfarben im unabhängigen TDM

Ein kurzer unabhängiger Team-Deathmatch-Raid auf Factory tagsüber, volle Karte,
ohne PFT, vorhandene Kits; höchstens fünf Minuten aktive Testzeit. Das ist der
gezielte zusätzliche Farbtest, kein erneuter vollständiger Panel-/Profiltest.
Gewählte eigene Seite und tatsächlich gestartete Teams anhand nativer
Teaminformationen bzw. vorhandener Match-/Spawn-Logs festhalten. Zugehörigkeit
nicht aus der Killfeed-Farbe selbst oder allein aus dem Rollenlabel ableiten.
Sind Akteure nicht eindeutig zuordenbar, diesen Teil ausdrücklich offenlassen.

| Fall | Vorgehen | Erwartung und Beleg |
|---|---|---|
| C02 Zwei tatsächliche Teams (Hauptziel) | Natürlich auftretende Kills zwischen einem bestätigten eigenen Bot und einem bestätigten Gegner aufnehmen. Beide Namen prüfen; die umgekehrte Killrichtung nur bei natürlichem Auftreten ergänzen. Für diese Beobachtung `TeamTracers` und `TeamLasers` nativ ausschalten und die beiden Schalter samt gewählter Palette dokumentieren; bei nicht bedienbaren Schaltern nur diesen Unterfall offenlassen. | Eigener Bot in freundlicher Farbe, Gegner in Gegnerfarbe, unabhängig davon, wer Angreifer oder Opfer ist. Bei Standardpalette also Cyan/Rot. Beide Namen vollständig und gut lesbar, Metadaten/Distanz und Scoreposition weiterhin korrekt. Die farbigen Namen funktionieren bei ausgeschalteten Tracern/Lasern; sichtbare Tracer oder Laser sind kein erforderlicher Beleg. Eigener lokaler Kill/Tod nur als bestanden ausweisen, wenn wirklich beobachtet. |
| C03 Palette und bestehende Einträge (optional) | Falls in diesem Raid nativ einfach möglich: nach einem belegten Eintrag `FriendlyTracerColor` auf Green und `OpponentTracerColor` auf Magenta stellen, anschließend einen neuen natürlichen Eintrag aufnehmen. Anfangswerte danach zurückstellen. Kein weiterer Raid oder künstlicher Kill dafür. | Neue freundliche Namen grün, neue Gegnernamen magenta. Vor der Änderung erfasste Einträge behalten ihre bisherigen Farben bis zum Ablauf. Bleibt beim Schließen des Menüs kein alter Eintrag sichtbar, ist nur der Farbwechsel neuer Einträge belegt; die unveränderten alten Einträge bleiben offen. Zeiten und Einstellungswerte pro Aufnahme angeben. |

Nach C02 kurz Inventar öffnen und zurückkehren; HUD verbirgt sich und kehrt ohne
Restflächen zurück. Keine weitere Menü-/Fade-Zeitserie erzwingen. PFT,
unbekannte/graue Akteure, eigene Kills/Tode oder beide Killrichtungen nur dann
als geprüft nennen, wenn der konkrete Fall tatsächlich beobachtet wurde.
Insbesondere beweist unabhängiges TDM keine PFT-Kompatibilität. Kein dritter
Raid und keine Manipulation für fehlende oder seltene Unterfälle.

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

Jeden Arena-Raid regulär verlassen, Menü ohne HUD-Reste aufnehmen und die
vorgesehenen Profilfelder je Raid vor/nach vergleichen. Eigene Prozesse regulär
beenden; eigene Testeinstellungen zurückstellen, fremde Änderungen erhalten. Client-/Serverhash
und Einstellungssicherungen abschließend vergleichen.

Schreibe `GPU-PANELS-R6-RESULTS-2026-09-11.md`: tatsächlicher Stand, je Fall
bestanden/fehlgeschlagen/blockiert/nicht ausgeführt, konkrete Beobachtungen und
Belegpfade, verbleibende Grenzen. K01 (Distanz) und C02 (Teamfarben) als getrennte
Hauptziele bewerten; C01/C03 und optionale Unterfälle nicht pauschal mitbestehen
lassen. Je Farbaufnahme Match, Beteiligte/Zuordnungsbeleg, Palette und Zustand
der beiden Effektschalter nennen. Bei schon vorhandenem r6-Bericht nur den neuen,
klar abgegrenzten Durchlauf ergänzen; frühere Beobachtungen nicht umschreiben.
Keine privaten Profilkennungen, Geheimnisse, kompletten Profile oder Rohlogs in
den Bericht. Ist das Repository frei von fremden Änderungen, ausschließlich
diesen r6-Bericht committen mit
`Co-authored-by: Codex <codex@openai.com>`. Andernfalls Bericht belassen und fremde
Änderungen unangetastet lassen. Kein Push und keine alten Berichte überschreiben.
