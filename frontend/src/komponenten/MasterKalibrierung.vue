<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { api, type Plan, type Punkt } from '../api'
import {
  abstand,
  formatMassstab,
  formatZahl,
  HINWEIS_ABWEICHUNG_PROZENT,
  kalibriereMaster
} from '../geometrie'
import type { Linie, Markierung } from './PlanAnsicht.vue'

const props = defineProps<{ plan: Plan }>()
const emit = defineEmits<{
  gespeichert: [plan: Plan]
  abbrechen: []
  /** Markierungen/Linien für die Planansicht und ob Klicks erwartet werden */
  anzeige: [markierungen: Markierung[], linien: Linie[], klickModus: boolean]
}>()

type Schritt = 'nullpunkt' | 's1a' | 's1b' | 's2a' | 's2b' | null

interface StreckeZustand {
  a: Punkt | null
  b: Punkt | null
  laenge: string // Texteingabe, erlaubt «12,5» oder «12.5»
}

const nullpunkt = ref<Punkt | null>(null)
const strecken = reactive<StreckeZustand[]>([
  { a: null, b: null, laenge: '' },
  { a: null, b: null, laenge: '' }
])
const schritt = ref<Schritt>(null)
const speichert = ref(false)
const fehler = ref<string | null>(null)

// Bestehende Kalibrierung übernehmen, damit sie angepasst werden kann
function vorbelegen() {
  nullpunkt.value = props.plan.nullpunkt ? { ...props.plan.nullpunkt } : null
  props.plan.messstrecken.slice(0, 2).forEach((m, i) => {
    strecken[i] = {
      a: { u: m.u1, v: m.v1 },
      b: { u: m.u2, v: m.v2 },
      laenge: String(m.laengeMeter)
    }
  })
  schritt.value = nullpunkt.value ? null : 'nullpunkt'
}
vorbelegen()

const laengeMeter = (s: StreckeZustand) => {
  const zahl = Number(s.laenge.replace(',', '.').trim())
  return Number.isFinite(zahl) && zahl > 0 ? zahl : null
}

const vorschau = computed(() =>
  kalibriereMaster(
    strecken
      .filter((s) => s.a && s.b && laengeMeter(s))
      .map((s) => ({ a: s.a!, b: s.b!, laengeMeter: laengeMeter(s)! }))
  )
)

const vollstaendig = computed(
  () => !!nullpunkt.value && strecken.every((s) => s.a && s.b && laengeMeter(s) !== null)
)

const anleitung = computed(() => {
  switch (schritt.value) {
    case 'nullpunkt': return 'Klicken Sie auf den markanten Punkt, der der Nullpunkt (0/0) sein soll.'
    case 's1a': return 'Strecke 1: Klicken Sie auf den Anfang einer Strecke mit bekannter Länge.'
    case 's1b': return 'Strecke 1: Klicken Sie auf das Ende der Strecke.'
    case 's2a': return 'Strecke 2: Klicken Sie auf den Anfang – idealerweise quer zur ersten Strecke.'
    case 's2b': return 'Strecke 2: Klicken Sie auf das Ende der Strecke.'
    default: return null
  }
})

/** Klick im Plan, je nach aktuellem Schritt */
function klick(p: Punkt) {
  fehler.value = null
  switch (schritt.value) {
    case 'nullpunkt': nullpunkt.value = p; schritt.value = strecken[0].a ? null : 's1a'; break
    case 's1a': strecken[0].a = p; strecken[0].b = null; schritt.value = 's1b'; break
    case 's1b': strecken[0].b = p; schritt.value = strecken[1].a ? null : 's2a'; break
    case 's2a': strecken[1].a = p; strecken[1].b = null; schritt.value = 's2b'; break
    case 's2b': strecken[1].b = p; schritt.value = null; break
  }
}

/** Ein bereits gesetzter Punkt wurde in der Planansicht verschoben */
function verschoben(id: string, p: Punkt) {
  fehler.value = null
  if (id === 'nullpunkt') nullpunkt.value = p
  else {
    const [nr, ende] = [Number(id[1]) - 1, id[2] as 'a' | 'b']
    strecken[nr][ende] = p
  }
}

defineExpose({ klick, verschoben })

// Anzeige in der Planansicht aktuell halten
watch(
  [nullpunkt, strecken, schritt],
  () => {
    const markierungen: Markierung[] = []
    const linien: Linie[] = []
    if (nullpunkt.value)
      markierungen.push({ id: 'nullpunkt', punkt: nullpunkt.value, art: 'nullpunkt', text: '0/0', verschiebbar: true })
    strecken.forEach((s, i) => {
      const nr = i + 1
      if (s.a) markierungen.push({ id: `s${nr}a`, punkt: s.a, art: 'messpunkt', text: `${nr}A`, verschiebbar: true })
      if (s.b) markierungen.push({ id: `s${nr}b`, punkt: s.b, art: 'messpunkt', text: `${nr}B`, verschiebbar: true })
      if (s.a && s.b) {
        const m = laengeMeter(s)
        linien.push({ id: `s${nr}`, von: s.a, bis: s.b, text: m ? `${formatZahl(m, 2)} m` : `Strecke ${nr}` })
      }
    })
    emit('anzeige', markierungen, linien, schritt.value !== null)
  },
  { deep: true, immediate: true }
)

async function speichern() {
  fehler.value = null
  if (!vollstaendig.value || !nullpunkt.value) {
    fehler.value = 'Bitte Nullpunkt und beide Strecken mit Länge erfassen.'
    return
  }
  speichert.value = true
  try {
    const plan = await api.masterplanKalibrieren(
      props.plan.id,
      props.plan.version,
      nullpunkt.value,
      strecken.map((s) => ({ u1: s.a!.u, v1: s.a!.v, u2: s.b!.u, v2: s.b!.v, laengeMeter: laengeMeter(s)! }))
    )
    emit('gespeichert', plan)
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

const pixel = (s: StreckeZustand) => (s.a && s.b ? abstand(s.a, s.b) : null)
</script>

<template>
  <aside class="panel">
    <h2>Masterplan kalibrieren</h2>
    <p class="leise klein">
      Legen Sie den Nullpunkt fest und messen Sie zwei Strecken bekannter Länge nach. Gesetzte Punkte lassen sich
      mit der Maus verschieben – zum genauen Treffen vorher hineinzoomen.
    </p>

    <p v-if="anleitung" class="anleitung" role="status">{{ anleitung }}</p>

    <section class="schritt">
      <div class="kopf">
        <h3>1 · Nullpunkt</h3>
        <button type="button" class="zweitrangig klein-btn" @click="schritt = 'nullpunkt'">
          {{ nullpunkt ? 'Neu setzen' : 'Setzen' }}
        </button>
      </div>
      <p v-if="nullpunkt" class="wert">u {{ Math.round(nullpunkt.u) }} · v {{ Math.round(nullpunkt.v) }} px</p>
      <p v-else class="leise klein">Noch nicht gesetzt</p>
    </section>

    <section v-for="(s, i) in strecken" :key="i" class="schritt">
      <div class="kopf">
        <h3>{{ i + 2 }} · Strecke {{ i + 1 }}</h3>
        <button type="button" class="zweitrangig klein-btn" @click="schritt = i === 0 ? 's1a' : 's2a'">
          {{ s.a ? 'Neu setzen' : 'Setzen' }}
        </button>
      </div>
      <label :for="`laenge-${i}`">Bekannte Länge</label>
      <div class="laenge">
        <input :id="`laenge-${i}`" v-model="s.laenge" type="text" inputmode="decimal" placeholder="z.B. 50" />
        <span>m</span>
      </div>
      <p v-if="pixel(s)" class="wert">
        {{ formatZahl(pixel(s)!, 1) }} px
        <template v-if="laengeMeter(s)"> → {{ formatMassstab(laengeMeter(s)! / pixel(s)!) }}</template>
      </p>
      <p v-if="s.laenge && laengeMeter(s) === null" class="fehler klein">Bitte eine positive Zahl eingeben.</p>
    </section>

    <section v-if="vorschau" class="ergebnis">
      <h3>Ergebnis</h3>
      <p class="wert gross">{{ formatMassstab(vorschau.massstab) }}</p>
      <p v-if="vorschau.abweichungProzent !== null"
        :class="vorschau.abweichungProzent > HINWEIS_ABWEICHUNG_PROZENT ? 'warnung' : 'gut-text'">
        Abweichung zwischen den Strecken: {{ formatZahl(vorschau.abweichungProzent, 1) }} %
        <template v-if="vorschau.abweichungProzent > HINWEIS_ABWEICHUNG_PROZENT">
          – bitte Punkte und Längen prüfen. Ist der Plan evtl. verzerrt?
        </template>
      </p>
    </section>

    <p v-if="fehler" class="fehler">{{ fehler }}</p>

    <div class="aktionen">
      <button type="button" :disabled="!vollstaendig || speichert" @click="speichern">
        {{ speichert ? 'Wird gespeichert …' : 'Speichern' }}
      </button>
      <button v-if="plan.referenziert" type="button" class="zweitrangig" @click="emit('abbrechen')">Abbrechen</button>
    </div>
  </aside>
</template>

<style scoped>
.panel {
  width: 21rem;
  flex-shrink: 0;
  overflow-y: auto;
  background: var(--flaeche);
  border-left: 1px solid var(--rand);
  padding: 1rem 1.1rem 1.5rem;
}
h3 {
  font-size: 0.95rem;
  margin: 0;
}
.klein {
  font-size: 0.85rem;
}
.anleitung {
  background: #ddf4ff;
  border: 1px solid #54aeff;
  border-radius: 6px;
  padding: 0.5rem 0.7rem;
  font-size: 0.9rem;
}
.schritt,
.ergebnis {
  border-top: 1px solid var(--rand);
  padding: 0.8rem 0;
}
.kopf {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.4rem;
}
.klein-btn {
  padding: 0.2rem 0.6rem;
  font-size: 0.85rem;
}
label {
  font-size: 0.85rem;
}
.laenge {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.laenge input {
  width: 8rem;
}
.wert {
  margin: 0.35rem 0 0;
  font-variant-numeric: tabular-nums;
  font-size: 0.9rem;
}
.gross {
  font-size: 1.1rem;
  font-weight: 600;
}
.warnung {
  color: #9a6700;
  font-size: 0.9rem;
}
.gut-text {
  color: var(--gut);
  font-size: 0.9rem;
}
.aktionen {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.8rem;
}
</style>
