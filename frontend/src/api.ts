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

export interface Passpunkt {
  nummer: number
  planU: number
  planV: number
  bezugU: number
  bezugV: number
}

/** Paar aus Punkt im Plan und demselben Punkt im Bezugsplan */
export interface PasspunktEingabe {
  planU: number
  planV: number
  bezugU: number
  bezugV: number
}

/** Linie im Plan (Planeinheiten), z.B. eine Richtung */
export interface LinieDaten {
  u1: number
  v1: number
  u2: number
  v2: number
}

/** Angaben einer Referenzierung mit eigenem Massstab (Massstab über die Messstrecken) */
export interface EigenerMassstab {
  anker: Punkt
  ankerBezug: Punkt | null
  ankerX: number | null
  ankerY: number | null
  winkel: number | null
  richtungPlan: LinieDaten | null
  richtungBezug: LinieDaten | null
}

/** Eintrag in der Planliste eines Standorts */
export interface PlanListe {
  id: number
  bezeichnung: string
  stockwerk: number
  istMasterplan: boolean
  methode: string
  referenziert: boolean
  bezugsplanId: number | null
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
  bezugsplanId: number | null
  passpunkte: Passpunkt[]
  eigenerMassstab: EigenerMassstab | null
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

  plaene: (standortId: number) => anfrage<PlanListe[]>(`/api/standorte/${standortId}/plaene`),

  planImportieren: (standortId: number, datei: File, bezeichnung: string, stockwerk: number, bezugsplanId: number) => {
    const formular = new FormData()
    formular.append('datei', datei)
    if (bezeichnung.trim()) formular.append('bezeichnung', bezeichnung.trim())
    formular.append('stockwerk', String(stockwerk))
    formular.append('bezugsplanId', String(bezugsplanId))
    return anfrage<Plan>(`/api/standorte/${standortId}/plaene`, { method: 'POST', body: formular })
  },

  planReferenzieren: (planId: number, version: number, bezugsplanId: number, passpunkte: PasspunktEingabe[]) =>
    anfrage<Plan>(`/api/plaene/${planId}/referenzierung`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ version, bezugsplanId, passpunkte })
    }),

  planEigenerMassstab: (planId: number, version: number, bezugsplanId: number, strecken: StreckeEingabe[], angaben: EigenerMassstab) =>
    anfrage<Plan>(`/api/plaene/${planId}/eigener-massstab`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ version, bezugsplanId, strecken, ...angaben })
    }),

  planAendern: (planId: number, version: number, bezeichnung: string, stockwerk: number) =>
    anfrage<Plan>(`/api/plaene/${planId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ version, bezeichnung, stockwerk })
    }),

  planLoeschen: (planId: number, version: number) =>
    anfrage<void>(`/api/plaene/${planId}?version=${version}`, { method: 'DELETE' }),

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

  /** Positionen im Plan; mit `alleStockwerke` auch die anderer Stockwerke (Masterplan: immer alle) */
  positionenImPlan: (planId: number, alleStockwerke = false) =>
    anfrage<PositionImPlan[]>(`/api/plaene/${planId}/positionen${alleStockwerke ? '?uebersicht=true' : ''}`),

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
