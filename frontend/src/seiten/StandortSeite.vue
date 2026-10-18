<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, type Plan, type PlanListe, type PositionImPlan, type Positionstyp, type Punkt, type Standort } from '../api'
import { formatDrehung, formatMassstab, formatZahl } from '../geometrie'
import { naechsteBezeichnung, stockwerkText } from '../format'
import PlanAnsicht, { type Linie, type Markierung } from '../komponenten/PlanAnsicht.vue'
import MasterKalibrierung from '../komponenten/MasterKalibrierung.vue'
import PositionenPanel from '../komponenten/PositionenPanel.vue'
import PlanReferenzierung, { type ReferenzAnzeige, type ReferenzWerkzeug, type Seite } from '../komponenten/PlanReferenzierung.vue'
import PlanEigenerMassstab from '../komponenten/PlanEigenerMassstab.vue'
import type { Methode } from '../komponenten/MethodenWahl.vue'
import PlanImport from '../komponenten/PlanImport.vue'
import PlanEinstellungen from '../komponenten/PlanEinstellungen.vue'

const props = defineProps<{ id: number }>()
const route = useRoute()
const router = useRouter()

const standort = ref<Standort | null>(null)
/** Alle aktiven Pläne des Standorts */
const plaene = ref<PlanListe[]>([])
/** Angezeigter Plan (Auswahl über ?plan=…, sonst der Masterplan) */
const plan = ref<Plan | null>(null)
const laedt = ref(true)
const ladeFehler = ref<string | null>(null)

const datei = ref<File | null>(null)
const bezeichnung = ref('')
const laedtHoch = ref(false)
const uploadFehler = ref<string | null>(null)

/**
 * ansehen = Positionsliste/Auswahl, setzen = Klick erzeugt neue Position,
 * kalibrieren = Masterplan kalibrieren, referenzieren = Plan über Passpunkte ausrichten,
 * importieren / einstellungen = entsprechende Seitenleiste
 */
type Modus = 'ansehen' | 'setzen' | 'kalibrieren' | 'referenzieren' | 'importieren' | 'einstellungen'
const modus = ref<Modus>('ansehen')
const meldung = ref<string | null>(null)
const ansicht = ref<InstanceType<typeof PlanAnsicht> | null>(null)

// Kalibrierung
const kalibrierung = ref<InstanceType<typeof MasterKalibrierung> | null>(null)
const werkzeugMarkierungen = ref<Markierung[]>([])
const werkzeugLinien = ref<Linie[]>([])
const kalibrierKlick = ref(false)

// Referenzierung (zwei Planansichten nebeneinander)
const referenz = ref<ReferenzWerkzeug | null>(null)
const referenzMethode = ref<Methode>('Passpunkte')
const refAnzeige = ref<ReferenzAnzeige | null>(null)
const kontrolleLinks = ref<Punkt | null>(null)
const kontrolleRechts = ref<Punkt | null>(null)

// Positionen
const alleStockwerke = ref(false)
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
  plaene.value = []
  try {
    standort.value = await api.standort(props.id)
    if (standort.value.masterplan) {
      plaene.value = await api.plaene(props.id)
      await planLaden()
    }
  } catch (e) {
    ladeFehler.value = (e as Error).message
  } finally {
    laedt.value = false
  }
}

/** Den in der Adresse gewählten Plan laden (unbekannt oder leer → Masterplan) */
async function planLaden() {
  const masterId = standort.value?.masterplan?.id
  if (!masterId) return
  const gewuenscht = Number(route.query.plan)
  const id = plaene.value.some((p) => p.id === gewuenscht) ? gewuenscht : masterId
  try {
    const neu = await api.plan(id)
    plan.value = neu
    positionen.value = []
    schliessen()
    kontrolleLinks.value = kontrolleRechts.value = null
    refAnzeige.value = null
    alleStockwerke.value = false
    vorschlag.value = { ...vorschlag.value, bezeichnung: '', stockwerk: neu.stockwerk }
    modus.value = neu.referenziert ? 'ansehen' : neu.istMasterplan ? 'kalibrieren' : 'referenzieren'
    referenzMethode.value = neu.methode === 'EigenerMassstab' ? 'EigenerMassstab' : 'Passpunkte'
    await positionenLaden()
  } catch (e) {
    ladeFehler.value = (e as Error).message
  }
}

function planWechseln(id: number) {
  router.push({ query: { ...route.query, plan: String(id) } })
}

async function planlisteLaden() {
  plaene.value = await api.plaene(props.id)
}

async function positionenLaden() {
  if (!plan.value?.referenziert) return
  const [liste, t] = await Promise.all([api.positionenImPlan(plan.value.id, alleStockwerke.value), api.positionstypen()])
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
    await planlisteLaden()
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
  await Promise.all([planlisteLaden(), positionenLaden()]) // Lage im Plan hat sich ggf. geändert
}

/** Kalibrierung (Masterplan) bzw. Referenzierung neu machen */
function lageAendern() {
  schliessen()
  if (plan.value) referenzMethode.value = plan.value.methode === 'EigenerMassstab' ? 'EigenerMassstab' : 'Passpunkte'
  modus.value = plan.value?.istMasterplan ? 'kalibrieren' : 'referenzieren'
}

// --- Referenzierung ---

async function referenzGespeichert(neu: Plan) {
  plan.value = neu
  modus.value = 'ansehen'
  refAnzeige.value = null
  kontrolleLinks.value = kontrolleRechts.value = null
  melden(`Plan «${neu.bezeichnung}» referenziert: ${formatMassstab(neu.massstab)} · Drehung ${formatDrehung(neu.drehung)}`)
  await Promise.all([planlisteLaden(), positionenLaden()])
}

function referenzAbbrechen() {
  refAnzeige.value = null
  kontrolleLinks.value = kontrolleRechts.value = null
  if (plan.value?.referenziert) modus.value = 'ansehen'
  else if (standort.value?.masterplan) {
    melden(`«${plan.value?.bezeichnung}» ist noch nicht referenziert. Sie finden den Plan in der Planauswahl.`)
    planWechseln(standort.value.masterplan.id)
  }
}

function methodeWechseln(m: Methode) {
  refAnzeige.value = null
  kontrolleLinks.value = kontrolleRechts.value = null
  referenzMethode.value = m
}

function kontrolle(links: Punkt | null, rechts: Punkt | null) {
  kontrolleLinks.value = links
  kontrolleRechts.value = rechts
}

// --- Plan importieren / Einstellungen ---

function seitenleiste(ziel: 'importieren' | 'einstellungen') {
  schliessen()
  modus.value = modus.value === ziel ? 'ansehen' : ziel
}

async function planImportiert(neu: Plan) {
  await planlisteLaden()
  melden(`Plan «${neu.bezeichnung}» importiert. Jetzt zwei Passpunkte setzen.`)
  planWechseln(neu.id)
}

async function planGeaendert(neu: Plan) {
  const stockwerkGeaendert = neu.stockwerk !== plan.value?.stockwerk
  plan.value = neu
  modus.value = 'ansehen'
  vorschlag.value = { ...vorschlag.value, stockwerk: neu.stockwerk }
  melden(`Plan «${neu.bezeichnung}» gespeichert.`)
  await planlisteLaden()
  if (stockwerkGeaendert) await positionenLaden()
}

async function planGeloescht() {
  const name = plan.value?.bezeichnung
  await planlisteLaden()
  melden(`Plan «${name}» gelöscht.`)
  if (standort.value?.masterplan) planWechseln(standort.value.masterplan.id)
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
  else if (modus.value === 'referenzieren') referenz.value?.klick('links', p)
  else if (modus.value === 'setzen') {
    auswahlId.value = null
    entwurf.value = p
  }
}

function verschoben(id: string, p: Punkt) {
  if (modus.value === 'kalibrieren') kalibrierung.value?.verschoben(id, p)
  else if (modus.value === 'referenzieren') referenz.value?.verschoben('links', id, p)
  else if (id === 'entwurf') entwurf.value = p
  else if (id === `pos-${auswahlId.value}`) verschobenNach.value = p
}

function zeiger(seite: Seite, p: Punkt | null) {
  if (modus.value === 'referenzieren') referenz.value?.zeiger(seite, p)
}

function markierungKlick(id: string) {
  if (!id.startsWith('pos-')) return
  auswaehlen(Number(id.slice(4)))
}

const markierungen = computed<Markierung[]>(() => {
  if (modus.value === 'kalibrieren') return werkzeugMarkierungen.value
  if (modus.value === 'referenzieren') return refAnzeige.value?.links ?? []
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
const linien = computed(() => {
  if (modus.value === 'kalibrieren') return werkzeugLinien.value
  if (modus.value === 'referenzieren') return refAnzeige.value?.linienLinks ?? []
  return []
})
const klickModus = computed(() => {
  if (modus.value === 'kalibrieren') return kalibrierKlick.value
  if (modus.value === 'referenzieren') return refAnzeige.value?.klickLinks ?? false
  return modus.value === 'setzen'
})
const werkzeugAktiv = computed(() => modus.value === 'kalibrieren' || modus.value === 'referenzieren')

/** Planauswahl als Baum: jeder Plan unter seinem Bezugsplan eingerückt */
const planOptionen = computed(() => {
  const ergebnis: { id: number; text: string }[] = []
  const gesehen = new Set<number>()
  const hinzufuegen = (p: PlanListe, tiefe: number) => {
    if (gesehen.has(p.id)) return
    gesehen.add(p.id)
    const einzug = tiefe > 0 ? '\u00a0\u00a0\u00a0'.repeat(tiefe - 1) + '↳ ' : ''
    const zusatz = p.istMasterplan ? ' (Masterplan)' : ` · ${stockwerkText(p.stockwerk)}`
    ergebnis.push({ id: p.id, text: `${einzug}${p.bezeichnung}${zusatz}${p.referenziert ? '' : ' – nicht referenziert'}` })
    plaene.value.filter((k) => k.bezugsplanId === p.id).forEach((k) => hinzufuegen(k, tiefe + 1))
  }
  plaene.value.filter((p) => p.istMasterplan).forEach((p) => hinzufuegen(p, 0))
  plaene.value.forEach((p) => hinzufuegen(p, 1)) // Rest (sollte nicht vorkommen)
  return ergebnis
})

const bezugsplanName = computed(() => plaene.value.find((p) => p.id === plan.value?.bezugsplanId)?.bezeichnung ?? '')

// Esc: Entwurf/Auswahl schliessen, dann Setzen-Modus beenden
function taste(e: KeyboardEvent) {
  if (e.key !== 'Escape' || werkzeugAktiv.value) return
  if (modus.value === 'importieren' || modus.value === 'einstellungen') modus.value = 'ansehen'
  else if (entwurf.value || auswahlId.value !== null) schliessen()
  else if (modus.value === 'setzen') modus.value = 'ansehen'
}
onMounted(() => window.addEventListener('keydown', taste))
onBeforeUnmount(() => window.removeEventListener('keydown', taste))

watch(() => props.id, laden, { immediate: true })
watch(() => route.query.plan, () => {
  if (!laedt.value && standort.value?.masterplan) planLaden()
})
watch(alleStockwerke, positionenLaden)
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
      <select class="planwahl" :value="plan.id" aria-label="Plan auswählen"
        @change="planWechseln(Number(($event.target as HTMLSelectElement).value))">
        <option v-for="o in planOptionen" :key="o.id" :value="o.id">{{ o.text }}</option>
      </select>
      <span v-if="plan.referenziert && plan.istMasterplan" class="kalibriert" :title="plan.abweichungProzent !== null
        ? `Abweichung zwischen den Strecken: ${formatZahl(plan.abweichungProzent, 1)} %` : ''">
        ✓ kalibriert · {{ formatMassstab(plan.massstab) }}
      </span>
      <span v-else-if="plan.referenziert" class="kalibriert"
        :title="`Referenziert auf «${bezugsplanName}» · Drehung ${formatDrehung(plan.drehung)}`">
        ✓ referenziert · {{ formatMassstab(plan.massstab) }}
      </span>
      <span v-else class="offen">{{ plan.istMasterplan ? 'nicht kalibriert' : 'nicht referenziert' }}</span>
      <label v-if="plan.referenziert && !plan.istMasterplan && !werkzeugAktiv" class="schalter"
        title="Auch Positionen anderer Stockwerke anzeigen">
        <input v-model="alleStockwerke" type="checkbox" /> alle Stockwerke
      </label>
      <span class="rechts">
        <template v-if="!werkzeugAktiv">
          <button v-if="plan.referenziert" type="button" class="klein-btn"
            :class="modus === 'setzen' ? 'aktiv-btn' : 'zweitrangig'" :aria-pressed="modus === 'setzen'"
            title="Klick in den Plan setzt eine neue Position (Esc beendet)" @click="setzenUmschalten">
            {{ modus === 'setzen' ? '✓ Position setzen' : '＋ Position setzen' }}
          </button>
          <button type="button" class="zweitrangig klein-btn" :class="{ gedrueckt: modus === 'einstellungen' }"
            @click="seitenleiste('einstellungen')">Plan-Einstellungen</button>
          <button v-if="standort.masterplan?.referenziert || plan.referenziert" type="button" class="zweitrangig klein-btn"
            :class="{ gedrueckt: modus === 'importieren' }" @click="seitenleiste('importieren')">＋ Plan importieren</button>
        </template>
        <a :href="plan.pdfUrl">Original-PDF</a>
      </span>
    </div>
    <p v-if="meldung" class="meldung" role="status">{{ meldung }}</p>
    <p v-else-if="modus === 'setzen' && !entwurf" class="meldung info" role="status">
      Klicken Sie in den Plan, um eine Position zu setzen. Esc beendet den Modus.
    </p>
    <div class="arbeitsflaeche">
      <PlanAnsicht ref="ansicht" :plan="plan" :markierungen="markierungen" :linien="linien" :klick-modus="klickModus"
        :kontrollpunkt="modus === 'referenzieren' ? kontrolleLinks : null"
        :titel="modus === 'referenzieren' ? `Dieser Plan: ${plan.bezeichnung}` : ''" class="ansicht"
        @klick="klick" @verschoben="verschoben" @markierung-klick="markierungKlick" @zeiger="(p: Punkt | null) => zeiger('links', p)" />
      <template v-if="modus === 'referenzieren'">
        <PlanAnsicht v-if="refAnzeige?.bezug" :key="refAnzeige.bezug.id" :plan="refAnzeige.bezug"
          :markierungen="refAnzeige.rechts" :linien="refAnzeige.linienRechts" :klick-modus="refAnzeige.klickRechts"
          :kontrollpunkt="kontrolleRechts" :titel="`Bezugsplan: ${refAnzeige.bezug.bezeichnung}`" class="ansicht bezug"
          @klick="(p: Punkt) => referenz?.klick('rechts', p)"
          @verschoben="(id: string, p: Punkt) => referenz?.verschoben('rechts', id, p)"
          @zeiger="(p: Punkt | null) => zeiger('rechts', p)" />
        <div v-else class="ansicht bezug platzhalter leise">Bezugsplan wird geladen …</div>
        <component :is="referenzMethode === 'Passpunkte' ? PlanReferenzierung : PlanEigenerMassstab" ref="referenz"
          :key="`${plan.id}-${referenzMethode}`" :plan="plan" :plaene="plaene"
          @anzeige="(a: ReferenzAnzeige) => (refAnzeige = a)" @kontrolle="kontrolle" @methode="methodeWechseln"
          @gespeichert="referenzGespeichert" @abbrechen="referenzAbbrechen" />
      </template>
      <MasterKalibrierung v-else-if="modus === 'kalibrieren'" ref="kalibrierung" :plan="plan" @anzeige="werkzeugAnzeige"
        @gespeichert="kalibrierungGespeichert" @abbrechen="modus = 'ansehen'" />
      <PlanImport v-else-if="modus === 'importieren'" :standort-id="standort.id" :plaene="plaene"
        :vorschlag-bezug="plan.referenziert ? plan.id : null" @importiert="planImportiert" @abbrechen="modus = 'ansehen'" />
      <PlanEinstellungen v-else-if="modus === 'einstellungen'" :plan="plan" :plaene="plaene"
        @gespeichert="planGeaendert" @geloescht="planGeloescht" @abbrechen="modus = 'ansehen'" @lage-aendern="lageAendern" />
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
.planwahl {
  font: inherit;
  font-size: 0.95rem;
  max-width: 22rem;
  padding: 0.25rem 0.4rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
  background: var(--flaeche);
}
.schalter {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin: 0;
  font-weight: 400;
  font-size: 0.85rem;
}
.gedrueckt {
  background: var(--hintergrund);
  border-color: var(--text-leise);
}
.bezug {
  border-left: 3px solid #54aeff;
}
.platzhalter {
  display: grid;
  place-items: center;
  background: #e9ecef;
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
