# Akimbo-Pistolenprototyp — GPU-2, 05.10.2026

## Umfang und Bedienung

Implementierung `3c16748`: ausdrücklich vorläufiger PM-Test, standardmäßig AUS.
In einer laufenden Arena-Runde die **PM** halten und in F12 unter
**Arena akimbo prototype → Active** einschalten. Der Effekt läuft 20 Spielsekunden;
Tod, Waffenwechsel, Runden-/Matchende oder Ausschalten beenden ihn früher.
Nach jedem Ende steht Active wieder auf false. Keine automatische Reaktivierung.
Der PM (t), andere Pistolen, normale Raids und Hideout sind nicht freigegeben.

Eine sichtbare Kopie und ein zusätzliches natives Geschoss pro regulärem Schuss.
Ein gemeinsamer Abzug und Nachladezyklus; das Original verbraucht eine Patrone,
das Zusatzgeschoss ist für diesen Versuch kostenlos. Kein Anvisieren während
des Effekts. Beim Nachladen, Sprinten und anderen Interaktionen verschwindet die
Kopie und die linke Hand übernimmt wieder die normale Animation. Noch kein Pickup,
zweiter Ausrüstungsslot oder eigenständiges zweites Waffeninventar.

## Build und unabhängige Prüfung

Isolierter Client-Build aus sauberem `3c16748`, keine Server/Core/BigBrain-Änderung.
0 Fehler, vier bestehende Nullable-Warnungen. Lokaler und GPU-2-Client SHA256:
`656FBCEA0B75E88A7889359F223CD6672E28C594836609F3B33F97DD2E4BD6A7`.
Alle abschließend über UiBridge gemeldeten geladenen Plugin-/Library-Identitäten
stimmen mit Host-Dateien und lokalem Build überein. GPU-Prozess:
`5e8928ac21104d77883ed390934c7e47`, EFT 0.16.9.5, Unity 2022.3.43f1, UiBridge
0.7.4 (`8218b027`). 1920×1080; Factory Tag / Gun Game.

66 Assertions am produktiv eingebundenen Lebensdauer-/Schusszustand, fünf
negative Kontrollen (Körperbindung, Ablaufgrenze, Original-Veto, Bereitschaft,
Rekursion). Das isoliert weder Unity noch den gesamten Laufzeitadapter.
Adversariales Code-Review korrigierte Idle-Zulassung und strikte
Interaktionssperre vor dem Build. Abschließende unabhängige Sichtprüfung der
Screenshots akzeptiert den groben Prototyp ohne offenen blockierenden Befund.

## Beobachtet

- PM-Kopie mit 60 eigenen MeshRenderern sichtbar. Zwei getrennte Arme/Pistolen,
  keine sichtbaren doppelten Körperteile. `first-view` und `shots-view`.
- Zwei einzelne Abzüge: echtes Magazin von 8+1 auf 6+1; zwei erfolgreiche
  zusätzliche native Ballistikaufrufe im Log um 19:55:07/08. Die Bonusmunition
  erhält jeweils eine eigene ID. Nach Ablauf meldet die Instanz exakt zwei
  Zusatzgeschosse. Keine rekursive Vervielfachung.
- Nachladen zeigt die normale beidhändige PM-Animation ohne Kopie (`reload`),
  anschließend wieder 8+1. Sprintaufnahme ohne zweite Pistole (`sprint`).
  Im geöffneten Inventar meldet die Kopie Visible=false (`inventory-state.json`).
- Bei bestätigter aktiver Instanz bleibt IsAiming nach dem nativen ADS-Befehl
  false (`aim-active.json`). Vorläufig ausschließlich Hüftfeuer.
- Ablauf nach 20 Sekunden, manuelles AUS, Messerwechsel, Testtod/Respawn und
  geschütztes Raidende räumen die Instanz auf. `knife-state.json` und
  `death-state.json` enthalten keine gebundene Waffe/Kopie mehr; erneute
  Aktivierung nach Respawn erzeugt wieder einen Bonus-Schuss.
- `off-view` zeigt die ursprüngliche Pistole und normale Stützhand. Vor dem
  Raidende genau eine eigene Replikawurzel; nach Rückkehr ins Menü keine.
  Aktivierungsversuch im Menü wird zurückgewiesen und Active zurückgesetzt.

Ein Kampfschuss gegen einen sichtbaren Bot erzeugte einen nativen Hitmarker
und einen Bonus-Ballistikaufruf (`combat-hit`). **Die zusätzliche Trefferwirkung
wurde nicht separat vom Originalgeschoss isoliert.** Auch die anschließende
lesende Inspektion gepoolter Shot-Objekte liefert keinen stabilen Nachweis zweier
separater Treffer; diese Rohdateien gelten ausdrücklich nicht als Schadensabnahme.

## Grenzen

Die linke Hand verwendet noch die verschobene Stützhandpose; Griff und Finger
umschließen die zweite PM sichtbar nicht vollständig. Keine eigenen Animationen,
kein abwechselndes Schießen, kein separater Schussklang/Rückstoß/Munitionszähler.
Einzelbilder prüfen nicht die Flüssigkeit aller Übergänge. Wandunterdrückung des
Zusatzgeschosses, Malfunktionen, Vault/Prone/Blindfire, Körper-/Rundenwechsel in
allen weiteren Modi, Kill-/Assist-Zuordnung und eine normale PMC-Runde wurden
nicht jeweils separat nativ abgenommen. Die Schutzbedingungen sind im Code,
ihre Offline-Prüfung ersetzt diese späteren Spieltests nicht.

Die erste Runde vergab zufällig eine APB und wurde ohne Akimbo beendet. Für den
reproduzierbaren PM-Test wurden nur vorübergehend die ganze Waffenleiter und
clientseitig rung001 gewählt. GunGameLadder ist wieder leer, gunGameRungs wieder
24; der Serverwert wurde auf GPU und der synchronisierten lokalen Installation
zurückgesetzt. Alle zuvor vorhandenen Client-Einstellungen bleiben unverändert;
einziger neuer Eintrag ist Active=false. Godmode-Testhook, Tracking und Bewegung
sind beendet. Spiel und Server sind wieder aus, wie vor dem Test.

Nach geschütztem Verlassen sind Inventory, Health (ohne UpdateTime), Skills,
Quests, TradersInfo, Encyclopedia und Experience identisch zur exakten
SPT-GPU-2-Profilkopie vor dem Test. Keine Error-/Fatal-Zeilen im abschließenden
Logexport (3758 Zeilen). Die bekannte Warnung zu `load_kill`-PCM sowie Warnungen
zu fehlenden nativen Scripts und der Respawn-Culling-Bereinigung bleiben separat;
keine Akimbo-Warnung oder -Exception.

Rohbelege unter `artifacts/akimbo-gpu/`: Build-/Deploy-Identität,
Screenshots, Status/Inspektor, vollständiger Logexport, Profil-/Config-Vergleich.
Keine Rohprofile im Git. Kein Push. Vier Arbeitsgates für diesen begrenzten
Prototyp erfüllt; keine Abnahme eines fertigen Akimbo-Pickups.
