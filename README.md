# ITNetzwerkDokumentierer
Software für die Dokumentation von IT Netzwerken

Webanwendung zur Erfassung von IT-Netzwerken anhand von Plänen: Positionen, Verbindungen, Geräte, Ports und Medien – mit Wegsuche und Übersicht über freie Kapazität.

## Dokumentation

- [Konzept Phase 1](docs/Konzept_Phase1.md) – Ziele, Begriffe, Use Cases, Datenmodell, Architektur
- [Diagramme](docs/diagramme/) – UML als PlantUML-Quelle (`.puml`) und PDF
- [Entwicklung und Test unter Windows](docs/Entwicklung.md) – Docker einrichten, Projekt starten
- [Datenbank](docs/Datenbank.md) – Tabellen, Regeln, Startwerte, Schema ändern

| Datei | Inhalt |
| --- | --- |
| `uc_01_uebersicht` | Use-Case-Übersicht |
| `uc_02_plaene` | Use Cases Pläne |
| `uc_03_topologie` | Use Cases Topologie |
| `uc_04_infrastruktur` | Use Cases Infrastruktur |
| `uc_05_auswertung` | Use Cases Auswertung und vor Ort |
| `kd_01_datenmodell` | Klassendiagramm Datenmodell |
| `kp_01_komponenten` | Komponenten und Betrieb |

## Technik

ASP.NET Core (C#, .NET 10) · PostgreSQL · Vue 3 + TypeScript (Leaflet folgt) · Caddy · Docker Compose

## Starten

Kurzfassung (Details und Windows-Einrichtung: [docs/Entwicklung.md](docs/Entwicklung.md)):

```
copy .env.example .env      # Passwörter eintragen
docker compose up -d --build
```

Dann https://localhost öffnen.
