# Continuous Healing und Late To The Party: Integration

Erstes Umsetzungspaket nach dem [Forge-Top-200-Audit](COMPATIBILITY-SURVEY-20261019.md).
Die übrigen Audit-Empfehlungen werden dadurch nicht als erledigt oder geprüft
eingestuft. Keine neue Mod-Sperre für eine passende veröffentlichte Version,
keine fremden DLLs im Arena-Paket und keine gespeicherten Fremdmod-Einstellungen.

## Verhalten

- [Continuous Healing 1.6.3+](CONTINUOUS-HEALING-COMPAT-20261019.md): drei
  spielerbezogene Heilungs-Prefixe greifen nicht auf Arena-Bots und abgelegte
  Spielerkörper durch. Die native Heilung läuft weiter. Continuous Healing bleibt
  beim aktuell lebenden lokalen Spieler aktiv.
- [Late To The Party 3.1.1+](LATE-TO-THE-PARTY-COMPAT-20261019.md): originale
  Waffen-Pickup-Quellen und ihre vorhandenen Teile sind von Loot-Erfassung,
  Auswahlliste und beiden Lösch-Einstiegen ausgenommen. Die Ausnahme überlebt
  einen laufenden nativen Transfer, endet aber mit dessen Besitzübergang.
  Normale Waffen, spätere Anbauteile und gewöhnlicher Loot sind nicht geschützt.
- Beide Integrationen sind optional. Ein unpassender vorhandener Vertrag
  verhindert nur den Arena-Start; normale PMC-/Scav-Raids umgehen die Zulassung.
  Neuere Versionen werden mit Warnung versucht, sofern die erforderlichen
  Metadaten, Methoden und Schutzpfade weiterhin passen.

Die gemeinsame Registrierung besitzt den Lebenszyklus des Plugins, nicht eines
einzelnen Menübesuchs. Keine Karte, Profil-ID oder globale Letzter-Raid-Variable
begründet Besitz. Die Pickup-Registrierung entfernt erledigte Quellen auch bei
fehlendem Late To The Party und hält die Unity-Objekte nur schwach.

## Prüfung vor dem Spieltest

- Release-Build von Client und BigBrain-Begleitbibliothek ohne Deployment:
  erfolgreich, vier bereits vorhandene Nullable-Kontext-Warnungen.
- Unit-Suite: **1.232 bestanden, zwei übersprungen**, keine Fehler.
- Native-Menü-/Start-Harness: **752 Assertions**.
- Optionale Zulassung: **51 Assertions und zehn kompilierte Fehlerkontrollen**.
- Continuous Healing: **212 Assertions und 14 kompilierte Fehlerkontrollen**;
  der Parent hat den finalen Prüfablauf unabhängig erneut ausgeführt.
- Late To The Party: **203 Assertions und 20 kompilierte Fehlerkontrollen**,
  einschließlich zusätzlichen direkten und verzögerten Löschpfaden; auch der
  Parent-Prüflauf auf dem eingefrorenen Stand bestand.
- Waffen-Pickup-Harness: **155 Assertions**, native Signaturkompilierung,
  M60-Definition und elf kompilierte Fehlerkontrollen.
- Pickup-Laufzeit/Layout/Geometrie: **500 Assertions und 41 kompilierte
  Fehlerkontrollen**; abschließende Quell- und Installationshashes unverändert.

Der bestehende Waffen-Pickup-Harness benötigte zwei kleine Reparaturen seiner
Testgrenzen: die mittlerweile verwendete Gesundheitsangabe in einem Beschreibungstext
und `LogDebug` im nativen Compile-Stand-in fehlten. `Item.CurrentAddress` wurde
für die neue Besitzprüfung ergänzt. Keine dieser Anpassungen ersetzt eine
Produktionsentscheidung durch einen fest verdrahteten erfolgreichen Testwert.

Eine separate adversarielle Quellprüfung hat die drei Continuous-Healing-Prefixe,
die LTTP-Aufrufkette, native Transfer-/RemoveItemEvent-Reihenfolge, schwache
Besitzbindung und gemeinsame Registrierung geprüft. Im finalen Stand blieb
kein Blocker offen. Diese Aussage ersetzt keinen echten Mono-/Harmony-Spieltest.

## Deployment und native Grenzen

Implementierungscommit: `f8f4a48949870005c3beb3084f6d4fc259dd29af`.
Der daraus isoliert gebaute Client/Begleiter trägt
`0.1.1+f8f4a48949870005c3beb3084f6d4fc259dd29af`.

| Datei | SHA-256 |
| --- | --- |
| `maschine-ArenaMode.Client.dll` | `df2c5583ce3bc908e30d698ed7ab6e4ee0fc1e2e0242f7d1b7cb1424b837e9fd` |
| `maschine-ArenaMode.BigBrainCompat.dll` | `f6c971318fa4488a651a559c9f9ee6c07d258c0177e4cb7893475f7de6569a7a` |

Beide Dateien wurden mit dem vorhandenen Deployment-Helfer nach Entwicklung
und GPU2 eingespielt und dort bytegleich geprüft. Der automatische Abgleich
hatte auf GPU2 noch den älteren `2624bef`-Client vorgefunden; nach Ablauf seines
Wartefensters erfolgte das direkte, auf entsperrte Arena-Dateien begrenzte
Deployment. Der anschließende GPU2-Prozess bestätigt den geladenen Build und
unveränderte installierte Bibliotheken; die einzelnen nativen Beobachtungen und
ihre Grenzen stehen im [GPU-Prüfbericht](GPU-HEALTH-LOOT-COMPAT-20261019.md).

Die gezielte native Prüfung auf GPU2 ist abgeschlossen: gewöhnlicher Factory-PMC
→ Arena Deathmatch mit vier Teilnehmern → gewöhnlicher Factory-PMC, im selben
Prozess. Beide normalen Raids belegten echte mehrgliedrige Heilung, Abbruch und
LTTP-Lootentfernung. In Arena liefen die lokale Heilung sowie natürliche
Bot-Start-/Effekt-End-Ereignisse durch die vorgesehenen Grenzen. LTTPs echter Scan
ließ die M60-Quelle mitsamt 14 ursprünglichen Teilen aus. Arenas eigene Rotation,
anschließende Aufnahme in den freien Waffenplatz und normales Fallenlassen
funktionierten; der erledigte Quell-Handle behielt keine Ausnahme.

Zwei Todesrespawns mit Killcam und anschließender Heilung funktionierten. Der
erste Tod entstand durch Gegnerfeuer; der zweite wurde nach Botbeschuss über
den Testhook ausgelöst, statt weiter auf einen tödlichen Treffer zu warten.
Ein verspäteter Heilungsrückruf eines alten lokalen Körpers wurde nicht
beobachtet. Ebenso bleiben direkte/queued LTTP-Löschung, ausstehende Transfers
und Rollback-Timings native Testgrenzen, trotz bestandener Offline-Fehlerkontrollen.
Die Bot-Heilung fiel nicht nachweislich mit einem lokalen Heilungsabbruch zusammen.

Beim Verlassen des ersten gewöhnlichen PMC-Raids traten zweimal NREs in
`BotWeaponSelector.OnWeaponTaken` auf, noch vor dem Arena-Raid. Die Quelle ist
nicht geklärt; diese Prüfung behauptet weder eine Behebung noch eine sichere
Ursachenzuweisung. LTTPs Accessibility-Warnungen stammten aus dessen temporär
aktivierter Debug-Ausgabe. Einzelheiten und weitere Loggrenzen stehen im GPU-Bericht.

Nach dem Test wurden Spiel und Server beendet, alle 232 gespeicherten Arena-,
drei CH- und 19 Killcam-Werte zurückgesetzt und verglichen. Ein unabhängiger
Vergleich bestätigt die 232 Arena-Werte auch auf beiden Installationen.
Acht temporäre LTTP-Dateien einschließlich erzeugter Loot-Rankings wurden
hashgeprüft archiviert und aus Entwicklung/GPU2 entfernt; es bleiben lediglich
vier leere Testordner. Der erste Bereinigungslauf scheiterte erst an diesen
leeren Ordnern, nachdem sämtliche Dateien entfernt waren; die anschließende
Ordnerbereinigung wurde vom Werkzeug abgewiesen und nicht umgangen.
Der abschließende DLL-Scan findet keine LTTP-DLL oder Arena-Duplikate.
Das bereits installierte Continuous Healing bleibt erhalten. Server/Core,
UiBridge und CH sind auf beiden Rechnern bytegleich zum Ausgangsstand.
Karten-Assets wurden nicht geändert. Die beiden getesteten Arena-DLLs bleiben
eingespielt; kein neuer Release und kein Push gehören zu diesem Paket.

Rohbelege liegen ignoriert unter `artifacts/health-loot-compat-20261019/`.
Die beobachteten medizinischen NREs aus älteren Raids gelten damit nicht
automatisch als erklärt oder behoben. Zukünftige Versionen, sämtliche
PFT-Kombinationen und Langzeittests bleiben außerhalb dieser gezielten Abnahme.
