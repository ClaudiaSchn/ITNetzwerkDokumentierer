<script setup lang="ts">
import { computed, ref } from 'vue'
import { api, type Plan, type PlanListe } from '../api'
import { stockwerkText } from '../format'
import { planText } from '../plaene'

/** Seitenleiste «Plan importieren»: weiteres PDF (Gebäude, Geschoss, Raum) mit Stockwerk und Bezugsplan */
const props = defineProps<{ standortId: number; plaene: PlanListe[]; vorschlagBezug: number | null }>()
const emit = defineEmits<{ importiert: [plan: Plan]; abbrechen: [] }>()

const datei = ref<File | null>(null)
const bezeichnung = ref('')
const stockwerk = ref(0)
const waehlbar = computed(() => props.plaene.filter((p) => p.referenziert))
const bezugId = ref<number | null>(
  waehlbar.value.some((p) => p.id === props.vorschlagBezug) ? props.vorschlagBezug : (waehlbar.value[0]?.id ?? null)
)
const laedtHoch = ref(false)
const fehler = ref<string | null>(null)

function dateiGewaehlt(ereignis: Event) {
  fehler.value = null
  datei.value = (ereignis.target as HTMLInputElement).files?.[0] ?? null
  if (datei.value && !bezeichnung.value) bezeichnung.value = datei.value.name.replace(/\.pdf$/i, '').replace(/_/g, ' ')
}

async function importieren() {
  fehler.value = null
  if (!datei.value || !datei.value.name.toLowerCase().endsWith('.pdf')) {
    fehler.value = 'Bitte eine PDF-Datei auswählen.'
    return
  }
  if (!Number.isInteger(stockwerk.value)) {
    fehler.value = 'Bitte ein ganzzahliges Stockwerk angeben.'
    return
  }
  if (bezugId.value === null) {
    fehler.value = 'Bitte einen Bezugsplan wählen.'
    return
  }
  laedtHoch.value = true
  try {
    emit('importiert', await api.planImportieren(props.standortId, datei.value, bezeichnung.value, stockwerk.value, bezugId.value))
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    laedtHoch.value = false
  }
}
</script>

<template>
  <form class="seitenleiste" @submit.prevent="importieren">
    <div class="kopf">
      <h2>Plan importieren</h2>
      <button type="button" class="zweitrangig klein-btn" title="Schliessen" @click="emit('abbrechen')">✕</button>
    </div>
    <p class="leise klein">
      Gebäude-, Geschoss- oder Raumplan als einseitiges PDF (bis 100 MB). Danach richten Sie ihn über zwei
      Passpunkte auf einen bestehenden Plan aus.
    </p>

    <div class="feld">
      <label for="plan-pdf">PDF-Datei</label>
      <input id="plan-pdf" type="file" accept="application/pdf,.pdf" @change="dateiGewaehlt" />
    </div>
    <div class="feld">
      <label for="plan-bezeichnung">Bezeichnung</label>
      <input id="plan-bezeichnung" v-model="bezeichnung" type="text" maxlength="100" placeholder="z.B. Gebäude A – EG" />
    </div>
    <div class="feld">
      <label for="plan-stockwerk">Stockwerk</label>
      <div class="stockwerk">
        <input id="plan-stockwerk" v-model.number="stockwerk" type="number" step="1" min="-10" max="100" />
        <span class="leise">{{ Number.isInteger(stockwerk) ? stockwerkText(stockwerk) : '' }}</span>
      </div>
      <p class="leise klein">0 = EG, 1 = 1. OG, −1 = 1. UG. Neue Positionen in diesem Plan erhalten dieses Stockwerk.</p>
    </div>
    <div class="feld">
      <label for="plan-bezug">Bezugsplan</label>
      <select id="plan-bezug" v-model="bezugId">
        <option v-for="p in waehlbar" :key="p.id" :value="p.id">{{ planText(p) }}</option>
      </select>
      <p class="leise klein">Der Plan, auf dem dieser Plan wiedererkennbar ist – meist der Masterplan oder ein Gebäudeplan.</p>
    </div>

    <p v-if="fehler" class="fehler">{{ fehler }}</p>
    <div class="aktionen">
      <button type="submit" :disabled="laedtHoch">{{ laedtHoch ? 'Wird hochgeladen und umgewandelt …' : 'Importieren' }}</button>
      <button type="button" class="zweitrangig" @click="emit('abbrechen')">Abbrechen</button>
    </div>
  </form>
</template>
