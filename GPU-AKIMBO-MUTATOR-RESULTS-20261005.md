# Akimbo-Mutator — GPU-2, 05.10.2026

## Ergebnis und Bedienung

**AKIMBO PISTOLS** ist im Kartenbildschirm ein dauerhafter, standardmäßig
ausgeschalteter Mutator; F12: `Combat / AkimboEnabled`. Er kopiert die aktuell
gehaltene Pistole einschließlich Automatikpistolen und Handrevolvern. Native
Handgun-Klasse plus `weapClass=pistol` schließen unter anderem Leuchtpistolen,
Revolver-Schrotflinten, Granatwerfer und UZI PRO Pistol aus. Keine neue Inventarwaffe
und kein Pickup. Alle Arena-Modi; gewöhnliche Raids und Bots ausgeschlossen.

Beide Läufe feuern **gleichzeitig**, kein abwechselndes Dual Wield. Das Original
verbraucht eine Patrone; das Bonusgeschoss ist kostenlos. Magazin, Nachladen,
Schussklang und Rückstoß bleiben gemeinsam. Hüftfeuer; während Nachladen,
Sprint/Interaktionen übernimmt wieder die native beidhändige Animation. Die
akzeptierte grobe linke Stützhand bleibt eine ausdrückliche Darstellungsgrenze.

## Änderungen, Build und Prüfungen

- `97deb6b`: persistenter Schalter, automatische Bindung, Pistolenfilter,
  Modellsicherheit, 15 Mutatoren im vorhandenen Kasten.
- `f11f998`: Magazinreferenz in der Bindung. Ein anderes Magazinmodell baut die
  Kopie nach der nativen Reload-Operation neu auf, statt alte Pool-Transforms
  weiterzuverwenden.
- `fadec49`: tatsächlicher `Weapon.MalfState` bestimmt die Störungssperre.
  `FirearmController.Malfunction` wird auch bei normalen leeren Schusszyklen
  gesetzt und ist allein kein verlässlicher Bereitschaftsfilter. Alle übrigen
  Operations-, Identitäts-, Kollisions- und Rekursionsprüfungen bleiben erhalten.

Finaler isolierter Build aus sauberem `fadec49`: null Fehler, vier bestehende
Nullable-Warnungen. Lokal und GPU-2 Client-SHA256:
`28947B729CEE2F8DDA5FF61D6F1B97823561C21BAFB81070C25170777563D9E2`.
Server, Redeploy.Core und BigBrainCompat unverändert. UiBridge 0.7.4 `8218b027`,
EFT 0.16.9.5, Unity 2022.3.43f1; finale Prozessinstanz `4201422c`, 24/24 geladene
Plugin-/Library-Identitäten passend zu Hostdateien und lokalem Build.

2086 Assertions an produktiven Akimbo-Zustandshelfern (darunter 2000 Wiederholungen
für stabile Bindung/Fehlersperre), elf negative Kontrollen. 946 Menü-Assertions,
14 negative Kontrollen. Diese Checks zertifizieren weder Unity-Lebenszyklus noch
alle Waffengeometrien. Unabhängiger adversarialer Review des Mutators und beider
nativen Korrekturen: kein verbleibender Blocker. Unabhängige Sichtprüfung des
Menüs und der finalen Revolveraufnahme ebenfalls ohne neuen Blocker.

## Native Belege

Factory bei Tag, 1920×1080, Gun Game. Aufeinander aufbauende Builds wurden jeweils
nach Spielende eingespielt, niemals in einen laufenden Prozess.

| Prüfung | Beobachtung / Beleg |
|---|---|
| Menü (`97deb6b`, unverändertes Layout im finalen Client) | OFF → ON → OFF → ON sichtbar; alle 15 Titel/Beschreibungen vollständig. `menu/`. `final-entry/step-7-screenshot.png` ergänzt Factory-Thumbnail, Zeitwahl und freie NEXT/BACK/READY-Flächen. |
| Glock 17 (`97deb6b`) | Zwei sichtbare Pistolen; zwei Abzüge, Magazin 17+1 → 15+1, zwei Bonusgeschosse. Reload, Messerwechsel und Rückkehr funktionieren. `glock17/`, `glock-*.json`, `knife*.json`. |
| Glock 18C (`97deb6b`, wiederholt auf `f11f998`) | 400-ms-Automatikfeuer verbraucht neun Patronen und erzeugt neun Bonusgeschosse. `final-auto-before/after/fired.json`. Kein exponentielles Vervielfachen. |
| Anderes Magazin (`f11f998`) | Native Installation eines leeren GL9/21 statt Glock/17 beendet die alte Bindung und erstellt die neue nach Reload-Ende. Ein vorhandener Kammerschuss erzeugt ein Bonusgeschoss. R lädt anschließend wieder ein volles 17er-Magazin. `final-new-mag*`, `final-mag-log.json`, `final-reloaded.json`. |
| Lebenszyklus (`f11f998`) | Testtod/Respawn bindet ohne erneutes Einschalten. OFF und Messer räumen auf; ON und erneutes Ziehen binden wieder. `final-respawn*`, `final-off.json`, `final-knife.json`, `final-redraw.json`. |
| Rhino 50DS (`fadec49`) | Sechs echte Schüsse und sechs Bonusgeschosse, Trommel 6 → 0. Siebter Trockenabzug erzeugt keinen Bonus. Beide Modelle bleiben sichtbar, obwohl Controllerflag=true: tatsächlicher Waffenstatus=None, Operation=Idling. `final-revolver-empty*`. |
| Revolver nach Reload (`fadec49`) | Trommel wieder 6/6, Kopie sichtbar, nächster Schuss erhöht Bonuszähler auf sieben. `final-revolver-reload*`, `final-revolver-after-reload-shot.json`. |
| Normaler PMC (`fadec49`) | AkimboEnabled=true bestätigt; normale M9A3 aus dem Stash, ein nativer Schuss verbraucht eine Patrone, keine Bindung/Kopie und kein Zählerzuwachs (7 → 7). ADS normal sichtbar. `control-preference.json`, `normal*.json`, `normal/`. |

Die frühe Testleiter aus der gespeicherten Datei entsprach nicht der tatsächlich
geladenen Serverleiter: zuerst wurde eine MP5K geladen. Kein Akimbo wurde darauf
aktiviert. Die gewünschten Pistolen wurden danach anhand der **live gelesenen**
Leiter ausgewählt, ohne `ladder.json` umzuschreiben. Für die Fixtures wurde
vorübergehend `gunGameRungs=999` genutzt; wieder 24 auf beiden Installationen.

Die Tests decken bewusst verschiedene native Pfade ab, nicht jede Pistole und
jedes Attachment. Glock-/Magazin-/Respawnprüfungen gelten für die angegebenen
Vorstufen; die letzte Änderung wurde gezielt mit leerem Revolver, Nachladen und
normalem PMC überprüft. Kein separater Nachweis aller Schadens-/Assist-/Kill-
Zuordnungen, Wandfälle, Tactical-Rundenwechsel oder sämtlicher Animationen.
Custom SkinnedMesh-Geometrie bleibt ausgeschlossen. Andere Attachmentwechsel
ohne Waffenwurzel-/Magazinwechsel können weiterhin OFF→ON oder Neuziehen erfordern.

## Profil, Konfiguration, Logs und Ende

**Arena-Isolation vor dem normalen Kontrollraid separat bestanden:** Inventory,
Health ohne UpdateTime, Skills, Quests, TradersInfo, Encyclopedia und Experience
bleiben gegenüber dem exakten Ausgangsprofil unverändert. Auch nach den beiden
Zwischenbuilds separat geprüft. `profile-initial-check.json`,
`profile-magazine-check.json`, `profile-arena-check.json`.

Der anschließende gewöhnliche PMC-Raid lief ohne Arena-Arming/-Profilschutz, mit
AI amount=None. Normale Änderungen an Inventar, Skills und Gesundheit wurden
archiviert. **Nur seine Testfixture wurde anschließend zurückgesetzt:** eigene
Baseline unmittelbar vor diesem Kontrollraid, exakte Profil-ID, beide Prozesse
beendet, Nachtestdatei gesichert, Bytehash der Wiederherstellung geprüft. Das ist
kein Beleg für Arenas automatische Wiederherstellung. Restaurierter Hash auf GPU
und synchronisiert lokal:
`72D4AB351024AF89ED30631D6FC9BA1C3ECB500C50DA8F362BED57651E7091D4`.

Alle vorherigen Arena-BepInEx-Werte auf GPU und lokal erhalten; einziger neuer
Wert `Combat / AkimboEnabled=false`. GunGameLadder wieder leer, Serverkonfiguration
auf beiden Rechnern byteidentisch zu vorher. Alte Prototype/Active-Zeile bleibt
wirkungsloses vorhandenes Konfigurationsmaterial. Sonstige Modkonfigurationen
wurden nicht verändert. Godmode/Tracking beendet; Spiel und Server wieder aus.

Finales Log: 3306 Zeilen, keine Error/Fatal-Zeile, keine Akimbo-Warnung. Die
Zwischenläufe enthielten je eine `BotFirstAid.CG_ApplyToSelf`-NRE im medizinischen
Cleanup beim Raidende; keine Akimbo-Methode im sichtbaren Stack. Ursache hier
nicht isoliert und nicht als behoben ausgegeben. Bekannte PCM-/native Script-
Warnungen bleiben außerhalb dieses Akimbo-Auftrags.

Rohbelege: `artifacts/akimbo-mutator-gpu/` (ignoriert, keine Profile im Git).
Deployment-Receipt und sauberes Build-Manifest `534a08f59f6d4a9da7e5416cceb62c1d`.
Vier Arbeitsgates erfüllt, keine offene Implementierungsaufgabe dieses Umfangs.
Keine Veröffentlichung und kein Push.
