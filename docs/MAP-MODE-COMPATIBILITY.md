# Karten und Modi – Stand 13.11.2026

Die gemeinsame Auswahlprüfung gilt im Menü, beim Armieren, bei Wiederholung und
in der Server-Vorbereitung. Historische pauschale Sperren sind entfernt; konkrete
Grenzen der Spawn- und Arealverwaltung bleiben erhalten. Tabelle am 31.10.2026 um
Bowl, Iceberg und Domination, am 12.11.2026 um Chop Shop ergänzt, am 13.11.2026 bei Tactical auf Equator, Iceberg
und Chop Shop nach den neuen Botpunkten von ArenaMapPort (Stand 12.11.2026) neu entschieden, am 13.11.2026 bei Domination um Labs,
Labyrinth und die drei Customs-Areale erweitert; der übrige Text ist
der Stand vom 17.10.2026, wo nicht anders vermerkt.

| Karte | Deathmatch / Gun Game | Team DM | Domination | Tactical solo / PFT | Zombies solo / PFT |
|---|---|---|---|---|---|
| Factory Tag/Nacht | Ja | Ja | Ja, drei von acht Punkten je Match | Ja | Ja |
| Labs, Labyrinth | Ja | Ja | Ja, Labs drei Punkte (A/B/C, Ebene 1), Labyrinth ein Punkt (X); ohne PitFireTeam | Ja (ohne Kontrollpunkt) | Ja |
| Bay5, Air Pit | Ja | Ja, Teamboxen 2v2–10v10 | Ja, Bay5 ein Punkt, Air Pit zwei | Ja | Ja |
| Bowl | Ja | Ja, Teamboxen 2v2–10v10 | Ja, zwei Punkte | Ja | Ja |
| Iceberg (Karte in ArenaMapPort 0.5.0; die 17 Botpunkte für Tactical nur im unveröffentlichten Build 0.5.0+160523d) | Ja | Ja, Teamboxen 2v2–10v10 | Ja, drei Punkte (A/B/C) | Ja / Ja: 17 Botpunkte, in der alten Kartenmitte mindestens neun über 25 m (bis 12.11.: Nein) | Ja / Ja |
| Chop Shop (Karte und Botpunkte nur im unveröffentlichten Build 0.5.0+160523d) | Ja | Ja, Teamboxen 2v2–10v10 | Ja, ein Punkt (X) | Solo ja; PFT nein: in der Hofmitte nur drei bis fünf entfernte Botpunkte (bis 12.11.: Nein) | Solo ja; PFT nein: zu wenig Platz für den Fünfertrupp mit Zombie-Welle |
| Equator (Tactical: 16 Botpunkte nur im unveröffentlichten Build 0.5.0+160523d) | Ja | Ja, Teamboxen 2v2–10v10 | Ja, ein Punkt | Ja / Ja: 16 Botpunkte, neun über 25 m vom Atrium, überall mindestens sechs (bis 12.11.: Nein) | Solo ja; PFT nein: zu wenig Platz für den Fünfertrupp mit Zombie-Welle |
| Customs, ganze Karte | Nein | Nein | Nein | Nein | Nein |
| Drei begrenzte Customs-Areale | Ja, mit Teilnehmerzahl | Ja, 2v2–5v5 | Ja, je Areal drei Punkte (A/B/C), 2v2–5v5; ohne PitFireTeam | Ja, standardmäßig 1v5 / PMC + 1–4 Begleiter gegen fünf | Ja, solo / PMC + 1–4 Begleiter |

Domination gibt es nie mit PitFireTeam, auf ganzen Karten und in den drei Customs-Arealen (Labs, Labyrinth und die Areale seit
13.11.2026, Messwerte und Ablauf in docs/reviews/DOMINATION-VANILLA-MAPS-20261113.md; die ganze Customs-Karte bleibt gesperrt;
im Areal gilt die Teamgröße 2–5 wie im Team DM); sie braucht
BigBrain und Arenas Begleitbibliothek. Auf den portierten Karten nutzt sie dieselben
Teamboxen und Spawn-Tore wie das unabhängige Team DM.

PFT setzt weiterhin die unterstützte Version, 1–4 eingeladene Begleiter und die
geschützte Ausrüstung voraus. Portierte Karten brauchen ArenaMapPort auf Client
und Server; individuelle Karten-Allowlisten bleiben verbindlich. Andere EFT-Karten
werden durch diese Änderung nicht freigegeben.

Für ganze Karten verwenden die Modi überwiegend die bestehenden generischen
Spawnpunkte. Ausnahme: Equator-Team-DM nutzt die ursprünglichen Nord-/Süd-Teamboxen
mit eigenen Arena-Markern für Einstieg und Respawns; siehe
[Equator-Teamstarts](reviews/EQUATOR-TEAM-SPAWNS-REVIEW.md). Auf den übrigen ganzen Karten
braucht Team DM keine vermessenen Fraktionsstarts wie die begrenzten Areale.
Mit aktueller ArenaMapPort-Version bleiben Equators originale Türen während
Vorbereitung und Countdown geschlossen. Der Kampf beginnt erst nach Öffnung
und erfolgreicher Wegeprüfung; bei laufenden Respawns bleiben die Türen offen.
Siehe [Spawn-Tore und Vorbereitungsschutz](reviews/EQUATOR-SPAWN-GATES-20261017.md).
Bay5 hat auch abseits der Spielerstarts genügend Punkte für den Fünfertrupp.
Equator hatte bis 12.11.2026 zwar genügend Punkte am ursprünglichen Spielereinstieg, aber nur vier
mindestens 25 m entfernte Botpunkte vom Atrium (0, 0.2, 2); ein überlebender
PFT-Spieler bleibt beim Rundenwechsel am Ort, deshalb war Tactical gesperrt. Seit dem 13.11.2026
ist Tactical auf Equator solo und mit PFT freigegeben: ArenaMapPort hat am 12.11. Botpunkte
hinzugefügt (16 statt 10), vom Atrium liegen jetzt neun mindestens 25 m entfernt, an jeder Stelle
der Karte mindestens sechs. Der Sicherheitsabstand von 25 m bleibt unverändert und wird nicht
durch erfundene Startpunkte umgangen. Das setzt einen ArenaMapPort-Build mit diesen Botpunkten voraus (unveröffentlicht, 0.5.0+160523d; das veröffentlichte 0.5.0 hat zehn Botpunkte auf Equator).

Zombies mit PFT sind auf Equator ebenfalls ausgeschlossen: Der kompakte Spielraum
ist für den Fünfertrupp zusammen mit den Zombies zu eng. Solo-Zombies bleiben
freigegeben. Die PFT-Sperre gilt auch mit weniger als vier Begleitern. Diese
gemeinsame Auswahlregel gilt auch für gespeicherte Auswahlen,
wiederholte Arm-Anfragen und Server-Vorbereitung; eine Equator-only-Allowliste
bietet folglich keinen PFT-Modus an. Normale PMC-Raids bleiben unverändert.

`ArenaAreaRuntime` aktiviert Wände, Navigation und reservierte Respawns in allen
Arena-Modi. Erst nach der erfolgreichen Navigationsprüfung beginnen Match,
Modus-Timer und Ausrüstungswechsel. PFT wartet ebenfalls und reserviert für seine
1–4 Begleiter getrennte, überprüfte Punkte innerhalb der Kontur. Der native
PFT-Ablauf und die authentifizierten Profile bleiben erhalten.

Unabhängiges Team DM zeigt die Teamgröße: auf Customs 2v2–5v5, in Equators
Teamboxen bis 10v10. Deathmatch und Gun Game behalten ihre Teilnehmerzahl.
Tactical und Zombies verwalten ihre Besetzung selbst.
Auf Customs bleibt eine gültige gespeicherte Kontur erhalten. Eine alte
Ganzkarten-Präferenz wird bei der Menüauswahl auf das erste Areal umgestellt;
ein endgültiger Raid-Start ohne gültige Customs-Kontur wird abgelehnt. Normale
PMC-Raids sind davon nicht betroffen.

Bay5, Equator und Air Pit besitzen gültige KI-Anker, aber keine nativen Deckungspunkte.
Der optionale PFT-Adapter ermittelt deshalb in genau diesen geschützten Arena-Raids
den nächstgelegenen vorhandenen Anker direkt. Er verändert weder PFT-Dateien noch
Kartengeometrie und erfindet keine Deckungspunkte. Die frühere separate Tactical-Sperre
auf Equator gilt seit dem 13.11.2026 nicht mehr (siehe oben); die PFT-Zombie-Sperre bleibt, auch wenn weniger als vier Begleiter eingeladen sind.

Air Pit verwendet seine vier vorhandenen Spieler- und (seit dem ArenaMapPort-Build vom 12.11.2026) 20 Bot-Spawns (zuvor 14). Eine neue
Kartenstandard-Migration ergänzt nur erkannte frühere Standardlisten; individuelle
Listen und ausdrücklich entfernte oder deaktivierte Karten bleiben erhalten.
Die ursprüngliche nördliche Air-Pit-Teambox liegt außerhalb der aktuellen
Navigationsfläche und wird nicht als fester Teamstart übernommen. Team DM und
Tactical verwenden hier zunächst die allgemeinen Spawnverfahren. Die Freigabe
umfasst kein neues Pickup-Layout; siehe [Air-Pit-Integration](reviews/AIRPIT-INTEGRATION-20261017.md).

Nachtrag 31.10.2026: Air Pit nutzt inzwischen seine ursprünglichen Nord-/Süd-Teamboxen
für Team DM (ArenaMapPort 0.2.2 oder neuer, siehe
[Air-Pit-Teamboxen](reviews/AIRPIT-TEAM-BOXES-REVIEW.md)); der Absatz oben ist der Stand
vom 17.10.

Bowl ist in allen Modi freigegeben; Team DM und Domination nutzen die ursprünglichen
Boxen USEC Ost / BEAR West hinter den Spawn-Toren. Siehe
[Bowl-Freigabe](reviews/BOWL-INTEGRATION-20261023.md) und
[Bowl-Teamboxen](reviews/BOWL-TEAM-BOXES-20261023.md).

Iceberg braucht einen ArenaMapPort-Build, der `arena_iceberg` mitbringt; das veröffentlichte
ArenaMapPort 0.5.0 (07.11.2026) tut das, 0.4.0 hat kein Iceberg. ArenaMode erkennt die Karte nur an der
vorhandenen, aktivierten Location, nie an der Version. Nur die 17 Botpunkte, die Tactical freigeben, brauchen
den unveröffentlichten Build 0.5.0+160523d; er trägt dieselbe Versionsnummer 0.5.0 wie das veröffentlichte Paket.
Tactical war auf Iceberg solo und mit PFT gesperrt: Von etwa 48 m² in der Kartenmitte
aus lagen nur vier der neun Botpunkte weiter als 25 m entfernt. Seit dem 13.11.2026 ist es
solo und mit PFT freigegeben: Der ArenaMapPort-Build vom 12.11. bringt 17 Botpunkte, in der
alten Kartenmitte liegen mindestens neun weiter als 25 m entfernt (auf dem Netz mindestens zehn).
Zombies (solo und PFT) bleiben freigegeben, die Karte ist nicht eng. Der Tactical-Kontrollpunkt
gilt hier wie auf Equator und Chop Shop über die vorhandenen Domination-Punkte (Iceberg: einer
von A/B/C je Runde). In den nativen Läufen vom 13.11.2026 wurde der Punkt auf allen drei Karten in jeder
Runde gesetzt und angesagt; ein Halten des Punkts ist nicht gelaufen (die Testzeiten beenden die Runde, sobald
der Punkt erscheint).
Team DM und Domination nutzen die CheckPoint-Taschen USEC West / BEAR Ost, auf die
geschlossenen Tore zugeschnitten; Domination hat drei Punkte A/B/C. Der native
GPU-Lauf vom 31.10.2026 steht im [Bericht](reports/2026-10/GPU-ICEBERG-20261031.md); Tactical lief am
13.11.2026 solo und mit PFT ([Bericht](reports/2026-11/GPU-SPAWN-ROWS-20261113.md)).
Siehe [Iceberg-Integration](reviews/ICEBERG-INTEGRATION-20261031.md).

Chop Shop (ArenaMapPort-ID `arena_autoservice`) braucht einen ArenaMapPort-Build, der diese Karte
mitbringt; das ist derzeit nur der unveröffentlichte `feature/autoservice`-Build, für die Botpunkte unten
0.5.0+160523d (12.11.2026).
Das veröffentlichte 0.5.0 hat kein Chop Shop und dieselbe Versionsnummer; ArenaMode erkennt die
Karte nur an der vorhandenen, aktivierten Location, nie an der Version. Tactical ist seit dem
13.11.2026 solo freigegeben und mit PFT gesperrt: Die Karte hat 16 Botpunkte (bis 12.11. sieben,
damals war Tactical ganz gesperrt); von jedem Spielerstart liegen 11 bis 13 weiter als 25 m entfernt,
von der Hofmitte fünf, mindestens drei in x -377..-373,5, z 485..489. Solo braucht nie genau fünf (ein kleinerer
oder späterer Trupp ist möglich; der Trupp wird angefordert, bevor der Überlebende zur Rundenpause auf einen
Spielerstart versetzt wird, und seine Punkte werden gewählt, wenn die Profile eintreffen; in den nativen Läufen
vom 13.11. war die Versetzung dann schon geschehen, der Code garantiert es nicht); ein
PFT-Anführer bleibt stehen, und fehlende Punkte beenden dort das ganze Match ohne Ergebnis.
Zombies mit PFT sind gesperrt (die zusammenhängende Lauffläche hat nur 1.916 m², dazu
1–2,4 m enge Gänge an den Toren); solo bleiben Zombies freigegeben. Team DM und Domination nutzen
die ursprünglichen Taschen USEC Ost / BEAR West; Domination hat einen Punkt (X). Standard sind
acht Teilnehmer (bis 12.11. sechs, als es sieben Botpunkte gab). Der native Lauf auf GPU-2 am 12.11.2026 (Menü, Deathmatch,
Team DM 4v4 und 10v10 mit gehaltenen Toren, Domination, Zombies solo, PMC-Kontrolle) lief ohne Fehler;
die Boxhöhe (−4,8 bis −1,0) und der Domination-Punkt (−369,290 / −3,805 / 499,490) haben gehalten. Der
Tor-Watchdog beträgt 15 s. Die beiden bekannten Grenzen dieses Laufs (Domination setzte nur 5 von 6 Pickups,
in der Hofmitte gab es eine tote Stelle für Zombie-, Deathmatch- und Gun-Game-Spawns) kamen von den sieben
Botpunkten und sind mit den 16 Botpunkten weg: Auf GPU-2 am 13.11.2026 setzte Domination 6 von 6 Pickups,
solo Zombies aus der Hofmitte füllten 32 von 32. Solo-Tactical lief am 13.11.2026 über drei Runden. Nicht gelaufen sind unter anderem PFT, Gun Game und ein zu
Ende gespieltes Match. Beide Ausgänge
liegen seit dem 12.11. in den Teamtaschen; ArenaMode deaktiviert die Extraktion, die Flares markieren nur die Taschen. Siehe
[Chop-Shop-Integration](reviews/CHOP-SHOP-INTEGRATION-20261112.md), den
[nativen Bericht](reports/2026-11/GPU-CHOP-SHOP-20261112.md) und die
[Folgeentscheidung zu den neuen Botpunkten](reviews/SPAWN-ROWS-FOLLOWUP-20261113.md).

Quellprüfung, Daten und Offline-Nachweise: `CUSTOMS-ALL-MODES-REVIEW.md`,
`PFT-PORTED-MAPS-REVIEW.md` und die ursprüngliche Analyse `MAP-MODE-ACCESS-REVIEW.md`.
Native Abnahme ist separat im aktuellen GPU-Bericht/Handoff dokumentiert; die
Freigabematrix behauptet keinen vollständigen Dauertest aller Kombinationen.

Hinweis für Testautomatisierung: Die externe UiBridge-`gpu-run raid`-Tabelle vom
17.10.2026 kennt AirPit noch nicht. Neue Kombinationen bis zu deren
Aktualisierung über die echten Menübuttons auswählen; ihre alte Ablehnung ist
keine Arena-Laufzeitsperre. Bay5-Zeiten und Equator-Wetter bleiben ArenaMapPort-Sache.
