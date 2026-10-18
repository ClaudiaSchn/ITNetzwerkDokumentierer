<script setup lang="ts">
/** Umschalter zwischen den beiden Arten, einen Plan zu referenzieren */
export type Methode = 'Passpunkte' | 'EigenerMassstab'

defineProps<{ methode: Methode }>()
const emit = defineEmits<{ wechseln: [methode: Methode] }>()
</script>

<template>
  <div class="methoden" role="tablist" aria-label="Art der Referenzierung">
    <button type="button" role="tab" :aria-selected="methode === 'Passpunkte'" :class="{ aktiv: methode === 'Passpunkte' }"
      title="Der Plan ist im Bezugsplan erkennbar" @click="emit('wechseln', 'Passpunkte')">Passpunkte</button>
    <button type="button" role="tab" :aria-selected="methode === 'EigenerMassstab'"
      :class="{ aktiv: methode === 'EigenerMassstab' }" title="Der Plan ist in keinem anderen Plan erkennbar"
      @click="emit('wechseln', 'EigenerMassstab')">Eigener Massstab</button>
  </div>
</template>

<style scoped>
.methoden {
  display: flex;
  border: 1px solid var(--rand);
  border-radius: 6px;
  overflow: hidden;
  margin: 0.6rem 0 0.4rem;
}
.methoden button {
  flex: 1;
  border: none;
  border-radius: 0;
  background: var(--flaeche);
  color: var(--text);
  padding: 0.35rem 0.5rem;
  font-size: 0.9rem;
}
.methoden button + button {
  border-left: 1px solid var(--rand);
}
.methoden button.aktiv {
  background: var(--akzent);
  color: #fff;
}
</style>
