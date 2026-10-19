<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { api, type Geraet, type Geraetetyp, type Portart, type PositionImPlan } from '../api'
import { naechsteBezeichnung } from '../format'
import GeraetetypFormular from './GeraetetypFormular.vue'

/** Geräte der ausgewählten Position: Liste, Gerät platzieren, Gerät öffnen */
const props = defineProps<{
  position: PositionImPlan
  geraetetypen: Geraetetyp[]
  portarten: Portart[]
  /** Wird erhöht, wenn sich Geräte geändert haben (z.B. in der Geräte-Seitenleiste) */
  aktualisierung: number
}>()
const emit = defineEmits<{
  oeffnen: [id: number]
  geaendert: []
  geraetetypAngelegt: [typ: Geraetetyp]
  portartAngelegt: [portart: Portart]
}>()

const geraete = ref<Geraet[]>([])
const formularOffen = ref(false)
const bezeichnung = ref('')
const typId = ref<number | null>(null)
const neuerTyp = ref(false)
const fehler = ref<string | null>(null)
const speichert = ref(false)
const feld = ref<HTMLInputElement | null>(null)
let letzte = { bezeichnung: '', typId: null as number | null }

const NEUER_TYP = -1

async function laden() {
  try {
    geraete.value = await api.geraete(props.position.id)
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
watch(() => [props.position.id, props.aktualisierung], () => {
  formularOffen.value = false
  laden()
}, { immediate: true })

function oeffnenFormular() {
  fehler.value = null
  formularOffen.value = true
  neuerTyp.value = false
  bezeichnung.value = letzte.bezeichnung ? naechsteBezeichnung(letzte.bezeichnung) : ''
  typId.value = letzte.typId ?? props.geraetetypen[0]?.id ?? null
  nextTick(() => feld.value?.focus())
}

watch(typId, (id) => {
  if (id === NEUER_TYP) {
    neuerTyp.value = true
    typId.value = letzte.typId ?? props.geraetetypen[0]?.id ?? null
  }
})

function typAngelegt(t: Geraetetyp) {
  if (!props.geraetetypen.some((x) => x.id === t.id)) emit('geraetetypAngelegt', t)
  typId.value = t.id
  neuerTyp.value = false
}

async function platzieren() {
  fehler.value = null
  if (!bezeichnung.value.trim()) {
    fehler.value = 'Bitte eine Bezeichnung eingeben, z.B. «PP-01» oder «SW-01».'
    return
  }
  if (typId.value === null) {
    fehler.value = 'Bitte einen Gerätetyp wählen.'
    return
  }
  speichert.value = true
  try {
    const g = await api.geraetPlatzieren(props.position.id, bezeichnung.value.trim(), typId.value)
    geraete.value = [...geraete.value, g].sort((a, b) => a.bezeichnung.localeCompare(b.bezeichnung, 'de'))
    letzte = { bezeichnung: g.bezeichnung, typId: g.geraetetypId }
    formularOffen.value = false
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}
</script>

<template>
  <section class="geraete">
    <div class="kopf">
      <h3>Geräte <span class="leise">({{ geraete.length }})</span></h3>
      <button v-if="!formularOffen" type="button" class="klein-btn" @click="oeffnenFormular">＋ Gerät</button>
    </div>

    <div v-if="formularOffen" class="formular" @keydown.enter.prevent>
      <div class="feld">
        <label for="gr-bez">Bezeichnung</label>
        <input id="gr-bez" ref="feld" v-model="bezeichnung" type="text" maxlength="100" placeholder="z.B. PP-01, SW-01"
          @keydown.enter.prevent="platzieren" />
      </div>
      <div class="feld">
        <label for="gr-typ">Gerätetyp</label>
        <select id="gr-typ" v-model="typId">
          <option v-for="t in geraetetypen" :key="t.id" :value="t.id">{{ t.name }}</option>
          <option :value="NEUER_TYP">＋ Neuer Gerätetyp …</option>
        </select>
        <p v-if="!neuerTyp && typId !== null" class="leise klein">
          {{ geraetetypen.find((t) => t.id === typId)?.kategorie === 'Durchgang' ? 'Durchgang' : 'Endgerät' }}:
          {{ geraetetypen.find((t) => t.id === typId)?.ports }}
        </p>
        <GeraetetypFormular v-if="neuerTyp" :portarten="portarten" :vorhandene="geraetetypen" @angelegt="typAngelegt" @abbrechen="neuerTyp = false"
          @portart-angelegt="(p) => emit('portartAngelegt', p)" />
      </div>
      <p v-if="fehler" class="fehler klein">{{ fehler }}</p>
      <div class="zeile">
        <button type="button" class="klein-btn" :disabled="speichert || neuerTyp" @click="platzieren">Platzieren</button>
        <button type="button" class="zweitrangig klein-btn" @click="formularOffen = false">Abbrechen</button>
      </div>
    </div>

    <p v-if="geraete.length === 0 && !formularOffen" class="leise klein">Noch keine Geräte an dieser Position.</p>
    <ul class="liste">
      <li v-for="g in geraete" :key="g.id">
        <button type="button" class="eintrag" title="Gerät mit Ports anzeigen" @click="emit('oeffnen', g.id)">
          <span class="name">{{ g.bezeichnung }}</span>
          <span class="leise klein">{{ g.geraetetyp }} · {{ g.belegtePorts }}/{{ g.anzahlPorts }} Ports belegt</span>
        </button>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.geraete {
  border-top: 1px solid var(--rand);
  margin-top: 1rem;
  padding-top: 0.8rem;
}
.kopf {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
h3 {
  font-size: 0.95rem;
  margin: 0;
}
.klein-btn {
  padding: 0.2rem 0.6rem;
  font-size: 0.85rem;
}
.klein {
  font-size: 0.85rem;
  margin: 0.25rem 0 0;
}
.formular {
  background: var(--hintergrund);
  border: 1px solid var(--rand);
  border-radius: 6px;
  padding: 0.6rem 0.7rem;
  margin: 0.6rem 0;
}
.feld {
  margin: 0.5rem 0;
}
label {
  font-size: 0.85rem;
}
select {
  font: inherit;
  width: 100%;
  padding: 0.4rem 0.5rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
  background: var(--flaeche);
}
.zeile {
  display: flex;
  gap: 0.35rem;
  margin-top: 0.4rem;
}
.liste {
  list-style: none;
  padding: 0;
  margin: 0.4rem 0 0;
}
.eintrag {
  width: 100%;
  text-align: left;
  display: flex;
  flex-direction: column;
  background: none;
  color: inherit;
  border: none;
  border-bottom: 1px solid var(--rand);
  border-radius: 0;
  padding: 0.4rem 0.25rem;
}
.eintrag:hover {
  background: var(--hintergrund);
}
.name {
  font-weight: 600;
}
</style>
