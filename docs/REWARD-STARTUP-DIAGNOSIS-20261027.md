# Wiederkehrende Reward-Warnungen — 27.10.2026

**Zwei gespeicherte Problemfälle in zwei Profilen.** Sie entstehen nicht bei jedem Start neu. Der normale Server prüft alle registrierten Profile vor dem Laden; unverändert unvollständige Belege ergeben jedes Mal dieselbe Warnung. Die bisherige Meldung nennt das betroffene Profil nicht.

Prüfung ausschließlich lesend: lokale Installation `D:\SPT41`, aktuelle Dateien von `SPT-GPU-2` über die Freigabe und Serverlogs. Keine Profildatei, Auszahlung, Zustimmung oder Reward-Historie verändert. Alle Uhrzeiten unten sind **UTC**.

| Warnung | Betroffen | Konkreter Befund |
|---|---|---|
| `reward-snapshot-restoration-unresolved` | `spt41dev` / `6a6f5c046536d896fc7500bd` | Snapshot-Generation `0b83a24140bd4887ba999dba53175331` steht seit 26.10., 15:19:42 auf `pending`. Keiner der drei vorhandenen signierten Eingänge trägt diese Wiederherstellungs-ID. |
| `reward-import-not-settled` | `SPT-GPU-2` / `6ab7db1c64f04b565cf78be9` | Journal und Importhistorie nennen Run `0958715f1e1d4733a2589dbd73af4de7`, Sequenz 16, als bezahlt. Der zugehörige permanente Zahlungsbeleg fehlt im aktuellen Profil. |

## Snapshot-Nachweis

Datei: `SPT_Runtime/user/arena-reward-snapshots/6a6f5c046536d896fc7500bd/0b83a24140bd4887ba999dba53175331.json`.

Das Log `spt-MASCHINE11-20261026.log` zeigt:

- 15:19:42: Snapshot aufgenommen.
- 15:23:11: Snapshot zurückgesetzt; danach `reward-recovery-restoration-unproven`.
- Folgeraids 15:24 und 15:28: `Reward evidence unavailable`; letzter Abschluss 15:36:26 mit `reward-durable-restoration-unproven`.

Der Profil-Schutzpfad meldet abgeschlossene Wiederherstellung. Der getrennte Reward-Pfad konnte jedoch die vollständige gespeicherte Profildatei nicht positiv dem beobachteten Snapshot zuordnen. Deshalb bleibt der Schutzvermerk bestehen. Zusätzlich existiert der nicht abgeschlossene Run `3108588f165841aeafe312720f19526a` ohne passenden Eingang.

Die genaue abweichende Profileigenschaft lässt sich aus diesen Logs nicht bestimmen. Der Guard speichert nur Generation und Zustand; der damalige vollständige Vergleichssnapshot lag im Arbeitsspeicher. Ein aktuelles, normal ladbares Profil ersetzt diesen fehlenden Nachweis nicht.

## Import-/Zahlungshistorie

Basis: `SPT_Runtime/user/arena-rewards/6ab7db1c64f04b565cf78be9/`.

- `journal.json`: **14** Zahlungen, alle `Committed`.
- Profilerweiterung `com.maschine.ArenaMode.rewards`: **13** Zahlungen; sämtliche Felder dieser 13 stimmen mit dem Journal überein.
- Einziger fehlender Schlüssel: `0958715f1e1d4733a2589dbd73af4de7:16`.
- Betrag: **2.000 RUB + 200 XP**. Journalzeit und Serverlog bestätigen die Auszahlung am **26.10., 07:54:56**.
- Die zugehörige Mail `c55f6b2cfba566156e0ef6c6` fehlt ebenfalls. Die Importmarkierung, Allocation und Entscheidung sind weiterhin vorhanden.

Das ist eine auseinanderlaufende dauerhafte Historie. Eine allein zurückgespielte oder anderweitig ersetzte Profildatei wäre damit vereinbar; **welcher Schreib- oder Synchronisierungsvorgang sie verursacht hat, ist nicht bewiesen**. Fehlende Mail allein wäre nach abgeschlossener Auszahlung zulässig; der fehlende permanente Zahlungsbeleg ist entscheidend. Erneutes Bezahlen könnte doppelt auszahlen.

## Quellpfad und Konsequenz

[RewardRuntimeInbox](../ArenaMode.Server/Rewards/RewardRuntimeInbox.cs) prüft zuerst offene Snapshots und Runs, danach bei bereits importierten Ergebnissen Journal **und** Profil-Ledger. [RewardStartupProcessor](../ArenaMode.Server/Rewards/RewardStartupProcessor.cs) wandelt diese beiden Verletzungen in `Quarantined` um. [ArenaRewardStartup](../ArenaMode.Server/Rewards/ArenaRewardStartup.cs) meldet sie bei jedem normalen Start erneut; der normale Profilstart geht weiter. Neue Belohnungen der betroffenen Profile bleiben zurückgehalten.

Der Pending-Guard, das GPU-2-Journal und das GPU-2-Profil waren beim Gegencheck auf dem GPU-Host und im lokalen Spiegel **SHA-256-identisch**. Beide Warnungen sind auch im aktuellen Log `spt-MASCHINE11-20261027.log` um 06:27 und 06:48 vorhanden.

**Sicherer Folgeschritt:** betroffene Historien samt passenden Backups gesondert abgleichen und eine ausdrücklich dokumentierte Wiederherstellung bzw. Aussonderung entwerfen. Es gibt dafür derzeit keinen geprüften automatischen Reparaturpfad. Nicht den Guard auf `restored` setzen, nicht Journal/Importmarker löschen und nicht nur den fehlenden Zahlungsbeleg ins Profil kopieren. Zur besseren Diagnose sollte die Warnung künftig Profil und betroffenen Vorgang nennen; das allein repariert die Daten nicht.
