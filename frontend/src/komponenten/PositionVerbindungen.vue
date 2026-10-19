<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { PositionImPlan, Verbindung } from '../api'
import { formatZahl } from '../geometrie'
import { stockwerkText } from '../format'

/** Verbindungen der ausgewählten Position, mit «Verbinden» und Löschen */
const props = defineProps<{
  position: PositionImPlan
  verbindungen: Verbindung[]
  /** Ids der Positionen, die im aktuellen Plan zu sehen sind */
  sichtbar: Set<number>
}>()
const emit = defineEmits<{
  verbinden: []
  auswaehlen: [id: number]
  loeschen: [verbindung: Verbindung]
}>()

const bestaetigen = ref<number | null>(null)
watch(() => props.position.id, () => (bestaetigen.value = null))

const eigene = computed(() =>
  props.verbindungen
    .filter((v) => v.a.id === props.position.id || v.b.id === props.position.id)
    .map((v) => ({ v, partner: v.a.id === props.position.id ? v.b : v.a, istA: v.a.id === props.position.id }))
    .sort((x, y) => x.partner.bezeichnung.localeCompare(y.partner.bezeichnung, 'de'))
)

function loeschen(v: Verbindung) {
  if (bestaetigen.value !== v.id) {
    bestaetigen.value = v.id
    return
  }
  bestaetigen.value = null
  emit('loeschen', v)
}
</script>

<template>
  <section class="verbindungen">
    <div class="kopf">
      <h3>Verbindungen <span class="leise">({{ eigene.length }})</span></h3>
      <button type="button" class="klein-btn" title="Verbindung zu einer anderen Position anlegen" @click="emit('verbinden')">
        ＋ Verbinden
      </button>
    </div>
    <p v-if="eigene.length === 0" class="leise klein">Noch keine Verbindungen.</p>
    <ul class="liste">
      <li v-for="e in eigene" :key="e.v.id">
        <button type="button" class="partner" :disabled="!sichtbar.has(e.partner.id)"
          :title="sichtbar.has(e.partner.id) ? 'Position anzeigen' : 'In diesem Plan nicht sichtbar'"
          @click="emit('auswaehlen', e.partner.id)">
          <span class="name">{{ e.istA ? '→' : '←' }} {{ e.partner.bezeichnung }}</span>
          <span class="leise klein">
            {{ e.partner.positionstyp }} · {{ stockwerkText(e.partner.stockwerk) }} · {{ formatZahl(e.v.laengeMeter, 0) }} m
            <template v-if="e.v.anzahlMedien"> · {{ e.v.anzahlMedien === 1 ? '1 Medium' : `${e.v.anzahlMedien} Medien` }}</template>
          </span>
        </button>
        <button type="button" class="weg" :class="{ bestaetigen: bestaetigen === e.v.id }"
          :disabled="e.v.anzahlMedien > 0"
          :title="e.v.anzahlMedien > 0 ? 'Gesperrt: Es laufen noch Medien darüber' : 'Verbindung löschen'"
          @click="loeschen(e.v)">
          {{ bestaetigen === e.v.id ? 'Löschen?' : '✕' }}
        </button>
      </li>
    </ul>
    <p class="leise klein">→ = diese Position ist Ende A. Luftlinie in Metern, ohne Leitungsverlauf.</p>
  </section>
</template>

<style scoped>
.verbindungen {
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
.liste {
  list-style: none;
  padding: 0;
  margin: 0.4rem 0 0;
}
.liste li {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--rand);
}
.partner {
  flex: 1;
  text-align: left;
  display: flex;
  flex-direction: column;
  background: none;
  color: inherit;
  border: none;
  border-radius: 0;
  padding: 0.4rem 0.25rem;
}
.partner:not(:disabled):hover {
  background: var(--hintergrund);
}
.partner:disabled {
  opacity: 1;
}
.name {
  font-weight: 600;
}
.weg {
  background: none;
  color: var(--text-leise);
  border: 1px solid transparent;
  padding: 0.15rem 0.45rem;
  font-size: 0.85rem;
}
.weg:not(:disabled):hover,
.weg.bestaetigen {
  color: var(--schlecht);
  border-color: var(--schlecht);
}
</style>
