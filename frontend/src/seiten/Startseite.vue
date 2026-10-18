<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, type Standort } from '../api'

const router = useRouter()
const standorte = ref<Standort[]>([])
const laedt = ref(true)
const ladeFehler = ref<string | null>(null)

const neuerName = ref('')
const speichert = ref(false)
const anlegeFehler = ref<string | null>(null)

async function laden() {
  laedt.value = true
  ladeFehler.value = null
  try {
    standorte.value = await api.standorte()
  } catch (e) {
    ladeFehler.value = (e as Error).message
  } finally {
    laedt.value = false
  }
}

async function anlegen() {
  anlegeFehler.value = null
  if (!neuerName.value.trim()) {
    anlegeFehler.value = 'Bitte einen Namen eingeben.'
    return
  }
  speichert.value = true
  try {
    const standort = await api.standortAnlegen(neuerName.value)
    neuerName.value = ''
    await router.push({ name: 'standort', params: { id: standort.id } })
  } catch (e) {
    anlegeFehler.value = (e as Error).message
  } finally {
    speichert.value = false
  }
}

onMounted(laden)
</script>

<template>
  <main class="inhalt">
    <h1>Standorte</h1>

    <p v-if="laedt" class="leise">Wird geladen …</p>
    <p v-else-if="ladeFehler" class="fehler">{{ ladeFehler }}</p>
    <p v-else-if="standorte.length === 0" class="leise">Noch keine Standorte erfasst. Legen Sie unten den ersten an.</p>

    <ul v-else class="liste">
      <li v-for="s in standorte" :key="s.id">
        <RouterLink :to="{ name: 'standort', params: { id: s.id } }" class="karte eintrag">
          <span class="name">{{ s.name }}</span>
          <span v-if="s.masterplan" class="leise">Masterplan: {{ s.masterplan.bezeichnung }}</span>
          <span v-else class="hinweis">Noch kein Masterplan</span>
        </RouterLink>
      </li>
    </ul>

    <form class="karte neu" @submit.prevent="anlegen">
      <h2>Neuer Standort</h2>
      <label for="standort-name">Name</label>
      <div class="zeile">
        <input id="standort-name" v-model="neuerName" type="text" maxlength="100" placeholder="z.B. Werk Nord"
          @input="anlegeFehler = null" />
        <button type="submit" :disabled="speichert">{{ speichert ? 'Wird angelegt …' : 'Anlegen' }}</button>
      </div>
      <p v-if="anlegeFehler" class="fehler">{{ anlegeFehler }}</p>
    </form>
  </main>
</template>

<style scoped>
.liste {
  list-style: none;
  padding: 0;
  margin: 1rem 0 1.5rem;
  display: grid;
  gap: 0.6rem;
}
.eintrag {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 1rem;
  text-decoration: none;
  color: inherit;
}
.eintrag:hover {
  border-color: var(--akzent);
}
.name {
  font-weight: 600;
  font-size: 1.05rem;
}
.hinweis {
  color: #9a6700;
  font-size: 0.9rem;
}
.neu {
  margin-top: 1rem;
}
.zeile {
  display: flex;
  gap: 0.5rem;
}
.zeile input {
  flex: 1;
}
</style>
