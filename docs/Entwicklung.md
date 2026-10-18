# Entwicklung und Test unter Windows

Diese Anleitung bringt das Projektgerüst auf einem Windows-Rechner zum Laufen. Am Ende zeigt https://localhost eine Statusseite, die bestätigt, dass Frontend, Backend und Datenbank zusammenarbeiten.

## Aufbau

```
ITNetzwerkDokumentierer/
├── backend/            ASP.NET Core Web API (C#, .NET 10) – Visual Studio: NetzDoku.slnx
├── frontend/           Vue 3 + TypeScript (Vite)
├── web/                Caddy: HTTPS, Login, liefert das Frontend aus, leitet /api weiter
├── docs/               Konzept und Diagramme
├── docker-compose.yml  startet alles: db, backend, web
└── .env.example        Vorlage für Passwörter und Einstellungen
```

## 1. Einmalig installieren

1. **WSL 2** (Linux-Unterbau für Docker): PowerShell **als Administrator** öffnen und ausführen:
   ```powershell
   wsl --install
   ```
   Danach neu starten.
2. **Docker Desktop** von https://www.docker.com/products/docker-desktop/ installieren, bei der Installation «Use WSL 2» aktiviert lassen. Docker Desktop starten und warten, bis unten links «Engine running» steht.
3. **Git für Windows** von https://git-scm.com/download/win, falls noch nicht vorhanden.

Hinweis zur Lizenz: Docker Desktop ist für private Nutzung und kleine Unternehmen (unter 250 Mitarbeitende **und** unter 10 Mio. USD Umsatz) kostenlos, sonst kostenpflichtig. Alternativen ohne Lizenzkosten sind **Rancher Desktop** (Open Source, gleiche Befehle) oder Docker Engine direkt in WSL. Auf der Linux-VM für den Betrieb wird ohnehin die freie Docker Engine verwendet.

## 2. Projekt holen

```powershell
cd C:\Projekte
git clone https://github.com/ClaudiaSchn/ITNetzwerkDokumentierer.git
cd ITNetzwerkDokumentierer
```

## 3. Einstellungen anlegen

```powershell
copy .env.example .env
```

Passwort-Hash für den Login erzeugen (Ihr gewünschtes Passwort einsetzen):

```powershell
docker run --rm caddy:2.10-alpine caddy hash-password --plaintext "MeinPasswort"
```

Die Ausgabe beginnt mit `$2a$14$…`. Dann `.env` im Editor öffnen und anpassen:

```
POSTGRES_PASSWORD=EinSicheresPasswort123
LOGIN_USER=it
LOGIN_PASSWORD_HASH='$2a$14$…der erzeugte Hash…'
```

Wichtig: Den Hash in **einfache Anführungszeichen** setzen, sonst werden die `$`-Zeichen falsch gelesen. Das Datenbankpasswort nur aus Buchstaben und Ziffern bilden. Die Datei `.env` bleibt lokal und wird nicht ins Repository übernommen.

## 4. Starten

```powershell
docker compose up -d --build
```

Der erste Start dauert einige Minuten (Images laden, Backend und Frontend bauen). Danach:

1. Browser öffnen: **https://localhost**
2. Zertifikatswarnung bestätigen («Erweitert» → «Weiter zu localhost»). Das Zertifikat stammt von Caddys eigener Zertifizierungsstelle; das ist im LAN gewollt.
3. Mit Benutzer und Passwort aus Schritt 3 anmelden.
4. Die Statusseite zeigt Frontend, Backend und Datenbank. Alle drei grün = Gerüst läuft.

## Nützliche Befehle

| Zweck | Befehl |
| --- | --- |
| Status der Container | `docker compose ps` |
| Protokolle ansehen | `docker compose logs -f backend` (oder `web`, `db`) |
| Nach Codeänderung neu bauen | `docker compose up -d --build` |
| Anhalten (Daten bleiben erhalten) | `docker compose down` |
| **Alles löschen, auch Daten** | `docker compose down -v` (Vorsicht!) |

## Häufige Probleme

| Meldung / Verhalten | Ursache und Lösung |
| --- | --- |
| `POSTGRES_PASSWORD in .env setzen` | `.env` fehlt oder Wert leer → Schritt 3 |
| `port is already allocated` / Port 80 oder 443 belegt | Anderes Programm (z.B. IIS, Skype) nutzt den Port → in `.env` `HTTP_PORT=8080`, `HTTPS_PORT=8443`, dann https://localhost:8443 |
| Login wird immer abgelehnt | Hash nicht in einfachen Anführungszeichen, oder Hash unvollständig kopiert |
| Statusseite: «Backend nicht erreichbar» | `docker compose logs backend` prüfen |
| Datenbank «nicht erreichbar» | `docker compose logs db`; wurde `POSTGRES_PASSWORD` nach dem ersten Start geändert, gilt das alte Passwort weiter (siehe unten) |
| Im Backend-Protokoll beim **allerersten** Start: `fail: … relation "__EFMigrationsHistory" does not exist` | Harmlos: EF Core prüft die Migrationstabelle, bevor sie angelegt ist. Danach läuft die Migration normal durch. |

Das Datenbankpasswort wird beim **allerersten** Start festgelegt. Wer es später ändern will, ändert es in der Datenbank oder löscht – nur solange noch keine echten Daten erfasst sind – mit `docker compose down -v` alles und startet neu.

## Entwickeln ohne Docker (optional)

Für schnelles Arbeiten an einzelnen Teilen:

- **Backend**: `backend/NetzDoku.slnx` in Visual Studio 2022 (ab 17.14) oder VS Code öffnen und starten; es lauscht auf http://localhost:5080. Benötigt das .NET 10 SDK und eine erreichbare PostgreSQL-Datenbank (Verbindung in `appsettings.Development.json`).
- **Frontend**: Node.js 22 installieren, dann im Ordner `frontend`:
  ```powershell
  npm install
  npm run dev
  ```
  Vite startet auf http://localhost:5173 und leitet `/api` an das Backend auf Port 5080 weiter.
