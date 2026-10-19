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

Die native Prüfung und das anschließende Entfernen der temporären LTTP-Installation
stehen bei Anlage dieses Dokuments noch aus. Continuous Healing war auf dem
Testhost bereits installiert und muss erhalten bleiben. Server/Core und
Karten-Assets von Arena bleiben bei diesem reinen Client-Paket unverändert.

Rohbelege liegen ignoriert unter `artifacts/health-loot-compat-20261019/`.
Die beobachteten medizinischen NREs aus älteren Raids gelten damit nicht
automatisch als erklärt oder behoben. Zukünftige Versionen, sämtliche
PFT-Kombinationen und Langzeittests bleiben außerhalb dieser gezielten Abnahme.
