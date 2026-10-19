<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { api, type Geraetekategorie, type Geraetetyp, type Portart } from '../api'

/** Neuen Gerätetyp anlegen: Name, Kategorie und Portgruppen (z.B. 24× RJ45, dann 4× SFP) */
const props = defineProps<{ portarten: Portart[]; vorhandene: Geraetetyp[] }>()
const emit = defineEmits<{ angelegt: [typ: Geraetetyp]; abbrechen: []; portartAngelegt: [portart: Portart] }>()

const name = ref('')
const kategorie = ref<Geraetekategorie>('Endgeraet')
const gruppen = reactive<{ anzahl: number; portartId: number | null }[]>([
  { anzahl: 24, portartId: props.portarten.find((p) => p.name === 'RJ45')?.id ?? props.portarten[0]?.id ?? null }
])
const neuePortart = ref('')
const fehler = ref<string | null>(null)
const speichert = ref(false)

const nummern = computed(() => gruppen.reduce((s, g) => s + (Number.isInteger(g.anzahl) && g.anzahl > 0 ? g.anzahl : 0), 0))
const vorschau = computed(() => {
  const teile = gruppen
    .filter((g) => g.anzahl > 0 && g.portartId !== null)
    .map((g) => `${g.anzahl}× ${props.portarten.find((p) => p.id === g.portartId)?.name ?? '?'}`)
  if (!teile.length) return ''
  const ports = kategorie.value === 'Durchgang' ? `${nummern.value * 2} Ports (je ${nummern.value} vorne und hinten)` : `${nummern.value} Ports`
  return `${teile.join(' + ')} → ${ports}`
})

async function portartAnlegen() {
  const n = neuePortart.value.trim()
  if (!n) return
  fehler.value = null
  const vorhanden = props.portarten.find((p) => p.name.toLocaleLowerCase('de') === n.toLocaleLowerCase('de'))
  try {
    const p = vorhanden ?? (await api.portartAnlegen(n))
    if (!vorhanden) emit('portartAngelegt', p)
    gruppen[gruppen.length - 1].portartId = p.id
    neuePortart.value = ''
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

async function anlegen() {
  fehler.value = null
  if (!name.value.trim()) {
    fehler.value = 'Bitte einen Namen eingeben, z.B. «Switch 48× RJ45».'
    return
  }
  // Gibt es den Typ schon (Gross-/Kleinschreibung egal)? Dann diesen verwenden.
  const vorhanden = props.vorhandene.find((t) => t.name.toLocaleLowerCase('de') === name.value.trim().toLocaleLowerCase('de'))
  if (vorhanden) {
    emit('angelegt', vorhanden)
    return
  }
  if (gruppen.some((g) => !Number.isInteger(g.anzahl) || g.anzahl < 1 || g.portartId === null)) {
    fehler.value = 'Bitte in jeder Portgruppe eine Anzahl ab 1 und eine Portart angeben.'
    return
  }
  speichert.value = true
  try {
    emit('angelegt', await api.geraetetypAnlegen(name.value.trim(), kategorie.value,
      gruppen.map((g) => ({ anzahl: g.anzahl, portartId: g.portartId! }))))
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}
</script>

<template>
  <div class="typformular" @keydown.enter.prevent>
    <h3>Neuer Gerätetyp</h3>
    <div class="feld">
      <label for="gt-name">Name</label>
      <input id="gt-name" v-model="name" type="text" maxlength="100" placeholder="z.B. Switch 48× RJ45 + 4× SFP" />
    </div>
    <div class="wahl">
      <label><input v-model="kategorie" type="radio" value="Endgeraet" /> Endgerät</label>
      <label><input v-model="kategorie" type="radio" value="Durchgang" /> Durchgang</label>
    </div>
    <p class="leise klein">
      {{ kategorie === 'Durchgang'
        ? 'Patchpanel, Netzwerkdose: Pro Nummer entstehen ein Port vorne und einer hinten, intern durchverbunden.'
        : 'Switch, Access Point, PC …: Die Ports werden über alle Gruppen fortlaufend nummeriert.' }}
    </p>
    <p class="klein titel">Ports</p>
    <div v-for="(g, i) in gruppen" :key="i" class="gruppe">
      <input v-model.number="g.anzahl" type="number" min="1" max="512" :aria-label="`Anzahl Gruppe ${i + 1}`" />
      <span>×</span>
      <select v-model="g.portartId" :aria-label="`Portart Gruppe ${i + 1}`">
        <option v-for="p in portarten" :key="p.id" :value="p.id">{{ p.name }}</option>
      </select>
      <button v-if="gruppen.length > 1" type="button" class="zweitrangig klein-btn" title="Gruppe entfernen"
        @click="gruppen.splice(i, 1)">✕</button>
    </div>
    <div class="zeile">
      <button type="button" class="zweitrangig klein-btn" @click="gruppen.push({ anzahl: 4, portartId: gruppen[gruppen.length - 1].portartId })">
        ＋ Gruppe
      </button>
      <input v-model="neuePortart" type="text" maxlength="100" placeholder="neue Portart" aria-label="Neue Portart"
        @keydown.enter.prevent="portartAnlegen" />
      <button type="button" class="zweitrangig klein-btn" title="Portart anlegen" @click="portartAnlegen">＋</button>
    </div>
    <p v-if="vorschau" class="wert">{{ vorschau }}</p>
    <p v-if="fehler" class="fehler klein">{{ fehler }}</p>
    <div class="zeile">
      <button type="button" class="klein-btn" :disabled="speichert" @click="anlegen">Gerätetyp anlegen</button>
      <button type="button" class="zweitrangig klein-btn" @click="emit('abbrechen')">Abbrechen</button>
    </div>
  </div>
</template>

<style scoped>
.typformular {
  margin-top: 0.5rem;
  padding: 0.6rem;
  border: 1px dashed var(--rand);
  border-radius: 6px;
  background: var(--flaeche);
}
.wahl {
  display: flex;
  gap: 1.2rem;
}
.wahl label {
  font-weight: 400;
  display: flex;
  gap: 0.35rem;
  align-items: center;
  margin: 0.15rem 0;
}
h3 {
  font-size: 0.95rem;
  margin: 0 0 0.3rem;
}
label {
  font-size: 0.85rem;
}
.klein {
  font-size: 0.85rem;
  margin: 0.25rem 0 0;
}
.wert {
  font-size: 0.9rem;
  margin: 0.4rem 0 0;
}
.klein-btn {
  padding: 0.2rem 0.6rem;
  font-size: 0.85rem;
}
input[type='number'],
input[type='text'],
select {
  font: inherit;
  font-size: 0.9rem;
  padding: 0.3rem 0.45rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
  background: var(--flaeche);
}
.feld input[type='text'] {
  width: 100%;
}
.titel {
  font-weight: 600;
  margin-top: 0.6rem !important;
}
.gruppe,
.zeile {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin-top: 0.35rem;
}
.gruppe input {
  width: 4.5rem !important;
}
.gruppe select {
  flex: 1;
}
.zeile input {
  flex: 1;
}
</style>
