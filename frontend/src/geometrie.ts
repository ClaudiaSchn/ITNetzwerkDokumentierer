// Umrechnung Plan ↔ absolute Koordinaten – gleiche Formeln wie im Backend (Dienste/Geometrie.cs).
//
// Planeinheiten (u, v): Pixel des Planbilds, Ursprung oben links, v nach unten.
// Absolute Koordinaten (x, y): Meter, x nach rechts, y nach oben, (0, 0) = Nullpunkt des Masterplans.
//   x = versatzX + massstab · ( u · cos θ + v · sin θ)
//   y = versatzY + massstab · ( u · sin θ − v · cos θ)

import type { Plan, Punkt } from './api'

export interface Strecke {
  a: Punkt
  b: Punkt
  laengeMeter: number
}

export const HINWEIS_ABWEICHUNG_PROZENT = 2

export function abstand(a: Punkt, b: Punkt): number {
  return Math.hypot(b.u - a.u, b.v - a.v)
}

export function nachAbsolut(plan: Pick<Plan, 'massstab' | 'drehung' | 'versatzX' | 'versatzY'>, p: Punkt) {
  const t = (plan.drehung * Math.PI) / 180
  const sin = Math.sin(t)
  const cos = Math.cos(t)
  return {
    x: plan.versatzX + plan.massstab * (p.u * cos + p.v * sin),
    y: plan.versatzY + plan.massstab * (p.u * sin - p.v * cos)
  }
}

/** Vorschau der Masterplan-Kalibrierung (längengewichteter Massstab). */
export function kalibriereMaster(strecken: Strecke[]) {
  const gueltig = strecken.filter((s) => s.laengeMeter > 0 && abstand(s.a, s.b) > 0)
  if (gueltig.length === 0) return null
  const massstab = gueltig.reduce((m, s) => m + s.laengeMeter, 0) / gueltig.reduce((p, s) => p + abstand(s.a, s.b), 0)
  const je = gueltig.map((s) => s.laengeMeter / abstand(s.a, s.b))
  const abweichungProzent = je.length > 1 ? ((Math.max(...je) - Math.min(...je)) / massstab) * 100 : null
  return { massstab, abweichungProzent }
}

export function formatZahl(wert: number, stellen = 2): string {
  return wert.toLocaleString('de-CH', { minimumFractionDigits: stellen, maximumFractionDigits: stellen })
}

/** Massstab lesbar, z.B. «1 px = 0.090 m». Genug Stellen, damit 3 signifikante Ziffern sichtbar sind. */
export function formatMassstab(meterProPixel: number): string {
  const stellen = Math.max(2, Math.min(6, 2 - Math.floor(Math.log10(meterProPixel))))
  return `1 px = ${formatZahl(meterProPixel, stellen)} m`
}
