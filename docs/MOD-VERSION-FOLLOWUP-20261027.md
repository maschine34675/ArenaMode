# Abweichende Modversionen in Arena — 27.10.2026

Neue private Sessions behalten bekannte Mods auch dann, wenn ihre Version von der geprüften Version abweicht. Die Abweichung bleibt ein Hinweis. Sie erzeugt keinen zusätzlichen Neustartvorschlag und entfernt weder den Mod noch seine gültigen Abhängigkeiten.

| Ursache | Korrektur |
|---|---|
| Die normale Whitelist-Entscheidung war bereits versionsoffen. `ComputeNormalSession` erzeugte danach einen zusätzlichen Ausschlussplan für abweichende Versionen, einschließlich abhängiger Mods und Gegenstücke auf der anderen Seite. | Neue Startanfragen verwenden direkt die normale Policy und ihre erlaubten Servermods; `versionPlan` bleibt leer. |
| UIFixes 6.0.4 wich von der geprüften 6.0.3 ab und wurde deshalb beim privaten Neustart entfernt. | UIFixes bleibt enthalten, sofern seine Identität und tatsächlichen Abhängigkeiten zugelassen sind. |
| Die untere Leiste erschien in Arena drei Pixel höher. | Der lokale UIFixes-Quellstand setzt `BottomPanel.localPosition` ausdrücklich auf `(0, -3, 0)`; ohne den Mod fehlt dieser Versatz. Kein zusätzlicher Arena-Offset wird eingeführt. |

Die Identitäts-Whitelist, explizite Sperren, Abhängigkeitsprüfung und tatsächliche API-/Profilschutzprüfungen bleiben bestehen. Alte laufende Sessions mit einem Versionsplan werden weiter gegen ihren ursprünglichen Plan und Hash geprüft; erst der nächste neue Einstieg verwendet die neue Regel. Die Policy-Datei und ihre geprüften Versionsangaben werden nicht umgeschrieben.

## Nachweise

- Produktion: `ArenaMode.Client/Session/ArenaWhitelistAdmission.cs`, `ArenaSideSelectionPatch.cs`. Gate, Helper, Server und gemeinsame Verträge unverändert.
- UIFixes-Ursache: `../_reference/UIFixes-main/src/Patches/MoveTaskbarPatch.cs:20–22`; Aktivierung in `src/Plugin.cs:58`. Die Dekompilierung der tatsächlich installierten 6.0.4 bestätigt denselben Versatz: [Binary-Beleg](../artifacts/ux-followup-20261027/installed-uifixes-taskbar.cs).
- `ArenaSessionVersionChecks` prüft ältere und neuere UIFixes-Versionen, Hinweise, erhaltene Abhängigkeiten, ausgeschlossene unbekannte Identitäten, die erzeugte Helper-Anfrage und die Zulassung im privaten Prozess. Bestehende Legacy-Plan-, Hash- und Manipulationsprüfungen bleiben erhalten; zwei neue Negativkontrollen erkennen erneut eingeführte Versionsfilter.
- Root-Prüfung: Versionsharness **88 Assertions und zehn erkannte Negativkontrollen**; vollständiger Client-Harness **174 erkannte Negativkontrollen**. Gesamtsuite **3213 bestanden / ein bekannter installierter-APBS-Integrationstest übersprungen**; CompactSessionUxChecks **25 Assertions**. Produktion und Tests unabhängig gegengelesen.
- Gemeinsamer Release-Build von Client und Companion erfolgreich (null Fehler, vier bestehende Nullable-Kontextwarnungen). Native Messwerte und geladene Identitäten: [gemeinsame Abnahme](UX-FOLLOWUP-20261027.md).
