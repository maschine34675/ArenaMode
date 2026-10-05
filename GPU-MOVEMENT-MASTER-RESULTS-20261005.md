# Movement master — GPU-2, 05.10.2026

Umgesetzt: Bunnyhop-Standard AN (`f2e5a88`), FAST MOVEMENT als direkte Bedienung
von `Movement / Enabled` (`d30e3b0`), gekürzte Hilfetexte nach Sichtprüfung
(`91a90ef`). Kein neuer Gameplay-Schalter und keine zweite Konfiguration.

## Auslieferung und Prüfung

Finaler Client lokal und auf SPT-GPU-2:
`DF5B3A97F044E96CE54E27464A8DB26463CC475B2B2C459010A0BEA5A2EEE9C0`.
Server, Redeploy.Core und BigBrainCompat unverändert. Isolierter Build erfolgreich
(0 Fehler, vier bestehende Nullable-Warnungen). Menü-Harness: 846 Assertions,
14 negative Kontrollen; nach Textpolitur erneut 846 Assertions bestanden.
Unabhängiges adversariales Review beider Änderungen ohne offenen Befund.

Finaler GPU-Prozess `afffd8bb5df44ba9ab899e978b5cc376`: 24/24 geladene
Plugin-Identitäten stimmen mit den installierten Builds überein. Im nativen
1080p-Kartenmenü sind alle 14 Schalter sichtbar und lesbar. FAST MOVEMENT zeigt
nach Klick OFF; acht untergeordnete Bewegungsoptionen werden N/A, während
Backpack/Killcam/Tracer unverändert bleiben. Der Master bleibt bedienbar;
erneutes Einschalten stellt die gespeicherten Anzeigen wieder her. Geprüft
ohne Kartenauswahl und mit Factory-Thumbnail, Zeitwahl und nativer Navigation.
Keine Überlappung mit NEXT/READY. Die kurzen Hilfetexte sind vollständig sichtbar.

## Kurzer Arena-Test und Grenzen

Factory/Gun Game lief zuvor auf `d30e3b0`, Prozess
`faaada6a16944641b2a4491cb98da379`, Client
`C3E81DBCF1F1136FC270E890F6D488B811932B8E77810982D9C7C3248094EA9F`.
Die anschließende Änderung betrifft ausschließlich Beschreibungstexte.
Master AN → AUS → AN wurde zusätzlich über denselben BepInEx-Eintrag geprüft:
vom selben Außen-Spawn erreichte der erste Sprung mit AN ungefähr Y=1,8,
mit AUS Y=1,4 (Start jeweils Y=0,3). Bunnyhop besaß bei AN einen aktuellen
Flight-Zustand, bei AUS keinen; nach Wiederanschalten wurde dieser wieder
angelegt. Das ist ein kurzer Nachweis des bestehenden Master-Guards, keine
neue Abnahme jeder Bewegungstechnik. In diesem Lauf wurde keine erfolgreiche
Hop-Kette gemessen; die bereits erfolgte native Bunnyhop-Abnahme und die
Bestätigung des Nutzers sind separat in `GPU-BUNNYHOP-RESULTS-20261005.md`
dokumentiert. Kein neuer Anspruch auf sofortiges Zurücksetzen gelatchter
ADS-/Vault-Werte bei F12-Änderungen mitten in einer Aktion.

Nach geschütztem Verlassen: Inventory, Health (ohne UpdateTime), Skills, Quests,
TradersInfo, Encyclopedia und Experience identisch zur exakten Profilkopie vor
dem Test. Godmode-Testhook wieder aus. Keine Änderungen an gespeicherten
Arena-Einstellungen; Bunnyhop war hier bereits AN. Für bestehende Nutzer wird
ein ausdrücklich gespeichertes AUS durch die neue Default-Bindung nicht migriert.
Beide Logs enthalten keine Error-/Fatal-Zeilen. Die bereits bekannte Warnung
zum nicht unterstützten `load_kill`-PCM-Format bleibt separat offen.
Spiel und SPT-Server am Ende wieder beendet, wie vor dem Test.

Lokale Rohbelege: `artifacts/movement-master-gpu/` (Deploy-Manifeste,
Prozessidentitäten, Menü-Szenarien/Screenshots, Bewegungsmessungen, Profil- und
Konfigurationsvergleich, Logs). Vier Arbeitsgates erfüllt; keines offen oder
aufgegeben. Kein Push ausgeführt.
