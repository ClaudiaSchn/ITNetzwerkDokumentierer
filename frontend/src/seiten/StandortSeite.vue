<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { api, type Plan, type PositionImPlan, type Positionstyp, type Punkt, type Standort } from '../api'
import { formatMassstab, formatZahl } from '../geometrie'
import { naechsteBezeichnung } from '../format'
import PlanAnsicht, { type Linie, type Markierung } from '../komponenten/PlanAnsicht.vue'
import MasterKalibrierung from '../komponenten/MasterKalibrierung.vue'
import PositionenPanel from '../komponenten/PositionenPanel.vue'

const props = defineProps<{ id: number }>()

const standort = ref<Standort | null>(null)
const plan = ref<Plan | null>(null)
const laedt = ref(true)
const ladeFehler = ref<string | null>(null)

const datei = ref<File | null>(null)
const bezeichnung = ref('')
const laedtHoch = ref(false)
const uploadFehler = ref<string | null>(null)

/** ansehen = Liste/Auswahl, setzen = Klick erzeugt neue Position, kalibrieren = Kalibrier-Werkzeug */
type Modus = 'ansehen' | 'setzen' | 'kalibrieren'
const modus = ref<Modus>('ansehen')
const meldung = ref<string | null>(null)
const ansicht = ref<InstanceType<typeof PlanAnsicht> | null>(null)

// Kalibrierung
const kalibrierung = ref<InstanceType<typeof MasterKalibrierung> | null>(null)
const werkzeugMarkierungen = ref<Markierung[]>([])
const werkzeugLinien = ref<Linie[]>([])
const kalibrierKlick = ref(false)

// Positionen
const positionen = ref<PositionImPlan[]>([])
const typen = ref<Positionstyp[]>([])
const auswahlId = ref<number | null>(null)
const entwurf = ref<Punkt | null>(null)
const verschiebenAktiv = ref(false)
const verschobenNach = ref<Punkt | null>(null)
const vorschlag = ref({ bezeichnung: '', positionstypId: null as number | null, stockwerk: 0 })

const auswahl = computed(() => positionen.value.find((p) => p.id === auswahlId.value) ?? null)

function melden(text: string) {
  meldung.value = text
  setTimeout(() => {
    if (meldung.value === text) meldung.value = null
  }, 5000)
}

async function laden() {
  laedt.value = true
  ladeFehler.value = null
  plan.value = null
  positionen.value = []
  auswahlId.value = null
  entwurf.value = null
  try {
    standort.value = await api.standort(props.id)
    if (standort.value.masterplan) plan.value = await api.plan(standort.value.masterplan.id)
    modus.value = plan.value && !plan.value.referenziert ? 'kalibrieren' : 'ansehen'
    if (plan.value?.referenziert) await positionenLaden()
  } catch (e) {
    ladeFehler.value = (e as Error).message
  } finally {
    laedt.value = false
  }
}

async function positionenLaden() {
  if (!plan.value?.referenziert) return
  const [liste, t] = await Promise.all([api.positionenImPlan(plan.value.id), api.positionstypen()])
  positionen.value = liste
  typen.value = t
}

function dateiGewaehlt(ereignis: Event) {
  uploadFehler.value = null
  const eingabe = ereignis.target as HTMLInputElement
  datei.value = eingabe.files?.[0] ?? null
  if (datei.value && !bezeichnung.value) bezeichnung.value = datei.value.name.replace(/\.pdf$/i, '')
}

async function hochladen() {
  uploadFehler.value = null
  if (!datei.value) {
    uploadFehler.value = 'Bitte zuerst eine PDF-Datei auswählen.'
    return
  }
  if (!datei.value.name.toLowerCase().endsWith('.pdf')) {
    uploadFehler.value = 'Bitte eine PDF-Datei auswählen.'
    return
  }
  laedtHoch.value = true
  try {
    plan.value = await api.masterplanImportieren(props.id, datei.value, bezeichnung.value)
    if (standort.value)
      standort.value.masterplan = { id: plan.value.id, bezeichnung: plan.value.bezeichnung, referenziert: false }
    modus.value = 'kalibrieren'
  } catch (e) {
    uploadFehler.value = (e as Error).message
  } finally {
    laedtHoch.value = false
  }
}

// --- Kalibrierung ---

function werkzeugAnzeige(m: Markierung[], l: Linie[], klick: boolean) {
  werkzeugMarkierungen.value = m
  werkzeugLinien.value = l
  kalibrierKlick.value = klick
}

async function kalibrierungGespeichert(neu: Plan) {
  plan.value = neu
  modus.value = 'ansehen'
  kalibrierKlick.value = false
  melden(`Masterplan kalibriert: ${formatMassstab(neu.massstab)}`)
  await positionenLaden() // Lage im Plan hat sich ggf. geändert
}

function kalibrierenStarten() {
  schliessen()
  modus.value = 'kalibrieren'
}

// --- Positionen ---

function setzenUmschalten() {
  if (modus.value === 'setzen') {
    modus.value = 'ansehen'
    entwurf.value = null
  } else {
    schliessen()
    modus.value = 'setzen'
  }
}

/** Panel schliessen: Entwurf verwerfen, Auswahl aufheben */
function schliessen() {
  entwurf.value = null
  auswahlId.value = null
  verschiebenAktiv.value = false
  verschobenNach.value = null
}

function positionGespeichert(p: PositionImPlan, neu: boolean) {
  const i = positionen.value.findIndex((x) => x.id === p.id)
  if (i >= 0) positionen.value[i] = p
  else positionen.value = [...positionen.value, p].sort((a, b) => a.bezeichnung.localeCompare(b.bezeichnung, 'de'))
  verschobenNach.value = null
  verschiebenAktiv.value = false
  if (neu) {
    // Serienerfassung: Vorgaben für die nächste Position
    vorschlag.value = {
      bezeichnung: naechsteBezeichnung(p.bezeichnung),
      positionstypId: p.positionstypId,
      stockwerk: p.stockwerk
    }
    entwurf.value = null
    melden(`Position «${p.bezeichnung}» gespeichert. Nächste Position in den Plan klicken.`)
  } else {
    auswahlId.value = null
    melden(`Position «${p.bezeichnung}» geändert.`)
  }
}

function positionGeloescht(id: number) {
  const p = positionen.value.find((x) => x.id === id)
  positionen.value = positionen.value.filter((x) => x.id !== id)
  schliessen()
  if (p) melden(`Position «${p.bezeichnung}» gelöscht.`)
}

function auswaehlen(id: number) {
  entwurf.value = null
  verschobenNach.value = null
  verschiebenAktiv.value = false
  auswahlId.value = id
  const p = positionen.value.find((x) => x.id === id)
  if (p) ansicht.value?.zentrieren({ u: p.u, v: p.v })
}

// --- Ereignisse aus der Planansicht ---

function klick(p: Punkt) {
  if (modus.value === 'kalibrieren') kalibrierung.value?.klick(p)
  else if (modus.value === 'setzen') {
    auswahlId.value = null
    entwurf.value = p
  }
}

function verschoben(id: string, p: Punkt) {
  if (modus.value === 'kalibrieren') kalibrierung.value?.verschoben(id, p)
  else if (id === 'entwurf') entwurf.value = p
  else if (id === `pos-${auswahlId.value}`) verschobenNach.value = p
}

function markierungKlick(id: string) {
  if (modus.value === 'kalibrieren' || !id.startsWith('pos-')) return
  auswaehlen(Number(id.slice(4)))
}

const markierungen = computed<Markierung[]>(() => {
  if (modus.value === 'kalibrieren') return werkzeugMarkierungen.value
  const liste: Markierung[] = positionen.value.map((p) => {
    const aktiv = p.id === auswahlId.value
    return {
      id: `pos-${p.id}`,
      punkt: aktiv && verschobenNach.value ? verschobenNach.value : { u: p.u, v: p.v },
      art: 'position',
      text: p.bezeichnung,
      klickbar: true,
      aktiv,
      verschiebbar: aktiv && verschiebenAktiv.value
    }
  })
  if (entwurf.value) liste.push({ id: 'entwurf', punkt: entwurf.value, art: 'position', neu: true, verschiebbar: true })
  if (plan.value?.nullpunkt) liste.unshift({ id: 'nullpunkt', punkt: plan.value.nullpunkt, art: 'nullpunkt', text: '0/0' })
  return liste
})
const linien = computed(() => (modus.value === 'kalibrieren' ? werkzeugLinien.value : []))
const klickModus = computed(() => (modus.value === 'kalibrieren' ? kalibrierKlick.value : modus.value === 'setzen'))

// Esc: Entwurf/Auswahl schliessen, dann Setzen-Modus beenden
function taste(e: KeyboardEvent) {
  if (e.key !== 'Escape' || modus.value === 'kalibrieren') return
  if (entwurf.value || auswahlId.value !== null) schliessen()
  else if (modus.value === 'setzen') modus.value = 'ansehen'
}
onMounted(() => window.addEventListener('keydown', taste))
onBeforeUnmount(() => window.removeEventListener('keydown', taste))

watch(() => props.id, laden, { immediate: true })
</script>

<template>
  <main v-if="laedt" class="inhalt"><p class="leise">Wird geladen …</p></main>
  <main v-else-if="ladeFehler" class="inhalt">
    <p class="fehler">{{ ladeFehler }}</p>
    <RouterLink to="/">Zurück zu den Standorten</RouterLink>
  </main>

  <!-- Masterplan vorhanden: Planansicht über die ganze Fläche -->
  <div v-else-if="standort && plan" class="plan-seite">
    <div class="leiste">
      <RouterLink to="/" class="zurueck">← Standorte</RouterLink>
      <strong>{{ standort.name }}</strong>
      <span class="leise">Masterplan: {{ plan.bezeichnung }}</span>
      <span v-if="plan.referenziert" class="kalibriert" :title="plan.abweichungProzent !== null
        ? `Abweichung zwischen den Strecken: ${formatZahl(plan.abweichungProzent, 1)} %` : ''">
        ✓ kalibriert · {{ formatMassstab(plan.massstab) }}
      </span>
      <span v-else class="offen">nicht kalibriert</span>
      <span class="rechts">
        <button v-if="plan.referenziert && modus !== 'kalibrieren'" type="button" class="klein-btn"
          :class="modus === 'setzen' ? 'aktiv-btn' : 'zweitrangig'" :aria-pressed="modus === 'setzen'"
          title="Klick in den Plan setzt eine neue Position (Esc beendet)" @click="setzenUmschalten">
          {{ modus === 'setzen' ? '✓ Position setzen' : '＋ Position setzen' }}
        </button>
        <button v-if="plan.referenziert && modus !== 'kalibrieren'" type="button" class="zweitrangig klein-btn"
          @click="kalibrierenStarten">Kalibrierung ändern</button>
        <a :href="plan.pdfUrl">Original-PDF</a>
      </span>
    </div>
    <p v-if="meldung" class="meldung" role="status">{{ meldung }}</p>
    <p v-else-if="modus === 'setzen' && !entwurf" class="meldung info" role="status">
      Klicken Sie in den Plan, um eine Position zu setzen. Esc beendet den Modus.
    </p>
    <div class="arbeitsflaeche">
      <PlanAnsicht ref="ansicht" :plan="plan" :markierungen="markierungen" :linien="linien" :klick-modus="klickModus"
        class="ansicht" @klick="klick" @verschoben="verschoben" @markierung-klick="markierungKlick" />
      <MasterKalibrierung v-if="modus === 'kalibrieren'" ref="kalibrierung" :plan="plan" @anzeige="werkzeugAnzeige"
        @gespeichert="kalibrierungGespeichert" @abbrechen="modus = 'ansehen'" />
      <PositionenPanel v-else-if="plan.referenziert" :plan="plan" :standort-id="standort.id" :positionen="positionen"
        :typen="typen" :entwurf="entwurf" :auswahl="auswahl" :verschoben-nach="verschobenNach" :vorschlag="vorschlag"
        @gespeichert="positionGespeichert" @geloescht="positionGeloescht" @abbrechen="schliessen"
        @auswaehlen="auswaehlen" @verschieben="(a: boolean) => { verschiebenAktiv = a; if (!a) verschobenNach = null }"
        @typ-angelegt="(t: Positionstyp) => (typen = [...typen, t].sort((a, b) => a.name.localeCompare(b.name, 'de')))" />
    </div>
  </div>

  <!-- Noch kein Masterplan: Import -->
  <main v-else-if="standort" class="inhalt">
    <RouterLink to="/" class="zurueck-link">← Standorte</RouterLink>
    <h1>{{ standort.name }}</h1>
    <form class="karte" @submit.prevent="hochladen">
      <h2>Masterplan importieren</h2>
      <p class="leise">
        Der Masterplan ist der Übersichtsplan des Standorts, auf den alle anderen Pläne ausgerichtet werden.
        Erlaubt ist ein einseitiges PDF bis 100 MB.
      </p>
      <div class="feld">
        <label for="pdf">PDF-Datei</label>
        <input id="pdf" type="file" accept="application/pdf,.pdf" @change="dateiGewaehlt" />
      </div>
      <div class="feld">
        <label for="bezeichnung">Bezeichnung</label>
        <input id="bezeichnung" v-model="bezeichnung" type="text" maxlength="100" placeholder="z.B. Campusplan" />
      </div>
      <button type="submit" :disabled="laedtHoch">
        {{ laedtHoch ? 'Wird hochgeladen und umgewandelt …' : 'Importieren' }}
      </button>
      <p v-if="uploadFehler" class="fehler">{{ uploadFehler }}</p>
    </form>
  </main>
</template>

<style scoped>
.plan-seite {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 3.25rem);
}
.leiste {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  padding: 0.5rem 1rem;
  background: var(--flaeche);
  border-bottom: 1px solid var(--rand);
}
.zurueck,
.rechts a {
  text-decoration: none;
}
.kalibriert {
  color: var(--gut);
  font-size: 0.9rem;
  font-weight: 600;
}
.offen {
  color: #9a6700;
  font-size: 0.9rem;
  font-weight: 600;
}
.rechts {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 1rem;
}
.klein-btn {
  padding: 0.2rem 0.7rem;
  font-size: 0.85rem;
}
.aktiv-btn {
  background: #fb8500;
  border-color: #c26400;
}
.meldung.info {
  background: #ddf4ff;
  border-bottom-color: #54aeff;
  color: #0a3069;
}
.meldung {
  margin: 0;
  padding: 0.45rem 1rem;
  background: #dafbe1;
  border-bottom: 1px solid #4ac26b;
  color: #116329;
  font-size: 0.9rem;
}
.arbeitsflaeche {
  flex: 1;
  min-height: 0;
  display: flex;
}
.ansicht {
  flex: 1;
  min-width: 0;
}
.zurueck-link {
  display: inline-block;
  margin-bottom: 0.75rem;
  text-decoration: none;
}
.feld {
  margin: 0.9rem 0;
}
</style>
