<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Patchung, type PortKurz, type Positionsgeraet, type Positionsport, type PositionImPlan } from '../api'
import { naechsteBezeichnung } from '../format'

/** Patchkabel innerhalb der ausgewählten Position, z.B. Patchpanel vorne → Switch */
const props = defineProps<{
  position: PositionImPlan
  /** Wird erhöht, wenn sich Geräte oder Belegungen geändert haben */
  aktualisierung: number
}>()
const emit = defineEmits<{ geaendert: []; geraet: [id: number] }>()

const patchungen = ref<Patchung[]>([])
const geraete = ref<Positionsgeraet[]>([])
const formularOffen = ref(false)
const port1 = ref<number | null>(null)
const port2 = ref<number | null>(null)
const bezeichnung = ref('')
const fehler = ref<string | null>(null)
const speichert = ref(false)
const loeschenBestaetigen = ref<number | null>(null)

async function laden() {
  try {
    const [p, g] = await Promise.all([api.patchungen(props.position.id), api.positionsports(props.position.id)])
    patchungen.value = p
    geraete.value = g
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
watch(() => [props.position.id, props.aktualisierung] as const, ([id], alt) => {
  if (id !== alt?.[0]) {
    // andere Position: Formular schliessen; sonst (nur Aktualisierung) offen lassen für Serienerfassung
    formularOffen.value = false
    port1.value = port2.value = null
  }
  loeschenBestaetigen.value = null
  laden()
}, { immediate: true })

const seiteKurz = (s: Positionsport['seite']) => (s === 'Vorne' ? ' vorne' : s === 'Hinten' ? ' hinten' : '')
const portText = (p: { nummer: number; seite: Positionsport['seite']; bezeichnung: string | null }) =>
  `${p.nummer}${seiteKurz(p.seite)}${p.bezeichnung ? ` (${p.bezeichnung})` : ''}`
const endeText = (p: PortKurz) => `${p.geraet} · ${portText(p)}`

/** Freie Ports je Gerät (weder angeschlossen noch gepatcht) */
const freie = computed(() =>
  geraete.value.map((g) => ({ g, ports: g.ports.filter((p) => p.belegt === null) })).filter((x) => x.ports.length > 0)
)
const freieAnzahl = computed(() => freie.value.reduce((s, x) => s + x.ports.length, 0))

function finde(id: number | null) {
  for (const g of geraete.value) {
    const p = g.ports.find((x) => x.id === id)
    if (p) return { g, p }
  }
  return null
}

/** Nächster freier Port nach diesem (gleiches Gerät, gleiche Seite, Nummer + 1) */
function naechster(id: number | null): number | null {
  const t = finde(id)
  if (!t) return null
  const n = t.g.ports.find((x) => x.nummer === t.p.nummer + 1 && x.seite === t.p.seite && x.belegt === null)
  return n?.id ?? null
}

function oeffnen() {
  fehler.value = null
  formularOffen.value = true
}

async function anlegen() {
  fehler.value = null
  if (port1.value === null || port2.value === null) {
    fehler.value = 'Bitte beide Ports wählen.'
    return
  }
  if (port1.value === port2.value) {
    fehler.value = 'Bitte zwei verschiedene Ports wählen.'
    return
  }
  speichert.value = true
  try {
    const p = await api.patchungAnlegen(props.position.id, port1.value, port2.value, bezeichnung.value.trim())
    // Serienerfassung: nächste Ports und nächste Bezeichnung vorschlagen
    const [n1, n2] = [naechster(port1.value), naechster(port2.value)]
    await laden()
    patchungen.value = patchungen.value.some((x) => x.id === p.id) ? patchungen.value : [...patchungen.value, p]
    port1.value = n1
    port2.value = n2
    bezeichnung.value = p.bezeichnung ? naechsteBezeichnung(p.bezeichnung) : ''
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

async function loeschen(p: Patchung) {
  if (loeschenBestaetigen.value !== p.id) {
    loeschenBestaetigen.value = p.id
    return
  }
  loeschenBestaetigen.value = null
  fehler.value = null
  try {
    await api.patchungLoeschen(p.id, p.version)
    await laden()
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

const portartenUnterschiedlich = computed(() => {
  const a = finde(port1.value)?.p.portart
  const b = finde(port2.value)?.p.portart
  return a && b && a !== b ? `${a} ↔ ${b}` : null
})
</script>

<template>
  <section class="patchungen">
    <div class="kopf">
      <h3>Patchungen <span class="leise">({{ patchungen.length }})</span></h3>
      <button v-if="!formularOffen && geraete.length > 0" type="button" class="klein-btn" @click="oeffnen">＋ Patchung</button>
    </div>
    <p v-if="geraete.length === 0" class="leise klein">Für Patchungen braucht es Geräte an dieser Position.</p>

    <div v-if="formularOffen" class="formular" @keydown.enter.prevent>
      <div class="feld">
        <label for="pa-1">Von Port</label>
        <select id="pa-1" v-model="port1">
          <option :value="null">– wählen –</option>
          <optgroup v-for="x in freie" :key="x.g.id" :label="x.g.bezeichnung">
            <option v-for="p in x.ports" :key="p.id" :value="p.id">{{ x.g.bezeichnung }} · {{ portText(p) }} – {{ p.portart }}</option>
          </optgroup>
        </select>
      </div>
      <div class="feld">
        <label for="pa-2">Nach Port</label>
        <select id="pa-2" v-model="port2">
          <option :value="null">– wählen –</option>
          <optgroup v-for="x in freie" :key="x.g.id" :label="x.g.bezeichnung">
            <option v-for="p in x.ports" :key="p.id" :value="p.id" :disabled="p.id === port1">
              {{ x.g.bezeichnung }} · {{ portText(p) }} – {{ p.portart }}
            </option>
          </optgroup>
        </select>
      </div>
      <p v-if="portartenUnterschiedlich" class="hinweis klein">
        Unterschiedliche Portarten ({{ portartenUnterschiedlich }}) – passt nur mit entsprechendem Patchkabel bzw. Modul.
      </p>
      <div class="feld">
        <label for="pa-bez">Beschriftung (optional)</label>
        <input id="pa-bez" v-model="bezeichnung" type="text" maxlength="100" placeholder="z.B. P-001" />
      </div>
      <p class="leise klein">{{ freieAnzahl }} freie Ports an dieser Position. Nach dem Anlegen werden die nächsten Ports vorgeschlagen.</p>
      <p v-if="fehler" class="fehler klein">{{ fehler }}</p>
      <div class="zeile">
        <button type="button" class="klein-btn" :disabled="speichert" @click="anlegen">Patchen</button>
        <button type="button" class="zweitrangig klein-btn" @click="formularOffen = false; fehler = null">Fertig</button>
      </div>
    </div>
    <p v-else-if="fehler" class="fehler klein">{{ fehler }}</p>

    <ul class="liste">
      <li v-for="p in patchungen" :key="p.id">
        <div class="patch">
          <span>
            <button type="button" class="link" title="Gerät öffnen" @click="emit('geraet', p.port1.geraetId)">{{ endeText(p.port1) }}</button>
            ⇄
            <button type="button" class="link" title="Gerät öffnen" @click="emit('geraet', p.port2.geraetId)">{{ endeText(p.port2) }}</button>
          </span>
          <span v-if="p.bezeichnung" class="leise klein">{{ p.bezeichnung }}</span>
        </div>
        <button type="button" class="weg" :class="{ bestaetigen: loeschenBestaetigen === p.id }"
          :title="'Patchung entfernen'" @click="loeschen(p)">{{ loeschenBestaetigen === p.id ? 'Entfernen?' : '✕' }}</button>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.patchungen {
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
  font-size: 0.9rem;
  width: 100%;
  padding: 0.35rem 0.45rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
  background: var(--flaeche);
}
.hinweis {
  background: #fff8c5;
  border: 1px solid #d4a72c;
  border-radius: 6px;
  padding: 0.3rem 0.5rem;
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
.liste li {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--rand);
  padding: 0.35rem 0.1rem;
  font-size: 0.9rem;
}
.patch {
  flex: 1;
  display: flex;
  flex-direction: column;
}
.link {
  background: none;
  border: none;
  padding: 0;
  color: inherit;
  font-weight: 600;
  font-size: inherit;
  text-align: left;
}
.link:hover {
  color: var(--akzent);
  text-decoration: underline;
}
.weg {
  background: none;
  color: var(--text-leise);
  border: 1px solid transparent;
  padding: 0.15rem 0.45rem;
  font-size: 0.85rem;
}
.weg:hover,
.weg.bestaetigen {
  color: var(--schlecht);
  border-color: var(--schlecht);
}
</style>
