<script setup lang="ts">
import type { Verlauf, VerlaufGlied, VerlaufPunkt } from '../api'
import { formatZahl } from '../geometrie'
import { stockwerkText } from '../format'

/** Leitungsverlauf als senkrechte Kette: Port – Kabel/Patch/Durchgang – Port … */
defineProps<{ verlauf: Verlauf; hervorPortId?: number | null }>()
const emit = defineEmits<{ geraet: [id: number]; medium: [id: number] }>()

const seite = (s: string) => (s === 'Vorne' ? ' vorne' : s === 'Hinten' ? ' hinten' : '')
const portText = (p: VerlaufPunkt) =>
  p.port ? `${p.port.geraet} · Port ${p.port.nummer}${seite(p.port.seite)}${p.port.bezeichnung ? ` (${p.port.bezeichnung})` : ''}` : ''
const anzahl = (v: Verlauf, art: VerlaufGlied['art']) => v.glieder.filter((g) => g.art === art).length
const gliedText = (g: VerlaufGlied) =>
  g.art === 'kabel' ? `Kabel ${g.kabelnummer} · Faser/Ader ${g.faser}`
    : g.art === 'patch' ? `Patchkabel${g.bezeichnung ? ` ${g.bezeichnung}` : ''}`
      : 'durch das Patchpanel'
</script>

<template>
  <ol class="kette">
    <template v-for="(p, i) in verlauf.punkte" :key="i">
      <li class="punkt" :class="{ offen: !p.port, hervor: p.port && p.port.id === hervorPortId, ende: p.port?.kategorie === 'Endgeraet' }">
        <span class="ort leise">{{ p.position.bezeichnung }} · {{ stockwerkText(p.position.stockwerk) }}</span>
        <button v-if="p.port" type="button" class="link" title="Gerät öffnen" @click="emit('geraet', p.port.geraetId)">
          {{ portText(p) }}
        </button>
        <span v-else class="offen-text">offenes Ende – nicht aufgelegt</span>
        <span v-if="p.port" class="leise klein"> {{ p.port.portart }}{{ p.port.kategorie === 'Endgeraet' ? ' · Endgerät' : '' }}</span>
      </li>
      <li v-if="i < verlauf.glieder.length" class="glied" :class="verlauf.glieder[i].art">
        <button v-if="verlauf.glieder[i].art === 'kabel'" type="button" class="link" title="Kabel öffnen"
          @click="emit('medium', verlauf.glieder[i].mediumId!)">{{ gliedText(verlauf.glieder[i]) }}</button>
        <span v-else>{{ gliedText(verlauf.glieder[i]) }}</span>
      </li>
    </template>
  </ol>
  <p class="leise klein">
    {{ anzahl(verlauf, 'kabel') }} Kabel, {{ anzahl(verlauf, 'patch') }} {{ anzahl(verlauf, 'patch') === 1 ? 'Patchung' : 'Patchungen' }} ·
    Luftlinie zusammen {{ formatZahl(verlauf.laengeMeter, 0) }} m
  </p>
</template>

<style scoped>
.kette {
  list-style: none;
  padding: 0;
  margin: 0.5rem 0;
  font-size: 0.9rem;
}
.punkt {
  border: 1px solid var(--rand);
  border-radius: 6px;
  padding: 0.35rem 0.55rem;
  background: var(--flaeche);
  display: flex;
  flex-direction: column;
}
.punkt.hervor {
  border-color: #fb8500;
  box-shadow: 0 0 0 2px #ffe0b2;
}
.punkt.ende {
  border-left: 4px solid #2da44e;
}
.punkt.offen {
  border-style: dashed;
  color: var(--text-leise);
}
.ort {
  font-size: 0.8rem;
}
.offen-text {
  font-style: italic;
}
.glied {
  margin-left: 1rem;
  padding: 0.3rem 0 0.3rem 0.9rem;
  border-left: 3px solid #1b7c83;
  font-size: 0.85rem;
}
.glied.patch {
  border-left-color: #8250df;
  color: #8250df;
}
.glied.durchgang {
  border-left-style: dotted;
  color: var(--text-leise);
}
.link {
  background: none;
  border: none;
  padding: 0;
  color: inherit;
  font-weight: 600;
  font-size: inherit;
  text-align: left;
}
.link:hover {
  color: var(--akzent);
  text-decoration: underline;
}
.klein {
  font-size: 0.85rem;
}
</style>
