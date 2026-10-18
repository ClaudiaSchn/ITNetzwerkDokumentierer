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

type Lage = Pick<Plan, 'massstab' | 'drehung' | 'versatzX' | 'versatzY'>

/** Umkehrung von nachAbsolut: absolute Koordinate (m) → Punkt im Planbild */
export function nachPlan(plan: Lage, w: { x: number; y: number }): Punkt {
  const t = (plan.drehung * Math.PI) / 180
  const sin = Math.sin(t)
  const cos = Math.cos(t)
  const dx = (w.x - plan.versatzX) / plan.massstab
  const dy = (w.y - plan.versatzY) / plan.massstab
  return { u: dx * cos + dy * sin, v: dx * sin - dy * cos }
}

/**
 * Vorschau der Referenzierung über zwei Passpunkte (gleiche Rechnung wie Geometrie.RichteAus im Backend):
 * p1/p2 im neuen Plan, w1/w2 dieselben Punkte absolut (aus dem Bezugsplan umgerechnet).
 */
export function richteAus(p1: Punkt, w1: { x: number; y: number }, p2: Punkt, w2: { x: number; y: number }): Lage | null {
  const zr = p2.u - p1.u
  const zi = -(p2.v - p1.v)
  const wr = w2.x - w1.x
  const wi = w2.y - w1.y
  const n = zr * zr + zi * zi
  if (n === 0) return null
  const ar = (wr * zr + wi * zi) / n
  const ai = (wi * zr - wr * zi) / n
  const massstab = Math.hypot(ar, ai)
  if (massstab === 0) return null
  return {
    massstab,
    drehung: (Math.atan2(ai, ar) * 180) / Math.PI,
    versatzX: w1.x - (ar * p1.u + ai * p1.v),
    versatzY: w1.y - (ai * p1.u - ar * p1.v)
  }
}

/** Drehung lesbar, z.B. «−90.0°» (0 bei sehr kleinen Werten) */
export function formatDrehung(grad: number): string {
  const g = Math.abs(grad) < 0.05 ? 0 : grad
  return `${formatZahl(g, 1)}°`
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
