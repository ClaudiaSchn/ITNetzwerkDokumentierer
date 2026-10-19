<script setup lang="ts">
import { ref, watch } from 'vue'
import { api, type Kommentar, type Kommentarstatus } from '../api'
import KommentarListe from './KommentarListe.vue'

/** Übersicht im Büro: Kommentare des ganzen Standorts auswerten und erledigen */
const props = defineProps<{ standortId: number }>()
const emit = defineEmits<{ schliessen: []; position: [id: number]; geaendert: [] }>()

const filter = ref<Kommentarstatus | 'alle'>('Offen')
const kommentare = ref<Kommentar[]>([])
const fehler = ref<string | null>(null)
const laedt = ref(false)

async function laden() {
  laedt.value = true
  fehler.value = null
  try {
    kommentare.value = await api.standortKommentare(props.standortId, filter.value)
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    laedt.value = false
  }
}
watch(filter, laden, { immediate: true })

function geaendert(k: Kommentar) {
  // Im Filter «offen» verschwindet ein erledigter Kommentar aus der Liste
  kommentare.value = filter.value === 'alle' || k.status === filter.value
    ? kommentare.value.map((x) => (x.id === k.id ? k : x))
    : kommentare.value.filter((x) => x.id !== k.id)
  emit('geaendert')
}

function geloescht(id: number) {
  kommentare.value = kommentare.value.filter((x) => x.id !== id)
  emit('geaendert')
}
</script>

<template>
  <aside class="seitenleiste">
    <div class="kopf">
      <h2>Kommentare</h2>
      <button type="button" class="zweitrangig klein-btn" title="Schliessen (Esc)" @click="emit('schliessen')">✕</button>
    </div>
    <div class="reiter" role="tablist">
      <button v-for="[k, t] in ([['Offen', 'offen'], ['Erledigt', 'erledigt'], ['alle', 'alle']] as const)" :key="k" type="button"
        role="tab" :aria-selected="filter === k" :class="{ an: filter === k }" @click="filter = k">{{ t }}</button>
    </div>
    <p v-if="fehler" class="fehler">{{ fehler }}</p>
    <p v-else-if="!laedt && kommentare.length === 0" class="leise">
      {{ filter === 'Offen' ? 'Keine offenen Kommentare an diesem Standort.' : 'Keine Kommentare.' }}
    </p>
    <KommentarListe :kommentare="kommentare" mit-position @geaendert="geaendert" @geloescht="geloescht"
      @position="(id) => emit('position', id)" />
  </aside>
</template>

<style scoped>
.reiter {
  display: flex;
  gap: 0.25rem;
  border-bottom: 1px solid var(--rand);
  margin: 0.5rem 0 0.6rem;
}
.reiter button {
  background: none;
  color: var(--text-leise);
  border: none;
  border-bottom: 3px solid transparent;
  border-radius: 0;
  padding: 0.35rem 0.7rem;
}
.reiter button.an {
  color: var(--text);
  border-bottom-color: var(--akzent);
  font-weight: 600;
}
</style>
