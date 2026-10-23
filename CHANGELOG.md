# Changelog

## 1.2.0 – 2026-10-24

- Eigene Gehäusefarben: stationäre Benzinraffinerie rot, Dieselgeneratoren gelb, Methangeneratoren grün.
- Sechs separate Materialinstanzen verwenden die vorhandenen Shader; die Originalgeräte und gemeinsam verwendeten Spielmaterialien bleiben unverändert.
- Zwei eigene BC1-Farbtexturen vereinheitlichen die mehrfarbigen Lackflächen der Raffinerie in Rot. Mip-Stufen, Kachelränder, Alphamodus und Streaminglayout bleiben erhalten; Chunk-Prüfsummen werden erneuert.
- Reproduzierbare Materialzuordnung und Farbwerte in `appearance.json` und dem AppearanceBuilder.
- Item-, Blueprint- und Skill-Identitäten sowie Leistungswerte und Rezepte bleiben erhalten.

## 1.1.0 – 2026-10-23

- Gemeinsamer Container für vier Generatoren und die stationäre Benzinraffinerie.
- Vorhandene Item-, Blueprint- und Skill-Identitäten beibehalten.
- Gewichtszulassungsgrenze für organisches Material in der neuen Raffinerie aufgehoben.
- Ausdrückliche Durchflussgrenze von 72.000/h am Methaneingang beibehalten.
- Installationsskript mit Sicherung und Erkennung der beiden Vorgängerpakete.
- Quellcode, Balanceparameter, Prüfsummen und Anleitung für den Nachbau.
- Statisch validierte Vorschau; vollständiger Spieltest steht aus.
