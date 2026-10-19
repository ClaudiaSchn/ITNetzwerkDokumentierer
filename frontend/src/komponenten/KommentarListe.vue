<script setup lang="ts">
import { ref } from 'vue'
import { api, type Kommentar } from '../api'
import { formatZeit } from '../format'

/**
 * Liste von Kommentaren mit Erledigen/Wieder öffnen, Text korrigieren und Löschen.
 * Wird an der Position und in der Standort-Übersicht verwendet.
 */
defineProps<{
  kommentare: Kommentar[]
  /** Position zu jedem Kommentar anzeigen (Übersicht) */
  mitPosition?: boolean
}>()
const emit = defineEmits<{
  geaendert: [kommentar: Kommentar]
  geloescht: [id: number]
  position: [id: number]
}>()

const fehler = ref<string | null>(null)
const bearbeiten = ref<number | null>(null)
const text = ref('')
const loeschenBestaetigen = ref<number | null>(null)
const arbeitet = ref(false)

async function status(k: Kommentar) {
  fehler.value = null
  arbeitet.value = true
  try {
    emit('geaendert', await api.kommentarAendern(k.id, k.version, { status: k.status === 'Offen' ? 'Erledigt' : 'Offen' }))
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    arbeitet.value = false
  }
}

function bearbeitenStarten(k: Kommentar) {
  bearbeiten.value = k.id
  text.value = k.text
  fehler.value = null
}

async function speichern(k: Kommentar) {
  fehler.value = null
  if (!text.value.trim()) {
    fehler.value = 'Der Text darf nicht leer sein.'
    return
  }
  try {
    emit('geaendert', await api.kommentarAendern(k.id, k.version, { text: text.value.trim() }))
    bearbeiten.value = null
  } catch (e) {
    fehler.value = (e as Error).message
  }
}

async function loeschen(k: Kommentar) {
  if (loeschenBestaetigen.value !== k.id) {
    loeschenBestaetigen.value = k.id
    return
  }
  loeschenBestaetigen.value = null
  fehler.value = null
  try {
    await api.kommentarLoeschen(k.id, k.version)
    emit('geloescht', k.id)
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
</script>

<template>
  <p v-if="fehler" class="fehler klein">{{ fehler }}</p>
  <ul class="kommentare">
    <li v-for="k in kommentare" :key="k.id" :class="{ erledigt: k.status === 'Erledigt' }">
      <div class="kopf">
        <span>
          <button v-if="mitPosition" type="button" class="link pos" title="Position im Plan zeigen"
            @click="emit('position', k.position.id)">{{ k.position.bezeichnung }}</button>
          <span class="leise klein">{{ k.verfasser }} · {{ formatZeit(k.erstelltAm) }}</span>
        </span>
        <span class="status" :class="k.status === 'Offen' ? 'offen' : 'zu'">{{ k.status === 'Offen' ? 'offen' : 'erledigt' }}</span>
      </div>
      <template v-if="bearbeiten === k.id">
        <textarea v-model="text" rows="3" maxlength="2000" :aria-label="`Text von Kommentar ${k.id}`"></textarea>
        <div class="aktionen-klein">
          <button type="button" class="klein-btn" @click="speichern(k)">Speichern</button>
          <button type="button" class="zweitrangig klein-btn" @click="bearbeiten = null">Abbrechen</button>
        </div>
      </template>
      <p v-else class="text">{{ k.text }}</p>
      <div v-if="bearbeiten !== k.id" class="aktionen-klein">
        <button type="button" class="klein-btn" :class="k.status === 'Offen' ? '' : 'zweitrangig'" :disabled="arbeitet"
          @click="status(k)">{{ k.status === 'Offen' ? '✓ Erledigt' : 'Wieder öffnen' }}</button>
        <button type="button" class="zweitrangig klein-btn" @click="bearbeitenStarten(k)">✎ Text</button>
        <button type="button" class="zweitrangig klein-btn weg" :class="{ bestaetigen: loeschenBestaetigen === k.id }"
          @click="loeschen(k)">{{ loeschenBestaetigen === k.id ? 'Wirklich löschen?' : 'Löschen' }}</button>
      </div>
    </li>
  </ul>
</template>

<style scoped>
.kommentare {
  list-style: none;
  padding: 0;
  margin: 0.4rem 0 0;
}
.kommentare li {
  border: 1px solid var(--rand);
  border-left: 4px solid #cf222e;
  border-radius: 6px;
  padding: 0.45rem 0.6rem;
  margin-bottom: 0.5rem;
  background: var(--flaeche);
}
.kommentare li.erledigt {
  border-left-color: var(--gut);
  opacity: 0.8;
}
.kopf {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 0.5rem;
}
.kopf > span:first-child {
  display: flex;
  flex-direction: column;
}
.status {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.05rem 0.45rem;
  border-radius: 10px;
  white-space: nowrap;
}
.status.offen {
  background: #ffebe9;
  color: #cf222e;
}
.status.zu {
  background: #dafbe1;
  color: var(--gut);
}
.text {
  margin: 0.35rem 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
textarea {
  font: inherit;
  width: 100%;
  margin: 0.35rem 0;
  padding: 0.35rem 0.45rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
  box-sizing: border-box;
}
.aktionen-klein {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}
.klein-btn {
  padding: 0.2rem 0.6rem;
  font-size: 0.85rem;
}
.weg.bestaetigen,
.weg:hover {
  color: var(--schlecht);
  border-color: var(--schlecht);
}
.link {
  background: none;
  border: none;
  padding: 0;
  color: var(--akzent);
  font-size: inherit;
  text-align: left;
}
.pos {
  font-weight: 600;
}
.klein {
  font-size: 0.85rem;
}
</style>
