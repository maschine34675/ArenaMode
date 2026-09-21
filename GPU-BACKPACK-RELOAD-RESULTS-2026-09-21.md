# Rucksack-Nachladen: Mono-Fix und GPU-Abnahme

## Ergebnis

**Bestanden, 2026-09-21:** Waffenpanel, R-Nachladen und G-Granaten funktionieren
im frischen EFT-Prozess mit dem korrigierten Client. Getestet auf Factory Tag,
Deathmatch/Endless/FFA mit dem bestehenden Testprofil `ArenaGPU20260908`.
Kein normaler PMC-Kontrollraid und kein gesonderter Tactical/PFT-Test durchgeführt.

Der ursprüngliche Logfehler um **18:25:08.964** ist eine
`ArrayTypeMismatchException`: Die Granatenabfrage (`ThrowWeap`) des Waffenpanels
läuft durch den für `Magazine` erzeugten Harmony-Wrapper. Auf Mono teilen sich
Referenztyp-Instanziierungen dieses generischen Codes die Implementierung.
Der Fehler entsteht bereits im nativen Original vor dem Postfix; ein Typcheck
im Postfix reicht deshalb nicht. Dieselbe gemeinsame Abfrage wird für lose
Munition und G benutzt. Details: [BACKPACK-RELOAD-REVIEW.md](BACKPACK-RELOAD-REVIEW.md).

Der neue Client verändert ausschließlich drei nichtgenerische Magazin-Verbraucher.
Geladene Harmony-Patches wurden kontrolliert: alle drei Transpiler vorhanden,
kein Arena-Hook mehr auf den generischen Reachable-Abfragen.

## Exakter getesteter Stand

- Implementierung: `0d9ceea23c8c3cbcfb011024fbc23eddd8c4125e`.
- Client ProductVersion: `0.1.0+0d9ceea23c8c3cbcfb011024fbc23eddd8c4125e`.
- Client SHA256: `C14FBCEC806AE884876CACEAEB67B0E41A18B81A0CC7D95C629A7CC0A8FC14B8`.
- Client MVID, GPU geladen und auf Datenträger:
  `9e53748a-8ed8-4ced-8eee-199860a52376`.
- UiBridge-Instanz: `1284f6edbf614f5787e597a6affb7ea4`, Start 18:51:32 MESZ.
- SPT 4.1.6, EFT 0.16.9.5-40743, Unity 2022.3.43f1.
- UiBridge `0.6.0+e8e638e2b76911a7c1168a8f192c415004089357`, SHA256
  `3F722D9020C97632122544066009A006B2C56D752C1623CCBC79798CE2807A22`.
- Server unverändert: SHA256
  `42246A592933D18D0D01DD6848ABAFA2EEC0B22B6483451D2111DAB76651C660`.
- Redeploy.Core unverändert: SHA256
  `673D9A106F747E5EE0A70EDE21E9DE9E5AB48E5DDE12E0B2922B2139ECDFE4DD`.

Nur die Client-DLL wurde lokal und auf dem GPU-Rechner ersetzt. Der isolierte
Build enthält ausschließlich den dokumentierten, fremden HANDOFF-Text als
uncommittete Änderung; kein Compiler-Input war uncommittet. Der Build-Manifest-
Wert `Deployed=false` beschreibt den isolierten Buildschritt; der spätere
Kopiervorgang und Spieltest stehen in den gesonderten Belegen.

## Im Spiel geprüft

Die Mengen unten sind **Magazin + Kammer**, keine HUD-Schätzung. UiBridge lieferte
die nativen Spielerzustände; R/G wurden über den normalen ECommand-Eingang
ausgelöst, ohne Inventar- oder Nachladezustände direkt zu setzen.

| Prüfung | Beobachtung | Ergebnis |
|---|---|---|
| M4A1, normales R | `18+1 → 30+1`, native Magazinoperation abgeschlossen | Bestanden |
| M4A1, Waffenpanel | Panel mit Waffe, Munition, Magazinen und Granaten direkt im Screenshot/Live-Snapshot sichtbar | Bestanden; kein separater M4-HUD-Snapshot archiviert |
| Rucksack, Erweiterung aus | Zwei leere Rig-Magazine, drei volle Magazine zuvor per UI in den Rucksack gelegt; R bleibt bei `0+1` | Negativkontrolle bestanden |
| Dieselbe Situation, Erweiterung an | R lädt `0+1 → 30+1` | Bestanden |
| MP-153, internes Magazin | Panel sichtbar, Schuss und R: `3+1 → 4+1`; `ReloadInternalMagOperation` beendet | Bestanden |
| Mosin, internes Magazin | Panel sichtbar, zweiter Schuss und R: `3+1 → 4+1`; `ReloadInternalMagWithOpenBoltOperation` beendet | Bestanden |
| G/Granate | `PressThrowGrenade` zieht RGD-5; `TryHighThrow`/`FinishHighThrow` wirft sie; ca. vier Sekunden später Granaten-Selbsttod und Arena-Respawn | Bestanden |
| Respawn | Nach Granatentod und zwei Kitwechseln weiterhin bedienbare Hände, Panel und Nachladen | Bestanden |

Die erste Mosin-Erwartung `5+1` war falsch: Beim Nachladen mit offenem Verschluss
wird wieder eine Patrone ins Patronenlager befördert. Der zweite Versuch belegt
die tatsächliche Erhöhung `3+1 → 4+1`; kein weiterer Fix war dafür nötig.
Ein erstes G-Kommando unmittelbar nach dem Inventarschließen wurde vom
Eingabefilter verworfen; die anschließende akzeptierte Eingabe zog die Granate.

Beleggrenzen: Der Nachher-Inventarsnapshot beim Rucksacktest ist leer und beweist
keine konkrete Magazin-ID-Übertragung. Die vorbereitete Inventaransicht,
manuelle Platzierung und die Aus/An-Kontrolle belegen die Rucksackfunktion.
Ebenso wurde vor der Explosion kein Granaten-Bestandsrückgang separat gemessen;
Wurf, spezifischer Granaten-Selbsttod-Auslöser und Respawn sind belegt.
Magazinwahl-Menü/Inventar-Nachladen sind offline über ihre eigenen Call-Sites
geprüft, in diesem Lauf jedoch nicht separat bedient.

## Logs, Profil und Rückstellung

Im neuen vollständigen GPU-Log keine `ArrayTypeMismatchException`, kein
`Arena weapon panel update failed` und keine Arena-Warnung im Testlauf.
Nicht verschwiegen: Unity meldet beim Start ein Mikrofon-Ausgabegeräteproblem,
die Szene fehlende `extractor_smoke`-Skripte; Redeploy meldet beim Respawn die
Bereinigung eines alten PerfectCulling-Samplers. Die absichtlich werfenden
`RespawnApiProbe.AlwaysThrow`-Subscriber bleiben installiert und werden von Core
abgefangen. Das sind keine neuen Inventarabfragefehler.

Teilnehmerzahl Factory Tag wurde ausschließlich für den Test **24 → 4 → 24**
gestellt. Alle 14 gespeicherten Teilnehmerwerte und alle fünf Kit-Einstellungen
stimmen danach wieder mit dem Ausgangszustand überein: `RandomKit=true`,
`SelectedKit` leer, `UseItemsAnywhere=true`. Defaults unverändert.
Der vollständige Profilvergleich vor/nach dem Lauf ergibt ausschließlich
`characters.pmc.Health.UpdateTime` und
`characters.pmc.Hideout.sptUpdateLastRunTimestamp`; Inventar, Fortschritt,
Gesundheitswerte und sonstiger Profilinhalt unverändert. Kein Profil zurückkopiert.
Nach regulärer Rückkehr ins Hauptmenü wurde EFT regulär beendet; kein Raid läuft.

## Offline-Prüfungen und Belege

- Rucksack: 51 Verhaltensprüfungen, 34 native IL-Verträge, sechs verworfene
  Fehlerfassungen, darunter die Wiederherstellung des unsicheren generischen Hooks.
- Nachladen: 779 Prüfungen; Granatenauswahl: 123/14 Fehlerkontrollen;
  HUD: 292/27; Infinite Ammo: 2839/4 plus 32 native IL-Verträge.
- Native Client-Build: null Fehler, vier bestehende Nullable-Warnungen.
- Unabhängiger Source-/Testreview ohne verbleibenden P0–P2-Befund; zusätzlicher
  Belegaudit bestätigte die obigen Ergebnisse mit den ausdrücklich genannten Grenzen.

Lokale Rohbelege liegen unter `artifacts/backpack-mono-fix/` (ignoriert, keine
Profile oder vollständigen Logs im Git): `before.log`, `gpu-after.log`,
`status-native.json`, `deployment.json`, Waffen-/Inventar-Snapshots,
Config-Vorher/Nachher und Profilvergleich. Das versionierte
[BACKPACK-RELOAD-DEPLOYMENT-20260921.json](BACKPACK-RELOAD-DEPLOYMENT-20260921.json)
enthält Prüfsummen und den kompakten Abnahmebeleg.
