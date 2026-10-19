<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Kommentar, type PositionImPlan } from '../api'
import { verfasserLesen, verfasserMerken } from '../format'
import KommentarListe from './KommentarListe.vue'

/** Kommentare der ausgewählten Position: Liste, neuer Kommentar */
const props = defineProps<{ position: PositionImPlan; aktualisierung?: number }>()
const emit = defineEmits<{ geaendert: [] }>()

const kommentare = ref<Kommentar[]>([])
const formularOffen = ref(false)
const text = ref('')
const verfasser = ref(verfasserLesen())
const fehler = ref<string | null>(null)
const speichert = ref(false)
const erledigteZeigen = ref(false)

async function laden() {
  try {
    kommentare.value = await api.kommentare(props.position.id)
  } catch (e) {
    fehler.value = (e as Error).message
  }
}
watch(() => [props.position.id, props.aktualisierung], () => {
  formularOffen.value = false
  laden()
}, { immediate: true })

const offen = computed(() => kommentare.value.filter((k) => k.status === 'Offen'))
const sichtbar = computed(() => (erledigteZeigen.value ? kommentare.value : offen.value))
const erledigt = computed(() => kommentare.value.length - offen.value.length)

async function anlegen() {
  fehler.value = null
  if (!text.value.trim()) {
    fehler.value = 'Bitte einen Text eingeben.'
    return
  }
  if (!verfasser.value.trim()) {
    fehler.value = 'Bitte Ihren Namen als Verfasser angeben.'
    return
  }
  speichert.value = true
  try {
    await api.kommentarAnlegen(props.position.id, text.value.trim(), verfasser.value.trim())
    verfasserMerken(verfasser.value.trim())
    text.value = ''
    formularOffen.value = false
    await laden()
    emit('geaendert')
  } catch (e) {
    fehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

function geaendert(k: Kommentar) {
  kommentare.value = kommentare.value.map((x) => (x.id === k.id ? k : x))
  emit('geaendert')
}

function geloescht(id: number) {
  kommentare.value = kommentare.value.filter((x) => x.id !== id)
  emit('geaendert')
}
</script>

<template>
  <section class="abschnitt-kommentare">
    <div class="kopf">
      <h3>Kommentare <span class="leise">({{ offen.length }} offen)</span></h3>
      <button v-if="!formularOffen" type="button" class="klein-btn" @click="formularOffen = true; fehler = null">＋ Kommentar</button>
    </div>
    <div v-if="formularOffen" class="formular">
      <label for="ko-text">Kommentar</label>
      <textarea id="ko-text" v-model="text" rows="3" maxlength="2000" placeholder="z.B. Dose ist beschriftet mit 2.14, nicht 2.12"></textarea>
      <label for="ko-verfasser">Verfasser</label>
      <input id="ko-verfasser" v-model="verfasser" type="text" maxlength="100" placeholder="Ihr Name" />
      <p v-if="fehler" class="fehler klein">{{ fehler }}</p>
      <div class="zeile">
        <button type="button" class="klein-btn" :disabled="speichert" @click="anlegen">Speichern</button>
        <button type="button" class="zweitrangig klein-btn" @click="formularOffen = false">Abbrechen</button>
      </div>
    </div>
    <p v-else-if="fehler" class="fehler klein">{{ fehler }}</p>
    <p v-if="kommentare.length === 0 && !formularOffen" class="leise klein">Keine Kommentare.</p>
    <KommentarListe :kommentare="sichtbar" @geaendert="geaendert" @geloescht="geloescht" />
    <button v-if="erledigt > 0" type="button" class="link klein" @click="erledigteZeigen = !erledigteZeigen">
      {{ erledigteZeigen ? 'Erledigte ausblenden' : `${erledigt} erledigte anzeigen` }}
    </button>
  </section>
</template>

<style scoped>
.abschnitt-kommentare {
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
label {
  font-size: 0.85rem;
  display: block;
  margin-top: 0.3rem;
}
textarea,
input {
  font: inherit;
  width: 100%;
  box-sizing: border-box;
  padding: 0.35rem 0.45rem;
  border: 1px solid var(--rand);
  border-radius: 6px;
}
.zeile {
  display: flex;
  gap: 0.35rem;
  margin-top: 0.5rem;
}
.link {
  background: none;
  border: none;
  padding: 0;
  color: var(--akzent);
}
</style>
