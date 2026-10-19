<script setup lang="ts">
import { computed, ref } from 'vue'
import type { PositionImPlan, Verbindung } from '../api'
import { stockwerkText } from '../format'

/** Seitenleiste im Modus «Verbinden»: zweite Position im Plan anklicken oder hier wählen */
const props = defineProps<{
  von: PositionImPlan
  positionen: PositionImPlan[]
  verbindungen: Verbindung[]
  /** Gerade wird eine Verbindung gespeichert */
  speichert: boolean
}>()
const emit = defineEmits<{ waehlen: [id: number]; fertig: [] }>()

const suche = ref('')

const verbunden = computed(() => {
  const ids = new Set<number>()
  for (const v of props.verbindungen) {
    if (v.a.id === props.von.id) ids.add(v.b.id)
    if (v.b.id === props.von.id) ids.add(v.a.id)
  }
  return ids
})

const kandidaten = computed(() => {
  const s = suche.value.trim().toLowerCase()
  return props.positionen
    .filter((p) => p.id !== props.von.id && !verbunden.value.has(p.id))
    .filter((p) => !s || p.bezeichnung.toLowerCase().includes(s) || p.positionstyp.toLowerCase().includes(s))
})
</script>

<template>
  <aside class="seitenleiste">
    <div class="kopf">
      <h2>Verbinden</h2>
      <button type="button" class="zweitrangig klein-btn" title="Fertig (Esc)" @click="emit('fertig')">Fertig</button>
    </div>
    <p class="von">
      Von <strong>{{ von.bezeichnung }}</strong>&nbsp;<span class="leise">({{ von.positionstyp }} · {{ stockwerkText(von.stockwerk) }})</span>
    </p>
    <p class="anleitung">
      Klicken Sie die zweite Position im Plan an oder wählen Sie sie unten aus. Danach können Sie gleich die nächste
      Verbindung ab «{{ von.bezeichnung }}» anlegen. Esc beendet.
    </p>
    <input v-model="suche" type="text" placeholder="Suchen nach Bezeichnung oder Typ" class="suche" />
    <p v-if="kandidaten.length === 0" class="leise klein">Keine weitere Position zum Verbinden in diesem Plan.</p>
    <ul class="liste">
      <li v-for="p in kandidaten" :key="p.id">
        <button type="button" class="eintrag" :disabled="speichert" @click="emit('waehlen', p.id)">
          <span class="name">{{ p.bezeichnung }}</span>
          <span class="leise klein">{{ p.positionstyp }} · {{ stockwerkText(p.stockwerk) }}</span>
        </button>
      </li>
    </ul>
    <p class="leise klein">Bereits verbunden: {{ verbunden.size }}</p>
  </aside>
</template>

<style scoped>
.von {
  margin: 0.5rem 0;
}
.suche {
  margin: 0.6rem 0;
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
  background: none;
  color: inherit;
  border: none;
  border-bottom: 1px solid var(--rand);
  border-radius: 0;
  padding: 0.45rem 0.25rem;
}
.eintrag:hover {
  background: var(--hintergrund);
}
.name {
  font-weight: 600;
}
</style>
