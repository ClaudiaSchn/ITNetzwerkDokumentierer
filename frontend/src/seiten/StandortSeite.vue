<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Plan, type Punkt, type Standort } from '../api'
import { formatMassstab, formatZahl } from '../geometrie'
import PlanAnsicht, { type Linie, type Markierung } from '../komponenten/PlanAnsicht.vue'
import MasterKalibrierung from '../komponenten/MasterKalibrierung.vue'

const props = defineProps<{ id: number }>()

const standort = ref<Standort | null>(null)
const plan = ref<Plan | null>(null)
const laedt = ref(true)
const ladeFehler = ref<string | null>(null)

const datei = ref<File | null>(null)
const bezeichnung = ref('')
const laedtHoch = ref(false)
const uploadFehler = ref<string | null>(null)

// Kalibrierung
const kalibrieren = ref(false)
const kalibrierung = ref<InstanceType<typeof MasterKalibrierung> | null>(null)
const werkzeugMarkierungen = ref<Markierung[]>([])
const werkzeugLinien = ref<Linie[]>([])
const klickModus = ref(false)
const meldung = ref<string | null>(null)

async function laden() {
  laedt.value = true
  ladeFehler.value = null
  plan.value = null
  try {
    standort.value = await api.standort(props.id)
    if (standort.value.masterplan) plan.value = await api.plan(standort.value.masterplan.id)
    kalibrieren.value = !!plan.value && !plan.value.referenziert
  } catch (e) {
    ladeFehler.value = (e as Error).message
  } finally {
    laedt.value = false
  }
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
    kalibrieren.value = true
  } catch (e) {
    uploadFehler.value = (e as Error).message
  } finally {
    laedtHoch.value = false
  }
}

function werkzeugAnzeige(m: Markierung[], l: Linie[], klick: boolean) {
  werkzeugMarkierungen.value = m
  werkzeugLinien.value = l
  klickModus.value = klick
}

function kalibrierungGespeichert(neu: Plan) {
  plan.value = neu
  kalibrieren.value = false
  klickModus.value = false
  meldung.value = `Masterplan kalibriert: ${formatMassstab(neu.massstab)}`
  setTimeout(() => (meldung.value = null), 5000)
}

// Ausserhalb der Kalibrierung: Nullpunkt dezent anzeigen
const markierungen = computed<Markierung[]>(() => {
  if (kalibrieren.value) return werkzeugMarkierungen.value
  return plan.value?.nullpunkt
    ? [{ id: 'nullpunkt', punkt: plan.value.nullpunkt, art: 'nullpunkt', text: '0/0' }]
    : []
})
const linien = computed(() => (kalibrieren.value ? werkzeugLinien.value : []))

const klick = (p: Punkt) => kalibrierung.value?.klick(p)
const verschoben = (id: string, p: Punkt) => kalibrierung.value?.verschoben(id, p)

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
        <button v-if="plan.referenziert && !kalibrieren" type="button" class="zweitrangig klein-btn"
          @click="kalibrieren = true">Kalibrierung ändern</button>
        <a :href="plan.pdfUrl">Original-PDF</a>
      </span>
    </div>
    <p v-if="meldung" class="meldung" role="status">{{ meldung }}</p>
    <div class="arbeitsflaeche">
      <PlanAnsicht :plan="plan" :markierungen="markierungen" :linien="linien" :klick-modus="kalibrieren && klickModus"
        class="ansicht" @klick="klick" @verschoben="verschoben" />
      <MasterKalibrierung v-if="kalibrieren" ref="kalibrierung" :plan="plan" @anzeige="werkzeugAnzeige"
        @gespeichert="kalibrierungGespeichert" @abbrechen="kalibrieren = false" />
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
