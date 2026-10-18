<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Plan, type PlanListe } from '../api'
import { formatDrehung, formatMassstab } from '../geometrie'
import { stockwerkText } from '../format'

/** Seitenleiste «Plan-Einstellungen»: Bezeichnung, Stockwerk, Lage, Referenzierung ändern, löschen */
const props = defineProps<{ plan: Plan; plaene: PlanListe[] }>()
const emit = defineEmits<{
  gespeichert: [plan: Plan]
  geloescht: [id: number]
  abbrechen: []
  /** Kalibrierung (Masterplan) bzw. Referenzierung neu machen */
  lageAendern: []
}>()

const bezeichnung = ref('')
const stockwerk = ref(0)
const fehler = ref<string | null>(null)
const speichert = ref(false)
const loeschenBestaetigen = ref(false)

function fuellen() {
  bezeichnung.value = props.plan.bezeichnung
  stockwerk.value = props.plan.stockwerk
  fehler.value = null
  loeschenBestaetigen.value = false
}
watch(() => props.plan, fuellen, { immediate: true })

const bezugsplan = computed(() => props.plaene.find((p) => p.id === props.plan.bezugsplanId) ?? null)
const abhaengig = computed(() => props.plaene.filter((p) => p.bezugsplanId === props.plan.id))
const geaendert = computed(() => bezeichnung.value.trim() !== props.plan.bezeichnung || stockwerk.value !== props.plan.stockwerk)

async function speichern() {
  fehler.value = null
  if (!bezeichnung.value.trim()) {
    fehler.value = 'Bitte eine Bezeichnung eingeben.'
    return
  }
  if (!Number.isInteger(stockwerk.value)) {
    fehler.value = 'Bitte ein ganzzahliges Stockwerk angeben.'
    return
  }
  speichert.value = true
  try {
    emit('gespeichert', await api.planAendern(props.plan.id, props.plan.version, bezeichnung.value.trim(), stockwerk.value))
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

async function loeschen() {
  if (!loeschenBestaetigen.value) {
    loeschenBestaetigen.value = true
    return
  }
  fehler.value = null
  try {
    await api.planLoeschen(props.plan.id, props.plan.version)
    emit('geloescht', props.plan.id)
  } catch (e) {
    fehler.value = (e as Error).message
    loeschenBestaetigen.value = false
  }
}
</script>

<template>
  <form class="seitenleiste" @submit.prevent="speichern">
    <div class="kopf">
      <h2>Plan-Einstellungen</h2>
      <button type="button" class="zweitrangig klein-btn" title="Schliessen" @click="emit('abbrechen')">✕</button>
    </div>

    <div class="feld">
      <label for="einst-bezeichnung">Bezeichnung</label>
      <input id="einst-bezeichnung" v-model="bezeichnung" type="text" maxlength="100" />
    </div>
    <div class="feld">
      <label for="einst-stockwerk">Stockwerk</label>
      <div class="stockwerk">
        <input id="einst-stockwerk" v-model.number="stockwerk" type="number" step="1" min="-10" max="100"
          :disabled="plan.istMasterplan" />
        <span class="leise">{{ Number.isInteger(stockwerk) ? stockwerkText(stockwerk) : '' }}</span>
      </div>
      <p v-if="plan.istMasterplan" class="leise klein">Der Masterplan liegt immer auf Stockwerk 0.</p>
      <p v-else class="leise klein">Bestehende Positionen behalten ihr Stockwerk.</p>
    </div>

    <div class="aktionen">
      <button type="submit" :disabled="!geaendert || speichert">{{ speichert ? 'Wird gespeichert …' : 'Speichern' }}</button>
    </div>

    <section class="abschnitt lage">
      <h3>Lage</h3>
      <template v-if="plan.referenziert">
        <p class="wert">{{ formatMassstab(plan.massstab) }}</p>
        <p v-if="!plan.istMasterplan" class="wert">Drehung: {{ formatDrehung(plan.drehung) }}</p>
        <p class="wert leise">
          {{ plan.istMasterplan ? 'Masterplan, kalibriert über Nullpunkt und zwei Strecken'
            : plan.methode === 'EigenerMassstab'
              ? `Eigener Massstab, Bezugsplan «${bezugsplan?.bezeichnung ?? '?'}»`
              : `Referenziert über zwei Passpunkte auf «${bezugsplan?.bezeichnung ?? '?'}»` }}
        </p>
      </template>
      <p v-else class="wert warnung">Noch nicht referenziert.</p>
      <button type="button" class="zweitrangig klein-btn" @click="emit('lageAendern')">
        {{ plan.istMasterplan ? 'Kalibrierung ändern' : 'Referenzierung ändern' }}
      </button>
      <p v-if="abhaengig.length" class="leise klein">
        Darauf referenziert: {{ abhaengig.map((p) => p.bezeichnung).join(', ') }} – diese Pläne werden bei einer
        Änderung automatisch mitgeführt.
      </p>
    </section>

    <section v-if="!plan.istMasterplan" class="abschnitt">
      <h3>Plan löschen</h3>
      <p class="leise klein">Positionen bleiben erhalten – sie hängen an ihrer Koordinate, nicht am Plan.</p>
      <div class="aktionen">
        <button type="button" class="gefahr" :disabled="abhaengig.length > 0" @click="loeschen">
          {{ loeschenBestaetigen ? 'Wirklich löschen?' : 'Plan löschen' }}
        </button>
      </div>
      <p v-if="abhaengig.length" class="leise klein">Gesperrt, solange andere Pläne darauf referenziert sind.</p>
    </section>

    <p v-if="fehler" class="fehler">{{ fehler }}</p>
  </form>
</template>

<style scoped>
.lage button {
  margin-top: 0.6rem;
}
.abschnitt .gefahr {
  margin-left: 0;
}
</style>
