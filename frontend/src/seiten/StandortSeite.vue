<script setup lang="ts">
import { ref, watch } from 'vue'
import { api, type Plan, type Standort } from '../api'
import PlanAnsicht from '../komponenten/PlanAnsicht.vue'

const props = defineProps<{ id: number }>()

const standort = ref<Standort | null>(null)
const plan = ref<Plan | null>(null)
const laedt = ref(true)
const ladeFehler = ref<string | null>(null)

const datei = ref<File | null>(null)
const bezeichnung = ref('')
const laedtHoch = ref(false)
const uploadFehler = ref<string | null>(null)

async function laden() {
  laedt.value = true
  ladeFehler.value = null
  plan.value = null
  try {
    standort.value = await api.standort(props.id)
    if (standort.value.masterplan) plan.value = await api.plan(standort.value.masterplan.id)
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
    if (standort.value) standort.value.masterplan = { id: plan.value.id, bezeichnung: plan.value.bezeichnung }
  } catch (e) {
    uploadFehler.value = (e as Error).message
  } finally {
    laedtHoch.value = false
  }
}

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
      <span class="leise">Masterplan: {{ plan.bezeichnung }} · {{ plan.bildBreite }} × {{ plan.bildHoehe }} px</span>
      <a :href="plan.pdfUrl" class="pdf">Original-PDF</a>
    </div>
    <PlanAnsicht :plan="plan" class="ansicht" />
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
  align-items: baseline;
  gap: 1rem;
  flex-wrap: wrap;
  padding: 0.6rem 1rem;
  background: var(--flaeche);
  border-bottom: 1px solid var(--rand);
}
.zurueck,
.pdf {
  text-decoration: none;
}
.pdf {
  margin-left: auto;
}
.ansicht {
  flex: 1;
  min-height: 0;
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
