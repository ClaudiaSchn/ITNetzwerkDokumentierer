<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type GeraetDetail, type Port } from '../api'

/**
 * Seitenleiste eines Geräts: Bezeichnung, Ports mit Portart, eigener Beschriftung und Belegung,
 * Beschriftungen nach Muster ausfüllen, Gerät entfernen.
 */
const props = defineProps<{ geraetId: number }>()
const emit = defineEmits<{
  schliessen: []
  /** Zurück zur Position des Geräts */
  zurPosition: [positionId: number]
  /** Gerät geändert oder entfernt (Listen und Zähler neu laden) */
  geaendert: []
  entfernt: []
  /** Medium öffnen, das einen Port belegt */
  medium: [mediumId: number]
  /** Anderes Gerät öffnen (Partner einer Patchung) */
  geraet: [geraetId: number]
  /** Leitungsverlauf eines Ports zeigen */
  verlauf: [portId: number]
}>()

const detail = ref<GeraetDetail | null>(null)
const fehler = ref<string | null>(null)
const meldung = ref<string | null>(null)
const bezeichnung = ref('')
/** Bearbeitete Portbeschriftungen nach Port-Id */
const beschriftung = ref<Record<number, string>>({})
const speichert = ref(false)
const entfernenBestaetigen = ref(false)

// Muster: {n} = Portnummer, z.B. «Gi1/0/{n}» oder «A{n}»
const muster = ref('')
const musterSeite = ref<'alle' | 'Vorne' | 'Hinten'>('alle')
const musterStart = ref(1)

async function laden() {
  fehler.value = null
  try {
    const d = await api.geraet(props.geraetId)
    detail.value = d
    bezeichnung.value = d.geraet.bezeichnung
    beschriftung.value = Object.fromEntries(d.ports.map((p) => [p.id, p.bezeichnung ?? '']))
    entfernenBestaetigen.value = false
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
watch(() => props.geraetId, laden, { immediate: true })

const durchgang = computed(() => detail.value?.geraet.kategorie === 'Durchgang')

/** Zeilen: pro Nummer der Einzel-Port bzw. Vorne/Hinten */
const zeilen = computed(() => {
  const map = new Map<number, { nummer: number; einzel?: Port; vorne?: Port; hinten?: Port }>()
  for (const p of detail.value?.ports ?? []) {
    const z = map.get(p.nummer) ?? { nummer: p.nummer }
    if (p.seite === 'Einzel') z.einzel = p
    else if (p.seite === 'Vorne') z.vorne = p
    else z.hinten = p
    map.set(p.nummer, z)
  }
  return [...map.values()].sort((a, b) => a.nummer - b.nummer)
})

const geaendertePorts = computed(() =>
  (detail.value?.ports ?? []).filter((p) => (beschriftung.value[p.id] ?? '').trim() !== (p.bezeichnung ?? ''))
)
const bezeichnungGeaendert = computed(() => !!detail.value && bezeichnung.value.trim() !== detail.value.geraet.bezeichnung)

function musterAnwenden() {
  if (!muster.value.includes('{n}')) {
    fehler.value = 'Das Muster braucht {n} als Platzhalter für die Portnummer, z.B. «Gi1/0/{n}».'
    return
  }
  fehler.value = null
  const versatz = (Number.isInteger(musterStart.value) ? musterStart.value : 1) - 1
  for (const p of detail.value?.ports ?? []) {
    if (musterSeite.value !== 'alle' && p.seite !== musterSeite.value) continue
    beschriftung.value[p.id] = muster.value.replaceAll('{n}', String(p.nummer + versatz))
  }
}

async function speichern() {
  if (!detail.value) return
  fehler.value = null
  meldung.value = null
  speichert.value = true
  try {
    if (bezeichnungGeaendert.value) {
      if (!bezeichnung.value.trim()) throw new Error('Bitte eine Bezeichnung eingeben.')
      const g = await api.geraetAendern(detail.value.geraet.id, detail.value.geraet.version, bezeichnung.value.trim())
      detail.value.geraet = g
    }
    if (geaendertePorts.value.length) {
      const d = await api.portsBeschriften(detail.value.geraet.id,
        geaendertePorts.value.map((p) => ({ id: p.id, version: p.version, bezeichnung: beschriftung.value[p.id].trim() || null })))
      detail.value = d
      beschriftung.value = Object.fromEntries(d.ports.map((p) => [p.id, p.bezeichnung ?? '']))
    }
    meldung.value = 'Gespeichert.'
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

async function entfernen() {
  if (!detail.value) return
  if (!entfernenBestaetigen.value) {
    entfernenBestaetigen.value = true
    return
  }
  try {
    await api.geraetEntfernen(detail.value.geraet.id, detail.value.geraet.version)
    emit('entfernt')
  } catch (e) {
    fehler.value = (e as Error).message
    entfernenBestaetigen.value = false
  }
}

const seiteKurz = (s: Port['seite']) => (s === 'Vorne' ? ' vorne' : s === 'Hinten' ? ' hinten' : '')
const patchText = (p: Port) =>
  p.patch ? `⇄ ${p.patch.partnerGeraet} · ${p.patch.partnerNummer}${seiteKurz(p.patch.partnerSeite)}` : ''
const belegungText = (p?: Port) =>
  p?.belegung ? `${p.belegung.kabelnummer} · ${p.belegung.ende}${p.belegung.nummer}` : ''
</script>

<template>
  <aside class="seitenleiste breit">
    <div class="kopf">
      <h2>Gerät</h2>
      <button type="button" class="zweitrangig klein-btn" title="Schliessen (Esc)" @click="emit('schliessen')">✕</button>
    </div>
    <template v-if="detail">
      <p class="leise klein">
        {{ detail.geraet.geraetetyp }} ({{ durchgang ? 'Durchgang' : 'Endgerät' }}) an
        <button type="button" class="link" @click="emit('zurPosition', detail.geraet.positionId)">{{ detail.position }}</button>
        · {{ detail.geraet.belegtePorts }}/{{ detail.geraet.anzahlPorts }} Ports belegt
      </p>
      <div class="feld">
        <label for="ger-bez">Bezeichnung</label>
        <input id="ger-bez" v-model="bezeichnung" type="text" maxlength="100" />
      </div>

      <details class="muster">
        <summary>Portbezeichnungen nach Muster ausfüllen</summary>
        <div class="zeile">
          <input v-model="muster" type="text" placeholder="z.B. Gi1/0/{n} oder A{n}" aria-label="Muster" />
          <label class="inline">ab</label>
          <input v-model.number="musterStart" type="number" min="0" class="start" aria-label="Erste Nummer" />
        </div>
        <div class="zeile">
          <select v-if="durchgang" v-model="musterSeite" aria-label="Seite">
            <option value="alle">vorne und hinten</option>
            <option value="Vorne">nur vorne</option>
            <option value="Hinten">nur hinten</option>
          </select>
          <button type="button" class="zweitrangig klein-btn" @click="musterAnwenden">Ausfüllen</button>
        </div>
        <p class="leise klein">{n} wird durch die Portnummer ersetzt. Erst «Speichern» übernimmt die Beschriftungen.</p>
      </details>

      <table class="ports">
        <thead>
          <tr v-if="durchgang">
            <th>Nr.</th><th>Vorne</th><th>Hinten</th>
          </tr>
          <tr v-else>
            <th>Nr.</th><th>Portart</th><th>Bezeichnung</th><th>Belegt durch</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="z in zeilen" :key="z.nummer">
            <td class="nr">{{ z.nummer }}</td>
            <template v-if="durchgang">
              <td v-for="p in [z.vorne, z.hinten]" :key="p?.id">
                <template v-if="p">
                  <input v-model="beschriftung[p.id]" type="text" maxlength="100" :placeholder="p.portart"
                    :aria-label="`Port ${p.nummer} ${p.seite}`" />
                  <button v-if="p.belegung" type="button" class="belegt" :title="`${p.belegung.medientyp} – Anschlüsse anzeigen`"
                    @click="emit('medium', p.belegung.mediumId)">{{ belegungText(p) }}</button>
                  <button v-else-if="p.patch" type="button" class="belegt patch" :title="`Patchung${p.patch.bezeichnung ? ' ' + p.patch.bezeichnung : ''} – Gerät öffnen`"
                    @click="emit('geraet', p.patch.partnerGeraetId)">{{ patchText(p) }}</button>
                  <button v-if="p.belegung || p.patch" type="button" class="verlauf-btn" title="Leitungsverlauf zeigen"
                    :aria-label="`Verlauf Port ${p.nummer} ${p.seite}`" @click="emit('verlauf', p.id)">⤳</button>
                </template>
              </td>
            </template>
            <template v-else-if="z.einzel">
              <td class="leise">{{ z.einzel.portart }}</td>
              <td><input v-model="beschriftung[z.einzel.id]" type="text" maxlength="100" :aria-label="`Port ${z.nummer}`" /></td>
              <td><button v-if="z.einzel.belegung" type="button" class="belegt" :title="`${z.einzel.belegung.medientyp} – Anschlüsse anzeigen`"
                @click="emit('medium', z.einzel.belegung.mediumId)">{{ belegungText(z.einzel) }}</button>
                <button v-else-if="z.einzel.patch" type="button" class="belegt patch"
                  :title="`Patchung${z.einzel.patch.bezeichnung ? ' ' + z.einzel.patch.bezeichnung : ''} – Gerät öffnen`"
                  @click="emit('geraet', z.einzel.patch.partnerGeraetId)">{{ patchText(z.einzel) }}</button>
                <button v-if="z.einzel.belegung || z.einzel.patch" type="button" class="verlauf-btn" title="Leitungsverlauf zeigen"
                  :aria-label="`Verlauf Port ${z.nummer}`" @click="emit('verlauf', z.einzel.id)">⤳</button>
                <span v-else class="leise">frei</span></td>
            </template>
          </tr>
        </tbody>
      </table>

      <p v-if="fehler" class="fehler">{{ fehler }}</p>
      <p v-else-if="meldung" class="gut-text">{{ meldung }}</p>
      <div class="aktionen">
        <button type="button" :disabled="speichert || (!bezeichnungGeaendert && !geaendertePorts.length)" @click="speichern">
          {{ speichert ? 'Wird gespeichert …' : geaendertePorts.length ? `Speichern (${geaendertePorts.length} Ports)` : 'Speichern' }}
        </button>
        <button type="button" class="gefahr" :disabled="detail.geraet.belegtePorts > 0" @click="entfernen">
          {{ entfernenBestaetigen ? 'Wirklich entfernen?' : 'Gerät entfernen' }}
        </button>
      </div>
      <p v-if="detail.geraet.belegtePorts > 0" class="leise klein">Entfernen erst, wenn kein Port mehr belegt ist.</p>
    </template>
    <p v-else-if="fehler" class="fehler">{{ fehler }}</p>
    <p v-else class="leise">Wird geladen …</p>
  </aside>
</template>

<style scoped>
.breit {
  width: 30rem;
}
.link {
  background: none;
  border: none;
  color: var(--akzent);
  padding: 0;
  text-decoration: underline;
  font-size: inherit;
}
.muster {
  margin: 0.6rem 0;
  font-size: 0.9rem;
}
.muster summary {
  cursor: pointer;
  color: var(--akzent);
}
.zeile {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin-top: 0.4rem;
}
.zeile input[type='text'] {
  flex: 1;
}
.start {
  width: 4.5rem !important;
}
label.inline {
  margin: 0;
  font-weight: 400;
}
.ports {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  margin-top: 0.4rem;
}
.ports th {
  text-align: left;
  font-weight: 600;
  border-bottom: 1px solid var(--rand);
  padding: 0.25rem;
}
.ports td {
  padding: 0.15rem 0.25rem;
  border-bottom: 1px solid #eef1f4;
  vertical-align: middle;
}
.ports input {
  font: inherit;
  width: 100%;
  padding: 0.2rem 0.35rem;
  border: 1px solid var(--rand);
  border-radius: 4px;
}
.nr {
  width: 2.2rem;
  font-variant-numeric: tabular-nums;
  color: var(--text-leise);
}
.belegt {
  background: none;
  border: none;
  padding: 0;
  text-decoration: underline dotted;
  display: inline-block;
  margin-top: 0.1rem;
  color: #1b7c83;
  font-weight: 600;
  font-size: 0.8rem;
}
.verlauf-btn {
  background: none;
  border: 1px solid transparent;
  color: #2da44e;
  padding: 0 0.3rem;
  margin-left: 0.2rem;
  font-size: 0.9rem;
  font-weight: 700;
}
.verlauf-btn:hover {
  border-color: #2da44e;
}
.belegt.patch {
  color: #8250df;
}
.gut-text {
  color: var(--gut);
}
</style>
