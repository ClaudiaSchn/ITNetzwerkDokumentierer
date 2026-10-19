<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Geraet, type Medientyp, type PositionImPlan, type Verlauf, type Weg } from '../api'
import { formatZahl } from '../geometrie'
import { stockwerkText } from '../format'
import VerlaufAnzeige from './VerlaufAnzeige.vue'

/**
 * Seitenleiste «Weg suchen»: von Position zu Position (über Verbindungen, optional nur mit freier Kapazität),
 * von Gerät zu Gerät (bestehende Leitungen) und Leitungsverlauf eines Ports. Das Ergebnis wird im Plan markiert.
 */
const props = defineProps<{
  standortId: number
  positionen: PositionImPlan[]
  medientypen: Medientyp[]
  /** Vorgabe für den Start (z.B. ausgewählte Position) */
  startPosition: number | null
  /** Leitungsverlauf dieses Ports anzeigen */
  verlaufPort: number | null
}>()
const emit = defineEmits<{
  fertig: []
  markieren: [verbindungIds: number[], positionIds: number[]]
  geraet: [id: number]
  medium: [id: number]
}>()

type Art = 'positionen' | 'geraete' | 'verlauf'
const art = ref<Art>(props.verlaufPort !== null ? 'verlauf' : 'positionen')
const fehler = ref<string | null>(null)
const sucht = ref(false)

const sortiert = computed(() => [...props.positionen].sort((a, b) => a.bezeichnung.localeCompare(b.bezeichnung, 'de')))
const name = (id: number | null) => props.positionen.find((p) => p.id === id)?.bezeichnung ?? ''

// --- Position zu Position ---
const von = ref<number | null>(props.startPosition)
const nach = ref<number | null>(null)
const nurFrei = ref(true)
const medientypId = ref<number | null>(null)
const wege = ref<Weg[] | null>(null)
const begrenzt = ref(false)
const gewaehlt = ref(0)

async function wegeSuchen() {
  fehler.value = null
  wege.value = null
  if (von.value === null || nach.value === null) {
    fehler.value = 'Bitte Start und Ziel wählen – auch per Klick in den Plan.'
    return
  }
  sucht.value = true
  try {
    const r = await api.wege(props.standortId, von.value, nach.value, nurFrei.value, medientypId.value)
    wege.value = r.wege
    begrenzt.value = r.begrenzt
    gewaehlt.value = 0
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    sucht.value = false
  }
}

/** Klick auf eine Position im Plan: erst Start, dann Ziel, danach Ziel ersetzen */
function positionKlick(id: number) {
  if (art.value === 'geraete') {
    if (gVonPos.value === null) gVonPos.value = id
    else if (id !== gVonPos.value) gNachPos.value = id
    return
  }
  art.value = 'positionen'
  if (von.value === null) von.value = id
  else if (id !== von.value) {
    nach.value = id
    wegeSuchen()
  }
}
defineExpose({ positionKlick })

function tauschen() {
  ;[von.value, nach.value] = [nach.value, von.value]
  if (wege.value) wegeSuchen()
}

// --- Gerät zu Gerät ---
const gVonPos = ref<number | null>(props.startPosition)
const gNachPos = ref<number | null>(null)
const gVonListe = ref<Geraet[]>([])
const gNachListe = ref<Geraet[]>([])
const gVon = ref<number | null>(null)
const gNach = ref<number | null>(null)
const geraeteWege = ref<Verlauf[] | null>(null)
const gGewaehlt = ref(0)

async function geraeteLaden(pos: number | null, ziel: 'von' | 'nach') {
  const liste = pos === null ? [] : await api.geraete(pos)
  if (ziel === 'von') {
    gVonListe.value = liste
    gVon.value = liste[0]?.id ?? null
  } else {
    gNachListe.value = liste
    gNach.value = liste[0]?.id ?? null
  }
}
watch(gVonPos, (p) => geraeteLaden(p, 'von'), { immediate: true })
watch(gNachPos, (p) => geraeteLaden(p, 'nach'))

async function geraeteSuchen() {
  fehler.value = null
  geraeteWege.value = null
  if (gVon.value === null || gNach.value === null) {
    fehler.value = 'Bitte an beiden Positionen ein Gerät wählen.'
    return
  }
  sucht.value = true
  try {
    geraeteWege.value = await api.geraetWege(gVon.value, gNach.value)
    gGewaehlt.value = 0
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    sucht.value = false
  }
}

// --- Leitungsverlauf ---
const verlauf = ref<Verlauf | null>(null)
async function verlaufLaden(portId: number | null) {
  verlauf.value = null
  if (portId === null) return
  art.value = 'verlauf'
  try {
    verlauf.value = await api.verlauf(portId)
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
watch(() => props.verlaufPort, verlaufLaden, { immediate: true })

// --- Markierung im Plan ---
watch([art, wege, gewaehlt, geraeteWege, gGewaehlt, verlauf, von, nach], () => {
  if (art.value === 'positionen') {
    const w = wege.value?.[gewaehlt.value]
    const pos = w ? [w.abschnitte[0].von.id, ...w.abschnitte.map((a) => a.nach.id)] : [von.value, nach.value].filter((x): x is number => x !== null)
    emit('markieren', w?.abschnitte.map((a) => a.verbindungId) ?? [], pos)
  } else {
    const v = art.value === 'geraete' ? geraeteWege.value?.[gGewaehlt.value] : verlauf.value
    emit('markieren', v?.verbindungIds ?? [], v ? [...new Set(v.punkte.map((p) => p.position.id))] : [])
  }
}, { immediate: true })

const posText = (p: { bezeichnung: string; stockwerk: number }) => `${p.bezeichnung} (${stockwerkText(p.stockwerk)})`
const verlaufTitel = (v: Verlauf) => {
  const a = v.punkte[0]?.port
  const b = v.punkte[v.punkte.length - 1]?.port
  return `${a ? `Port ${a.nummer}` : '?'} → ${b ? `Port ${b.nummer}` : '?'} · ${v.glieder.filter((g) => g.art === 'kabel').map((g) => g.kabelnummer).join(', ') || 'Patch'}`
}
</script>

<template>
  <aside class="seitenleiste breit">
    <div class="kopf">
      <h2>Weg suchen</h2>
      <button type="button" class="zweitrangig klein-btn" title="Beenden (Esc)" @click="emit('fertig')">Fertig</button>
    </div>
    <div class="reiter" role="tablist">
      <button v-for="[k, t] in ([['positionen', 'Positionen'], ['geraete', 'Geräte'], ['verlauf', 'Verlauf']] as const)" :key="k"
        type="button" role="tab" :aria-selected="art === k" :class="{ an: art === k }" @click="art = k; fehler = null">{{ t }}</button>
    </div>

    <!-- Position zu Position -->
    <template v-if="art === 'positionen'">
      <p class="leise klein">Start und Ziel wählen oder nacheinander im Plan anklicken.</p>
      <div class="raster">
        <label for="weg-von">Von</label>
        <select id="weg-von" v-model="von">
          <option :value="null">– wählen –</option>
          <option v-for="p in sortiert" :key="p.id" :value="p.id">{{ posText(p) }}</option>
        </select>
        <label for="weg-nach">Nach</label>
        <span class="zeile">
          <select id="weg-nach" v-model="nach">
            <option :value="null">– wählen –</option>
            <option v-for="p in sortiert" :key="p.id" :value="p.id" :disabled="p.id === von">{{ posText(p) }}</option>
          </select>
          <button type="button" class="zweitrangig klein-btn" title="Start und Ziel tauschen" @click="tauschen">⇅</button>
        </span>
        <label for="weg-typ">Medientyp</label>
        <select id="weg-typ" v-model="medientypId">
          <option :value="null">alle</option>
          <option v-for="t in medientypen" :key="t.id" :value="t.id">{{ t.name }}</option>
        </select>
      </div>
      <label class="haken"><input v-model="nurFrei" type="checkbox" /> nur Wege mit freier Kapazität</label>
      <p class="leise klein">
        Frei ist eine Faser/Ader, wenn beide Enden noch nicht aufgelegt sind oder auf einem Patchpanel liegen, dessen
        Vorderseite frei ist.
      </p>
      <button type="button" :disabled="sucht" @click="wegeSuchen">{{ sucht ? 'Sucht …' : 'Suchen' }}</button>

      <template v-if="wege">
        <p v-if="wege.length === 0" class="hinweis">
          Kein Weg von «{{ name(von) }}» nach «{{ name(nach) }}»{{ nurFrei ? ' mit freier Kapazität' : '' }}{{ medientypId !== null ? ' mit diesem Medientyp' : '' }}.
        </p>
        <p v-else-if="begrenzt" class="leise klein">Die Suche wurde begrenzt; es werden die kürzesten gefundenen Wege gezeigt.</p>
        <div v-for="(w, i) in wege" :key="i" class="weg" :class="{ an: gewaehlt === i }" role="button" tabindex="0"
          @click="gewaehlt = i" @keydown.enter="gewaehlt = i">
          <div class="weg-kopf">
            <strong>Weg {{ i + 1 }}</strong>
            <span>{{ formatZahl(w.laengeMeter, 0) }} m · {{ w.abschnitte.length }} {{ w.abschnitte.length === 1 ? 'Abschnitt' : 'Abschnitte' }}</span>
            <span :class="w.frei > 0 ? 'gut' : 'schlecht'">{{ w.frei }} frei</span>
          </div>
          <ol v-if="gewaehlt === i" class="abschnitte">
            <li v-for="a in w.abschnitte" :key="a.verbindungId">
              <span class="strecke">{{ a.von.bezeichnung }} → {{ a.nach.bezeichnung }}</span>
              <span class="leise klein">{{ formatZahl(a.laengeMeter, 0) }} m</span>
              <ul class="medien">
                <li v-for="m in a.medien" :key="m.id">
                  <button type="button" class="link" @click.stop="emit('medium', m.id)">{{ m.kabelnummer }}</button>
                  <span class="leise">&nbsp;{{ m.medientyp }} ·&nbsp;</span>
                  <span :class="m.frei > 0 ? 'gut' : 'schlecht'">{{ m.frei }}/{{ m.anzahl }} frei</span>
                </li>
                <li v-if="a.medien.length === 0" class="leise">keine Medien</li>
              </ul>
            </li>
          </ol>
        </div>
      </template>
    </template>

    <!-- Gerät zu Gerät -->
    <template v-else-if="art === 'geraete'">
      <p class="leise klein">Findet bestehende Leitungen zwischen zwei Geräten – über Kabel, Patchpanels und Patchungen.</p>
      <div class="raster">
        <label for="gw-vp">Von Position</label>
        <select id="gw-vp" v-model="gVonPos">
          <option :value="null">– wählen –</option>
          <option v-for="p in sortiert" :key="p.id" :value="p.id">{{ posText(p) }}</option>
        </select>
        <label for="gw-vg">Gerät</label>
        <select id="gw-vg" v-model="gVon" :disabled="!gVonListe.length">
          <option v-for="g in gVonListe" :key="g.id" :value="g.id">{{ g.bezeichnung }}</option>
        </select>
        <label for="gw-np">Nach Position</label>
        <select id="gw-np" v-model="gNachPos">
          <option :value="null">– wählen –</option>
          <option v-for="p in sortiert" :key="p.id" :value="p.id">{{ posText(p) }}</option>
        </select>
        <label for="gw-ng">Gerät</label>
        <select id="gw-ng" v-model="gNach" :disabled="!gNachListe.length">
          <option v-for="g in gNachListe" :key="g.id" :value="g.id">{{ g.bezeichnung }}</option>
        </select>
      </div>
      <button type="button" :disabled="sucht" @click="geraeteSuchen">{{ sucht ? 'Sucht …' : 'Suchen' }}</button>
      <template v-if="geraeteWege">
        <p v-if="geraeteWege.length === 0" class="hinweis">Zwischen diesen Geräten gibt es keine durchgehende Leitung.</p>
        <p v-else class="leise klein">{{ geraeteWege.length }} {{ geraeteWege.length === 1 ? 'Leitung' : 'Leitungen' }} gefunden.</p>
        <div v-for="(v, i) in geraeteWege" :key="i" class="weg" :class="{ an: gGewaehlt === i }" role="button" tabindex="0"
          @click="gGewaehlt = i" @keydown.enter="gGewaehlt = i">
          <div class="weg-kopf"><strong>{{ verlaufTitel(v) }}</strong></div>
          <VerlaufAnzeige v-if="gGewaehlt === i" :verlauf="v" @geraet="(id) => emit('geraet', id)" @medium="(id) => emit('medium', id)" />
        </div>
      </template>
    </template>

    <!-- Leitungsverlauf -->
    <template v-else>
      <p v-if="!verlauf" class="leise klein">
        Den Verlauf eines Ports zeigen: Gerät öffnen und beim belegten Port auf «⤳» klicken.
      </p>
      <VerlaufAnzeige v-else :verlauf="verlauf" :hervor-port-id="verlaufPort" @geraet="(id) => emit('geraet', id)"
        @medium="(id) => emit('medium', id)" />
    </template>

    <p v-if="fehler" class="fehler">{{ fehler }}</p>
  </aside>
</template>

<style scoped>
.breit {
  width: 30rem;
}
.reiter {
  display: flex;
  gap: 0.25rem;
  border-bottom: 1px solid var(--rand);
  margin: 0.5rem 0 0.6rem;
}
.reiter button {
  background: none;
  color: var(--text-leise);
  border: none;
  border-bottom: 3px solid transparent;
  border-radius: 0;
  padding: 0.35rem 0.7rem;
}
.reiter button.an {
  color: var(--text);
  border-bottom-color: var(--akzent);
  font-weight: 600;
}
.raster {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.4rem 0.6rem;
  align-items: center;
  margin: 0.5rem 0;
}
.raster label {
  margin: 0;
}
.zeile {
  display: flex;
  gap: 0.35rem;
}
.zeile select {
  flex: 1;
}
select {
  font: inherit;
  font-size: 0.9rem;
  width: 100%;
  padding: 0.35rem 0.45rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
  background: var(--flaeche);
}
.haken {
  font-weight: 400;
  display: flex;
  gap: 0.4rem;
  align-items: center;
  margin: 0.3rem 0 0;
}
.hinweis {
  background: #fff8c5;
  border: 1px solid #d4a72c;
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  margin-top: 0.7rem;
}
.weg {
  border: 1px solid var(--rand);
  border-radius: 6px;
  padding: 0.45rem 0.6rem;
  margin-top: 0.6rem;
  cursor: pointer;
}
.weg.an {
  border-color: #2da44e;
  box-shadow: 0 0 0 2px #d1f0db;
  cursor: default;
}
.weg-kopf {
  display: flex;
  gap: 0.8rem;
  align-items: baseline;
  flex-wrap: wrap;
}
.abschnitte {
  margin: 0.5rem 0 0;
  padding-left: 1.2rem;
}
.abschnitte > li {
  margin-bottom: 0.4rem;
}
.strecke {
  font-weight: 600;
  margin-right: 0.4rem;
}
.medien {
  list-style: none;
  padding: 0;
  margin: 0.15rem 0 0;
  font-size: 0.85rem;
}
.gut {
  color: var(--gut);
  font-weight: 600;
}
.schlecht {
  color: var(--schlecht);
  font-weight: 600;
}
.link {
  background: none;
  border: none;
  padding: 0;
  color: var(--akzent);
  font-size: inherit;
}
.klein {
  font-size: 0.85rem;
}
</style>
