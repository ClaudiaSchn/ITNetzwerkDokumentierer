<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { api, type Medientyp, type Medium, type Portart, type Verbindung } from '../api'
import { formatZahl } from '../geometrie'
import { naechsteBezeichnung, stockwerkText } from '../format'

/** Seitenleiste einer Verbindung: beide Enden und die Medien (Kabel, Funkstrecken) darauf */
const props = defineProps<{
  verbindung: Verbindung
  /** Ids der Positionen, die im aktuellen Plan zu sehen sind */
  sichtbar: Set<number>
  medientypen: Medientyp[]
  portarten: Portart[]
}>()
const emit = defineEmits<{
  schliessen: []
  auswaehlen: [positionId: number]
  /** Medien wurden geändert (Zähler an der Verbindung neu laden) */
  geaendert: []
  loeschen: [verbindung: Verbindung]
  medientypAngelegt: [typ: Medientyp]
  portartAngelegt: [portart: Portart]
  /** Anschlüsse eines Mediums bearbeiten */
  anschliessen: [mediumId: number]
}>()

const medien = ref<Medium[]>([])
const laedt = ref(false)
const fehler = ref<string | null>(null)

// Formular: null = zu, 'neu' = neues Medium, sonst Id des bearbeiteten Mediums
const formular = ref<'neu' | number | null>(null)
const kabelnummer = ref('')
const medientypId = ref<number | null>(null)
const anzahl = ref(1)
const speichert = ref(false)
const kabelFeld = ref<HTMLInputElement | null>(null)
/** Vorgaben für das nächste neue Medium (Serienerfassung) */
const vorschlag = ref({ kabelnummer: '', medientypId: null as number | null, anzahl: 1 })

const entfernenBestaetigen = ref<number | null>(null)
const verbindungLoeschenBestaetigen = ref(false)

// Neuer Medientyp direkt im Formular
const NEUER_TYP = -1
const neuerTypOffen = ref(false)
const neuerTypName = ref('')
const neuerTypPortarten = ref<number[]>([])
const neuePortart = ref('')

async function laden() {
  laedt.value = true
  fehler.value = null
  try {
    medien.value = await api.medien(props.verbindung.id)
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    laedt.value = false
  }
}

watch(
  () => props.verbindung.id,
  () => {
    formular.value = null
    entfernenBestaetigen.value = null
    verbindungLoeschenBestaetigen.value = false
    laden()
  },
  { immediate: true }
)

function neu() {
  fehler.value = null
  formular.value = 'neu'
  kabelnummer.value = vorschlag.value.kabelnummer
  medientypId.value = vorschlag.value.medientypId ?? props.medientypen[0]?.id ?? null
  anzahl.value = vorschlag.value.anzahl
  neuerTypOffen.value = false
  nextTick(() => kabelFeld.value?.select())
}

function bearbeiten(m: Medium) {
  fehler.value = null
  formular.value = m.id
  kabelnummer.value = m.kabelnummer
  medientypId.value = m.medientypId
  anzahl.value = m.anzahlAnschluesse
  neuerTypOffen.value = false
}

watch(medientypId, (id) => {
  if (id === NEUER_TYP) {
    neuerTypOffen.value = true
    medientypId.value = vorschlag.value.medientypId ?? props.medientypen[0]?.id ?? null
  }
})

async function portartAnlegen() {
  const name = neuePortart.value.trim()
  if (!name) return
  fehler.value = null
  const vorhanden = props.portarten.find((p) => p.name.toLocaleLowerCase('de') === name.toLocaleLowerCase('de'))
  try {
    const p = vorhanden ?? (await api.portartAnlegen(name))
    if (!vorhanden) emit('portartAngelegt', p)
    if (!neuerTypPortarten.value.includes(p.id)) neuerTypPortarten.value = [...neuerTypPortarten.value, p.id]
    neuePortart.value = ''
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

async function medientypAnlegen() {
  fehler.value = null
  const name = neuerTypName.value.trim()
  if (!name) {
    fehler.value = 'Bitte einen Namen für den Medientyp eingeben.'
    return
  }
  const vorhanden = props.medientypen.find((t) => t.name.toLocaleLowerCase('de') === name.toLocaleLowerCase('de'))
  if (vorhanden) {
    medientypId.value = vorhanden.id
    neuerTypOffen.value = false
    return
  }
  try {
    const t = await api.medientypAnlegen(name, neuerTypPortarten.value)
    emit('medientypAngelegt', t)
    medientypId.value = t.id
    neuerTypOffen.value = false
    neuerTypName.value = ''
    neuerTypPortarten.value = []
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

async function speichern() {
  fehler.value = null
  if (!kabelnummer.value.trim()) {
    fehler.value = 'Bitte eine Kabelnummer eingeben.'
    return
  }
  if (medientypId.value === null || medientypId.value === NEUER_TYP) {
    fehler.value = 'Bitte einen Medientyp wählen.'
    return
  }
  if (!Number.isInteger(anzahl.value) || anzahl.value < 1) {
    fehler.value = 'Bitte eine ganze Zahl ab 1 für die Anschlüsse eingeben.'
    return
  }
  const daten = { kabelnummer: kabelnummer.value.trim(), medientypId: medientypId.value, anzahlAnschluesse: anzahl.value }
  speichert.value = true
  try {
    if (formular.value === 'neu') {
      const m = await api.mediumZuweisen(props.verbindung.id, daten)
      medien.value = [...medien.value, m].sort((a, b) => a.kabelnummer.localeCompare(b.kabelnummer, 'de'))
      vorschlag.value = { kabelnummer: naechsteBezeichnung(m.kabelnummer), medientypId: m.medientypId, anzahl: m.anzahlAnschluesse }
    } else if (typeof formular.value === 'number') {
      const alt = medien.value.find((x) => x.id === formular.value)!
      const m = await api.mediumAendern(alt.id, alt.version, daten)
      medien.value = medien.value.map((x) => (x.id === m.id ? m : x))
    }
    formular.value = null
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

async function entfernen(m: Medium) {
  if (entfernenBestaetigen.value !== m.id) {
    entfernenBestaetigen.value = m.id
    return
  }
  entfernenBestaetigen.value = null
  fehler.value = null
  try {
    await api.mediumEntfernen(m.id, m.version)
    medien.value = medien.value.filter((x) => x.id !== m.id)
    if (formular.value === m.id) formular.value = null
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

function verbindungLoeschen() {
  if (!verbindungLoeschenBestaetigen.value) {
    verbindungLoeschenBestaetigen.value = true
    return
  }
  emit('loeschen', props.verbindung)
}

const ende = (p: Verbindung['a']) => `${p.positionstyp} · ${stockwerkText(p.stockwerk)}`
const frei = (m: Medium, belegt: number) => m.anzahlAnschluesse - belegt
const typInfo = computed(() => props.medientypen.find((t) => t.id === medientypId.value) ?? null)
</script>

<template>
  <aside class="seitenleiste">
    <div class="kopf">
      <h2>Verbindung</h2>
      <button type="button" class="zweitrangig klein-btn" title="Schliessen (Esc)" @click="emit('schliessen')">✕</button>
    </div>

    <div class="enden">
      <button v-for="(p, i) in [verbindung.a, verbindung.b]" :key="p.id" type="button" class="ende"
        :disabled="!sichtbar.has(p.id)" :title="sichtbar.has(p.id) ? 'Position anzeigen' : 'In diesem Plan nicht sichtbar'"
        @click="emit('auswaehlen', p.id)">
        <span class="buchstabe">{{ i === 0 ? 'A' : 'B' }}</span>
        <span>
          <span class="name">{{ p.bezeichnung }}</span><br />
          <span class="leise klein">{{ ende(p) }}</span>
        </span>
      </button>
    </div>
    <p class="leise klein">Luftlinie {{ formatZahl(verbindung.laengeMeter, 1) }} m (ohne Leitungsverlauf)</p>

    <section class="abschnitt">
      <div class="kopf">
        <h3>Medien <span class="leise">({{ medien.length }})</span></h3>
        <button v-if="formular !== 'neu'" type="button" class="klein-btn" @click="neu">＋ Medium</button>
      </div>

      <!-- Formular für neues oder bearbeitetes Medium -->
      <form v-if="formular !== null" class="formular" @submit.prevent="speichern">
        <h3 class="klein-titel">{{ formular === 'neu' ? 'Neues Medium' : 'Medium bearbeiten' }}</h3>
        <div class="feld">
          <label for="med-kabel">Kabelnummer</label>
          <input id="med-kabel" ref="kabelFeld" v-model="kabelnummer" type="text" maxlength="50" placeholder="z.B. K-0815" />
        </div>
        <div class="feld">
          <label for="med-typ">Medientyp</label>
          <select id="med-typ" v-model="medientypId">
            <option v-for="t in medientypen" :key="t.id" :value="t.id">{{ t.name }}</option>
            <option :value="NEUER_TYP">＋ Neuer Medientyp …</option>
          </select>
          <p v-if="typInfo && !neuerTypOffen" class="leise klein">Anschliessbar an: {{ typInfo.portarten.map((p) => p.name).join(', ') }}</p>
          <div v-if="neuerTypOffen" class="neuer-typ">
            <input v-model="neuerTypName" type="text" maxlength="100" placeholder="Name, z.B. LWL OM4" aria-label="Name des neuen Medientyps" />
            <p class="klein">Erlaubte Portarten:</p>
            <div class="portarten">
              <label v-for="p in portarten" :key="p.id">
                <input v-model="neuerTypPortarten" type="checkbox" :value="p.id" /> {{ p.name }}
              </label>
            </div>
            <div class="zeile">
              <input v-model="neuePortart" type="text" maxlength="100" placeholder="weitere Portart" aria-label="Neue Portart"
                @keydown.enter.prevent="portartAnlegen" />
              <button type="button" class="zweitrangig klein-btn" @click="portartAnlegen">＋</button>
            </div>
            <div class="zeile">
              <button type="button" class="klein-btn" @click="medientypAnlegen">Medientyp anlegen</button>
              <button type="button" class="zweitrangig klein-btn" @click="neuerTypOffen = false">Abbrechen</button>
            </div>
          </div>
        </div>
        <div class="feld">
          <label for="med-anzahl">Anschlüsse pro Ende</label>
          <input id="med-anzahl" v-model.number="anzahl" type="number" min="1" max="288" step="1" class="anzahl" />
          <p class="leise klein">Anzahl Fasern bzw. Adernpaare: z.B. 12 bei einem 12-Faser-Kabel, 1 bei einem Kupferkabel.</p>
        </div>
        <div class="aktionen">
          <button type="submit" :disabled="speichert || neuerTypOffen">{{ speichert ? 'Wird gespeichert …' : 'Speichern' }}</button>
          <button type="button" class="zweitrangig" @click="formular = null; fehler = null">Abbrechen</button>
        </div>
      </form>

      <p v-if="laedt" class="leise klein">Wird geladen …</p>
      <p v-else-if="medien.length === 0 && formular === null" class="leise klein">
        Noch keine Medien. Mit «＋ Medium» ein Kabel oder eine Funkstrecke erfassen.
      </p>
      <ul class="liste">
        <li v-for="m in medien" :key="m.id" :class="{ offen: formular === m.id }">
          <div class="medium">
            <span class="name">{{ m.kabelnummer }}</span>
            <span class="leise klein">{{ m.medientyp }} · {{ m.anzahlAnschluesse }} {{ m.anzahlAnschluesse === 1 ? 'Anschluss' : 'Anschlüsse' }} pro Ende</span>
            <span class="klein frei">frei: A {{ frei(m, m.belegtA) }}/{{ m.anzahlAnschluesse }} · B {{ frei(m, m.belegtB) }}/{{ m.anzahlAnschluesse }}</span>
          </div>
          <button type="button" class="symbol anschl" title="Fasern/Adern an Ports anschliessen" @click="emit('anschliessen', m.id)">Anschlüsse</button>
          <button type="button" class="symbol" title="Bearbeiten" @click="bearbeiten(m)">✎</button>
          <button type="button" class="symbol weg" :class="{ bestaetigen: entfernenBestaetigen === m.id }"
            :disabled="m.belegtA + m.belegtB > 0"
            :title="m.belegtA + m.belegtB > 0 ? 'Gesperrt: Anschlüsse sind belegt' : 'Medium entfernen'" @click="entfernen(m)">
            {{ entfernenBestaetigen === m.id ? 'Entfernen?' : '✕' }}
          </button>
        </li>
      </ul>
    </section>

    <p v-if="fehler" class="fehler">{{ fehler }}</p>

    <section class="abschnitt">
      <button type="button" class="gefahr" :disabled="medien.length > 0" @click="verbindungLoeschen">
        {{ verbindungLoeschenBestaetigen ? 'Wirklich löschen?' : 'Verbindung löschen' }}
      </button>
      <p v-if="medien.length > 0" class="leise klein">Gesperrt, solange Medien darüber laufen.</p>
    </section>
  </aside>
</template>

<style scoped>
.enden {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  margin: 0.6rem 0 0.2rem;
}
.ende {
  display: flex;
  gap: 0.6rem;
  align-items: center;
  text-align: left;
  background: var(--hintergrund);
  color: inherit;
  border: 1px solid var(--rand);
  padding: 0.4rem 0.6rem;
}
.ende:disabled {
  opacity: 1;
}
.ende:not(:disabled):hover {
  border-color: var(--akzent);
}
.buchstabe {
  display: inline-grid;
  place-items: center;
  width: 1.6rem;
  height: 1.6rem;
  border-radius: 50%;
  background: #1b7c83;
  color: #fff;
  font-weight: 700;
  font-size: 0.85rem;
  flex-shrink: 0;
}
.name {
  font-weight: 600;
}
.formular {
  background: var(--hintergrund);
  border: 1px solid var(--rand);
  border-radius: 6px;
  padding: 0.6rem 0.7rem;
  margin: 0.6rem 0;
}
.klein-titel {
  margin-bottom: 0.2rem;
}
.anzahl {
  width: 6rem !important;
}
.neuer-typ {
  margin-top: 0.5rem;
  padding: 0.5rem;
  border: 1px dashed var(--rand);
  border-radius: 6px;
}
.portarten {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem 0.8rem;
  margin: 0.3rem 0;
}
.portarten label {
  font-weight: 400;
  display: flex;
  align-items: center;
  gap: 0.25rem;
  margin: 0;
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
  gap: 0.2rem;
  border-bottom: 1px solid var(--rand);
  padding: 0.35rem 0.1rem;
}
.liste li.offen {
  background: #fff8c5;
}
.medium {
  flex: 1;
  display: flex;
  flex-direction: column;
}
.frei {
  color: var(--gut);
}
.symbol {
  background: none;
  color: var(--text-leise);
  border: 1px solid transparent;
  padding: 0.15rem 0.45rem;
  font-size: 0.85rem;
}
.anschl {
  color: var(--akzent);
  border-color: var(--rand);
}
.symbol:not(:disabled):hover {
  border-color: var(--rand);
}
.weg:not(:disabled):hover,
.weg.bestaetigen {
  color: var(--schlecht);
  border-color: var(--schlecht);
}
.abschnitt .gefahr {
  margin-left: 0;
}
</style>
