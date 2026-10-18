<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { api, type Plan, type PlanListe, type Punkt } from '../api'
import { abstand, formatDrehung, formatMassstab, formatZahl, nachAbsolut, nachPlan, richteAus } from '../geometrie'
import { stockwerkText } from '../format'
import type { Linie, Markierung } from './PlanAnsicht.vue'

/** links = der zu referenzierende Plan, rechts = Bezugsplan */
export type Seite = 'links' | 'rechts'

/** Was die beiden Planansichten zeigen sollen */
export interface ReferenzAnzeige {
  bezug: Plan | null
  links: Markierung[]
  rechts: Markierung[]
  linienRechts: Linie[]
  klick: boolean
}

const props = defineProps<{ plan: Plan; plaene: PlanListe[] }>()
const emit = defineEmits<{
  gespeichert: [plan: Plan]
  abbrechen: []
  anzeige: [anzeige: ReferenzAnzeige]
  /** Entsprechender Punkt zum Mauszeiger im jeweils anderen Plan */
  kontrolle: [links: Punkt | null, rechts: Punkt | null]
}>()

const MIN_ABSTAND_PX = 20

interface Paar {
  links: Punkt | null
  rechts: Punkt | null
}

const paare = reactive<Paar[]>([
  { links: null, rechts: null },
  { links: null, rechts: null }
])
/** Index des Passpunkts, der gerade gesetzt wird (null = beide fertig) */
const aktiv = ref<number | null>(0)
const bezugId = ref<number | null>(props.plan.bezugsplanId)
const bezug = ref<Plan | null>(null)
const fehler = ref<string | null>(null)
const speichert = ref(false)

// Wählbar: referenzierte Pläne, ausser dieser Plan selbst und Pläne, die (über eine Kette) auf ihm aufbauen
const waehlbar = computed(() => {
  const nachfolger = new Set<number>()
  let neu = [props.plan.id]
  while (neu.length) {
    neu = props.plaene.filter((p) => p.bezugsplanId !== null && neu.includes(p.bezugsplanId) && !nachfolger.has(p.id)).map((p) => p.id)
    neu.forEach((id) => nachfolger.add(id))
  }
  return props.plaene.filter((p) => p.referenziert && p.id !== props.plan.id && !nachfolger.has(p.id))
})

// Bestehende Referenzierung übernehmen, damit sie angepasst werden kann
if (props.plan.passpunkte.length === 2) {
  props.plan.passpunkte.forEach((p, i) => {
    paare[i] = { links: { u: p.planU, v: p.planV }, rechts: { u: p.bezugU, v: p.bezugV } }
  })
  aktiv.value = null
}
if (bezugId.value === null || !waehlbar.value.some((p) => p.id === bezugId.value))
  bezugId.value = waehlbar.value[0]?.id ?? null

watch(
  bezugId,
  async (id, alt) => {
    fehler.value = null
    bezug.value = null
    if (alt !== undefined) {
      // Anderer Bezugsplan: die Punkte darin gelten nicht mehr
      paare.forEach((p) => (p.rechts = null))
      naechsterSchritt()
    }
    if (id === null) return
    try {
      bezug.value = await api.plan(id)
    } catch (e) {
      fehler.value = (e as Error).message
    }
  },
  { immediate: true }
)

function naechsterSchritt() {
  const i = paare.findIndex((p) => !p.links || !p.rechts)
  aktiv.value = i >= 0 ? i : null
}

function neuSetzen(i: number) {
  fehler.value = null
  paare[i] = { links: null, rechts: null }
  aktiv.value = i
}

/** Klick in eine der beiden Planansichten: Punkt des aktuellen Passpunkts auf dieser Seite setzen */
function klick(seite: Seite, p: Punkt) {
  if (aktiv.value === null) return
  fehler.value = null
  paare[aktiv.value][seite] = p
  const paar = paare[aktiv.value]
  if (paar.links && paar.rechts) naechsterSchritt()
}

function verschoben(seite: Seite, id: string, p: Punkt) {
  const i = Number(id.slice(1)) - 1
  if (paare[i]) paare[i][seite] = p
}

// Vorschau der Lage, sobald alle vier Punkte gesetzt sind
const lage = computed(() => {
  const [a, b] = paare
  if (!bezug.value || !a.links || !a.rechts || !b.links || !b.rechts) return null
  return richteAus(a.links, nachAbsolut(bezug.value, a.rechts), b.links, nachAbsolut(bezug.value, b.rechts))
})

const zuNahe = computed(() => {
  const [a, b] = paare
  const links = a.links && b.links && abstand(a.links, b.links) < MIN_ABSTAND_PX
  const rechts = a.rechts && b.rechts && abstand(a.rechts, b.rechts) < MIN_ABSTAND_PX
  return links || rechts
})

/** Kontrolle: Mauszeiger in einem Plan → gleiche Stelle im anderen Plan */
function zeiger(seite: Seite, p: Punkt | null) {
  if (!p || !lage.value || !bezug.value) {
    emit('kontrolle', null, null)
    return
  }
  if (seite === 'links') emit('kontrolle', null, nachPlan(bezug.value, nachAbsolut(lage.value, p)))
  else emit('kontrolle', nachPlan(lage.value, nachAbsolut(bezug.value, p)), null)
}

defineExpose({ klick, verschoben, zeiger })

const anleitung = computed(() => {
  if (aktiv.value === null) return null
  const nr = aktiv.value + 1
  const paar = paare[aktiv.value]
  if (!paar.links && !paar.rechts)
    return `Passpunkt ${nr}: Klicken Sie einen gut erkennbaren Punkt (z.B. eine Gebäudeecke) im linken Plan an – und denselben Punkt im Bezugsplan rechts. Die Reihenfolge ist egal.`
  if (!paar.links) return `Passpunkt ${nr}: Jetzt denselben Punkt im linken Plan (${props.plan.bezeichnung}) anklicken.`
  return `Passpunkt ${nr}: Jetzt denselben Punkt im Bezugsplan rechts anklicken.`
})

// Anzeige in beiden Planansichten aktuell halten
watch(
  [paare, aktiv, bezug, lage],
  () => {
    const links: Markierung[] = []
    const rechts: Markierung[] = []
    paare.forEach((p, i) => {
      const id = `p${i + 1}`
      const text = `P${i + 1}`
      if (p.links) links.push({ id, punkt: p.links, art: 'messpunkt', text, verschiebbar: true })
      if (p.rechts) rechts.push({ id, punkt: p.rechts, art: 'messpunkt', text, verschiebbar: true })
    })
    // Umriss des Plans im Bezugsplan
    const linienRechts: Linie[] = []
    if (lage.value && bezug.value) {
      const { bildBreite: b, bildHoehe: h } = props.plan
      const ecken = [
        { u: 0, v: 0 },
        { u: b, v: 0 },
        { u: b, v: h },
        { u: 0, v: h }
      ].map((e) => nachPlan(bezug.value!, nachAbsolut(lage.value!, e)))
      ecken.forEach((e, i) => linienRechts.push({ id: `umriss${i}`, von: e, bis: ecken[(i + 1) % 4] }))
    }
    emit('anzeige', { bezug: bezug.value, links, rechts, linienRechts, klick: aktiv.value !== null })
  },
  { deep: true, immediate: true }
)

const vollstaendig = computed(() => !!lage.value && !zuNahe.value)

async function speichern() {
  fehler.value = null
  if (!vollstaendig.value || bezugId.value === null) {
    fehler.value = 'Bitte beide Passpunkte in beiden Plänen setzen.'
    return
  }
  speichert.value = true
  try {
    const plan = await api.planReferenzieren(
      props.plan.id,
      props.plan.version,
      bezugId.value,
      paare.map((p) => ({ planU: p.links!.u, planV: p.links!.v, bezugU: p.rechts!.u, bezugV: p.rechts!.v }))
    )
    emit('gespeichert', plan)
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

const planText = (p: PlanListe) => `${p.bezeichnung}${p.istMasterplan ? ' (Masterplan)' : ` · ${stockwerkText(p.stockwerk)}`}`
</script>

<template>
  <aside class="seitenleiste">
    <h2>Plan referenzieren</h2>
    <p class="leise klein">
      Wählen Sie zwei Punkte, die in beiden Plänen gut erkennbar sind – möglichst weit auseinander. Links ist dieser
      Plan, rechts der Bezugsplan. Gesetzte Punkte lassen sich mit der Maus verschieben.
    </p>

    <div class="feld">
      <label for="bezugsplan">Bezugsplan</label>
      <select id="bezugsplan" v-model="bezugId">
        <option v-for="p in waehlbar" :key="p.id" :value="p.id">{{ planText(p) }}</option>
      </select>
      <p v-if="waehlbar.length === 0" class="fehler klein">Es gibt keinen referenzierten Plan als Bezug.</p>
    </div>

    <p v-if="anleitung" class="anleitung" role="status">{{ anleitung }}</p>

    <section v-for="(p, i) in paare" :key="i" class="abschnitt">
      <div class="kopf">
        <h3>Passpunkt {{ i + 1 }}</h3>
        <button type="button" class="zweitrangig klein-btn" @click="neuSetzen(i)">
          {{ p.links || p.rechts ? 'Neu setzen' : 'Setzen' }}
        </button>
      </div>
      <p class="wert">
        <span :class="p.links ? 'gesetzt' : 'leise'">{{ p.links ? '✓' : '–' }} in diesem Plan</span>
        ·
        <span :class="p.rechts ? 'gesetzt' : 'leise'">{{ p.rechts ? '✓' : '–' }} im Bezugsplan</span>
      </p>
      <p v-if="p.rechts && bezug" class="wert leise">
        x {{ formatZahl(nachAbsolut(bezug, p.rechts).x) }} m · y {{ formatZahl(nachAbsolut(bezug, p.rechts).y) }} m
      </p>
    </section>

    <p v-if="zuNahe" class="fehler klein">
      Die beiden Passpunkte liegen zu nahe beieinander. Bitte zwei weit auseinanderliegende Punkte wählen.
    </p>

    <section v-if="lage" class="abschnitt">
      <h3>Ergebnis</h3>
      <p class="wert gross">{{ formatMassstab(lage.massstab) }}</p>
      <p class="wert">Drehung gegenüber dem Masterplan: {{ formatDrehung(lage.drehung) }}</p>
      <p class="wert">
        Plangrösse: {{ formatZahl(plan.bildBreite * lage.massstab, 1) }} × {{ formatZahl(plan.bildHoehe * lage.massstab, 1) }} m
      </p>
      <p class="leise klein">
        Kontrolle: Fahren Sie mit der Maus über einen der Pläne – der orange Ring zeigt dieselbe Stelle im anderen
        Plan. Der gestrichelte Rahmen rechts ist der Umriss dieses Plans.
      </p>
    </section>

    <p v-if="fehler" class="fehler">{{ fehler }}</p>

    <div class="aktionen">
      <button type="button" :disabled="!vollstaendig || speichert" @click="speichern">
        {{ speichert ? 'Wird gespeichert …' : 'Speichern' }}
      </button>
      <button type="button" class="zweitrangig" @click="emit('abbrechen')">Abbrechen</button>
    </div>
  </aside>
</template>

<style scoped>
.gesetzt {
  color: var(--gut);
  font-weight: 600;
}
.gross {
  font-size: 1.1rem;
  font-weight: 600;
}
</style>
