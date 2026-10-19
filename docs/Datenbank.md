# Datenbank

Das Schema setzt das Klassendiagramm [`diagramme/kd_01_datenmodell.puml`](diagramme/kd_01_datenmodell.puml) in PostgreSQL um. Es entsteht aus den C#-Klassen in `backend/NetzDoku.Api/Modell/` und der Konfiguration in `Data/NetzDbContext.cs` (Entity Framework Core, Code First).

## Grundsätze

- **Namen:** Tabellen im Plural, Spalten in `snake_case` (`positionen.standort_id`), damit SQL ohne Anführungszeichen lesbar bleibt.
- **Schlüssel:** jede Tabelle hat `id` (Ganzzahl, automatisch vergeben).
- **Optimistisches Sperren:** über die PostgreSQL-Systemspalte `xmin`. Sie ändert sich bei jeder Änderung einer Zeile automatisch; in C# heisst sie `Version`. Speichert jemand eine veraltete Version, wirft EF Core eine `DbUpdateConcurrencyException`.
- **Aufzählungen** werden als Text gespeichert (`'Master'`, `'Vorne'`, `'Offen'` …).
- **Migration beim Start:** Das Backend bringt die Datenbank beim Start automatisch auf den neuesten Stand und legt beim allerersten Start Stammdaten an.

## Tabellen

| Tabelle | Inhalt | Wichtige Spalten |
| --- | --- | --- |
| `standorte` | Firmenstandorte | `name` (eindeutig) |
| `plaene` | Pläne (PDF) mit Referenzierung | `standort_id`, `ist_masterplan`, `methode`, `stockwerk`, `massstab`, `drehung`, `versatz_x/y`, `bezugsplan_id`, `ersetzt_plan_id`, `aktiv`, `referenziert`, `pdf_datei`, `bild_datei`, `bild_breite`, `bild_hoehe`, `eigener_massstab_daten` (jsonb) |
| `passpunkte` | gespeicherte Passpunkte einer Referenzierung | `plan_id`, `nummer` (1/2), `plan_x/y`, `bezug_x/y` |
| `messstrecken` | bekannte Strecken der Kalibrierung (Masterplan, eigener Massstab) | `plan_id`, `nummer`, `u1/v1`, `u2/v2`, `laenge_meter` |
| `positionen` | Rack, Bodendose … | `standort_id`, `positionstyp_id`, `bezeichnung`, `x`, `y`, `stockwerk` |
| `verbindungen` | «Verbindung existiert» zwischen zwei Positionen | `standort_id`, `position_a_id`, `position_b_id` |
| `kommentare` | Kommentare an Positionen | `position_id`, `text`, `verfasser`, `status`, `erstellt_am` |
| `geraete` | Geräte an Positionen | `position_id`, `geraetetyp_id`, `bezeichnung` |
| `ports` | Ports der Geräte | `geraet_id`, `nummer`, `seite` (Einzel/Vorne/Hinten), `portart_id`, `bezeichnung` |
| `medien` | Kabel/Funkstrecken auf Verbindungen | `verbindung_id`, `standort_id`, `medientyp_id`, `kabelnummer`, `anzahl_anschluesse` |
| `anschluesse` | Enden der Adern/Fasern | `medium_id`, `ende` (A/B), `nummer`, `port_id` |
| `positionstypen`, `portarten` | Stammdaten | `name` (eindeutig) |
| `medientypen` | Stammdaten | `name`; erlaubte Portarten in `medientyp_erlaubte_portarten` |
| `geraetetypen` | Gerätevorlagen | `name`, `kategorie` (Durchgang/Endgeraet) |
| `portvorlagen` | Ports einer Gerätevorlage | `geraetetyp_id`, `nummer`, `seite`, `portart_id` |

Abweichung vom Klassendiagramm: `verbindungen` und `medien` tragen zusätzlich `standort_id`. Das ist nötig, damit die Datenbank die Eindeutigkeit der Kabelnummer pro Standort selbst prüfen kann, und vereinfacht Abfragen.

Bei Methode «Eigener Massstab» steht der Massstab in `messstrecken`; Ankerpunkt (Klick im Bezugsplan oder Koordinate in Metern) und Drehung (Winkel oder Richtung in beiden Plänen) stehen als JSON in `eigener_massstab_daten`. So lässt sich die Lage nachvollziehen und bei Änderungen am Bezugsplan neu berechnen. Ein angeklickter Anker oder eine Richtung wandert mit dem Bezugsplan mit; eine eingegebene Koordinate und ein Winkel bleiben fest.

Beim Import wird die PDF-Seite einmalig in ein PNG umgewandelt (`bild_datei`, längste Kante höchstens 6000 px, höchstens 200 dpi). Planeinheiten (u, v) sind Pixel dieses Bilds, Ursprung oben links, v nach unten. PDF und Bild liegen in der Planablage (Docker-Volume `plan-ablage`).

Absolute Koordinaten (x, y) sind Meter: x nach rechts, y nach oben, (0, 0) = Nullpunkt des Masterplans. Umrechnung Planpunkt (u, v) → (x, y), θ = `drehung` in Grad (Code: `Dienste/Geometrie.cs` und `frontend/src/geometrie.ts`):

```
x = versatz_x + massstab · ( u · cos θ + v · sin θ)
y = versatz_y + massstab · ( u · sin θ − v · cos θ)
```

Das Minuszeichen beim y entsteht, weil v im Bild nach unten zählt, y aber nach oben.

**Kalibrierung des Masterplans:** Aus zwei bekannten Strecken wird der Massstab bestimmt, gewichtet nach Länge (Summe Meter ÷ Summe Pixel), weil längere Strecken genauer sind. Die Abweichung zwischen den Strecken dient als Plausibilitätsprüfung (Hinweis ab 2 %). Der Versatz ergibt sich aus dem Nullpunkt (u₀, v₀): `versatz_x = −massstab · u₀`, `versatz_y = massstab · v₀`. Bis zur Kalibrierung ist `referenziert = false` und die Werte sind Platzhalter.

## Wo welche Regel gilt

Die Nummern beziehen sich auf die Regeln im [Konzept](Konzept_Phase1.md#regeln).

| Regel | Durchgesetzt von | Umsetzung |
| --- | --- | --- |
| 1 Ein aktiver Masterplan pro Standort | Datenbank | Index `ux_plaene_ein_masterplan_pro_standort` |
| 1 Masterplan: Stockwerk 0, kein Bezugsplan, Methode Master | Datenbank | `ck_plaene_masterplan`, `ck_plaene_methode_passt` |
| – Jeder andere Plan hat einen Bezugsplan, Massstab > 0 | Datenbank | `ck_plaene_bezugsplan_noetig`, `ck_plaene_massstab_positiv` |
| 2 Bezugsplan am selben Standort, keine Zyklen | Backend | Prüfung beim Importieren und Referenzieren |
| – Bezugsplan muss selbst referenziert sein; Passpunkte ≥ 20 px auseinander | Backend | Prüfung beim Referenzieren |
| – Lage eines Plans geändert → abhängige Pläne werden neu berechnet | Backend | `Referenzierung.NachfolgerNeuBerechnen` über die gespeicherten Passpunkte, in derselben Transaktion |
| – Plan löschen nur, wenn kein anderer Plan darauf referenziert ist; Masterplan nie | Backend + Datenbank | Backend meldet die Pläne (409), Fremdschlüssel `ON DELETE RESTRICT` |
| 4 Positionsbezeichnung eindeutig pro Standort | Datenbank | Index auf `positionen (standort_id, bezeichnung)` |
| – Position nur auf kalibriertem Plan, innerhalb des Planbilds; Stockwerk −10…100 | Backend | Prüfung beim Erfassen und Verschieben (`PositionEndpunkte`) |
| 10 Position löschen nur ohne Geräte und Verbindungen | Backend + Datenbank | Backend meldet die Anzahl (409), Fremdschlüssel sichern zusätzlich ab |
| 4 Kabelnummer eindeutig pro Standort | Datenbank | Index auf `medien (standort_id, kabelnummer)` |
| 5 Zwei verschiedene Positionen | Datenbank | `ck_verbindungen_zwei_positionen` |
| 5 Höchstens eine Verbindung pro Paar (A–B = B–A) | Datenbank | Index `ux_verbindungen_positionspaar` |
| 5 Beide Positionen am selben Standort | Backend | Prüfung beim Anlegen |
| – Verbindung löschen nur ohne Medien | Backend + Datenbank | Backend meldet die Anzahl (409), Fremdschlüssel `ON DELETE RESTRICT` |
| – Verbindungen nur im Übersichtsmodus anlegen | Oberfläche | Masterplan oder «alle Stockwerke»; «Verbinden» schaltet den Übersichtsmodus bei Bedarf ein |
| 6 Pro Medium, Ende und Nummer ein Anschluss | Datenbank | Index auf `anschluesse (medium_id, ende, nummer)` |
| 6 Nummer ≤ Anzahl Anschlüsse | Backend | Anschlüsse werden beim Anlegen des Mediums erzeugt; beim Ändern der Anzahl ergänzt bzw. entfernt |
| – Medium ändern: Medientyp nur ohne belegte Anschlüsse, verkleinern nur, wenn die wegfallenden frei sind | Backend | Prüfung beim Ändern (409) |
| 10 Medium entfernen nur ohne belegte Anschlüsse | Backend + Datenbank | Backend meldet die Anzahl (409); Anschlüsse werden mitgelöscht |
| 7 Ende A nur an Ports an Position A | Backend | Prüfung beim Anschliessen |
| 8 Port mit höchstens einem Anschluss belegt | Datenbank | Index auf `anschluesse (port_id)` |
| 8 Portart für Medientyp erlaubt | Backend | Prüfung beim Anschliessen |
| 9 Pro Gerät, Nummer und Seite ein Port | Datenbank | Index auf `ports (geraet_id, nummer, seite)` |
| 9 Durchgang: Vorne/Hinten, Endgerät: Einzel | Backend | Ports werden aus der Vorlage erzeugt |
| – Gerätebezeichnung eindeutig pro Position | Backend | Prüfung beim Platzieren und Umbenennen (409) |
| 10 Gerät entfernen nur ohne belegte Ports | Backend + Datenbank | Backend meldet die Anzahl (409), Fremdschlüssel Anschluss → Port `ON DELETE RESTRICT`; Ports werden mitgelöscht |
| 10 Löschen nur, wenn nichts mehr daran hängt | Datenbank | Fremdschlüssel mit `ON DELETE RESTRICT` |
| 12 Gleichzeitige Änderungen | Datenbank + EF Core | `xmin` als Versionsnummer; der Browser schickt die geladene Version mit, bei Abweichung Antwort 409 |

Was beim Löschen automatisch mitgelöscht wird: Kommentare einer Position, Passpunkte und Messstrecken eines Plans, Ports eines Geräts, Anschlüsse eines Mediums, Portvorlagen eines Gerätetyps. Alles andere blockiert das Löschen, solange etwas daran hängt.

## Startwerte

Beim ersten Start werden angelegt (danach in der Anwendung pflegbar):

- **Positionstypen:** Rack, Verteiler, Bodendose, Wanddose, Richtfunksender
- **Portarten:** RJ45, LC, SC, SFP, Funk
- **Medientypen:** LWL Singlemode (LC, SC), LWL Multimode (LC, SC), Kupfer (RJ45), Richtfunk (Funk)
- **Gerätetypen:** Patchpanel 24× RJ45, Netzwerkdose 2× RJ45, LWL-Patchpanel 12× LC (alle Durchgang), Switch 24× RJ45 + 4× SFP (Endgerät)

## Schema ändern

1. Klassen in `Modell/` bzw. Konfiguration in `Data/NetzDbContext.cs` anpassen.
2. Im Ordner `backend` eine Migration erzeugen (benötigt das .NET 10 SDK):
   ```powershell
   dotnet tool restore
   dotnet ef migrations add NameDerAenderung --project NetzDoku.Api --output-dir Data/Migrationen
   ```
3. Die erzeugte Datei in `Data/Migrationen/` kurz durchsehen und mit committen.
4. Beim nächsten Start wendet das Backend die Migration automatisch an.

Direkt in die Datenbank schauen (Container muss laufen):

```powershell
docker compose exec db psql -U netzdoku -d netzdoku
```

Nützlich darin: `\dt` (Tabellen), `\d positionen` (Spalten, Indizes, Constraints), `\q` (beenden).
