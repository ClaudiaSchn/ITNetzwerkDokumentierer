// Hilfen rund um die Planliste eines Standorts
import type { PlanListe } from './api'
import { stockwerkText } from './format'

/** Anzeige eines Plans in Auswahllisten, z.B. «Gebäude A – EG · EG» oder «Campusplan (Masterplan)» */
export function planText(p: PlanListe): string {
  return `${p.bezeichnung}${p.istMasterplan ? ' (Masterplan)' : ` · ${stockwerkText(p.stockwerk)}`}`
}

/**
 * Pläne, die als Bezugsplan für `planId` in Frage kommen: referenziert, nicht der Plan selbst und
 * keiner, der (über eine Kette) auf ihm aufbaut – sonst entstünde ein Kreis.
 */
export function waehlbareBezugsplaene(planId: number, plaene: PlanListe[]): PlanListe[] {
  const nachfolger = new Set<number>()
  let neu = [planId]
  while (neu.length) {
    neu = plaene
      .filter((p) => p.bezugsplanId !== null && neu.includes(p.bezugsplanId) && !nachfolger.has(p.id))
      .map((p) => p.id)
    neu.forEach((id) => nachfolger.add(id))
  }
  return plaene.filter((p) => p.referenziert && p.id !== planId && !nachfolger.has(p.id))
}
