# Teilnehmerzahl pro Karte

Stand: 2026-09-21. Gesamtzahl einschließlich des lokalen Spielers, auch während
seines Todes/Respawns. Native Spieltests stehen für diese Änderung noch aus.

## Menü und Formate

Der native Kartenbildschirm zeigt unter der Bereichsauswahl Minus/Wert/Plus
und eine Empfehlung. Die Werte werden pro nativer Karten-ID gespeichert;
Deathmatch/Gun Game teilen sich eine Einstellung, unabhängiges Team-DM hat eine
separate. Factory Tag/Nacht sind eigene Einträge. Nicht über den periodischen
Server-Settings-Abgleich gesteuert: die Auswahl gehört diesem Client.

| Karte | Vorgabe insgesamt |
|---|---:|
| Equator | 4 |
| Bay5 | 8 |
| Factory Tag/Nacht | 10 |
| Labs | 16 |
| Labyrinth | 8 (aktualisiert am 2026-10-19) |

Vorgaben sind Empfehlungen, keine Kartenobergrenzen. Deathmatch/Gun Game erlauben
2–24, ganze Karten im Team-DM gerade 4–24. Team-DM zeigt zusätzlich das Format,
z. B. 8 (4v4). Die optionalen MapPort-Karten bleiben auf Deathmatch/Gun Game
beschränkt. Customs-Areale verwenden weiter ihre vorhandenen Teamstarts und
Bereichsgrößen; Tactical/PitFireTeam behalten ihre bisherigen Formate.

Die Einstellung wird vor dem nativen Laden eingefroren und exakt vom Server
bestätigt. Fehlende/falsche Zahlen, JSON-Strings statt Zahlen oder fehlende
Boss-Zusagen brechen den Start geschützt ab. Ein alter Server kann daher eine
neue Menüauswahl nicht versehentlich mit unkontrollierten Wellen starten.
Änderungen während des Ladens beeinflussen die laufende Besetzung nicht.
Ältere direkte Requests ohne `participants` behalten dagegen ihren alten Vertrag.

## Population und Bosse

Ausgewählte Deathmatch-/Gun-Game-Raids erhalten einen leeren, eigenen Raid-Klon
ohne native Wellen oder zusätzliche ABPS-Pumpen. Arena reserviert einzelne
Botplätze vor der asynchronen Profilerzeugung und füllt nach bestätigten Toden
nach. Es zählen gesunde PreActive- und Active-Bodies, keine Leichen. Der Mensch
behält seinen Platz während des Respawns. Die bisherigen Burst-Untergrenzen und
Überbuchungen bestimmen für diese Raids nicht mehr die Teilnehmerzahl; die
zeitabhängige PMC-/Schwierigkeitsverteilung bleibt erhalten.

Ein bereits nativ übergebener, noch ungeklärter Spawn bleibt reserviert. Es gibt
keinen Timeout, der denselben Platz ein zweites Mal ausgibt. Fehlgeschlagene
Entfernungen werden ebenfalls weiter berücksichtigt. Das bevorzugt eine
sichtbare Unterbesetzung bei einem wirklichen nativen Aktivierungsfehler gegenüber
unbemerktem Überschreiten der gewählten Zahl. Das Log meldet die Besetzung und
offene native Reservierungen regelmäßig.

Bei im Server aktivierten Labyrinth-Bossen gehört **ein Bossplatz** zur Gesamtzahl:
zuerst Shadow of Tagilla, nach dessen Tod Killa, danach wieder Tagilla. Keine
zusätzlichen Escort-/Helper-Wellen und kein Killa-Timer außerhalb der gewählten
Zahl. Die Bosse erhalten die native Boss-Initialisierung ohne zusätzliche Follower.
Die serverseitige Boss-Konfiguration wird mit dem Armieren eingefroren.

## Sicherheit und Prüfung

Die unveränderte Snapshot-/End-Raid-Protektion bleibt maßgeblich. Neue ganze
Karten mit Teilnehmerauswahl verlangen zusätzlich dieselbe erfolgreich veränderte
Kloninstanz bei der nativen Antwort. Vor der Client-Aktivierung werden Karte,
Caps, leere Wellen und geschlossene Pumpen erneut geprüft. Normale Raids bleiben
außerhalb sämtlicher neuer Verhaltensänderungen.

Die unabhängige Gegenprüfung fand zwei native Randfälle: fehlende Boss-Logik bei
direkter Aktivierung sowie vorzeitig freigegebene Reservierungen nach einem
zweiten Callback und misslungener Entfernung. Beide wurden vor Auslieferung
behoben und durch Fehlerkontrollen abgedeckt. Weitere Details und abgegrenzte
Nachweise: `PARTICIPANT-MENU-REVIEW.md`, `PARTICIPANT-SERVER-REVIEW.md` und
`SOLO-POPULATION-REVIEW.md`. Die Integration prüft strikte Antworttypen,
Einfrieren trotz Einstellungsänderung und tatsächliche symmetrische TDM-Besetzung
mit `tools/ParticipantCountChecks/Verify.ps1`.

## Ausstehender Spieltest

Client **und Server** nach Einspielen neu starten. Menü auf Equator zunächst 4,
dann z. B. 6 wählen; zu Factory und zurück wechseln sowie einmal den Client
neu starten, um die Speicherung zu prüfen. Erwartet sind 3 bzw. 5 gleichzeitig
lebende/aktivierende Bots plus der reservierte Spielerplatz. Mehrere Bot-Tode
und eigene Respawns prüfen; eine verzögerte Aktivierung darf nicht zu Extras führen.

Labyrinth mit aktivierten Bossen: Boss zählt mit; nach Tagilla kommt Killa, ohne
zusätzliche nativen Bosswellen. Factory/Labs Team-DM etwa 4 und 12 Teilnehmer:
2v2 bzw. 6v6, eigene Seite jeweils ein Bot weniger wegen des Spielers. Tactical,
begrenztes Customs und gewöhnlicher PMC-/Scav-Raid dienen als Kontrollen.
Logs einschließlich der Population-Zeilen sichern. Kompilierung, kontrollierte
Doubles und IL-Verträge beweisen weder native Harmony-Ausführung noch sichtbare
UI, hörbare Ansagen oder Boss-KI im Raid.
