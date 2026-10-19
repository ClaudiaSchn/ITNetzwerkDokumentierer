<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { api, type Anschluss, type GeraetWahl, type MediumAnschluesse, type Portauswahl, type PortWahl } from '../api'

/**
 * Seitenleiste eines Mediums: jeden Anschluss (Faser/Ader) an Ende A und B an einen Port hängen
 * oder lösen; mehrere fortlaufend auf einmal.
 */
const props = defineProps<{ mediumId: number }>()
const emit = defineEmits<{ schliessen: []; geaendert: [] }>()

type Ende = 'A' | 'B'

const daten = ref<MediumAnschluesse | null>(null)
const auswahl = ref<Portauswahl | null>(null)
const fehler = ref<string | null>(null)
const arbeitet = ref(false)

async function laden() {
  fehler.value = null
  try {
    const [d, a] = await Promise.all([api.mediumAnschluesse(props.mediumId), api.portauswahl(props.mediumId)])
    daten.value = d
    auswahl.value = a
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
watch(() => props.mediumId, laden, { immediate: true })

/** Zeilen: pro Nummer der Anschluss an Ende A und an Ende B */
const zeilen = computed(() => {
  const n = daten.value?.medium.anzahlAnschluesse ?? 0
  const liste = daten.value?.anschluesse ?? []
  return Array.from({ length: n }, (_, i) => ({
    nummer: i + 1,
    A: liste.find((a) => a.ende === 'A' && a.nummer === i + 1)!,
    B: liste.find((a) => a.ende === 'B' && a.nummer === i + 1)!
  }))
})

const geraeteAn = (ende: Ende): GeraetWahl[] => (ende === 'A' ? auswahl.value?.a : auswahl.value?.b) ?? []
const positionName = (ende: Ende) => (ende === 'A' ? daten.value?.positionA.bezeichnung : daten.value?.positionB.bezeichnung) ?? ''

const seiteKurz = (s: PortWahl['seite']) => (s === 'Vorne' ? ' vorne' : s === 'Hinten' ? ' hinten' : '')
const portText = (p: { nummer: number; seite: PortWahl['seite']; bezeichnung: string | null }) =>
  `${p.nummer}${seiteKurz(p.seite)}${p.bezeichnung ? ` (${p.bezeichnung})` : ''}`

/** Freie Ports mit passender Portart an diesem Ende */
const freiePorts = (ende: Ende) =>
  geraeteAn(ende)
    .map((g) => ({ g, ports: g.ports.filter((p) => p.erlaubt && p.anschlussId === null) }))
    .filter((x) => x.ports.length > 0)

const keineGeraete = (ende: Ende) => geraeteAn(ende).length === 0
/** Hinweis nur, solange an diesem Ende noch etwas anzuschliessen ist */
const offen = (ende: Ende) => zeilen.value.some((z) => z[ende] && !z[ende].port)
const keinePassenden = (ende: Ende) => offen(ende) && !keineGeraete(ende) && freiePorts(ende).length === 0

async function setzen(a: Anschluss, portId: number | null) {
  fehler.value = null
  arbeitet.value = true
  try {
    daten.value = await api.anschlussSetzen(a.id, a.version, portId)
    auswahl.value = await api.portauswahl(props.mediumId)
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
    await laden()
  } finally {
    arbeitet.value = false
  }
}

function waehlen(a: Anschluss, ereignis: Event) {
  const wert = (ereignis.target as HTMLSelectElement).value
  if (wert) setzen(a, Number(wert))
}

// --- Fortlaufend anschliessen ---
const serie = reactive({ ende: 'A' as Ende, geraetId: null as number | null, seite: 'Hinten', abPort: 1, abNummer: 1, anzahl: 1 })
const serieGeraet = computed(() => geraeteAn(serie.ende).find((g) => g.id === serie.geraetId) ?? null)

watch(() => [serie.ende, daten.value?.medium.id], () => {
  serie.geraetId = geraeteAn(serie.ende)[0]?.id ?? null
  // erste freie Nummer an diesem Ende vorschlagen
  const frei = zeilen.value.find((z) => !z[serie.ende]?.port)
  serie.abNummer = frei?.nummer ?? 1
  serie.anzahl = Math.max(1, (daten.value?.medium.anzahlAnschluesse ?? 1) - serie.abNummer + 1)
})
watch(() => serie.geraetId, () => {
  const g = serieGeraet.value
  if (!g) return
  serie.seite = g.kategorie === 'Durchgang' ? 'Hinten' : 'Einzel'
  const p = g.ports.find((x) => x.erlaubt && x.anschlussId === null && (g.kategorie !== 'Durchgang' || x.seite === serie.seite))
  serie.abPort = p?.nummer ?? 1
})

async function fortlaufend() {
  if (!daten.value || serie.geraetId === null) return
  fehler.value = null
  arbeitet.value = true
  try {
    daten.value = await api.fortlaufendAnschliessen(daten.value.medium.id, {
      ende: serie.ende,
      abNummer: serie.abNummer,
      anzahl: serie.anzahl,
      geraetId: serie.geraetId,
      seite: serieGeraet.value?.kategorie === 'Durchgang' ? serie.seite : null,
      abPortnummer: serie.abPort
    })
    auswahl.value = await api.portauswahl(props.mediumId)
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    arbeitet.value = false
  }
}
</script>

<template>
  <aside class="seitenleiste breit">
    <div class="kopf">
      <h2>{{ daten ? `Kabel ${daten.medium.kabelnummer}` : 'Kabel' }}</h2>
      <button type="button" class="zweitrangig klein-btn" title="Zurück (Esc)" @click="emit('schliessen')">← Zurück</button>
    </div>
    <template v-if="daten">
      <p class="leise klein">
        {{ daten.medium.medientyp }} · {{ daten.medium.anzahlAnschluesse }}
        {{ daten.medium.anzahlAnschluesse === 1 ? 'Anschluss' : 'Anschlüsse' }} pro Ende ·
        angeschlossen A {{ daten.medium.belegtA }}/{{ daten.medium.anzahlAnschluesse }},
        B {{ daten.medium.belegtB }}/{{ daten.medium.anzahlAnschluesse }}
      </p>

      <p v-for="e in (['A', 'B'] as const)" v-show="keineGeraete(e) || keinePassenden(e)" :key="e" class="hinweis klein">
        <template v-if="keineGeraete(e)">An «{{ positionName(e) }}» (Ende {{ e }}) gibt es noch kein Gerät. Bitte zuerst dort ein Gerät platzieren.</template>
        <template v-else>An «{{ positionName(e) }}» (Ende {{ e }}) gibt es keinen freien Port mit passender Portart.</template>
      </p>

      <details class="serie">
        <summary>Mehrere fortlaufend anschliessen</summary>
        <div class="raster">
          <label for="se-ende">Ende</label>
          <select id="se-ende" v-model="serie.ende">
            <option value="A">A – {{ positionName('A') }}</option>
            <option value="B">B – {{ positionName('B') }}</option>
          </select>
          <label for="se-geraet">Gerät</label>
          <select id="se-geraet" v-model="serie.geraetId">
            <option v-for="g in geraeteAn(serie.ende)" :key="g.id" :value="g.id">{{ g.bezeichnung }}</option>
          </select>
          <template v-if="serieGeraet?.kategorie === 'Durchgang'">
            <label for="se-seite">Seite</label>
            <select id="se-seite" v-model="serie.seite">
              <option value="Hinten">hinten</option>
              <option value="Vorne">vorne</option>
            </select>
          </template>
          <label for="se-ab">Anschlüsse ab</label>
          <span class="zeile"><input id="se-ab" v-model.number="serie.abNummer" type="number" min="1" />
            <label for="se-anzahl" class="inline">Anzahl</label>
            <input id="se-anzahl" v-model.number="serie.anzahl" type="number" min="1" /></span>
          <label for="se-port">ab Port</label>
          <input id="se-port" v-model.number="serie.abPort" type="number" min="1" />
        </div>
        <p class="leise klein">
          Beispiel: Anschlüsse ab 1, Anzahl 12, ab Port 1 → Faser 1 an Port 1 … Faser 12 an Port 12.
          Gelingt ein Anschluss nicht, wird nichts gespeichert.
        </p>
        <button type="button" class="klein-btn" :disabled="arbeitet || serie.geraetId === null" @click="fortlaufend">Anschliessen</button>
      </details>

      <p v-if="fehler" class="fehler">{{ fehler }}</p>

      <table class="anschluesse">
        <thead>
          <tr>
            <th>Nr.</th>
            <th>A – {{ positionName('A') }}</th>
            <th>B – {{ positionName('B') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="z in zeilen" :key="z.nummer">
            <td class="nr">{{ z.nummer }}</td>
            <td v-for="e in (['A', 'B'] as const)" :key="e">
              <span v-if="z[e]?.port" class="belegt">
                <span>{{ z[e].port!.geraet }} · {{ portText(z[e].port!) }}</span>
                <button type="button" class="symbol" :disabled="arbeitet" title="Anschluss lösen"
                  :aria-label="`Anschluss ${e}${z.nummer} lösen`" @click="setzen(z[e], null)">✕</button>
              </span>
              <select v-else-if="z[e]" :disabled="arbeitet || freiePorts(e).length === 0" :aria-label="`Anschluss ${e}${z.nummer}`"
                @change="waehlen(z[e], $event)">
                <option value="">– frei –</option>
                <optgroup v-for="x in freiePorts(e)" :key="x.g.id" :label="x.g.bezeichnung">
                  <option v-for="p in x.ports" :key="p.id" :value="p.id">{{ x.g.bezeichnung }} · {{ portText(p) }}</option>
                </optgroup>
              </select>
            </td>
          </tr>
        </tbody>
      </table>
    </template>
    <p v-else-if="fehler" class="fehler">{{ fehler }}</p>
    <p v-else class="leise">Wird geladen …</p>
  </aside>
</template>

<style scoped>
.breit {
  width: 32rem;
}
.hinweis {
  background: #fff8c5;
  border: 1px solid #d4a72c;
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
}
.serie {
  margin: 0.6rem 0;
  font-size: 0.9rem;
}
.serie summary {
  cursor: pointer;
  color: var(--akzent);
}
.raster {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.35rem 0.6rem;
  align-items: center;
  margin: 0.5rem 0;
}
.raster label {
  margin: 0;
}
.zeile {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
label.inline {
  margin: 0;
  font-weight: 400;
}
.raster input[type='number'] {
  width: 5rem;
}
.anschluesse {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  margin-top: 0.5rem;
  table-layout: fixed;
}
.anschluesse th {
  text-align: left;
  border-bottom: 1px solid var(--rand);
  padding: 0.25rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.anschluesse th:first-child {
  width: 2.2rem;
}
.anschluesse td {
  padding: 0.15rem 0.25rem;
  border-bottom: 1px solid #eef1f4;
}
.anschluesse select {
  width: 100%;
  font-size: 0.85rem;
  padding: 0.2rem 0.3rem;
}
.nr {
  color: var(--text-leise);
  font-variant-numeric: tabular-nums;
}
.belegt {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.2rem;
  color: #1b7c83;
  font-weight: 600;
}
.symbol {
  background: none;
  color: var(--text-leise);
  border: 1px solid transparent;
  padding: 0 0.35rem;
  font-size: 0.8rem;
}
.symbol:not(:disabled):hover {
  color: var(--schlecht);
  border-color: var(--schlecht);
}
</style>
