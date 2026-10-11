# Respawn Stufe 0 r2 — Belege

Frischer Lauf vom 13.09.2026 nach angefordertem Serverneustart. Vier native Raids; Pflichtstopp beim Ausstieg aus C. Bericht: [GPU-RESPAWN-STAGE0-TEST-RESULTS-r2.md](../../docs/GPU-RESPAWN-STAGE0-TEST-RESULTS-r2.md).

- `screenshots/`: 42 unveränderte Originalbilder. `index.json` enthält Aufnahmezeit, Abmessungen und Original-SHA256. Launcher-/Codex-Aufnahmen wurden ausgeschlossen.
- `logs/`: gezielte Auszüge mit Client-Zeilennummern und lokalen MESZ-Zeiten sowie Server-Zeilennummern und UTC. Private Kennungen sind als `[private-id]` redigiert. Der vollständige relevante BotFirstAid-Stack ist enthalten.
- `audit/`: Startgate, Teilfall-/Endzählungen, Dateibindung, Zeiten, native Eingaben, Prozessdaten, Rückstellung und bereinigter vollständiger Profilvergleich. Keine Rohprofile.
- `SHA256SUMS.txt`: Byte-Hashes aller Belege außer der Manifestdatei selbst; der Ergebnisbericht ist zusätzlich mit relativem Pfad aufgeführt.

Vollständige Logs, Profilkopien und Einstellungsdateien liegen ausschließlich im ignorierten `artifacts/gpu-respawn-stage0/respawn-stage0-r2/2026-09-13T06-09-57-689Z/`. Keine Profile wurden zurückkopiert oder bearbeitet.

Die acht Pflicht-DLLs blieben unverändert. Für die optionale BotDropDiag.dll wurde am Ende ein abweichender Hash erfasst; die auf Disk gelesenen Bytes belegen keine im Prozess geladenen Bytes. Kein Gesamt-PASS.
