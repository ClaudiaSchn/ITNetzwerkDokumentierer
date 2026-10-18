<script setup lang="ts">
import { onMounted, ref } from 'vue'

interface Status {
  backend: string
  datenbank: string
  version: string
  zeit: string
}

const status = ref<Status | null>(null)
const fehler = ref<string | null>(null)
const laedt = ref(false)

async function pruefen() {
  laedt.value = true
  fehler.value = null
  try {
    const antwort = await fetch('/api/health')
    if (!antwort.ok) throw new Error(`HTTP ${antwort.status}`)
    status.value = (await antwort.json()) as Status
  } catch (e) {
    status.value = null
    fehler.value = e instanceof Error ? e.message : String(e)
  } finally {
    laedt.value = false
  }
}

onMounted(pruefen)
</script>

<template>
  <main>
    <h1>Netzwerkdokumentation</h1>
    <p class="hinweis">Projektgerüst – Phase 1</p>

    <section class="karte">
      <h2>Systemstatus</h2>
      <p v-if="laedt">Wird geprüft …</p>
      <p v-else-if="fehler" class="schlecht">Backend nicht erreichbar: {{ fehler }}</p>
      <dl v-else-if="status">
        <dt>Frontend</dt>
        <dd class="gut">läuft</dd>
        <dt>Backend</dt>
        <dd class="gut">{{ status.backend }}</dd>
        <dt>Datenbank</dt>
        <dd :class="status.datenbank === 'verbunden' ? 'gut' : 'schlecht'">{{ status.datenbank }}</dd>
        <dt>Version</dt>
        <dd>{{ status.version }}</dd>
      </dl>
      <button type="button" :disabled="laedt" @click="pruefen">Erneut prüfen</button>
    </section>
  </main>
</template>

<style>
:root {
  font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;
  color: #1f2328;
  background: #f6f7f9;
}
body {
  margin: 0;
}
main {
  max-width: 40rem;
  margin: 0 auto;
  padding: 2rem 1rem;
}
h1 {
  margin-bottom: 0.25rem;
}
.hinweis {
  margin-top: 0;
  color: #59636e;
}
.karte {
  background: #fff;
  border: 1px solid #d1d9e0;
  border-radius: 8px;
  padding: 1rem 1.25rem;
}
dl {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 0.4rem 1.5rem;
}
dt {
  color: #59636e;
}
dd {
  margin: 0;
}
.gut {
  color: #1a7f37;
  font-weight: 600;
}
.schlecht {
  color: #cf222e;
  font-weight: 600;
}
button {
  font: inherit;
  padding: 0.4rem 0.9rem;
  border-radius: 6px;
  border: 1px solid #d1d9e0;
  background: #f6f8fa;
  cursor: pointer;
}
</style>
