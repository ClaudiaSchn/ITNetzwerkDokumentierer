/** Stockwerk lesbar: 0 → «EG», 2 → «2. OG», −1 → «1. UG». */
export function stockwerkText(stockwerk: number): string {
  if (stockwerk === 0) return 'EG'
  return stockwerk > 0 ? `${stockwerk}. OG` : `${-stockwerk}. UG`
}

/**
 * Vorschlag für die nächste Bezeichnung bei Serienerfassung: die letzte Zahl wird um 1 erhöht,
 * führende Nullen bleiben erhalten («BD-0815» → «BD-0816», «Dose 9» → «Dose 10»).
 * Ohne Zahl am Ende gibt es keinen Vorschlag.
 */
export function naechsteBezeichnung(bezeichnung: string): string {
  const treffer = /^(.*?)(\d+)(\D*)$/.exec(bezeichnung)
  if (!treffer) return ''
  const [, vorne, zahl, hinten] = treffer
  const neu = String(Number(zahl) + 1).padStart(zahl.length, '0')
  return `${vorne}${neu}${hinten}`
}

/** Zeitpunkt kurz und lesbar: «19.10.2026, 14:05» */
export function formatZeit(iso: string): string {
  return new Date(iso).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
}

const VERFASSER_SCHLUESSEL = 'netzdoku.verfasser'

/** Zuletzt verwendeter Verfasser (nur in diesem Browser gespeichert) */
export function verfasserLesen(): string {
  try {
    return localStorage.getItem(VERFASSER_SCHLUESSEL) ?? ''
  } catch {
    return ''
  }
}

export function verfasserMerken(name: string) {
  try {
    localStorage.setItem(VERFASSER_SCHLUESSEL, name)
  } catch {
    // Speicher nicht verfügbar (z.B. privates Fenster) – dann eben nicht merken
  }
}
