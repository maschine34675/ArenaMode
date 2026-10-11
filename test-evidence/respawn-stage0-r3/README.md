# Belege: respawn-stage0-r3

Native GPU-Prüfung vom 13.09.2026. Ergebnisbericht: [GPU-RESPAWN-STAGE0-TEST-RESULTS-r3.md](../../docs/GPU-RESPAWN-STAGE0-TEST-RESULTS-r3.md). Keine vollständige Abnahme; drei Raids E, A und C, anschließend vorsorglicher Stopp wegen zweier SAIN-Initialisierungsfehler.

- `screenshots/`: 28 unveränderte Originalbilder. Die Aufnahmefunktion hatte JPEG/JFIF-Inhalte zunächst privat mit `.png` benannt; öffentliche Erweiterungen wurden auf `.jpg` korrigiert, ohne die Bytes zu ändern.
- `audit/screenshot-index.json`: Aufnahmeintervalle, Bildmaße und SHA256 der Originale.
- `logs/`: gezielte Logauszüge; private Kennungen redigiert. Keine vollständigen Logs oder Profile.
- `audit/`: Startmarker, Buildversionen/Hashes, Einstellungen, Profile, Quellenzählungen, Eingabefolge und Zeitbudget. E/A/C-Fallzählungen und Gesamtzählung haben getrennte Grenzen.
- `SHA256SUMS.txt`: SHA256 aller öffentlichen Dateien in diesem Ordner außer dem Manifest selbst sowie des zugehörigen Berichts. Pfade sind relativ zu diesem Ordner.

Vollständige Ausgangsdateien verbleiben ausschließlich unter dem ignorierten `artifacts/gpu-respawn-stage0/respawn-stage0-r3/2026-09-13T08-15-06-087Z/`. Frühe private C-stop-Zwischenaudits werden durch C-end-audit und final-run-audit ersetzt und sind hier nicht veröffentlicht.
