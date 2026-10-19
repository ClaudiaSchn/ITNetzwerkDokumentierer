<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { api, type Plan, type PositionImPlan, type Positionstyp, type Punkt } from '../api'
import { formatZahl, nachAbsolut } from '../geometrie'
import { stockwerkText } from '../format'

/**
 * Seitenleiste für Positionen. Drei Ansichten:
 *  – Entwurf: neue Position nach Klick in den Plan
 *  – Auswahl: bestehende Position ansehen, ändern, verschieben, löschen
 *  – sonst:   Liste aller Positionen im Plan mit Suche
 */
const props = defineProps<{
  plan: Plan
  standortId: number
  positionen: PositionImPlan[]
  typen: Positionstyp[]
  /** Klickpunkt einer neuen Position */
  entwurf: Punkt | null
  /** Ausgewählte bestehende Position */
  auswahl: PositionImPlan | null
  /** Neuer, noch nicht gespeicherter Punkt der ausgewählten Position (nach Verschieben) */
  verschobenNach: Punkt | null
  /** Vorgaben für die nächste neue Position (Serienerfassung) */
  vorschlag: { bezeichnung: string; positionstypId: number | null; stockwerk: number }
}>()

const emit = defineEmits<{
  gespeichert: [position: PositionImPlan, neu: boolean]
  geloescht: [id: number]
  abbrechen: []
  auswaehlen: [id: number]
  verschieben: [aktiv: boolean]
  typAngelegt: [typ: Positionstyp]
}>()

// Formular (für Entwurf und Auswahl)
const bezeichnung = ref('')
const typId = ref<number | null>(null)
const stockwerk = ref(0)
const fehler = ref<string | null>(null)
const speichert = ref(false)
const loeschenBestaetigen = ref(false)
const verschiebenAktiv = ref(false)
const bezeichnungFeld = ref<HTMLInputElement | null>(null)

// Neuer Positionstyp direkt im Formular
const neuerTypOffen = ref(false)
const neuerTypName = ref('')

const NEUER_TYP = -1

function formularFuellen() {
  fehler.value = null
  loeschenBestaetigen.value = false
  neuerTypOffen.value = false
  if (props.auswahl) {
    bezeichnung.value = props.auswahl.bezeichnung
    typId.value = props.auswahl.positionstypId
    stockwerk.value = props.auswahl.stockwerk
  } else if (props.entwurf) {
    bezeichnung.value = props.vorschlag.bezeichnung
    typId.value = props.vorschlag.positionstypId ?? props.typen[0]?.id ?? null
    stockwerk.value = props.vorschlag.stockwerk
    nextTick(() => bezeichnungFeld.value?.select())
  }
}

watch(() => props.auswahl?.id, () => {
  verschiebenAktiv.value = false
  formularFuellen()
}, { immediate: true })
watch(() => props.entwurf !== null, formularFuellen)
watch(verschiebenAktiv, (aktiv) => emit('verschieben', aktiv))

watch(typId, (id) => {
  if (id === NEUER_TYP) {
    neuerTypOffen.value = true
    typId.value = props.vorschlag.positionstypId ?? props.typen[0]?.id ?? null
  }
})

async function typAnlegen() {
  fehler.value = null
  const name = neuerTypName.value.trim()
  if (!name) return
  // Gibt es den Typ schon (Gross-/Kleinschreibung egal)? Dann einfach auswählen.
  const vorhanden = props.typen.find((t) => t.name.toLocaleLowerCase('de') === name.toLocaleLowerCase('de'))
  if (vorhanden) {
    typId.value = vorhanden.id
    neuerTypName.value = ''
    neuerTypOffen.value = false
    return
  }
  try {
    const typ = await api.positionstypAnlegen(neuerTypName.value)
    emit('typAngelegt', typ)
    typId.value = typ.id
    neuerTypName.value = ''
    neuerTypOffen.value = false
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

// Koordinaten zur Anzeige
const punktAnzeige = computed(() => {
  const p = props.verschobenNach ?? (props.auswahl ? { u: props.auswahl.u, v: props.auswahl.v } : props.entwurf)
  return p ? nachAbsolut(props.plan, p) : null
})

async function speichern() {
  fehler.value = null
  if (!bezeichnung.value.trim()) {
    fehler.value = 'Bitte eine Bezeichnung eingeben.'
    return
  }
  if (typId.value === null) {
    fehler.value = 'Bitte einen Positionstyp wählen.'
    return
  }
  if (!Number.isInteger(stockwerk.value)) {
    fehler.value = 'Bitte für das Stockwerk eine ganze Zahl eingeben (0 = EG, −1 = 1. UG).'
    return
  }
  const daten = { bezeichnung: bezeichnung.value.trim(), positionstypId: typId.value, stockwerk: stockwerk.value }
  speichert.value = true
  try {
    if (props.auswahl) {
      const p = await api.positionAendern(props.auswahl.id, props.auswahl.version, props.plan.id, daten,
        props.verschobenNach ?? undefined)
      verschiebenAktiv.value = false
      emit('gespeichert', p, false)
    } else if (props.entwurf) {
      const p = await api.positionErfassen(props.standortId, props.plan.id, props.entwurf, daten)
      emit('gespeichert', p, true)
    }
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

async function loeschen() {
  if (!props.auswahl) return
  if (!loeschenBestaetigen.value) {
    loeschenBestaetigen.value = true
    return
  }
  fehler.value = null
  try {
    await api.positionLoeschen(props.auswahl.id, props.auswahl.version)
    emit('geloescht', props.auswahl.id)
  } catch (e) {
    fehler.value = (e as Error).message
    loeschenBestaetigen.value = false
  }
}

const loeschenGesperrt = computed(() =>
  props.auswahl && (props.auswahl.anzahlGeraete > 0 || props.auswahl.anzahlVerbindungen > 0)
    ? `Gesperrt: ${props.auswahl.anzahlGeraete} Gerät(e), ${props.auswahl.anzahlVerbindungen} Verbindung(en)`
    : null
)

// Liste
const suche = ref('')
const gefiltert = computed(() => {
  const s = suche.value.trim().toLowerCase()
  return s
    ? props.positionen.filter((p) => p.bezeichnung.toLowerCase().includes(s) || p.positionstyp.toLowerCase().includes(s))
    : props.positionen
})
</script>

<template>
  <aside class="panel">
    <!-- Neue Position oder Auswahl bearbeiten -->
    <form v-if="entwurf || auswahl" @submit.prevent="speichern">
      <div class="kopf">
        <h2>{{ auswahl ? 'Position' : 'Neue Position' }}</h2>
        <button type="button" class="zweitrangig klein-btn" title="Schliessen (Esc)" @click="emit('abbrechen')">✕</button>
      </div>

      <div class="feld">
        <label for="pos-bezeichnung">Bezeichnung</label>
        <input id="pos-bezeichnung" ref="bezeichnungFeld" v-model="bezeichnung" type="text" maxlength="100"
          placeholder="z.B. Serverraum 1, HV4, BD-0815" @input="fehler = null" />
      </div>

      <div class="feld">
        <label for="pos-typ">Typ</label>
        <select id="pos-typ" v-model="typId">
          <option v-for="t in typen" :key="t.id" :value="t.id">{{ t.name }}</option>
          <option :value="NEUER_TYP">＋ Neuer Typ …</option>
        </select>
        <div v-if="neuerTypOffen" class="neuer-typ">
          <input v-model="neuerTypName" type="text" maxlength="100" placeholder="Name des neuen Typs"
            @keydown.enter.prevent="typAnlegen" />
          <button type="button" class="klein-btn" @click="typAnlegen">Anlegen</button>
          <button type="button" class="zweitrangig klein-btn" @click="neuerTypOffen = false">✕</button>
        </div>
      </div>

      <div class="feld">
        <label for="pos-stockwerk">Stockwerk</label>
        <div class="stockwerk">
          <input id="pos-stockwerk" v-model.number="stockwerk" type="number" step="1" min="-10" max="100" />
          <span class="leise">{{ Number.isInteger(stockwerk) ? stockwerkText(stockwerk) : '' }}</span>
        </div>
        <p class="leise klein">0 = EG, 1 = 1. OG, −1 = 1. UG</p>
      </div>

      <p v-if="punktAnzeige" class="koordinate">
        x {{ formatZahl(punktAnzeige.x) }} m · y {{ formatZahl(punktAnzeige.y) }} m
        <span v-if="verschobenNach" class="hinweis">· verschoben, noch nicht gespeichert</span>
      </p>

      <template v-if="auswahl">
        <label class="schalter">
          <input v-model="verschiebenAktiv" type="checkbox" />
          Verschieben (Punkt im Plan ziehen)
        </label>
        <p class="leise klein">
          {{ auswahl.anzahlGeraete }} Gerät(e) · {{ auswahl.anzahlVerbindungen }} Verbindung(en)
        </p>
      </template>
      <p v-else class="leise klein">Der grüne Punkt lässt sich vor dem Speichern noch verschieben.</p>

      <p v-if="fehler" class="fehler">{{ fehler }}</p>

      <div class="aktionen">
        <button type="submit" :disabled="speichert">{{ speichert ? 'Wird gespeichert …' : 'Speichern' }}</button>
        <button type="button" class="zweitrangig" @click="emit('abbrechen')">Abbrechen</button>
        <button v-if="auswahl" type="button" class="gefahr" :disabled="!!loeschenGesperrt"
          :title="loeschenGesperrt ?? ''" @click="loeschen">
          {{ loeschenBestaetigen ? 'Wirklich löschen?' : 'Löschen' }}
        </button>
      </div>
      <p v-if="loeschenGesperrt" class="leise klein">{{ loeschenGesperrt }} – Löschen erst, wenn nichts mehr daran hängt.</p>
      <!-- Zusatz zur ausgewählten Position (z.B. Verbindungen) -->
      <slot v-if="auswahl" name="auswahl" />
    </form>

    <!-- Liste -->
    <template v-else>
      <h2>Positionen <span class="leise">({{ positionen.length }})</span></h2>
      <input v-model="suche" type="text" placeholder="Suchen nach Bezeichnung oder Typ" class="suche" />
      <p v-if="positionen.length === 0" class="leise klein">
        Noch keine Positionen. Mit «Position setzen» oben in den Plan klicken.
      </p>
      <ul class="liste">
        <li v-for="p in gefiltert" :key="p.id">
          <button type="button" class="eintrag" @click="emit('auswaehlen', p.id)">
            <span class="name">{{ p.bezeichnung }}</span>
            <span class="leise klein">{{ p.positionstyp }} · {{ stockwerkText(p.stockwerk) }}</span>
          </button>
        </li>
      </ul>
    </template>
  </aside>
</template>

<style scoped>
.panel {
  width: 21rem;
  flex-shrink: 0;
  overflow-y: auto;
  background: var(--flaeche);
  border-left: 1px solid var(--rand);
  padding: 1rem 1.1rem 1.5rem;
}
.kopf {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.klein {
  font-size: 0.85rem;
  margin: 0.25rem 0 0;
}
.klein-btn {
  padding: 0.2rem 0.6rem;
  font-size: 0.85rem;
}
.feld {
  margin: 0.8rem 0;
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
.neuer-typ {
  display: flex;
  gap: 0.35rem;
  margin-top: 0.4rem;
}
.stockwerk {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}
.stockwerk input {
  width: 6rem;
  font: inherit;
  padding: 0.4rem 0.5rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
}
.koordinate {
  font-variant-numeric: tabular-nums;
  font-size: 0.9rem;
  margin: 0.6rem 0;
}
.hinweis {
  color: #9a6700;
}
.schalter {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 400;
  font-size: 0.9rem;
  margin: 0.6rem 0 0.2rem;
}
.aktionen {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.9rem;
}
.gefahr {
  background: var(--flaeche);
  color: var(--schlecht);
  border-color: var(--schlecht);
  margin-left: auto;
}
.suche {
  margin: 0.4rem 0 0.6rem;
}
.liste {
  list-style: none;
  padding: 0;
  margin: 0;
}
.eintrag {
  width: 100%;
  text-align: left;
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  background: none;
  color: inherit;
  border: none;
  border-bottom: 1px solid var(--rand);
  border-radius: 0;
  padding: 0.5rem 0.25rem;
}
.eintrag:hover {
  background: var(--hintergrund);
}
.name {
  font-weight: 600;
}
</style>
