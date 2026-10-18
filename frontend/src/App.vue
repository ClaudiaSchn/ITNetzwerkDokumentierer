<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type Status } from './api'

const status = ref<Status | null>(null)
const statusFehler = ref(false)

onMounted(async () => {
  try {
    status.value = await api.status()
  } catch {
    statusFehler.value = true
  }
})
</script>

<template>
  <header class="kopf">
    <RouterLink to="/" class="marke">Netzwerkdokumentation</RouterLink>
    <span class="status" :title="status ? `Version ${status.version}` : ''">
      <template v-if="statusFehler"><span class="punkt schlecht-bg"></span>Backend nicht erreichbar</template>
      <template v-else-if="status">
        <span class="punkt" :class="status.datenbank === 'verbunden' ? 'gut-bg' : 'schlecht-bg'"></span>
        Datenbank {{ status.datenbank }}
      </template>
    </span>
  </header>
  <RouterView />
</template>

<style scoped>
.kopf {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  height: 3.25rem;
  padding: 0 1rem;
  background: #24292f;
  color: #fff;
}
.marke {
  color: #fff;
  font-weight: 700;
  text-decoration: none;
  font-size: 1.05rem;
}
.status {
  font-size: 0.85rem;
  color: #d0d7de;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.punkt {
  width: 0.6rem;
  height: 0.6rem;
  border-radius: 50%;
  display: inline-block;
}
.gut-bg {
  background: #3fb950;
}
.schlecht-bg {
  background: #f85149;
}
</style>
