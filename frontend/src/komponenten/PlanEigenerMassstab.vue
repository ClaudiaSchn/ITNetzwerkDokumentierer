<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { api, type EigenerMassstab, type Plan, type PlanListe, type Punkt } from '../api'
import {
  abstand,
  formatDrehung,
  formatMassstab,
  formatZahl,
  HINWEIS_ABWEICHUNG_PROZENT,
  kalibriereMaster,
  mitEigenemMassstab,
  nachAbsolut,
  nachPlan,
  richtungAbsolut,
  richtungImPlan,
  zahlAus
} from '../geometrie'
import { planText, waehlbareBezugsplaene } from '../plaene'
import type { Linie, Markierung } from './PlanAnsicht.vue'
import type { ReferenzAnzeige, Seite } from './PlanReferenzierung.vue'
import MethodenWahl, { type Methode } from './MethodenWahl.vue'

/**
 * Referenzieren mit eigenem Massstab – für Pläne, die auf keinem anderen Plan erkennbar sind:
 *  1. Massstab über eine oder zwei bekannte Strecken im Plan
 *  2. Lage: Ankerpunkt im Plan, dessen Lage im Bezugsplan angeklickt oder als Koordinate eingegeben wird
 *  3. Drehung: als Winkel oder als Richtung (dieselbe Richtung in beiden Plänen, z.B. die Nordpfeile)
 */
const props = defineProps<{ plan: Plan; plaene: PlanListe[] }>()
const emit = defineEmits<{
  gespeichert: [plan: Plan]
  abbrechen: []
  methode: [methode: Methode]
  anzeige: [anzeige: ReferenzAnzeige]
  kontrolle: [links: Punkt | null, rechts: Punkt | null]
}>()

const MIN_PX = 20

/** Klickbare Punkte; s… = Strecken, rp… = Richtung im Plan, rb… = Richtung im Bezugsplan */
type Slot = 's1a' | 's1b' | 's2a' | 's2b' | 'anker' | 'ankerBezug' | 'rpa' | 'rpb' | 'rba' | 'rbb'
const RECHTS: Slot[] = ['ankerBezug', 'rba', 'rbb']
const seiteVon = (s: Slot): Seite => (RECHTS.includes(s) ? 'rechts' : 'links')

const punkte = reactive<Record<Slot, Punkt | null>>({
  s1a: null, s1b: null, s2a: null, s2b: null, anker: null, ankerBezug: null, rpa: null, rpb: null, rba: null, rbb: null
})
const laengen = reactive(['', ''])
const lageArt = ref<'klick' | 'koordinate'>('klick')
const ankerX = ref('')
const ankerY = ref('')
const drehArt = ref<'winkel' | 'richtung'>('winkel')
const winkel = ref('0')
const schritt = ref<Slot | null>(null)

const bezugId = ref<number | null>(props.plan.bezugsplanId)
const bezug = ref<Plan | null>(null)
const fehler = ref<string | null>(null)
const speichert = ref(false)

const waehlbar = computed(() => waehlbareBezugsplaene(props.plan.id, props.plaene))
if (bezugId.value === null || !waehlbar.value.some((p) => p.id === bezugId.value)) bezugId.value = waehlbar.value[0]?.id ?? null

// Bestehende Referenzierung mit eigenem Massstab übernehmen
const alt = props.plan.methode === 'EigenerMassstab' ? props.plan.eigenerMassstab : null
if (alt) {
  props.plan.messstrecken.slice(0, 2).forEach((m, i) => {
    punkte[i === 0 ? 's1a' : 's2a'] = { u: m.u1, v: m.v1 }
    punkte[i === 0 ? 's1b' : 's2b'] = { u: m.u2, v: m.v2 }
    laengen[i] = String(m.laengeMeter)
  })
  punkte.anker = alt.anker
  if (alt.ankerBezug) punkte.ankerBezug = alt.ankerBezug
  else {
    lageArt.value = 'koordinate'
    ankerX.value = String(alt.ankerX ?? '')
    ankerY.value = String(alt.ankerY ?? '')
  }
  if (alt.winkel !== null) winkel.value = String(alt.winkel)
  else if (alt.richtungPlan && alt.richtungBezug) {
    drehArt.value = 'richtung'
    punkte.rpa = { u: alt.richtungPlan.u1, v: alt.richtungPlan.v1 }
    punkte.rpb = { u: alt.richtungPlan.u2, v: alt.richtungPlan.v2 }
    punkte.rba = { u: alt.richtungBezug.u1, v: alt.richtungBezug.v1 }
    punkte.rbb = { u: alt.richtungBezug.u2, v: alt.richtungBezug.v2 }
  }
}

/** Pflichtpunkte in der Reihenfolge, in der sie abgefragt werden */
const pflicht = computed<Slot[]>(() => [
  's1a', 's1b', 'anker',
  ...(lageArt.value === 'klick' ? (['ankerBezug'] as Slot[]) : []),
  ...(drehArt.value === 'richtung' ? (['rpa', 'rpb', 'rba', 'rbb'] as Slot[]) : [])
])
function naechsterSchritt() {
  schritt.value = pflicht.value.find((s) => !punkte[s]) ?? null
}
if (!alt) naechsterSchritt()

watch(
  bezugId,
  async (id, vorher) => {
    fehler.value = null
    bezug.value = null
    if (vorher !== undefined) {
      // Anderer Bezugsplan: Punkte darin gelten nicht mehr
      RECHTS.forEach((s) => (punkte[s] = null))
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
watch([lageArt, drehArt], naechsterSchritt)

/** Punkt (neu) setzen lassen; bei Strecken/Richtungen das ganze Paar */
function setzen(start: Slot) {
  fehler.value = null
  const paar: Partial<Record<Slot, Slot>> = { s1a: 's1b', s2a: 's2b', rpa: 'rpb', rba: 'rbb' }
  punkte[start] = null
  if (paar[start]) punkte[paar[start]!] = null
  schritt.value = start
}

function klick(seite: Seite, p: Punkt) {
  const s = schritt.value
  if (!s || seiteVon(s) !== seite) return
  fehler.value = null
  punkte[s] = p
  const folge: Partial<Record<Slot, Slot>> = { s1a: 's1b', s2a: 's2b', rpa: 'rpb', rba: 'rbb' }
  if (folge[s] && !punkte[folge[s]!]) schritt.value = folge[s]!
  else naechsterSchritt()
}

function verschoben(_seite: Seite, id: string, p: Punkt) {
  if (id in punkte) punkte[id as Slot] = p
}

// --- Rechnung ---

const streckenDaten = computed(() =>
  ([['s1a', 's1b'], ['s2a', 's2b']] as const).map(([a, b], i) => ({
    a: punkte[a],
    b: punkte[b],
    laenge: zahlAus(laengen[i]),
    text: laengen[i]
  }))
)
const gueltigeStrecken = computed(() =>
  streckenDaten.value
    .filter((s) => s.a && s.b && s.laenge !== null && s.laenge > 0)
    .map((s) => ({ a: s.a!, b: s.b!, laengeMeter: s.laenge! }))
)
const massstab = computed(() => (streckenDaten.value[0].a && streckenDaten.value[0].b && streckenDaten.value[0].laenge
  ? kalibriereMaster(gueltigeStrecken.value) : null))

const ankerWelt = computed(() => {
  if (lageArt.value === 'koordinate') {
    const x = zahlAus(ankerX.value)
    const y = zahlAus(ankerY.value)
    return x !== null && y !== null ? { x, y } : null
  }
  return bezug.value && punkte.ankerBezug ? nachAbsolut(bezug.value, punkte.ankerBezug) : null
})

const drehung = computed(() => {
  if (drehArt.value === 'winkel') return zahlAus(winkel.value)
  const { rpa, rpb, rba, rbb } = punkte
  if (!bezug.value || !rpa || !rpb || !rba || !rbb) return null
  return richtungAbsolut(nachAbsolut(bezug.value, rba), nachAbsolut(bezug.value, rbb)) - richtungImPlan(rpa, rpb)
})

const lage = computed(() =>
  massstab.value && ankerWelt.value && punkte.anker && drehung.value !== null
    ? mitEigenemMassstab(massstab.value.massstab, drehung.value, punkte.anker, ankerWelt.value)
    : null
)

const probleme = computed(() => {
  const liste: string[] = []
  streckenDaten.value.forEach((s, i) => {
    if (s.a && s.b && abstand(s.a, s.b) < MIN_PX) liste.push(`Strecke ${i + 1} ist zu kurz.`)
    if (s.text && (s.laenge === null || s.laenge <= 0)) liste.push(`Bitte für Strecke ${i + 1} eine positive Länge eingeben.`)
    if (i === 1 && (s.a || s.text) && !(s.a && s.b && s.laenge)) liste.push('Strecke 2 ist unvollständig – ergänzen oder «Entfernen».')
  })
  if (drehArt.value === 'winkel' && winkel.value.trim() && zahlAus(winkel.value) === null) liste.push('Bitte einen Winkel in Grad eingeben.')
  if (drehArt.value === 'richtung') {
    if (punkte.rpa && punkte.rpb && abstand(punkte.rpa, punkte.rpb) < MIN_PX) liste.push('Die Richtung im Plan ist zu kurz.')
    if (punkte.rba && punkte.rbb && abstand(punkte.rba, punkte.rbb) < MIN_PX) liste.push('Die Richtung im Bezugsplan ist zu kurz.')
  }
  if (lageArt.value === 'koordinate' && (ankerX.value.trim() || ankerY.value.trim()) && !ankerWelt.value)
    liste.push('Bitte x und y des Ankerpunkts in Metern eingeben.')
  return liste
})

const vollstaendig = computed(() => !!lage.value && probleme.value.length === 0)

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
  switch (schritt.value) {
    case 's1a': return 'Strecke 1: Klicken Sie im linken Plan auf den Anfang einer Strecke mit bekannter Länge (z.B. Massstabsbalken oder bemasste Wand).'
    case 's1b': return 'Strecke 1: Klicken Sie auf das Ende der Strecke.'
    case 's2a': return 'Strecke 2: Anfang einer zweiten bekannten Strecke – idealerweise quer zur ersten.'
    case 's2b': return 'Strecke 2: Klicken Sie auf das Ende der Strecke.'
    case 'anker': return 'Ankerpunkt: Klicken Sie im linken Plan auf einen Punkt, dessen Lage Sie kennen (z.B. eine Gebäudeecke).'
    case 'ankerBezug': return 'Ankerpunkt: Klicken Sie im Bezugsplan rechts auf die Stelle, wo dieser Punkt liegt.'
    case 'rpa': return 'Richtung: Klicken Sie im linken Plan auf den Anfang einer Richtung, z.B. das Ende des Nordpfeils.'
    case 'rpb': return 'Richtung: Klicken Sie auf das Ende der Richtung, z.B. die Spitze des Nordpfeils.'
    case 'rba': return 'Richtung: Klicken Sie im Bezugsplan rechts auf den Anfang derselben Richtung, z.B. das Ende des Nordpfeils.'
    case 'rbb': return 'Richtung: Klicken Sie auf das Ende der Richtung im Bezugsplan.'
    default: return null
  }
})

// Anzeige in beiden Planansichten aktuell halten
watch(
  [punkte, laengen, lage, bezug, schritt, lageArt, drehArt, ankerWelt],
  () => {
    const links: Markierung[] = []
    const linienLinks: Linie[] = []
    const rechts: Markierung[] = []
    const linienRechts: Linie[] = []
    const mp = (liste: Markierung[], id: Slot, text: string) => {
      const p = punkte[id]
      if (p) liste.push({ id, punkt: p, art: 'messpunkt', text, verschiebbar: true })
    }
    streckenDaten.value.forEach((s, i) => {
      const nr = i + 1
      mp(links, i === 0 ? 's1a' : 's2a', `${nr}A`)
      mp(links, i === 0 ? 's1b' : 's2b', `${nr}B`)
      if (s.a && s.b)
        linienLinks.push({ id: `s${nr}`, von: s.a, bis: s.b, text: s.laenge ? `${formatZahl(s.laenge, 2)} m` : `Strecke ${nr}` })
    })
    if (punkte.anker) links.push({ id: 'anker', punkt: punkte.anker, art: 'nullpunkt', text: 'Anker', verschiebbar: true })
    if (lageArt.value === 'klick' && punkte.ankerBezug)
      rechts.push({ id: 'ankerBezug', punkt: punkte.ankerBezug, art: 'nullpunkt', text: 'Anker', verschiebbar: true })
    else if (lageArt.value === 'koordinate' && ankerWelt.value && bezug.value)
      rechts.push({ id: 'ankerAnzeige', punkt: nachPlan(bezug.value, ankerWelt.value), art: 'nullpunkt', text: 'Anker' })
    if (drehArt.value === 'richtung') {
      mp(links, 'rpa', 'R1')
      mp(links, 'rpb', 'R2')
      mp(rechts, 'rba', 'R1')
      mp(rechts, 'rbb', 'R2')
      if (punkte.rpa && punkte.rpb) linienLinks.push({ id: 'rp', von: punkte.rpa, bis: punkte.rpb })
      if (punkte.rba && punkte.rbb) linienRechts.push({ id: 'rb', von: punkte.rba, bis: punkte.rbb })
    }
    // Umriss des Plans im Bezugsplan
    if (lage.value && bezug.value) {
      const { bildBreite: b, bildHoehe: h } = props.plan
      const ecken = [{ u: 0, v: 0 }, { u: b, v: 0 }, { u: b, v: h }, { u: 0, v: h }]
        .map((e) => nachPlan(bezug.value!, nachAbsolut(lage.value!, e)))
      ecken.forEach((e, i) => linienRechts.push({ id: `umriss${i}`, von: e, bis: ecken[(i + 1) % 4] }))
    }
    const s = schritt.value
    emit('anzeige', {
      bezug: bezug.value, links, linienLinks, rechts, linienRechts,
      klickLinks: !!s && seiteVon(s) === 'links', klickRechts: !!s && seiteVon(s) === 'rechts'
    })
  },
  { deep: true, immediate: true }
)

function streckeZweiEntfernen() {
  punkte.s2a = punkte.s2b = null
  laengen[1] = ''
  if (schritt.value === 's2a' || schritt.value === 's2b') naechsterSchritt()
}

async function speichern() {
  fehler.value = null
  if (!vollstaendig.value || bezugId.value === null || !punkte.anker) {
    fehler.value = probleme.value[0] ?? 'Bitte Massstab, Lage und Drehung vollständig angeben.'
    return
  }
  const linie = (a: Punkt | null, b: Punkt | null) => (a && b ? { u1: a.u, v1: a.v, u2: b.u, v2: b.v } : null)
  const angaben: EigenerMassstab = {
    anker: punkte.anker,
    ankerBezug: lageArt.value === 'klick' ? punkte.ankerBezug : null,
    ankerX: lageArt.value === 'koordinate' ? ankerWelt.value!.x : null,
    ankerY: lageArt.value === 'koordinate' ? ankerWelt.value!.y : null,
    winkel: drehArt.value === 'winkel' ? zahlAus(winkel.value) : null,
    richtungPlan: drehArt.value === 'richtung' ? linie(punkte.rpa, punkte.rpb) : null,
    richtungBezug: drehArt.value === 'richtung' ? linie(punkte.rba, punkte.rbb) : null
  }
  speichert.value = true
  try {
    const plan = await api.planEigenerMassstab(
      props.plan.id,
      props.plan.version,
      bezugId.value,
      gueltigeStrecken.value.map((s) => ({ u1: s.a.u, v1: s.a.v, u2: s.b.u, v2: s.b.v, laengeMeter: s.laengeMeter })),
      angaben
    )
    emit('gespeichert', plan)
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

const gesetzt = (...slots: Slot[]) => slots.every((s) => punkte[s])
</script>

<template>
  <aside class="seitenleiste">
    <h2>Plan referenzieren</h2>
    <MethodenWahl methode="EigenerMassstab" @wechseln="(m) => emit('methode', m)" />
    <p class="leise klein">
      Für Pläne, die auf keinem anderen Plan erkennbar sind: Massstab über bekannte Strecken, dazu die Lage eines
      Punkts und die Drehung. Gesetzte Punkte lassen sich mit der Maus verschieben.
    </p>

    <div class="feld">
      <label for="em-bezugsplan">Bezugsplan</label>
      <select id="em-bezugsplan" v-model="bezugId">
        <option v-for="p in waehlbar" :key="p.id" :value="p.id">{{ planText(p) }}</option>
      </select>
    </div>

    <p v-if="anleitung" class="anleitung" role="status">{{ anleitung }}</p>

    <!-- 1 · Massstab -->
    <section class="abschnitt">
      <h3>1 · Massstab</h3>
      <div v-for="(s, i) in streckenDaten" :key="i" class="strecke">
        <div class="kopf">
          <span class="untertitel">Strecke {{ i + 1 }}<span v-if="i === 1" class="leise"> (optional)</span></span>
          <span class="knoepfe">
            <button type="button" class="zweitrangig klein-btn" @click="setzen(i === 0 ? 's1a' : 's2a')">
              {{ s.a ? 'Neu setzen' : 'Setzen' }}
            </button>
            <button v-if="i === 1 && (s.a || s.text)" type="button" class="zweitrangig klein-btn"
              @click="streckeZweiEntfernen">Entfernen</button>
          </span>
        </div>
        <div class="zeile">
          <input :id="`em-laenge-${i}`" v-model="laengen[i]" type="text" inputmode="decimal" placeholder="Länge"
            :aria-label="`Länge Strecke ${i + 1} in Metern`" />
          <span>m</span>
          <span v-if="s.a && s.b" class="leise klein">{{ formatZahl(abstand(s.a, s.b), 0) }} px</span>
        </div>
      </div>
      <p v-if="massstab" class="wert">{{ formatMassstab(massstab.massstab) }}
        <span v-if="massstab.abweichungProzent !== null"
          :class="massstab.abweichungProzent > HINWEIS_ABWEICHUNG_PROZENT ? 'warnung' : 'leise'">
          · Abweichung {{ formatZahl(massstab.abweichungProzent, 1) }} %</span>
      </p>
    </section>

    <!-- 2 · Lage -->
    <section class="abschnitt">
      <div class="kopf">
        <h3>2 · Lage</h3>
        <button type="button" class="zweitrangig klein-btn" @click="setzen('anker')">
          {{ punkte.anker ? 'Anker neu setzen' : 'Anker setzen' }}
        </button>
      </div>
      <p class="leise klein">Ein Punkt im Plan (Anker), dessen Lage bekannt ist.</p>
      <div class="wahl">
        <label><input v-model="lageArt" type="radio" value="klick" /> im Bezugsplan anklicken</label>
        <label><input v-model="lageArt" type="radio" value="koordinate" /> Koordinate eingeben</label>
      </div>
      <template v-if="lageArt === 'klick'">
        <p class="wert">
          <span :class="punkte.anker ? 'ok' : 'leise'">{{ punkte.anker ? '✓' : '–' }} im Plan</span> ·
          <span :class="punkte.ankerBezug ? 'ok' : 'leise'">{{ punkte.ankerBezug ? '✓' : '–' }} im Bezugsplan</span>
          <button v-if="punkte.anker" type="button" class="link" @click="setzen('ankerBezug')">neu anklicken</button>
        </p>
      </template>
      <div v-else class="zeile">
        <label for="em-x" class="inline">x</label>
        <input id="em-x" v-model="ankerX" type="text" inputmode="decimal" placeholder="z.B. 150" />
        <label for="em-y" class="inline">y</label>
        <input id="em-y" v-model="ankerY" type="text" inputmode="decimal" placeholder="z.B. −60" />
        <span>m</span>
      </div>
      <p v-if="ankerWelt" class="wert leise">Anker bei x {{ formatZahl(ankerWelt.x) }} m · y {{ formatZahl(ankerWelt.y) }} m</p>
    </section>

    <!-- 3 · Drehung -->
    <section class="abschnitt">
      <h3>3 · Drehung</h3>
      <div class="wahl">
        <label><input v-model="drehArt" type="radio" value="winkel" /> Winkel</label>
        <label><input v-model="drehArt" type="radio" value="richtung" /> Richtung in beiden Plänen</label>
      </div>
      <template v-if="drehArt === 'winkel'">
        <div class="zeile">
          <input id="em-winkel" v-model="winkel" type="text" inputmode="decimal" aria-label="Winkel in Grad" />
          <span>°</span>
        </div>
        <p class="leise klein">
          Um wie viel der Plan gegenüber dem Masterplan gedreht ist, positiv = gegen den Uhrzeigersinn. Beispiel: Zeigt
          der Nordpfeil im Plan nach links und im Masterplan nach oben, sind es −90°.
        </p>
      </template>
      <template v-else>
        <p class="leise klein">
          Dieselbe Richtung in beiden Plänen, z.B. die beiden Nordpfeile (vom Ende zur Spitze) oder parallele Wände.
        </p>
        <p class="wert">
          <span :class="gesetzt('rpa', 'rpb') ? 'ok' : 'leise'">{{ gesetzt('rpa', 'rpb') ? '✓' : '–' }} im Plan</span>
          <button type="button" class="link" @click="setzen('rpa')">{{ punkte.rpa ? 'neu' : 'setzen' }}</button> ·
          <span :class="gesetzt('rba', 'rbb') ? 'ok' : 'leise'">{{ gesetzt('rba', 'rbb') ? '✓' : '–' }} im Bezugsplan</span>
          <button type="button" class="link" @click="setzen('rba')">{{ punkte.rba ? 'neu' : 'setzen' }}</button>
        </p>
      </template>
    </section>

    <ul v-if="probleme.length" class="fehler klein liste">
      <li v-for="p in probleme" :key="p">{{ p }}</li>
    </ul>

    <section v-if="lage" class="abschnitt">
      <h3>Ergebnis</h3>
      <p class="wert gross">{{ formatMassstab(lage.massstab) }}</p>
      <p class="wert">Drehung gegenüber dem Masterplan: {{ formatDrehung(lage.drehung) }}</p>
      <p class="wert">
        Plangrösse: {{ formatZahl(plan.bildBreite * lage.massstab, 1) }} × {{ formatZahl(plan.bildHoehe * lage.massstab, 1) }} m
      </p>
      <p class="leise klein">
        Kontrolle: Der gestrichelte Rahmen rechts ist der Umriss dieses Plans; der orange Ring zeigt zur Maus die
        gleiche Stelle im anderen Plan.
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
.strecke {
  margin: 0.5rem 0 0.6rem;
}
.untertitel {
  font-size: 0.9rem;
  font-weight: 600;
}
.knoepfe {
  display: flex;
  gap: 0.3rem;
}
.zeile {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  margin-top: 0.3rem;
}
.zeile input {
  width: 6.5rem;
}
label.inline {
  margin: 0;
}
.wahl {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  margin: 0.4rem 0;
}
.wahl label {
  font-weight: 400;
  font-size: 0.9rem;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.ok {
  color: var(--gut);
  font-weight: 600;
}
.link {
  background: none;
  border: none;
  color: var(--akzent);
  padding: 0 0.2rem;
  font-size: 0.85rem;
  text-decoration: underline;
}
.gross {
  font-size: 1.1rem;
  font-weight: 600;
}
.liste {
  padding-left: 1.1rem;
  margin: 0.5rem 0;
}
</style>
