// Zugriff auf das Backend. Fehler kommen als Problem Details (title = deutsche Meldung).

export interface PlanKurz {
  id: number
  bezeichnung: string
  referenziert: boolean
}

export interface Punkt {
  u: number
  v: number
}

export interface Messstrecke {
  nummer: number
  u1: number
  v1: number
  u2: number
  v2: number
  laengeMeter: number
  massstab: number
}

export interface StreckeEingabe {
  u1: number
  v1: number
  u2: number
  v2: number
  laengeMeter: number
}

export interface Standort {
  id: number
  name: string
  masterplan: PlanKurz | null
}

export interface Plan {
  id: number
  version: number
  standortId: number
  bezeichnung: string
  stockwerk: number
  istMasterplan: boolean
  methode: string
  bildBreite: number
  bildHoehe: number
  bildUrl: string
  pdfUrl: string
  referenziert: boolean
  massstab: number
  drehung: number
  versatzX: number
  versatzY: number
  nullpunkt: Punkt | null
  messstrecken: Messstrecke[]
  abweichungProzent: number | null
}

export interface Positionstyp {
  id: number
  name: string
}

/** Position, wie sie in einem Plan erscheint (u, v = Lage im Planbild, x, y = Meter) */
export interface PositionImPlan {
  id: number
  version: number
  bezeichnung: string
  positionstypId: number
  positionstyp: string
  stockwerk: number
  x: number
  y: number
  u: number
  v: number
  anzahlGeraete: number
  anzahlVerbindungen: number
}

export interface PositionDaten {
  bezeichnung: string
  positionstypId: number
  stockwerk: number
}

export interface Status {
  backend: string
  datenbank: string
  version: string
  zeit: string
}

export class ApiFehler extends Error {}

async function anfrage<T>(url: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  // Pflicht-Header für ändernde Aufrufe (Schutz gegen Cross-Site-Request-Forgery)
  if (init.method && init.method !== 'GET') headers.set('X-NetzDoku', '1')

  let antwort: Response
  try {
    antwort = await fetch(url, { ...init, headers })
  } catch {
    throw new ApiFehler('Der Server ist nicht erreichbar.')
  }

  if (!antwort.ok) {
    let meldung = `Fehler ${antwort.status}`
    try {
      const problem = await antwort.json()
      if (problem?.title) meldung = problem.title
    } catch {
      /* keine JSON-Antwort */
    }
    throw new ApiFehler(meldung)
  }
  if (antwort.status === 204) return undefined as T
  return (await antwort.json()) as T
}

export const api = {
  status: () => anfrage<Status>('/api/health'),

  standorte: () => anfrage<Standort[]>('/api/standorte'),

  standort: (id: number) => anfrage<Standort>(`/api/standorte/${id}`),

  standortAnlegen: (name: string) =>
    anfrage<Standort>('/api/standorte', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name })
    }),

  masterplanImportieren: (standortId: number, datei: File, bezeichnung: string) => {
    const formular = new FormData()
    formular.append('datei', datei)
    if (bezeichnung.trim()) formular.append('bezeichnung', bezeichnung.trim())
    return anfrage<Plan>(`/api/standorte/${standortId}/masterplan`, { method: 'POST', body: formular })
  },

  plan: (id: number) => anfrage<Plan>(`/api/plaene/${id}`),

  masterplanKalibrieren: (planId: number, version: number, nullpunkt: Punkt, strecken: StreckeEingabe[]) =>
    anfrage<Plan>(`/api/plaene/${planId}/kalibrierung`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ version, nullpunkt, strecken })
    }),

  positionstypen: () => anfrage<Positionstyp[]>('/api/positionstypen'),

  positionstypAnlegen: (name: string) =>
    anfrage<Positionstyp>('/api/positionstypen', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name })
    }),

  positionenImPlan: (planId: number) => anfrage<PositionImPlan[]>(`/api/plaene/${planId}/positionen`),

  positionErfassen: (standortId: number, planId: number, punkt: Punkt, daten: PositionDaten) =>
    anfrage<PositionImPlan>(`/api/standorte/${standortId}/positionen`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ planId, u: punkt.u, v: punkt.v, ...daten })
    }),

  /** Ändert eine Position; mit `punkt` wird sie zusätzlich verschoben. */
  positionAendern: (id: number, version: number, planId: number, daten: PositionDaten, punkt?: Punkt) =>
    anfrage<PositionImPlan>(`/api/positionen/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ version, planId, ...daten, u: punkt?.u ?? null, v: punkt?.v ?? null })
    }),

  positionLoeschen: (id: number, version: number) =>
    anfrage<void>(`/api/positionen/${id}?version=${version}`, { method: 'DELETE' })
}
