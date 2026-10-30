# Bestehende Team-Harnesse nachführen

Beim abschließenden Killcam-Regressionslauf zeigten vorhandene Prüfer veraltete
Quelltextannahmen. Die Laufzeitlogik wird dafür nicht verändert:

- `PitFireTeamChecks` verlangte noch drei statt der inzwischen fünf Argumente
  von `MarkArmed`. Der Vertrag verlangt nun auch die ausgehandelte Teilnehmerzahl
  und Bossreservierung. Negativbeispiele mit falscher Reihenfolge, fehlenden oder
  fremden Werten werden abgelehnt, nicht durch beliebige Zusatzargumente erlaubt.
- `TeamGiveUpChecks` erwartete einen einzeiligen Scoring-Guard. Seit der
  Assist-Bereinigung enthält dieser einen Block. Beide TDM-Fehlerkontrollen
  werden innerhalb des vollständigen, klammerbalancierten TDM-Zweigs angewandt;
  der benachbarte Deathmatch-Zweig bleibt unberührt. Die konkrete erwartete
  Verhaltensverletzung bleibt dieselbe.
- Der TDM-Fehler `human-slot` wird inzwischen schon vom früher ausgeführten
  Test für die ausgehandelte Gesamtzahl erkannt (`Bots.Count == total - 1`).
  Der Prüfer erwartet genau diese frühere Assertion statt beliebiger Fehler.
- Das BurstIntegration-Double erhält den inzwischen vorhandenen optionalen
  Parameter `grouped`. Die Fehlerkontrolle für die Übergabe an den nativen Spawn
  muss den tatsächlich kompilierten `SpawnPair`-Abschnitt auswählen: ein weiterer
  gleicher Guard existiert inzwischen im separaten Solo-Populationspfad.

Diese Anpassungen sind reine Harness-Wartung und keine Bestätigung eines
nativen Raids. Datiertes Handoff und Killcam-Review führen die abschließenden
Test- und Deployment-Ergebnisse getrennt auf.
