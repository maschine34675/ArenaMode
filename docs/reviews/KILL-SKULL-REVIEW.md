# Kill-Bestätigung mit Arena-Totenkopf

Stand: 2026-09-29. Ergänzt die UI-Asset-Integration aus `b65bf86`.

`HitmarkerElement` zeigt das bereits eingebettete Original-Arena-Asset `kill.png`
zusätzlich zum bestehenden Trefferkreuz: 32 HUD-Einheiten, mittig 42 Einheiten
unter der Zielmitte, weiß mit dunkler Kontur. 0,15 Sekunden voll sichtbar, danach
0,65 Sekunden ausblenden. Keine neue Einstellung; der vorhandene Hitmarker-Schalter
gilt auch hierfür. Farben, Geräusche und Dauer des bisherigen Kreuzes bleiben.
Ein normaler Treffer setzt dessen vorherige Puls-Skalierung wieder zurück.

Das Signal kommt aus dem bestehenden, deduplizierten `ArenaMatch.OnKill`, vor
den Killfeed-Schaltern in `ArenaHud`. Nur `KilledByLocal && !LocalIsVictim &&
ScoreEligible && AwardEligible` bestätigt einen Kill. Dadurch keine Bestätigung
für fremde/Selbst-/Team-/administrative Tode und keine Auswertung eines womöglich
veralteten Angreifers bei Umwelt-/Blutungsschaden. Keine erneute Körper-/Profil-
Abfrage im HUD. Die vorhandene Klassifizierung und Punktevergabe bleiben unverändert.
`ArenaDamage.Kill` erzeugt keinen zweiten Totenkopf.

Der Totenkopf ist ein Geschwisterobjekt des Kreuzes mit eigenem CanvasGroup-Timer;
weitere Treffer überschreiben, verlängern oder färben ihn nicht. Pro HUD ein
passives Sprite; keine Allokation beim Kill. Eigener `ArenaUiAssetOwner` räumt mit
dem Canvas auf. Bei fehlendem Asset bleibt das Trefferkreuz funktionsfähig.
Es werden keine DragonDen-Grafiken oder DLLs übernommen. Herkunft und Rechte des
Arena-Assets stehen weiterhin in `ArenaMode.Client/Assets/Ui/NOTICE.md`.

Der Killfeed-Formatter hängt kein zusätzliches `HS` mehr an. Das separate
Kopftreffer-Icon, Namen, Rollen, Waffenbezeichnung und Distanz bleiben unverändert.
Ein wörtliches `HS` in einem Namen oder einer Waffenbezeichnung wird nicht entfernt.
Das Kürzel in der Scoreboard-Spaltenüberschrift ist nicht betroffen.

## Verifikation

- `tools/ArenaUiAssetChecks/Verify.ps1`: echte Produktionsklassen, 160 Assertions,
  zehn kompilierte Fehlerkontrollen einschließlich falschem Kill-Absender,
  Verwechslung Damage/Kill und nie ausblendendem Totenkopf. Besitz, Fallback,
  unabhängige Lebensdauer und Wiederverwendung sind modelliert; keine GPU-Aussage.
- `tools/KillfeedTextChecks/Verify.ps1`: vollständige Daten, kein doppeltes HS,
  unveränderte wörtliche Namen/Waffen, Gegenprobe mit wieder eingefügtem HS.
- `tools/DoubleJumpChecks/Build-Staged.ps1`: isolierter Client-Release-Build ohne
  automatisches Austauschen installierter Dateien.
- Unabhängige adversariale Review ohne offene Produktionsfehler. Der ursprüngliche
  Harness-Initializer eines abzulehnenden Kills wurde explizit auf
  `ScoreEligible=false` korrigiert; das DTO hat standardmäßig `true`.

## Nativer Test

UiBridge 0.7.3 bietet Zielverfolgung und echten Waffenabzug für lokale Einzelspieler-
Tests. Nach dem Deployment tatsächliche geladene MVID kontrollieren, Testprofil
unmittelbar vor Arena sichern/erfassen, regulär Deathmatch starten und echte eigene
Kopf-/Körperkills prüfen. Keine synthetischen Arena-Events und kein Kill-Hook, der
nur den lokalen Spieler tötet. Testhilfen anschließend deaktivieren und geschützt
verlassen; Profildateien werden niemals manuell zurückgespielt.

Der lesende Inspektor kann Kill-Zeitstempel, Sprite und CanvasGroup-Alpha vor/nach
echten Ereignissen belegen. Das kurze sichtbare Fenster verlangt zusätzlich einen
rechtzeitigen Screenshot; ein verpasster Einzel-Screenshot beweist keinen Fehler.
Ergebnisse und Grenzen werden separat in `GPU-KILL-SKULL-RESULTS-20260929.md`
festgehalten, mit Deployment-/Build-Identität statt bloßem Quellstand.
