using Microsoft.EntityFrameworkCore;

namespace NetzDoku.Api.Data;

/// <summary>
/// Datenbankzugriff. Die Entitäten aus dem Klassendiagramm
/// (docs/diagramme/kd_01_datenmodell.puml) folgen im nächsten Schritt.
/// </summary>
public class NetzDbContext(DbContextOptions<NetzDbContext> optionen) : DbContext(optionen)
{
}
